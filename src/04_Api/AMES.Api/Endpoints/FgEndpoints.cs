using AMES.Api.Auth;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Api.Endpoints;

public static class FgEndpoints
{
    // ── DTOs ─────────────────────────────────────────────────────────────
    public sealed record StockRow(int StockId, string? StockNumber, string ItemNo, string? ItemName,
        int? LotId, string? CustomerCode, decimal Qty, string? Location, string? Status, DateTime? StockTs);
    public sealed record OrderRow(int ShipmentOrderId, string? ShipOrderNumber, string? CustomerCode,
        string? CustomerPo, DateTime? ShipDate, string? CarrierCode, string? DestPlant, string? Status, int LineCount);
    public sealed record HistoryRow(int LoadingId, string? LoadingNumber, int? ShipmentOrderId,
        string? ShipOrderNumber, string? CustomerCode, string? LicensePlate, string? DriverName,
        DateTime? DepartureTs, string? OTDStatus);
    public sealed record DashboardDto(int OpenOrders, int ReadyToShip, int InTransit, int DeliveredToday,
        int PendingReturns, decimal StockOnHand);

    public sealed record PutAwayReq(int WoId, string ItemNo, decimal Qty, string ActualLoc, int PalletCount);
    public sealed record PickReq(int ShipmentOrderId, int StockId, decimal Qty);
    public sealed record LoadingReq(int ShipmentOrderId, string LicensePlate, string DriverName, string DockNo, string? SealNo);
    public sealed record DeliveryReq(int ShipmentOrderId, int? LoadingId);
    public sealed record DayEndReq(string CloseMode, string? Note);
    public sealed record ReturnReq(string CustomerCode, int? OriginalShipmentOrderId, string ReturnReason, decimal Qty, string ItemNo);

    // ── Routes ───────────────────────────────────────────────────────────
    public static void MapFg(this WebApplication app, AmesConnectionFactory factory)
    {
        var g = app.MapGroup("/api/fg").WithTags("Finished Goods");

        // FG-01 Stocking — write FG_Stock + FG_PutAway
        g.MapPost("/putaway", (HttpContext ctx, PutAwayReq body) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();
            using var conn = factory.OpenConnection();
            using var tx   = conn.BeginTransaction();
            try
            {
                int stockId;
                using (var cmd = new SqlCommand("""
                    INSERT INTO dbo.FG_Stock
                        (StockNumber, WoID, ItemNo, Qty, Location, Status, HoldFlag,
                         StockTS, CreatedBy, CreatedTS)
                    OUTPUT INSERTED.StockID
                    VALUES (CONCAT('STK-', FORMAT(SYSDATETIME(),'yyMMddHHmmss')),
                            @W, @I, @Q, @L, 'Available', 0,
                            SYSDATETIME(), 'pda', SYSDATETIME());
                    """, conn, tx))
                {
                    cmd.Parameters.AddWithValue("@W", body.WoId);
                    cmd.Parameters.AddWithValue("@I", body.ItemNo);
                    cmd.Parameters.AddWithValue("@Q", body.Qty);
                    cmd.Parameters.AddWithValue("@L", body.ActualLoc);
                    stockId = (int)cmd.ExecuteScalar()!;
                }
                using (var cmd = new SqlCommand("""
                    INSERT INTO dbo.FG_PutAway
                        (StockID, WoID, ItemNo, Qty, ActualLoc, PalletCount,
                         LabelPrintedTS, OperatorID, Status, CreatedBy, CreatedTS)
                    VALUES (@S, @W, @I, @Q, @L, @P,
                            SYSDATETIME(), @Op, 'Confirmed', 'pda', SYSDATETIME());
                    """, conn, tx))
                {
                    cmd.Parameters.AddWithValue("@S",  stockId);
                    cmd.Parameters.AddWithValue("@W",  body.WoId);
                    cmd.Parameters.AddWithValue("@I",  body.ItemNo);
                    cmd.Parameters.AddWithValue("@Q",  body.Qty);
                    cmd.Parameters.AddWithValue("@L",  body.ActualLoc);
                    cmd.Parameters.AddWithValue("@P",  body.PalletCount);
                    cmd.Parameters.AddWithValue("@Op", s.OperatorId);
                    cmd.ExecuteNonQuery();
                }
                tx.Commit();
                return Results.Ok(new { StockId = stockId });
            }
            catch { tx.Rollback(); throw; }
        });

