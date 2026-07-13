using AMES.Api.Auth;
using AMES.Data.Connection;
using System.Data;
using Microsoft.Data.SqlClient;

namespace AMES.Api.Endpoints;

public static class WhEndpoints
{
    private const string WhLocationCorcd = "5010";
    private const string WhLocationBizcd = "5011";

    // ── DTOs ─────────────────────────────────────────────────────────────
    public sealed record InboundScheduleRow(int PoId, string PoNumber, int? PoLineNo, string? VendorId,
        string? ItemNo, string? ItemName, string? CarCode, string? Unit, decimal OrderQty, decimal ReceivedQty,
        decimal NonDeliverQty, DateTime? DueDate, DateTime? PoCreateDate, string? Status);

    public sealed record InventoryRow(int InventoryId, string ItemNo, string? ItemName, string LocationId,
        int? LotId, decimal OnHandQty, decimal ReservedQty, DateTime? ExpiryDate);

    public sealed record LocationRow(string LocationId, string? LocationName, string? Zone, int LineCount, decimal TotalQty,
        string? WarehouseCode = null, string? WarehouseName = null, string? AreaCode = null, string? AreaName = null,
        string? ZoneName = null, string? X = null, string? Y = null, string? Z = null,
        string? PlantCode = null, string? LocationType = null, decimal? Capacity = null);

    public sealed record InboundScanRow(string ReceiveType, string? Yn, string LotNo, string Barcode,
        string? SourceTable, string? NoteNo, string? CaseBarcode, string? CaseNo, string? InvoiceNo,
        string? ContainerNo, string? PartNo, string? PartName, decimal Qty, string? Unit, string? PoNo,
        int? PoSeq, string? VendorId, string? VendorName, DateTime? ProductionDate, DateTime? DeliveryDate,
        DateTime? ArrivalDate, DateTime? ShipDate, DateTime? PackDate, string? ReceivedLocation,
        string? ReceivedStatus);

    public sealed record InboundReceiveReq(string Mode, string Barcode, string LocationId);
    public sealed record InboundCancelReq(string Mode, string Barcode);
    public sealed record InboundAdjustReq(string Mode, string Barcode, decimal DeltaQty, string ReasonCode,
        string? ReasonNote, string SupervisorPin);
    public sealed record InboundReceiveResult(bool Success, string Message, InboundScanRow? Row);

    public sealed record ReleaseScheduleRow(string PickSlipNo, string? RequestLocation, DateTime? RequestDate,
        string? RequestTime, DateTime? PrintDate, DateTime? CloseDate, string? CloseUserId,
        int LineCount, decimal RequestBoxQty, decimal PickedBoxQty, decimal RequestQty, decimal PickedQty,
        string Status, string? FirstItemNo, string? FirstItemName, string? SuggestedLocation, string? SuggestedZone);

    public sealed record ReleasePickLineRow(string PickSlipNo, string ItemNo, string? ItemName,
        decimal RequestBoxQty, decimal PickedBoxQty, decimal PickedQty, string? RequestUserId,
        string? SuggestedLocation1, string? SuggestedLocation2, string? SuggestedLocation3, string Status);

    public sealed record ReleaseLotRow(string PickSlipNo, string LotNo, string? ItemNo, string? ItemName,
        decimal Qty, string? Unit, string? LocationNo, string? LocationName, string? ZoneCode,
        string? InvStatus, string? ProdDate, string? RcvDate, bool IsFifoSuggested, bool IsValid,
        string? Message);

    public sealed record TransactionRow(long TxnId, DateTime TxnTime, string TxnType, string? ItemNo,
        string? LocationId, decimal QtyBefore, decimal Delta, decimal QtyAfter, string? ReasonCode);

    public sealed record ReceiveReq(string LotCode, decimal Qty, string LocationId);
    public sealed record AdjustReq(string ItemNo, string LocationId, decimal Delta, string ReasonCode, string? Note);
    public sealed record PickReq(string PickSlipNo, string LotNo, decimal Qty);
    public sealed record PickResult(bool Success, string Message, ReleaseLotRow? Row);

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

        g.MapGet("/inbound/scan", (HttpContext ctx, string mode, string barcode) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();

            try
            {
                return Results.Ok(ExecuteInboundScan(factory, mode, barcode));
            }
            catch
            {
                return Results.Problem("Warehouse database is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

        g.MapPost("/inbound/receive-sis", (HttpContext ctx, InboundReceiveReq body) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();

            return Results.Ok(ExecuteInboundReceive(factory, body, s.EmployeeNo, "[SIS_TEST].[PDA_WH002_RECEIVE]", "Received"));
        });

        g.MapPost("/inbound/move-location", (HttpContext ctx, InboundReceiveReq body) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();

            return Results.Ok(ExecuteInboundReceive(factory, body, s.EmployeeNo, "[SIS_TEST].[PDA_WH002_MOVE_LOCATION]", "Location changed"));
        });

        g.MapPost("/inbound/cancel", (HttpContext ctx, InboundCancelReq body) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();

            return Results.Ok(ExecuteInboundCancel(factory, body, s.EmployeeNo));
        });

