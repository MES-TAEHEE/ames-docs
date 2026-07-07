using AMES.Api.Auth;
using AMES.Data.Connection;
using System.Data;
using Microsoft.Data.SqlClient;

namespace AMES.Api.Endpoints;

public static class WhEndpoints
{
    // ── DTOs ─────────────────────────────────────────────────────────────
    public sealed record InboundScheduleRow(int PoId, string PoNumber, int? PoLineNo, string? VendorId,
        string? ItemNo, string? ItemName, string? CarCode, string? Unit, decimal OrderQty, decimal ReceivedQty,
        decimal NonDeliverQty, DateTime? DueDate, DateTime? PoCreateDate, string? Status);

    public sealed record InventoryRow(int InventoryId, string ItemNo, string? ItemName, string LocationId,
        int? LotId, decimal OnHandQty, decimal ReservedQty, DateTime? ExpiryDate);

    public sealed record LocationRow(string LocationId, string? LocationName, string? Zone, int LineCount, decimal TotalQty);

    public sealed record ReleaseScheduleRow(int ReleaseScheduleId, int? WoId, string? WoNumber,
        string ItemNo, string? ItemName, decimal DemandQty, decimal PickedQty, DateTime? RequiredAt, string? Status);

    public sealed record TransactionRow(long TxnId, DateTime TxnTime, string TxnType, string? ItemNo,
        string? LocationId, decimal QtyBefore, decimal Delta, decimal QtyAfter, string? ReasonCode);

    public sealed record ReceiveReq(string LotCode, decimal Qty, string LocationId);
    public sealed record AdjustReq(string ItemNo, string LocationId, decimal Delta, string ReasonCode, string? Note);
    public sealed record PickReq(int ReleaseScheduleId, string LotCode, decimal Qty);

    // ── Routes ───────────────────────────────────────────────────────────
    public static void MapWh(this WebApplication app, AmesConnectionFactory factory)
    {
        var g = app.MapGroup("/api/wh").WithTags("Warehouse");

        // WH-01 Inbound Schedule
        g.MapGet("/inbound/schedule", (HttpContext ctx, int? year, int? quarter, string? vendorId, string? lang) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();

            var today = DateTime.Today;
            var queryYear = year ?? today.Year;
            var queryQuarter = quarter ?? ((today.Month - 1) / 3) + 1;
            var language = string.IsNullOrWhiteSpace(lang) ? "EN" : lang;

            using var conn = factory.OpenConnection();
            using var cmd = new SqlCommand("[SIS_TEST].[APG_WM40120_INQUERY_VENDER_BACK_ORDER]", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@IN_CORCD", "1000");
            cmd.Parameters.AddWithValue("@IN_BIZCD", "5011");
            cmd.Parameters.AddWithValue("@IN_YYYY", queryYear.ToString());
            cmd.Parameters.AddWithValue("@IN_QUATER", queryQuarter.ToString());
            cmd.Parameters.AddWithValue("@IN_VENDCD", string.IsNullOrWhiteSpace(vendorId) ? DBNull.Value : vendorId);
            cmd.Parameters.AddWithValue("@IN_LANG_SET", language);

            using var rdr = cmd.ExecuteReader();
            var rows = new List<InboundScheduleRow>();
            var poId = 1;
            while (rdr.Read())
            {
                var orderQty = GetDecimal(rdr, "PO_QTY");
                var receivedQty = GetDecimal(rdr, "GRN_QTY");
                var remainQty = GetDecimal(rdr, "NON_DELI_QTY");
                var dueDate = GetDate(rdr, "PO_DELI_DATE");
                var poCreateDate = GetDate(rdr, "PO_DATE");
                var status = remainQty <= 0
                    ? "Complete"
                    : dueDate.HasValue && dueDate.Value.Date < today ? "Late"
                    : "In Progress";

                rows.Add(new InboundScheduleRow(
                    poId++,
                    GetString(rdr, "PONO") ?? "",
                    GetInt(rdr, "PONO_SEQ"),
                    GetString(rdr, "VENDNM") ?? GetString(rdr, "VENDCD"),
                    GetString(rdr, "PARTNO"),
                    GetString(rdr, "PARTNM"),
                    GetString(rdr, "VINCD"),
                    GetString(rdr, "PO_UNIT"),
                    orderQty,
                    receivedQty,
                    remainQty,
                    dueDate,
                    poCreateDate,
                    status));
            }

            return Results.Ok(rows);
        });

        // Today's inbound (kept from earlier sample)
        g.MapGet("/inbound/today", (HttpContext ctx) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            const string sql = """
                SELECT TOP 30 l.LotID, l.LotCode, l.ItemNo, i.ItemName,
                       ISNULL(l.BatchSize,0) AS Qty,
                       '' AS Vendor, l.ProducedAt AS ArrivedAt
                FROM   dbo.tbl_Lot l
                LEFT JOIN dbo.MD_Item i ON i.ItemNo = l.ItemNo
                WHERE  l.ProcessCode = 'WH'
                ORDER BY l.ProducedAt DESC;
                """;
            return Query(factory, sql, r => new InboundRowDto(
                (int)r["LotID"], r["LotCode"] as string ?? "",
                r["ItemNo"] as string, r["ItemName"] as string,
                r.GetDecimal(r.GetOrdinal("Qty")),
                r["Vendor"] as string, r["ArrivedAt"] as DateTime?));
        });