        // FG-02 Inventory
        g.MapGet("/inventory", (HttpContext ctx, string? q) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            const string sql = """
                SELECT TOP 100 s.StockID, s.StockNumber, s.ItemNo, m.ItemName,
                       s.LotID, s.CustomerCode, ISNULL(s.Qty,0) AS Qty,
                       s.Location, s.Status, s.StockTS
                FROM   dbo.FG_Stock s
                LEFT JOIN dbo.MD_Item m ON m.ItemNo = s.ItemNo
                WHERE  (@Q = '' OR s.ItemNo LIKE '%' + @Q + '%' OR m.ItemName LIKE '%' + @Q + '%')
                ORDER BY s.StockTS DESC;
                """;
            return QueryWithParam(factory, sql, "@Q", q ?? "", r => new StockRow(
                (int)r["StockID"], r["StockNumber"] as string,
                r["ItemNo"] as string ?? "", r["ItemName"] as string,
                r["LotID"] as int?, r["CustomerCode"] as string,
                r.GetDecimal(r.GetOrdinal("Qty")),
                r["Location"] as string, r["Status"] as string, r["StockTS"] as DateTime?));
        });

        // FG-03 Shipment Order list
        g.MapGet("/orders", (HttpContext ctx) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            const string sql = """
                SELECT TOP 50 o.ShipmentOrderID, o.ShipOrderNumber, o.CustomerCode,
                       o.CustomerPO, o.ShipDate, o.CarrierCode, o.DestPlant,
                       ISNULL(o.Status,'Open') AS Status,
                       (SELECT COUNT(*) FROM dbo.FG_ShipmentOrderLine l
                        WHERE l.ShipmentOrderID = o.ShipmentOrderID) AS LineCount
                FROM   dbo.FG_ShipmentOrder o
                ORDER BY ISNULL(o.ShipDate,'9999-01-01'), o.ShipmentOrderID DESC;
                """;
            return Query(factory, sql, r => new OrderRow(
                (int)r["ShipmentOrderID"], r["ShipOrderNumber"] as string,
                r["CustomerCode"] as string, r["CustomerPO"] as string,
                r["ShipDate"] as DateTime?, r["CarrierCode"] as string,
                r["DestPlant"] as string, r["Status"] as string,
                (int)r["LineCount"]));
        });

        // FG-04 FIFO Pick — write FG_PickingFifo + reserve a FG_Stock row
        g.MapPost("/pick", (HttpContext ctx, PickReq body) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();
            using var conn = factory.OpenConnection();
            using var cmd  = new SqlCommand("""
                INSERT INTO dbo.FG_PickingFifo
                    (PickNumber, ShipmentOrderID, PickerID, StartTS, EndTS,
                     FifoViolations, OverrideCount, PickedQty, OrderedQty,
                     Status, CreatedBy, CreatedTS)
                OUTPUT INSERTED.PickID
                VALUES (CONCAT('PICK-', FORMAT(SYSDATETIME(),'yyMMddHHmmss')),
                        @So, @Op, SYSDATETIME(), SYSDATETIME(),
                        0, 0, @Q, @Q, 'Picked', 'pda', SYSDATETIME());

                UPDATE dbo.FG_Stock
                SET    Status='Reserved', ModifiedTS=SYSDATETIME()
                WHERE  StockID = @Stk;
                """, conn);
            cmd.Parameters.AddWithValue("@So",  body.ShipmentOrderId);
            cmd.Parameters.AddWithValue("@Stk", body.StockId);
            cmd.Parameters.AddWithValue("@Q",   body.Qty);
            cmd.Parameters.AddWithValue("@Op",  s.OperatorId);
            var id = (int)cmd.ExecuteScalar()!;
            return Results.Ok(new { PickId = id });
        });

        // FG-05 Loading confirm
        g.MapPost("/loading", (HttpContext ctx, LoadingReq body) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();
            using var conn = factory.OpenConnection();
            using var cmd  = new SqlCommand("""
                INSERT INTO dbo.FG_LoadingConfirm
                    (LoadingNumber, ShipmentOrderID, LicensePlate, DriverName,
                     DockNo, ArrivalTS, DepartureTS, SealNo, OTDStatus,
                     OperatorID, ConfirmedAt, CreatedBy, CreatedTS)
                OUTPUT INSERTED.LoadingID
                VALUES (CONCAT('LDG-', FORMAT(SYSDATETIME(),'yyMMddHHmmss')),
                        @So, @Lp, @Dn, @Dk, SYSDATETIME(), SYSDATETIME(),
                        @Sl, 'OnTime', @Op, SYSDATETIME(), 'pda', SYSDATETIME());

                UPDATE dbo.FG_ShipmentOrder SET Status='Shipped', ModifiedTS=SYSDATETIME()
                WHERE  ShipmentOrderID = @So;
                """, conn);
            cmd.Parameters.AddWithValue("@So", body.ShipmentOrderId);
            cmd.Parameters.AddWithValue("@Lp", body.LicensePlate);
            cmd.Parameters.AddWithValue("@Dn", body.DriverName);
            cmd.Parameters.AddWithValue("@Dk", body.DockNo);
            cmd.Parameters.AddWithValue("@Sl", (object?)body.SealNo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Op", s.OperatorId);
            var id = (int)cmd.ExecuteScalar()!;
            return Results.Ok(new { LoadingId = id });
        });

        // FG-06 Delivery Note issue
        g.MapPost("/delivery", (HttpContext ctx, DeliveryReq body) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();
            using var conn = factory.OpenConnection();
            using var cmd  = new SqlCommand("""
                INSERT INTO dbo.FG_DeliveryNote
                    (DnNumber, ShipmentOrderID, LoadingID, CustomerCode,
                     FormatTemplate, Revision, IssuedAt, IssuedBy,
                     EdiStatus, CreatedBy, CreatedTS)
                OUTPUT INSERTED.DeliveryNoteID
                VALUES (CONCAT('DN-', FORMAT(SYSDATETIME(),'yyMMddHHmmss')),
                        @So, @Ld,
                        (SELECT TOP 1 CustomerCode FROM dbo.FG_ShipmentOrder WHERE ShipmentOrderID=@So),
                        'STANDARD', 1, SYSDATETIME(), @Op,
                        'Sent', 'pda', SYSDATETIME());
                """, conn);
            cmd.Parameters.AddWithValue("@So", body.ShipmentOrderId);
            cmd.Parameters.AddWithValue("@Ld", (object?)body.LoadingId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Op", s.OperatorId);
            var id = (int)cmd.ExecuteScalar()!;
            return Results.Ok(new { DeliveryNoteId = id });
        });

        // FG-07 Day End Close
        g.MapPost("/dayend", (HttpContext ctx, DayEndReq body) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();
            using var conn = factory.OpenConnection();
            using var cmd  = new SqlCommand("""
                INSERT INTO dbo.FG_DayEndClose
                    (CloseNumber, CloseDate, ClosedBy, ClosedAt, CloseMode,
                     ChecklistJSON, ErpFeedStatus, CreatedBy, CreatedTS)
                OUTPUT INSERTED.DayEndCloseID
                VALUES (CONCAT('DEC-', FORMAT(SYSDATETIME(),'yyMMddHHmm')),
                        CAST(GETDATE() AS DATE), @By, SYSDATETIME(), @M,
                        @N, 'Pending', 'pda', SYSDATETIME());
                """, conn);
            cmd.Parameters.AddWithValue("@By", s.OperatorId);
            cmd.Parameters.AddWithValue("@M",  body.CloseMode);
            cmd.Parameters.AddWithValue("@N",  (object?)body.Note ?? DBNull.Value);
            var id = (int)cmd.ExecuteScalar()!;
            return Results.Ok(new { DayEndCloseId = id });
        });

        // FG-08 Shipment History
        g.MapGet("/history", (HttpContext ctx) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            const string sql = """
                SELECT TOP 50 l.LoadingID, l.LoadingNumber, l.ShipmentOrderID,
                       o.ShipOrderNumber, o.CustomerCode, l.LicensePlate,
                       l.DriverName, l.DepartureTS, l.OTDStatus
                FROM   dbo.FG_LoadingConfirm l
                LEFT JOIN dbo.FG_ShipmentOrder o ON o.ShipmentOrderID = l.ShipmentOrderID
                ORDER BY l.DepartureTS DESC;
                """;
            return Query(factory, sql, r => new HistoryRow(
                (int)r["LoadingID"], r["LoadingNumber"] as string,
                r["ShipmentOrderID"] as int?, r["ShipOrderNumber"] as string,
                r["CustomerCode"] as string, r["LicensePlate"] as string,
                r["DriverName"] as string, r["DepartureTS"] as DateTime?,
                r["OTDStatus"] as string));
        });

        // FG-09 Dashboard rollup
        g.MapGet("/dashboard", (HttpContext ctx) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            using var conn = factory.OpenConnection();
            using var cmd  = new SqlCommand("""
                SELECT
                  (SELECT COUNT(*) FROM dbo.FG_ShipmentOrder WHERE Status IN ('Open','Released'))     AS OpenOrders,
                  (SELECT COUNT(*) FROM dbo.FG_ShipmentOrder WHERE Status = 'Ready')                  AS ReadyToShip,
                  (SELECT COUNT(*) FROM dbo.FG_ShipmentOrder WHERE Status = 'Shipped')                AS InTransit,
                  (SELECT COUNT(*) FROM dbo.FG_LoadingConfirm
                     WHERE CAST(DepartureTS AS DATE) = CAST(GETDATE() AS DATE))                       AS DeliveredToday,
                  (SELECT COUNT(*) FROM dbo.FG_CustomerReturn WHERE Status IN ('Open','Inspecting')) AS PendingReturns,
                  ISNULL((SELECT SUM(Qty) FROM dbo.FG_Stock WHERE Status='Available'), 0)             AS StockOnHand;
                """, conn);
            using var rdr = cmd.ExecuteReader();
            rdr.Read();
            return Results.Ok(new DashboardDto(
                (int)rdr["OpenOrders"],
                (int)rdr["ReadyToShip"],
                (int)rdr["InTransit"],
                (int)rdr["DeliveredToday"],
                (int)rdr["PendingReturns"],
                rdr.GetDecimal(rdr.GetOrdinal("StockOnHand"))));
        });

        // FG-RTN Return
        g.MapPost("/return", (HttpContext ctx, ReturnReq body) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();
            using var conn = factory.OpenConnection();
            using var cmd  = new SqlCommand("""
                INSERT INTO dbo.FG_CustomerReturn
                    (ReturnNumber, CustomerCode, OriginalShipmentOrderID,
                     ReturnReason, ItemsJSON, Status, ReceivedAt, ReceivedBy,
                     CapaTriggered, CreatedBy, CreatedTS)
                OUTPUT INSERTED.ReturnID
                VALUES (CONCAT('RMA-', FORMAT(SYSDATETIME(),'yyMMddHHmmss')),
                        @C, @So, @R,
                        CONCAT('[{"itemNo":"', @I, '","qty":', @Q, '}]'),
                        'Open', SYSDATETIME(), @By, 0,
                        'pda', SYSDATETIME());
                """, conn);
            cmd.Parameters.AddWithValue("@C",  body.CustomerCode);
            cmd.Parameters.AddWithValue("@So", (object?)body.OriginalShipmentOrderId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@R",  body.ReturnReason);
            cmd.Parameters.AddWithValue("@I",  body.ItemNo);
            cmd.Parameters.AddWithValue("@Q",  body.Qty);
            cmd.Parameters.AddWithValue("@By", s.OperatorId);
            var id = (int)cmd.ExecuteScalar()!;
            return Results.Ok(new { ReturnId = id });
        });
    }

    // ── helpers ──────────────────────────────────────────────────────────
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
}