        g.MapPost("/inbound/adjust-qty", (HttpContext ctx, InboundAdjustReq body) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();

            return Results.Ok(ExecuteInboundAdjust(factory, body, s.EmployeeNo));
        });

        g.MapGet("/location/scan", (HttpContext ctx, string locationId) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();

            try
            {
                return Results.Ok(QuerySisLocation(factory, locationId));
            }
            catch
            {
                return Results.Problem("Warehouse database is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }
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
            return Results.Ok(QueryReleaseSchedule(factory));
        });

        g.MapGet("/release/schedule/{pickSlipNo}/lines", (HttpContext ctx, string pickSlipNo) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            return Results.Ok(QueryReleasePickLines(factory, pickSlipNo));
        });

        g.MapGet("/release/lot", (HttpContext ctx, string pickSlipNo, string lotNo) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            using var conn = factory.OpenConnection();
            var row = ValidateReleaseLot(conn, null, pickSlipNo, lotNo);
            return Results.Ok(row);
        });

        // WH-07 PDA Release pick
        g.MapPost("/release/pick", (HttpContext ctx, PickReq body) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();

            using var conn = factory.OpenConnection();
            using var tx = conn.BeginTransaction();

            var row = ValidateReleaseLot(conn, tx, body.PickSlipNo, body.LotNo);
            if (!row.IsValid)
            {
                tx.Rollback();
                return Results.BadRequest(new PickResult(false, row.Message ?? "LOT cannot be picked.", row));
            }

            var pickQty = body.Qty <= 0 ? row.Qty : body.Qty;
            if (pickQty != row.Qty)
            {
                tx.Rollback();
                return Results.BadRequest(new PickResult(false,
                    "Partial LOT split is not supported in this PDA flow yet. Pick the full LOT quantity.", row));
            }

            EnsureReleaseAuditTable(conn, tx);

            using var cmd = new SqlCommand("""
                UPDATE SIS_TEST.WMS2020
                SET    INV_STATUS = N'O1',
                       PICK_SLIPNO = @PickSlipNo,
                       WORK_DATE = CONVERT(nvarchar(10), CONVERT(date, SYSDATETIME()), 23),
                       WORK_TIME = REPLACE(CONVERT(nvarchar(8), CONVERT(time(0), SYSDATETIME())), N':', N''),
                       STD_DATE = CONVERT(nvarchar(10), CONVERT(date, SYSDATETIME()), 23),
                       USER_ID = @By,
                       UPDATE_ID = @By,
                       UPDATE_DATE = SYSDATETIME()
                WHERE  LOTNO = @LotNo
                  AND  (INV_STATUS LIKE N'I%' AND INV_STATUS <> N'IC');

                INSERT INTO SIS_TEST.PDA_WH_RELEASE_PICK_AUDIT
                    (PICK_SLIPNO, LOTNO, PARTNO, QTY, LOCATION_NO, BEFORE_STATUS, AFTER_STATUS,
                     WORKER_ID, TERMINAL_ID, CREATED_AT)
                VALUES
                    (@PickSlipNo, @LotNo, @PartNo, @Qty, @LocationNo, @BeforeStatus, N'O1',
                     @By, @TerminalId, SYSDATETIME());
                """, conn, tx);
            cmd.Parameters.AddWithValue("@PickSlipNo", body.PickSlipNo.Trim());
            cmd.Parameters.AddWithValue("@LotNo", body.LotNo.Trim());
            cmd.Parameters.AddWithValue("@PartNo", (object?)row.ItemNo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Qty", pickQty);
            cmd.Parameters.AddWithValue("@LocationNo", (object?)row.LocationNo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@BeforeStatus", (object?)row.InvStatus ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@By", s.OperatorId);
            cmd.Parameters.AddWithValue("@TerminalId", s.TerminalId);
            var affected = cmd.ExecuteNonQuery();

            if (affected < 2)
            {
                tx.Rollback();
                return Results.BadRequest(new PickResult(false, "LOT status changed before picking. Scan again.", row));
            }

            tx.Commit();
            return Results.Ok(new PickResult(true, "Release pick completed.", row with
            {
                InvStatus = "O1",
                Message = "Release pick completed."
            }));
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

    private static List<ReleaseScheduleRow> QueryReleaseSchedule(AmesConnectionFactory factory)
    {
        using var conn = factory.OpenConnection();
        if (!HasReleaseTables(conn))
            return QueryDemoReleaseSchedule(conn);

        using var cmd = new SqlCommand("""
            WITH Header AS (
                SELECT
                    A.PICK_SLIPNO,
                    MIN(A.REQ_LOCATION) AS REQ_LOCATION,
                    MAX(TRY_CONVERT(date, A.REQ_DATE)) AS REQ_DATE_DT,
                    MAX(CONVERT(nvarchar(20), A.REQ_TIME)) AS REQ_TIME_TEXT,
                    MAX(TRY_CONVERT(datetime2, A.PRINT_DATE)) AS PRINT_DATE,
                    MAX(TRY_CONVERT(datetime2, A.CLOSE_DATE)) AS CLOSE_DATE,
                    MAX(A.CLOSE_USER_ID) AS CLOSE_USER_ID,
                    COUNT(*) AS LINE_COUNT,
                    SUM(COALESCE(TRY_CONVERT(decimal(18,3), A.REQ_BOX_QTY), 0)) AS REQ_BOX_QTY
                FROM SIS_TEST.WMS3050 A
                WHERE NULLIF(LTRIM(RTRIM(A.PICK_SLIPNO)), N'') IS NOT NULL
                GROUP BY A.PICK_SLIPNO
            ),
            Picked AS (
                SELECT
                    W.PICK_SLIPNO,
                    COUNT(*) AS PICKED_BOX_QTY,
                    SUM(COALESCE(TRY_CONVERT(decimal(18,3), W.QTY), 0)) AS PICKED_QTY
                FROM SIS_TEST.WMS2020 W
                WHERE W.INV_STATUS = N'O1'
                  AND NULLIF(LTRIM(RTRIM(W.PICK_SLIPNO)), N'') IS NOT NULL
                GROUP BY W.PICK_SLIPNO
            ),
            FirstLine AS (
                SELECT
                    A.PICK_SLIPNO,
                    A.PARTNO,
                    COALESCE(MAX(P.PARTNM), A.PARTNO) AS PARTNM,
                    ROW_NUMBER() OVER (PARTITION BY A.PICK_SLIPNO ORDER BY MIN(A.SEQNO), A.PARTNO) AS RN
                FROM SIS_TEST.WMS3050 A
                LEFT JOIN SIS_TEST.ACD0020L P
                       ON P.PARTNO = A.PARTNO
                      AND (P.LANG_SET = N'EN' OR P.LANG_SET IS NULL)
                GROUP BY A.PICK_SLIPNO, A.PARTNO
            )
            SELECT TOP (100)
                H.PICK_SLIPNO,
                H.REQ_LOCATION,
                H.REQ_DATE_DT AS REQ_DATE,
                H.REQ_TIME_TEXT AS REQ_TIME,
                H.PRINT_DATE,
                H.CLOSE_DATE,
                H.CLOSE_USER_ID,
                H.LINE_COUNT,
                H.REQ_BOX_QTY,
                COALESCE(PK.PICKED_BOX_QTY, 0) AS PICKED_BOX_QTY,
                H.REQ_BOX_QTY AS REQ_QTY,
                COALESCE(PK.PICKED_QTY, 0) AS PICKED_QTY,
                FL.PARTNO AS FIRST_PARTNO,
                FL.PARTNM AS FIRST_PARTNM,
                INV.LOCATION_NO AS SUGGESTED_LOCATION,
                INV.ZONECD AS SUGGESTED_ZONE,
                CASE
                    WHEN H.CLOSE_DATE IS NOT NULL THEN N'Closed'
                    WHEN COALESCE(PK.PICKED_BOX_QTY, 0) >= H.REQ_BOX_QTY AND H.REQ_BOX_QTY > 0 THEN N'Picked'
                    WHEN COALESCE(PK.PICKED_BOX_QTY, 0) > 0 THEN N'Partial'
                    WHEN H.REQ_DATE_DT < CONVERT(date, GETDATE()) THEN N'Late'
                    ELSE N'Open'
                END AS STATUS
            FROM Header H
            LEFT JOIN Picked PK
                   ON PK.PICK_SLIPNO = H.PICK_SLIPNO
            LEFT JOIN FirstLine FL
                   ON FL.PICK_SLIPNO = H.PICK_SLIPNO
                  AND FL.RN = 1
            OUTER APPLY (
                SELECT TOP (1) S.LOCATION_NO, L.ZONECD
                FROM SIS_TEST.WMS2020 S
                LEFT JOIN SIS_TEST.WMS1040 L
                       ON L.LOCATION_NO = S.LOCATION_NO
                WHERE S.PARTNO = FL.PARTNO
                  AND S.INV_STATUS LIKE N'I%'
                  AND S.INV_STATUS <> N'IC'
                  AND NULLIF(LTRIM(RTRIM(S.LOCATION_NO)), N'') IS NOT NULL
                ORDER BY COALESCE(TRY_CONVERT(date, S.RCV_DATE), TRY_CONVERT(date, S.PROD_DATE), CONVERT(date, '9999-12-31')),
                         S.LOCATION_NO,
                         S.LOTNO
            ) INV
            ORDER BY H.REQ_DATE_DT, H.PICK_SLIPNO;
            """, conn);

        using var rdr = cmd.ExecuteReader();
        var rows = new List<ReleaseScheduleRow>();
        while (rdr.Read()) rows.Add(ReadReleaseScheduleRow(rdr));
        return rows;
    }

    private static List<ReleasePickLineRow> QueryReleasePickLines(AmesConnectionFactory factory, string pickSlipNo)
    {
        using var conn = factory.OpenConnection();
        if (!HasReleaseTables(conn)) return new List<ReleasePickLineRow>();

        using var cmd = new SqlCommand("""
            WITH RequiredParts AS (
                SELECT
                    A.PICK_SLIPNO,
                    A.PARTNO,
                    COALESCE(MAX(P.PARTNM), A.PARTNO) AS PARTNM,
                    SUM(COALESCE(TRY_CONVERT(decimal(18,3), A.REQ_BOX_QTY), 0)) AS REQ_BOX_QTY,
                    MAX(A.REQ_USERID) AS REQ_USERID
                FROM SIS_TEST.WMS3050 A
                LEFT JOIN SIS_TEST.ACD0020L P
                       ON P.PARTNO = A.PARTNO
                      AND (P.LANG_SET = N'EN' OR P.LANG_SET IS NULL)
                WHERE A.PICK_SLIPNO = @PickSlipNo
                GROUP BY A.PICK_SLIPNO, A.PARTNO
            ),
            Picked AS (
                SELECT
                    W.PICK_SLIPNO,
                    W.PARTNO,
                    COUNT(*) AS PICKED_BOX_QTY,
                    SUM(COALESCE(TRY_CONVERT(decimal(18,3), W.QTY), 0)) AS PICKED_QTY
                FROM SIS_TEST.WMS2020 W
                WHERE W.PICK_SLIPNO = @PickSlipNo
                  AND W.INV_STATUS = N'O1'
                GROUP BY W.PICK_SLIPNO, W.PARTNO
            ),
            RankedLocations AS (
                SELECT
                    S.PARTNO,
                    S.LOCATION_NO,
                    ROW_NUMBER() OVER (
                        PARTITION BY S.PARTNO
                        ORDER BY COALESCE(TRY_CONVERT(date, S.RCV_DATE), TRY_CONVERT(date, S.PROD_DATE), CONVERT(date, '9999-12-31')),
                                 S.LOCATION_NO,
                                 S.LOTNO
                    ) AS RN
                FROM SIS_TEST.WMS2020 S
                INNER JOIN RequiredParts R
                        ON R.PARTNO = S.PARTNO
                WHERE S.INV_STATUS LIKE N'I%'
                  AND S.INV_STATUS <> N'IC'
                  AND NULLIF(LTRIM(RTRIM(S.LOCATION_NO)), N'') IS NOT NULL
            ),
            Locations AS (
                SELECT
                    PARTNO,
                    MAX(CASE WHEN RN = 1 THEN LOCATION_NO END) AS LOC_01,
                    MAX(CASE WHEN RN = 2 THEN LOCATION_NO END) AS LOC_02,
                    MAX(CASE WHEN RN = 3 THEN LOCATION_NO END) AS LOC_03
                FROM RankedLocations
                WHERE RN <= 3
                GROUP BY PARTNO
            )
            SELECT
                R.PICK_SLIPNO,
                R.PARTNO,
                R.PARTNM,
                R.REQ_BOX_QTY,
                COALESCE(P.PICKED_BOX_QTY, 0) AS PICKED_BOX_QTY,
                COALESCE(P.PICKED_QTY, 0) AS PICKED_QTY,
                R.REQ_USERID,
                L.LOC_01,
                L.LOC_02,
                L.LOC_03,
                CASE
                    WHEN COALESCE(P.PICKED_BOX_QTY, 0) >= R.REQ_BOX_QTY AND R.REQ_BOX_QTY > 0 THEN N'Picked'
                    WHEN COALESCE(P.PICKED_BOX_QTY, 0) > 0 THEN N'Partial'
                    ELSE N'Open'
                END AS STATUS
            FROM RequiredParts R
            LEFT JOIN Picked P
                   ON P.PICK_SLIPNO = R.PICK_SLIPNO
                  AND P.PARTNO = R.PARTNO
            LEFT JOIN Locations L
                   ON L.PARTNO = R.PARTNO
            ORDER BY R.PARTNO;
            """, conn);
        cmd.Parameters.AddWithValue("@PickSlipNo", pickSlipNo.Trim());

        using var rdr = cmd.ExecuteReader();
        var rows = new List<ReleasePickLineRow>();
        while (rdr.Read())
        {
            rows.Add(new ReleasePickLineRow(
                GetString(rdr, "PICK_SLIPNO") ?? "",
                GetString(rdr, "PARTNO") ?? "",
                GetString(rdr, "PARTNM"),
                GetDecimal(rdr, "REQ_BOX_QTY"),
                GetDecimal(rdr, "PICKED_BOX_QTY"),
                GetDecimal(rdr, "PICKED_QTY"),
                GetString(rdr, "REQ_USERID"),
                GetString(rdr, "LOC_01"),
                GetString(rdr, "LOC_02"),
                GetString(rdr, "LOC_03"),
                GetString(rdr, "STATUS") ?? "Open"));
        }

        return rows;
    }

    private static ReleaseLotRow ValidateReleaseLot(SqlConnection conn, SqlTransaction? tx, string pickSlipNo, string lotNo)
    {
        pickSlipNo = pickSlipNo.Trim();
        lotNo = lotNo.Trim();

        if (!HasReleaseTables(conn, tx))
            return InvalidReleaseLot(pickSlipNo, lotNo, "Release tables are not available in SIS_TEST.");

        if (string.IsNullOrWhiteSpace(pickSlipNo))
            return InvalidReleaseLot(pickSlipNo, lotNo, "Pick Slip No is required.");

        if (string.IsNullOrWhiteSpace(lotNo))
            return InvalidReleaseLot(pickSlipNo, lotNo, "LOT No is required.");

        using (var slipCmd = new SqlCommand("""
            SELECT TOP (1) CLOSE_DATE
            FROM SIS_TEST.WMS3050
            WHERE PICK_SLIPNO = @PickSlipNo;
            """, conn, tx))
        {
            slipCmd.Parameters.AddWithValue("@PickSlipNo", pickSlipNo);
            var closeDate = slipCmd.ExecuteScalar();
            if (closeDate is null)
                return InvalidReleaseLot(pickSlipNo, lotNo, "Pick Slip was not found.");
            if (closeDate != DBNull.Value && !string.IsNullOrWhiteSpace(Convert.ToString(closeDate)))
                return InvalidReleaseLot(pickSlipNo, lotNo, "Pick Slip is already closed.");
        }

        ReleaseLotRow? row;
        using (var lotCmd = new SqlCommand("""
            SELECT TOP (1)
                S.LOTNO,
                S.PARTNO,
                COALESCE(PL.PARTNM, S.PARTNO) AS PARTNM,
                COALESCE(TRY_CONVERT(decimal(18,3), S.QTY), 0) AS QTY,
                P.UNIT,
                S.LOCATION_NO,
                L.LOCATION_NM,
                L.ZONECD,
                S.INV_STATUS,
                CONVERT(nvarchar(20), S.PROD_DATE) AS PROD_DATE,
                CONVERT(nvarchar(20), S.RCV_DATE) AS RCV_DATE
            FROM SIS_TEST.WMS2020 S
            LEFT JOIN SIS_TEST.ACD0020 P
                   ON P.PARTNO = S.PARTNO
            LEFT JOIN SIS_TEST.ACD0020L PL
                   ON PL.PARTNO = S.PARTNO
                  AND (PL.LANG_SET = N'EN' OR PL.LANG_SET IS NULL)
            LEFT JOIN SIS_TEST.WMS1040 L
                   ON L.LOCATION_NO = S.LOCATION_NO
            WHERE S.LOTNO = @LotNo;
            """, conn, tx))
        {
            lotCmd.Parameters.AddWithValue("@LotNo", lotNo);
            using var rdr = lotCmd.ExecuteReader();
            if (!rdr.Read())
                return InvalidReleaseLot(pickSlipNo, lotNo, "LOT was not found.");

            row = new ReleaseLotRow(
                pickSlipNo,
                GetString(rdr, "LOTNO") ?? lotNo,
                GetString(rdr, "PARTNO"),
                GetString(rdr, "PARTNM"),
                GetDecimal(rdr, "QTY"),
                GetString(rdr, "UNIT"),
                GetString(rdr, "LOCATION_NO"),
                GetString(rdr, "LOCATION_NM"),
                GetString(rdr, "ZONECD"),
                GetString(rdr, "INV_STATUS"),
                GetString(rdr, "PROD_DATE"),
                GetString(rdr, "RCV_DATE"),
                false,
                false,
                null);
        }

        if (row is null)
            return InvalidReleaseLot(pickSlipNo, lotNo, "LOT was not found.");

        if (string.IsNullOrWhiteSpace(row.InvStatus)
            || !row.InvStatus.StartsWith("I", StringComparison.OrdinalIgnoreCase)
            || row.InvStatus.Equals("IC", StringComparison.OrdinalIgnoreCase))
            return row with { IsValid = false, Message = $"LOT is not available for release. Current status is {row.InvStatus ?? "-"}." };

        var requestedBoxes = ScalarDecimal(conn, tx, """
            SELECT SUM(COALESCE(TRY_CONVERT(decimal(18,3), REQ_BOX_QTY), 0))
            FROM SIS_TEST.WMS3050
            WHERE PICK_SLIPNO = @PickSlipNo
              AND PARTNO = @PartNo;
            """, ("@PickSlipNo", pickSlipNo), ("@PartNo", row.ItemNo ?? ""));

        if (requestedBoxes <= 0)
            return row with { IsValid = false, Message = "Wrong item. This LOT is not requested by the selected Pick Slip." };

        var pickedBoxes = ScalarDecimal(conn, tx, """
            SELECT COUNT(*)
            FROM SIS_TEST.WMS2020
            WHERE PICK_SLIPNO = @PickSlipNo
              AND PARTNO = @PartNo
              AND INV_STATUS = N'O1';
            """, ("@PickSlipNo", pickSlipNo), ("@PartNo", row.ItemNo ?? ""));

        if (pickedBoxes >= requestedBoxes)
            return row with { IsValid = false, Message = "This item is already fully picked for the selected Pick Slip." };

        var oldestLot = ScalarString(conn, tx, """
            SELECT TOP (1) LOTNO
            FROM SIS_TEST.WMS2020
            WHERE PARTNO = @PartNo
              AND INV_STATUS LIKE N'I%'
              AND INV_STATUS <> N'IC'
            ORDER BY COALESCE(TRY_CONVERT(date, RCV_DATE), TRY_CONVERT(date, PROD_DATE), CONVERT(date, '9999-12-31')),
                     LOCATION_NO,
                     LOTNO;
            """, ("@PartNo", row.ItemNo ?? ""));

        if (!string.IsNullOrWhiteSpace(oldestLot)
            && !oldestLot.Equals(row.LotNo, StringComparison.OrdinalIgnoreCase))
            return row with { IsFifoSuggested = false, IsValid = false, Message = $"FIFO violation. Pick LOT {oldestLot} first." };

        return row with { IsFifoSuggested = true, IsValid = true, Message = "LOT is ready to pick." };
    }

    private static void EnsureReleaseAuditTable(SqlConnection conn, SqlTransaction tx)
    {
        using var cmd = new SqlCommand("""
            IF OBJECT_ID(N'SIS_TEST.PDA_WH_RELEASE_PICK_AUDIT', N'U') IS NULL
            BEGIN
                CREATE TABLE SIS_TEST.PDA_WH_RELEASE_PICK_AUDIT
                (
                    AUDIT_ID bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    PICK_SLIPNO nvarchar(30) NOT NULL,
                    LOTNO nvarchar(50) NOT NULL,
                    PARTNO nvarchar(50) NULL,
                    QTY decimal(18,3) NOT NULL,
                    LOCATION_NO nvarchar(50) NULL,
                    BEFORE_STATUS nvarchar(20) NULL,
                    AFTER_STATUS nvarchar(20) NOT NULL,
                    WORKER_ID nvarchar(80) NULL,
                    TERMINAL_ID nvarchar(80) NULL,
                    CREATED_AT datetime2 NOT NULL
                );
            END
            """, conn, tx);
        cmd.ExecuteNonQuery();
    }

    private static ReleaseLotRow InvalidReleaseLot(string pickSlipNo, string lotNo, string message)
        => new(pickSlipNo, lotNo, null, null, 0, null, null, null, null, null, null, null, false, false, message);

    private static bool HasReleaseTables(SqlConnection conn, SqlTransaction? tx = null)
        => TableExists(conn, tx, "SIS_TEST", "WMS3050")
           && TableExists(conn, tx, "SIS_TEST", "WMS2020")
           && TableExists(conn, tx, "SIS_TEST", "WMS1040")
           && TableExists(conn, tx, "SIS_TEST", "ACD0020")
           && TableExists(conn, tx, "SIS_TEST", "ACD0020L");

    private static List<ReleaseScheduleRow> QueryDemoReleaseSchedule(SqlConnection conn)
    {
        if (!TableExists(conn, null, "dbo", "WH_ReleaseSchedule"))
            return new List<ReleaseScheduleRow>();

        using var cmd = new SqlCommand("""
            SELECT TOP (50)
                CONVERT(nvarchar(30), rs.ReleaseScheduleID) AS PICK_SLIPNO,
                NULL AS REQ_LOCATION,
                CONVERT(date, rs.RequiredAt) AS REQ_DATE,
                CONVERT(nvarchar(20), CONVERT(time(0), rs.RequiredAt)) AS REQ_TIME,
                NULL AS PRINT_DATE,
                NULL AS CLOSE_DATE,
                NULL AS CLOSE_USER_ID,
                1 AS LINE_COUNT,
                ISNULL(rs.DemandQty, 0) AS REQ_BOX_QTY,
                ISNULL(rs.PickedQty, 0) AS PICKED_BOX_QTY,
                ISNULL(rs.DemandQty, 0) AS REQ_QTY,
                ISNULL(rs.PickedQty, 0) AS PICKED_QTY,
                rs.ItemNo AS FIRST_PARTNO,
                i.ItemName AS FIRST_PARTNM,
                NULL AS SUGGESTED_LOCATION,
                NULL AS SUGGESTED_ZONE,
                ISNULL(rs.Status, N'Open') AS STATUS
            FROM dbo.WH_ReleaseSchedule rs
            LEFT JOIN dbo.MD_Item i
                   ON i.ItemNo = rs.ItemNo
            ORDER BY ISNULL(rs.RequiredAt, '9999-01-01'), rs.ReleaseScheduleID;
            """, conn);
        using var rdr = cmd.ExecuteReader();
        var rows = new List<ReleaseScheduleRow>();
        while (rdr.Read()) rows.Add(ReadReleaseScheduleRow(rdr));
        return rows;
    }

    private static ReleaseScheduleRow ReadReleaseScheduleRow(SqlDataReader rdr)
        => new(
            GetString(rdr, "PICK_SLIPNO") ?? "",
            GetString(rdr, "REQ_LOCATION"),
            GetDate(rdr, "REQ_DATE"),
            GetString(rdr, "REQ_TIME"),
            GetDate(rdr, "PRINT_DATE"),
            GetDate(rdr, "CLOSE_DATE"),
            GetString(rdr, "CLOSE_USER_ID"),
            GetInt(rdr, "LINE_COUNT") ?? 0,
            GetDecimal(rdr, "REQ_BOX_QTY"),
            GetDecimal(rdr, "PICKED_BOX_QTY"),
            GetDecimal(rdr, "REQ_QTY"),
            GetDecimal(rdr, "PICKED_QTY"),
            GetString(rdr, "STATUS") ?? "Open",
            GetString(rdr, "FIRST_PARTNO"),
            GetString(rdr, "FIRST_PARTNM"),
            GetString(rdr, "SUGGESTED_LOCATION"),
            GetString(rdr, "SUGGESTED_ZONE"));

    private static bool TableExists(SqlConnection conn, SqlTransaction? tx, string schema, string table)
    {
        using var cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID(@ObjectName, N'U') IS NULL THEN 0 ELSE 1 END;", conn, tx);
        cmd.Parameters.Add("@ObjectName", SqlDbType.NVarChar, 256).Value = $"{schema}.{table}";
        return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
    }

    private static decimal ScalarDecimal(SqlConnection conn, SqlTransaction? tx, string sql, params (string Name, object? Value)[] parameters)
    {
        using var cmd = new SqlCommand(sql, conn, tx);
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        var result = cmd.ExecuteScalar();
        return result == null || result == DBNull.Value ? 0 : Convert.ToDecimal(result);
    }

    private static string? ScalarString(SqlConnection conn, SqlTransaction? tx, string sql, params (string Name, object? Value)[] parameters)
    {
        using var cmd = new SqlCommand(sql, conn, tx);
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        var result = cmd.ExecuteScalar();
        return result == null || result == DBNull.Value ? null : Convert.ToString(result);
    }

    // ── Helpers ─────────────────────────────────────────────────────────
    private static InboundScanRow? ExecuteInboundScan(AmesConnectionFactory factory, string mode, string barcode)
    {
        var proc = string.Equals(mode, "CKD", StringComparison.OrdinalIgnoreCase)
            ? "[SIS_TEST].[PDA_WH002_SCAN_CKD]"
            : "[SIS_TEST].[PDA_WH002_SCAN_LOCAL]";

        using var conn = factory.OpenConnection();
        using var cmd = new SqlCommand(proc, conn)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 15
        };
        cmd.Parameters.Add("@IN_BARCODE", SqlDbType.NVarChar, 50).Value = barcode.Trim();

        using var rdr = cmd.ExecuteReader();
        return rdr.Read() ? ReadInboundScanRow(rdr) : null;
    }

    private static InboundReceiveResult ExecuteInboundReceive(
        AmesConnectionFactory factory,
        InboundReceiveReq body,
        string userId,
        string proc,
        string successMessage)
    {
        try
        {
            using var conn = factory.OpenConnection();
            using var cmd = new SqlCommand(proc, conn)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 15
            };
            cmd.Parameters.Add("@IN_MODE", SqlDbType.NVarChar, 10).Value = body.Mode.Trim();
            cmd.Parameters.Add("@IN_BARCODE", SqlDbType.NVarChar, 50).Value = body.Barcode.Trim();
            cmd.Parameters.Add("@IN_LOCATION_NO", SqlDbType.NVarChar, 30).Value = body.LocationId.Trim();
            cmd.Parameters.Add("@IN_USERID", SqlDbType.NVarChar, 40).Value = userId;

            using var rdr = cmd.ExecuteReader();
            var row = rdr.Read() ? ReadInboundScanRow(rdr) : null;
            return new InboundReceiveResult(true, successMessage, row);
        }
        catch
        {
            return new InboundReceiveResult(false, "Warehouse database is unavailable.", null);
        }
    }

    private static InboundReceiveResult ExecuteInboundCancel(AmesConnectionFactory factory, InboundCancelReq body, string userId)
    {
        try
        {
            using var conn = factory.OpenConnection();
            using var cmd = new SqlCommand("[SIS_TEST].[PDA_WH002_CANCEL]", conn)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 15
            };
            cmd.Parameters.Add("@IN_MODE", SqlDbType.NVarChar, 10).Value = body.Mode.Trim();
            cmd.Parameters.Add("@IN_BARCODE", SqlDbType.NVarChar, 50).Value = body.Barcode.Trim();
            cmd.Parameters.Add("@IN_USERID", SqlDbType.NVarChar, 40).Value = userId;

            using var rdr = cmd.ExecuteReader();
            var row = rdr.Read() ? ReadInboundScanRow(rdr) : null;
            return new InboundReceiveResult(true, "Incoming canceled", row);
        }
        catch
        {
            return new InboundReceiveResult(false, "Warehouse database is unavailable.", null);
        }
    }

    private static InboundReceiveResult ExecuteInboundAdjust(AmesConnectionFactory factory, InboundAdjustReq body, string userId)
    {
        try
        {
            using var conn = factory.OpenConnection();
            using var cmd = new SqlCommand("[SIS_TEST].[PDA_WH002_ADJUST_QTY]", conn)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 15
            };
            cmd.Parameters.Add("@IN_MODE", SqlDbType.NVarChar, 10).Value = body.Mode.Trim();
            cmd.Parameters.Add("@IN_BARCODE", SqlDbType.NVarChar, 50).Value = body.Barcode.Trim();
            cmd.Parameters.Add("@IN_DELTA_QTY", SqlDbType.Decimal).Value = body.DeltaQty;
            cmd.Parameters["@IN_DELTA_QTY"].Precision = 18;
            cmd.Parameters["@IN_DELTA_QTY"].Scale = 3;
            cmd.Parameters.Add("@IN_REASON_CODE", SqlDbType.NVarChar, 30).Value = body.ReasonCode.Trim();
            cmd.Parameters.Add("@IN_REASON_NOTE", SqlDbType.NVarChar, 500).Value =
                string.IsNullOrWhiteSpace(body.ReasonNote) ? DBNull.Value : body.ReasonNote.Trim();
            cmd.Parameters.Add("@IN_SUPERVISOR_PIN", SqlDbType.NVarChar, 40).Value = body.SupervisorPin.Trim();
            cmd.Parameters.Add("@IN_USERID", SqlDbType.NVarChar, 40).Value = userId;

            using var rdr = cmd.ExecuteReader();
            var row = rdr.Read() ? ReadInboundScanRow(rdr) : null;
            return new InboundReceiveResult(true, "Quantity adjusted", row);
        }
        catch
        {
            return new InboundReceiveResult(false, "Warehouse database is unavailable.", null);
        }
    }

    private static LocationRow? QuerySisLocation(AmesConnectionFactory factory, string locationId)
    {
        using var conn = factory.OpenConnection();
        using var cmd = new SqlCommand("""
            SELECT TOP (1)
                L.LOCATION_NO AS LocationID,
                L.LOCATION_NM AS LocationName,
                L.ZONECD AS ZoneCode,
                L.WHCD AS WarehouseCode,
                W.WHNM AS WarehouseName,
                L.AREACD AS AreaCode,
                A.AREANM AS AreaName,
                Z.ZONENM AS ZoneName,
                L.RACK_X AS Aisle,
                L.RACK_Y AS Bay,
                L.RACK_Z AS Slot,
                CAST(NULL AS nvarchar(20)) AS PlantCode,
                CAST(N'SIS' AS nvarchar(20)) AS LocationType,
                CAST(NULL AS decimal(18,3)) AS Capacity,
                COUNT(S.LOTNO) AS LineCount,
                COALESCE(SUM(S.QTY), 0) AS TotalQty
            FROM SIS_TEST.WMS1040 L
            LEFT JOIN SIS_TEST.WMS1010 W
                ON W.CORCD = L.CORCD
               AND W.BIZCD = L.BIZCD
               AND W.WHCD = L.WHCD
            LEFT JOIN SIS_TEST.WMS1020 A
                ON A.CORCD = L.CORCD
               AND A.BIZCD = L.BIZCD
               AND A.AREACD = L.AREACD
            LEFT JOIN SIS_TEST.WMS1030 Z
                ON Z.CORCD = L.CORCD
               AND Z.BIZCD = L.BIZCD
               AND Z.ZONECD = L.ZONECD
            LEFT JOIN SIS_TEST.WMS2020 S
                ON S.LOCATION_NO = L.LOCATION_NO
            WHERE L.CORCD = @LocationCorcd
              AND L.BIZCD = @LocationBizcd
              AND COALESCE(L.USE_YN, N'Y') = N'Y'
              AND UPPER(L.LOCATION_NO) = UPPER(@LocationID)
            GROUP BY L.LOCATION_NO, L.LOCATION_NM, L.ZONECD, L.WHCD, W.WHNM,
                L.AREACD, A.AREANM, Z.ZONENM, L.RACK_X, L.RACK_Y, L.RACK_Z
            ORDER BY L.LOCATION_NO;
            """, conn)
        {
            CommandTimeout = 15
        };
        cmd.Parameters.Add("@LocationCorcd", SqlDbType.NVarChar, 10).Value = WhLocationCorcd;
        cmd.Parameters.Add("@LocationBizcd", SqlDbType.NVarChar, 10).Value = WhLocationBizcd;
        cmd.Parameters.Add("@LocationID", SqlDbType.NVarChar, 30).Value = locationId.Trim();

        using var rdr = cmd.ExecuteReader();
        return rdr.Read() ? ReadLocationRow(rdr) : null;
    }

    private static InboundScanRow ReadInboundScanRow(SqlDataReader rdr)
    {
        return new InboundScanRow(
            GetString(rdr, "RECEIVE_TYPE") ?? "",
            GetString(rdr, "YN"),
            GetString(rdr, "LOTNO") ?? "",
            GetString(rdr, "BARCODE") ?? "",
            GetString(rdr, "SOURCE_TABLE"),
            GetString(rdr, "NOTENO"),
            GetString(rdr, "CASE_BARCODE"),
            GetString(rdr, "CASE_NO"),
            GetString(rdr, "INVOICE_NO"),
            GetString(rdr, "CONTAINER_NO"),
            GetString(rdr, "PARTNO"),
            GetString(rdr, "PARTNM"),
            GetDecimal(rdr, "QTY"),
            GetString(rdr, "UNIT"),
            GetString(rdr, "PONO"),
            GetInt(rdr, "PONO_SEQ"),
            GetString(rdr, "VENDCD"),
            GetString(rdr, "VENDNM"),
            GetDate(rdr, "PROD_DATE"),
            GetDate(rdr, "DELI_DATE"),
            GetDate(rdr, "ARRIV_DATE"),
            GetDate(rdr, "SHIP_DATE"),
            GetDate(rdr, "PACK_DATE"),
            GetString(rdr, "RECEIVED_LOCATION"),
            GetString(rdr, "RECEIVED_STATUS"));
    }

    private static LocationRow ReadLocationRow(SqlDataReader rdr)
    {
        return new LocationRow(
            GetString(rdr, "LocationID") ?? "",
            GetString(rdr, "LocationName"),
            GetString(rdr, "ZoneCode"),
            GetInt(rdr, "LineCount") ?? 0,
            GetDecimal(rdr, "TotalQty"),
            GetString(rdr, "WarehouseCode"),
            GetString(rdr, "WarehouseName"),
            GetString(rdr, "AreaCode"),
            GetString(rdr, "AreaName"),
            GetString(rdr, "ZoneName"),
            GetString(rdr, "Aisle"),
            GetString(rdr, "Bay"),
            GetString(rdr, "Slot"),
            GetString(rdr, "PlantCode"),
            GetString(rdr, "LocationType"),
            GetNullableDecimal(rdr, "Capacity"));
    }

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

    private static decimal? GetNullableDecimal(SqlDataReader rdr, string name)
    {
        if (!HasColumn(rdr, name)) return null;
        var value = rdr[name];
        return value == DBNull.Value ? null : Convert.ToDecimal(value);
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