        // WH-02 PDA Inbound — receive a scanned lot against the schedule
        g.MapPost("/inbound/receive", (HttpContext ctx, ReceiveReq body) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();
            using var conn = factory.OpenConnection();
            using var cmd  = new SqlCommand("""
                INSERT INTO dbo.WH_Receiving
                    (ReceivingNo, ItemNo, ReceivedQty, LocationID, LotCode,
                     ReceivedAt, ReceivedBy, TerminalID, QcStatus, LabelPrinted,
                     CreatedBy, CreatedTS)
                OUTPUT INSERTED.ReceivingID
                VALUES (CONCAT('RCV-', FORMAT(SYSDATETIME(),'yyMMddHHmmss')),
                        (SELECT TOP 1 ItemNo FROM dbo.tbl_Lot WHERE LotCode=@L),
                        @Q, @Loc, @L, SYSDATETIME(), @By, @T, 'Pending', 0,
                        'pda', SYSDATETIME());
                """, conn);
            cmd.Parameters.AddWithValue("@L", body.LotCode);
            cmd.Parameters.AddWithValue("@Q", body.Qty);
            cmd.Parameters.AddWithValue("@Loc", body.LocationId);
            cmd.Parameters.AddWithValue("@By", s.OperatorId);
            cmd.Parameters.AddWithValue("@T", s.TerminalId);
            var id = (int)cmd.ExecuteScalar()!;
            return Results.Ok(new { ReceivingId = id });
        });

        // WH-03 Inventory Status
        g.MapGet("/inventory", (HttpContext ctx, string? q) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            var sql = """
                SELECT TOP 100 inv.InventoryID, inv.ItemNo, i.ItemName, inv.LocationID,
                       inv.LotID, ISNULL(inv.OnHandQty,0) AS OnHandQty,
                       ISNULL(inv.ReservedQty,0) AS ReservedQty, inv.ExpiryDate
                FROM   dbo.WH_Inventory inv
                LEFT JOIN dbo.MD_Item i ON i.ItemNo = inv.ItemNo
                WHERE  (@Q = '' OR inv.ItemNo LIKE '%' + @Q + '%' OR i.ItemName LIKE '%' + @Q + '%')
                ORDER BY inv.ItemNo, inv.LocationID;
                """;
            return QueryWithParam(factory, sql, "@Q", q ?? "", r => new InventoryRow(
                (int)r["InventoryID"], r["ItemNo"] as string ?? "", r["ItemName"] as string,
                r["LocationID"] as string ?? "", r["LotID"] as int?,
                r.GetDecimal(r.GetOrdinal("OnHandQty")), r.GetDecimal(r.GetOrdinal("ReservedQty")),
                r["ExpiryDate"] as DateTime?));
        });

        // WH-04 Location Map
        g.MapGet("/locations", (HttpContext ctx) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            const string sql = """
                SELECT l.LocationID, l.LocationName, l.ZoneCode,
                       (SELECT COUNT(*)            FROM dbo.WH_Inventory i WHERE i.LocationID=l.LocationID) AS LineCount,
                       ISNULL((SELECT SUM(i.OnHandQty) FROM dbo.WH_Inventory i WHERE i.LocationID=l.LocationID),0) AS TotalQty
                FROM   dbo.MD_Location l
                WHERE  ISNULL(l.ActiveFlag,1) = 1
                ORDER BY l.ZoneCode, l.LocationID;
                """;
            return Query(factory, sql, r => new LocationRow(
                r["LocationID"] as string ?? "", r["LocationName"] as string,
                r["ZoneCode"] as string, (int)r["LineCount"],
                r.GetDecimal(r.GetOrdinal("TotalQty"))));
        });

        // WH-05 Inventory Adjust
        g.MapPost("/inventory/adjust", (HttpContext ctx, AdjustReq body) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();
            using var conn = factory.OpenConnection();
            using var cmd  = new SqlCommand("""
                DECLARE @Before DECIMAL(14,3) =
                    ISNULL((SELECT TOP 1 OnHandQty FROM dbo.WH_Inventory
                            WHERE ItemNo=@I AND LocationID=@Loc), 0);
                DECLARE @After  DECIMAL(14,3) = @Before + @D;

                INSERT INTO dbo.WH_InventoryAdjust
                    (AdjustNo, ItemNo, LocationID, QtyBefore, Delta, QtyAfter,
                     ReasonCode, ReasonNote, Status, RequestedBy, CreatedBy, CreatedTS)
                OUTPUT INSERTED.AdjustID
                VALUES (CONCAT('ADJ-', FORMAT(SYSDATETIME(),'yyMMddHHmmss')),
                        @I, @Loc, @Before, @D, @After, @R, @N,
                        'Posted', @By, 'pda', SYSDATETIME());
                """, conn);
            cmd.Parameters.AddWithValue("@I",   body.ItemNo);
            cmd.Parameters.AddWithValue("@Loc", body.LocationId);
            cmd.Parameters.AddWithValue("@D",   body.Delta);
            cmd.Parameters.AddWithValue("@R",   body.ReasonCode);
            cmd.Parameters.AddWithValue("@N",   (object?)body.Note ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@By",  s.OperatorId);
            var id = (int)cmd.ExecuteScalar()!;
            return Results.Ok(new { AdjustId = id });
        });

        // WH-06 Release Schedule
        g.MapGet("/release/schedule", (HttpContext ctx) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            const string sql = """
                SELECT TOP 50 rs.ReleaseScheduleID, rs.WoID, w.WoNumber,
                       rs.ItemNo, i.ItemName,
                       ISNULL(rs.DemandQty,0) AS DemandQty,
                       ISNULL(rs.PickedQty,0) AS PickedQty,
                       rs.RequiredAt, ISNULL(rs.Status,'Open') AS Status
                FROM   dbo.WH_ReleaseSchedule rs
                LEFT JOIN dbo.PP_WorkOrder w ON w.WoID = rs.WoID
                LEFT JOIN dbo.MD_Item      i ON i.ItemNo = rs.ItemNo
                ORDER BY ISNULL(rs.RequiredAt, '9999-01-01'), rs.ReleaseScheduleID;
                """;
            return Query(factory, sql, r => new ReleaseScheduleRow(
                (int)r["ReleaseScheduleID"], r["WoID"] as int?, r["WoNumber"] as string,
                r["ItemNo"] as string ?? "", r["ItemName"] as string,
                r.GetDecimal(r.GetOrdinal("DemandQty")), r.GetDecimal(r.GetOrdinal("PickedQty")),
                r["RequiredAt"] as DateTime?, r["Status"] as string));
        });

        // WH-07 PDA Release pick
        g.MapPost("/release/pick", (HttpContext ctx, PickReq body) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();
            using var conn = factory.OpenConnection();
            using var cmd  = new SqlCommand("""
                INSERT INTO dbo.WH_ReleasePicking
                    (PickingNo, ReleaseScheduleID, ItemNo, LotID, PickedQty,
                     PickedAt, PickedBy, TerminalID, CreatedBy, CreatedTS)
                OUTPUT INSERTED.PickingID
                VALUES (CONCAT('PCK-', FORMAT(SYSDATETIME(),'yyMMddHHmmss')),
                        @S,
                        (SELECT TOP 1 ItemNo FROM dbo.tbl_Lot WHERE LotCode=@L),
                        (SELECT TOP 1 LotID  FROM dbo.tbl_Lot WHERE LotCode=@L),
                        @Q, SYSDATETIME(), @By, @T, 'pda', SYSDATETIME());

                UPDATE dbo.WH_ReleaseSchedule
                SET    PickedQty  = ISNULL(PickedQty,0) + @Q,
                       Status     = CASE WHEN ISNULL(PickedQty,0) + @Q >= ISNULL(DemandQty,0) THEN 'Picked' ELSE 'Partial' END,
                       ModifiedBy = @By,
                       ModifiedTS = SYSDATETIME()
                WHERE  ReleaseScheduleID = @S;
                """, conn);
            cmd.Parameters.AddWithValue("@S",  body.ReleaseScheduleId);
            cmd.Parameters.AddWithValue("@L",  body.LotCode);
            cmd.Parameters.AddWithValue("@Q",  body.Qty);
            cmd.Parameters.AddWithValue("@By", s.OperatorId);
            cmd.Parameters.AddWithValue("@T",  s.TerminalId);
            var id = (int)cmd.ExecuteScalar()!;
            return Results.Ok(new { PickingId = id });
        });

        // WH-08 Transaction History
        g.MapGet("/transactions", (HttpContext ctx, int? days) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            var d = days ?? 7;
            var sql = $$"""
                SELECT TOP 100 TxnID, TxnTime, ISNULL(TxnType,'?') AS TxnType,
                       ItemNo, LocationID,
                       ISNULL(QtyBefore,0) AS QtyBefore,
                       ISNULL(Delta,0)     AS Delta,
                       ISNULL(QtyAfter,0)  AS QtyAfter,
                       ReasonCode
                FROM   dbo.WH_TransactionHistory
                WHERE  TxnTime > DATEADD(day, -{{d}}, SYSDATETIME())
                ORDER BY TxnTime DESC;
                """;
            return Query(factory, sql, r => new TransactionRow(
                (long)r["TxnID"], (DateTime)r["TxnTime"], r["TxnType"] as string ?? "?",
                r["ItemNo"] as string, r["LocationID"] as string,
                r.GetDecimal(r.GetOrdinal("QtyBefore")),
                r.GetDecimal(r.GetOrdinal("Delta")),
                r.GetDecimal(r.GetOrdinal("QtyAfter")),
                r["ReasonCode"] as string));
        });
    }

    // Backwards-compat DTO from the v0 sample endpoint
    public sealed record InboundRowDto(int LotId, string LotCode, string? ItemNo, string? ItemName,
        decimal Qty, string? Vendor, DateTime? ArrivedAt);

    // ── Helpers ─────────────────────────────────────────────────────────
    private static IResult Query<T>(AmesConnectionFactory factory, string sql, Func<SqlDataReader, T> map)
    {
        using var conn = factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        using var rdr  = cmd.ExecuteReader();
        var list = new List<T>();
        while (rdr.Read()) list.Add(map(rdr));
        return Results.Ok(list);
    }
    private static IResult QueryWithParam<T>(AmesConnectionFactory factory, string sql, string p, object v,
                                              Func<SqlDataReader, T> map)
    {
        using var conn = factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(p, v);
        using var rdr  = cmd.ExecuteReader();
        var list = new List<T>();
        while (rdr.Read()) list.Add(map(rdr));
        return Results.Ok(list);
    }

    private static bool HasColumn(SqlDataReader rdr, string name)
    {
        for (var i = 0; i < rdr.FieldCount; i++)
        {
            if (string.Equals(rdr.GetName(i), name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string? GetString(SqlDataReader rdr, string name)
    {
        if (!HasColumn(rdr, name)) return null;
        var value = rdr[name];
        return value == DBNull.Value ? null : Convert.ToString(value);
    }

    private static int? GetInt(SqlDataReader rdr, string name)
    {
        if (!HasColumn(rdr, name)) return null;
        var value = rdr[name];
        return value == DBNull.Value ? null : Convert.ToInt32(value);
    }

    private static decimal GetDecimal(SqlDataReader rdr, string name)
    {
        if (!HasColumn(rdr, name)) return 0;
        var value = rdr[name];
        return value == DBNull.Value ? 0 : Convert.ToDecimal(value);
    }

    private static DateTime? GetDate(SqlDataReader rdr, string name)
    {
        if (!HasColumn(rdr, name)) return null;
        var value = rdr[name];
        if (value == DBNull.Value) return null;
        if (value is DateTime dt) return dt;
        return DateTime.TryParse(Convert.ToString(value), out var parsed) ? parsed : null;
    }
}
