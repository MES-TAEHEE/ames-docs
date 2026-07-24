using AMES.Api.Auth;
using AMES.Api.Logging;
using AMES.Data.Connection;
using System.Data;
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
    public sealed record PutAwayScanRow(int? LotId, string LotNo, int? WoId, string? WoNumber,
        string ItemNo, string? ItemName, string? CustomerCode, decimal Qty, string? Unit,
        DateTime? MfgDate, DateTime? ExpiryDate, string? QcInspectionNo, DateTime? QcPassTs,
        bool IsQcPassed, bool AlreadyStocked, int? ExistingStockId, string? ExistingLocation,
        string? ExistingStatus, string BarcodeType, string StorageMethod, string NextScanType,
        string NextScanLabel, string? PackSpecId, string Message);
    public sealed record PutAwayLocationRow(string LocationId, string? LocationName, string? ZoneCode,
        string? Aisle, string? Bay, string? Slot, decimal Capacity, decimal CurrentQty,
        decimal AvailableQty, string? CurrentCustomerCode, bool IsValid, string Message,
        string ScanType, string ScannedBarcode);
    public sealed record PutAwayConfirmReq(string Barcode, string LocationId, string? SuggestedLocation,
        string? OverrideReason, int? PalletCount, int? PalletQty, string? StorageMethod,
        string? ContainerType, string? ContainerBarcode);
    public sealed record PutAwayResult(bool Success, string Message, int? StockId, PutAwayScanRow? Row,
        PutAwayLocationRow? Location);
    public sealed record PickReq(int ShipmentOrderId, int StockId, decimal Qty);
    public sealed record LoadingReq(int ShipmentOrderId, string LicensePlate, string DriverName, string DockNo, string? SealNo);
    public sealed record DeliveryReq(int ShipmentOrderId, int? LoadingId);
    public sealed record DayEndReq(string CloseMode, string? Note);
    public sealed record ReturnReq(string CustomerCode, int? OriginalShipmentOrderId, string ReturnReason, decimal Qty, string ItemNo);

    private const string BarcodeLot = "LOT";
    private const string BarcodeWo = "WORK_ORDER";
    private const string BarcodeLocation = "LOCATION";
    private const string BarcodeBox = "BOX";
    private const string BarcodePallet = "PALLET";
    private const string BarcodeRack = "RACK";
    private const string BarcodeUnknown = "UNKNOWN";

    private sealed record ParsedFgBarcode(string Raw, string Value, string Kind);

    // ── Routes ───────────────────────────────────────────────────────────
    public static void MapFg(this WebApplication app, AmesConnectionFactory factory)
    {
        var g = app.MapGroup("/api/fg").WithTags("Finished Goods");

        // FG-01 PDA Put-Away - scan QC passed FG LOT or WO.
        g.MapGet("/putaway/scan", (HttpContext ctx, string barcode) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(barcode))
                return Results.BadRequest(new PutAwayResult(false, "Scan FG LOT or WO first.", null, null, null));

            var parsed = ParseFgBarcode(barcode);
            if (IsStorageBarcode(parsed.Kind))
            {
                var message = $"Scan FG LOT or WO first. You scanned {BarcodeKindLabel(parsed.Kind)}.";
                WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                    s, "SCAN_FG_LOT", "FG001", parsed.Kind, parsed.Raw, "FAIL", message,
                    lotNo: parsed.Value));
                return Results.BadRequest(new PutAwayResult(false, message, null, null, null));
            }

            using var conn = factory.OpenConnection();
            var row = FindPutAwayScanRow(conn, null, parsed.Value);
            if (row is null)
            {
                WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                    s, "SCAN_FG_LOT", "FG001", parsed.Kind, parsed.Raw, "FAIL", "FG LOT or WO was not found.",
                    lotNo: parsed.Value));
                return Results.NotFound(new PutAwayResult(false, "FG LOT or WO was not found.", null, null, null));
            }

            var success = row.IsQcPassed && !row.AlreadyStocked;
            WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                s, "SCAN_FG_LOT", "FG001", row.BarcodeType, parsed.Raw, success ? "SUCCESS" : "FAIL", row.Message,
                lotNo: row.LotNo, partNo: row.ItemNo, locationId: row.ExistingLocation, qty: row.Qty));

            return Results.Ok(new PutAwayResult(success, row.Message, row.ExistingStockId, row, null));
        });

        // FG-01 PDA Put-Away - auto slotting suggestion.
        g.MapGet("/putaway/suggest-location", (HttpContext ctx, string itemNo, string? customerCode, decimal qty) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();

            using var conn = factory.OpenConnection();
            var location = SuggestPutAwayLocation(conn, null, itemNo, customerCode, qty);
            return location is null
                ? Results.NotFound(new PutAwayResult(false, "No available FG location was found.", null, null, null))
                : Results.Ok(location);
        });

        // FG-01 PDA Put-Away - validate scanned FG location.
        g.MapGet("/putaway/location", (HttpContext ctx, string locationId, string itemNo, string? customerCode, decimal qty, string? expectedScanType) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(locationId))
                return Results.BadRequest(new PutAwayResult(false, "Scan Location No first.", null, null, null));

            using var conn = factory.OpenConnection();
            var location = ValidatePutAwayLocation(conn, null, locationId.Trim(), itemNo, customerCode, qty, expectedScanType);
            return location is null
                ? Results.NotFound(new PutAwayResult(false, "FG location was not found.", null, null, null))
                : Results.Ok(location);
        });

        // FG-01 PDA Put-Away - confirm Stock insert + PutAway history.
        g.MapPost("/putaway/confirm", (HttpContext ctx, PutAwayConfirmReq body) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(body.Barcode))
                return Results.BadRequest(new PutAwayResult(false, "Scan FG LOT or WO first.", null, null, null));
            if (string.IsNullOrWhiteSpace(body.LocationId))
                return Results.BadRequest(new PutAwayResult(false, "Scan Location No first.", null, null, null));

            using var conn = factory.OpenConnection();
            using var tx = conn.BeginTransaction();
            try
            {
                var parsedLot = ParseFgBarcode(body.Barcode);
                var row = FindPutAwayScanRow(conn, tx, parsedLot.Value);
                if (row is null)
                {
                    tx.Rollback();
                    return Results.NotFound(new PutAwayResult(false, "FG LOT or WO was not found.", null, null, null));
                }
                if (!row.IsQcPassed)
                {
                    tx.Rollback();
                    WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                        s, "FG_PUTAWAY", "FG001", "LOT_WO", body.Barcode, "FAIL", row.Message,
                        lotNo: row.LotNo, partNo: row.ItemNo, qty: row.Qty));
                    return Results.BadRequest(new PutAwayResult(false, row.Message, null, row, null));
                }
                if (row.AlreadyStocked)
                {
                    tx.Rollback();
                    WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                        s, "FG_PUTAWAY", "FG001", "LOT_WO", body.Barcode, "FAIL", row.Message,
                        lotNo: row.LotNo, partNo: row.ItemNo, locationId: row.ExistingLocation, qty: row.Qty));
                    return Results.BadRequest(new PutAwayResult(false, row.Message, row.ExistingStockId, row, null));
                }

                var locationScan = string.IsNullOrWhiteSpace(body.ContainerBarcode)
                    ? body.LocationId
                    : body.ContainerBarcode;
                var location = ValidatePutAwayLocation(conn, tx, locationScan.Trim(), row.ItemNo, row.CustomerCode, row.Qty, row.NextScanType);
                if (location is null || !location.IsValid)
                {
                    tx.Rollback();
                    var message = location?.Message ?? "FG location was not found.";
                    WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                        s, "FG_PUTAWAY", "FG001", "LOCATION", body.LocationId, "FAIL", message,
                        lotNo: row.LotNo, partNo: row.ItemNo, locationId: body.LocationId, qty: row.Qty));
                    return Results.BadRequest(new PutAwayResult(false, message, null, row, location));
                }

                var pack = ResolvePalletSplit(conn, tx, row.ItemNo, row.Qty, body.PalletCount, body.PalletQty);
                var stockId = InsertPutAwayStock(conn, tx, row, location, body.SuggestedLocation, body.OverrideReason,
                    pack.PalletCount, pack.PalletQty, s.OperatorId, row.StorageMethod, location.ScanType, location.ScannedBarcode);

                tx.Commit();

                using var readConn = factory.OpenConnection();
                var updated = FindPutAwayScanRow(readConn, null, parsedLot.Value);
                WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                    s, "FG_PUTAWAY", "FG001", location.ScanType, location.ScannedBarcode, "SUCCESS", "FG Put-Away confirmed.",
                    lotNo: row.LotNo, partNo: row.ItemNo, locationId: location.LocationId, qty: row.Qty));

                return Results.Ok(new PutAwayResult(true, "FG Put-Away confirmed.", stockId, updated ?? row, location));
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        });

        // FG-01 Stocking — write FG_Inventory + FG_PutAway
        g.MapPost("/putaway", (HttpContext ctx, PutAwayReq body) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();
            using var conn = factory.OpenConnection();
            using var tx   = conn.BeginTransaction();
            try
            {
                int stockId;
                using (var cmd = new SqlCommand("""
                    INSERT INTO dbo.FG_Inventory
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
                FROM   dbo.FG_Inventory s
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

        // FG-04 FIFO Pick — write FG_PickingFifo + reserve a FG_Inventory row
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

                UPDATE dbo.FG_Inventory
                SET    Status='Reserved', ModifiedBy=@Op, ModifiedTS=SYSDATETIME()
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

                UPDATE dbo.FG_ShipmentOrder SET Status='Shipped', ModifiedBy=@Op, ModifiedTS=SYSDATETIME()
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
                  ISNULL((SELECT SUM(Qty) FROM dbo.FG_Inventory WHERE Status='Available'), 0)             AS StockOnHand;
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
    private static PutAwayScanRow? FindPutAwayScanRow(SqlConnection conn, SqlTransaction? tx, string barcode)
    {
        using var cmd = new SqlCommand("""
            SELECT TOP (1)
                L.LotID,
                L.LotCode,
                W.WoID,
                W.WoNumber,
                COALESCE(NULLIF(L.ItemNo, ''), W.ItemNo) AS ItemNo,
                I.ItemName,
                COALESCE(NULLIF(Q.CustomerCode, ''), NULLIF(S.CustomerCode, '')) AS CustomerCode,
                CAST(COALESCE(NULLIF(Q.BatchQty, 0), NULLIF(L.RemainingQty, 0), NULLIF(L.BatchSize, 0), NULLIF(W.CompletedQty, 0), NULLIF(W.OrderQty, 0), 0) AS DECIMAL(14,3)) AS Qty,
                I.DefaultUOM AS Unit,
                L.ProducedAt AS MfgDate,
                L.ExpiryDate,
                Q.InspectionNo AS QcInspectionNo,
                Q.InsEndTS AS QcPassTs,
                CASE
                    WHEN Q.InspectionID IS NOT NULL OR UPPER(ISNULL(L.QualityFlag, '')) IN ('PASS', 'PASSED', 'OK') THEN 1
                    ELSE 0
                END AS IsQcPassed,
                S.StockID AS ExistingStockId,
                S.Location AS ExistingLocation,
                S.Status AS ExistingStatus,
                P.PackSpecID,
                P.StorageMethod
            FROM dbo.tbl_Lot L
            LEFT JOIN dbo.PP_WorkOrder W
                ON W.WoID = L.WoID
            LEFT JOIN dbo.MD_Item I
                ON I.ItemNo = COALESCE(NULLIF(L.ItemNo, ''), W.ItemNo)
            OUTER APPLY
            (
                SELECT TOP (1) QI.InspectionID, QI.InspectionNo, QI.CustomerCode, QI.BatchQty, QI.InsEndTS
                FROM dbo.QC_Inspection QI
                WHERE (QI.LotID = L.LotID OR (L.WoID IS NOT NULL AND QI.WoID = L.WoID))
                  AND UPPER(ISNULL(QI.Verdict, '')) IN ('PASS', 'PASSED', 'OK')
                ORDER BY QI.InsEndTS DESC, QI.InspectionID DESC
            ) Q
            OUTER APPLY
            (
                SELECT TOP (1) FS.StockID, FS.CustomerCode, FS.Location, FS.Status
                FROM dbo.FG_Inventory FS WITH (UPDLOCK, HOLDLOCK)
                WHERE (FS.LotID = L.LotID OR (L.WoID IS NOT NULL AND FS.WoID = L.WoID))
                  AND UPPER(ISNULL(FS.Status, '')) NOT IN ('CANCELED', 'CANCELLED')
                ORDER BY FS.StockTS DESC, FS.StockID DESC
            ) S
            OUTER APPLY
            (
                SELECT TOP (1)
                    PS.PackSpecID,
                    UPPER(ISNULL(NULLIF(PS.PackType, ''), 'LOCATION')) AS StorageMethod
                FROM dbo.MD_PackagingSpec PS
                WHERE PS.ItemID = COALESCE(NULLIF(L.ItemNo, ''), W.ItemNo)
                  AND UPPER(ISNULL(PS.Status, 'ACTIVE')) IN ('ACTIVE', 'USE', 'Y')
                ORDER BY
                    CASE UPPER(ISNULL(PS.PackType, ''))
                        WHEN 'PALLET' THEN 0
                        WHEN 'BOX' THEN 1
                        WHEN 'RACK' THEN 2
                        ELSE 3
                    END,
                    PS.PackSpecID
            ) P
            WHERE UPPER(L.LotCode) = UPPER(@Barcode)
               OR UPPER(ISNULL(W.WoNumber, '')) = UPPER(@Barcode)
            ORDER BY CASE WHEN UPPER(L.LotCode) = UPPER(@Barcode) THEN 0 ELSE 1 END,
                     L.ProducedAt DESC,
                     L.LotID DESC;
            """, conn, tx);
        cmd.Parameters.Add("@Barcode", SqlDbType.NVarChar, 80).Value = barcode;

        using var rdr = cmd.ExecuteReader();
        if (!rdr.Read()) return null;

        var existingStockId = GetInt(rdr, "ExistingStockId");
        var existingLocation = GetString(rdr, "ExistingLocation");
        var isQcPassed = GetBool(rdr, "IsQcPassed");
        var lotNo = GetString(rdr, "LotCode") ?? barcode;
        var woNumber = GetString(rdr, "WoNumber");
        var barcodeType = string.Equals(lotNo, barcode, StringComparison.OrdinalIgnoreCase)
            ? BarcodeLot
            : BarcodeWo;
        var storageMethod = NormalizeStorageMethod(GetString(rdr, "StorageMethod"));
        var nextScanType = NextScanTypeForStorage(storageMethod);
        var nextScanLabel = NextScanLabel(nextScanType);
        var message = !isQcPassed
            ? "QC PASS is required before FG Put-Away."
            : existingStockId.HasValue
                ? $"This FG LOT is already stocked at {ValueOrDash(existingLocation)}."
                : $"QC PASS matched. Scan {BarcodeKindLabel(nextScanType)}.";

        return new PutAwayScanRow(
            GetInt(rdr, "LotID"),
            lotNo,
            GetInt(rdr, "WoID"),
            woNumber,
            GetString(rdr, "ItemNo") ?? "",
            GetString(rdr, "ItemName"),
            GetString(rdr, "CustomerCode"),
            GetDecimal(rdr, "Qty"),
            GetString(rdr, "Unit"),
            GetDate(rdr, "MfgDate"),
            GetDate(rdr, "ExpiryDate"),
            GetString(rdr, "QcInspectionNo"),
            GetDate(rdr, "QcPassTs"),
            isQcPassed,
            existingStockId.HasValue,
            existingStockId,
            existingLocation,
            GetString(rdr, "ExistingStatus"),
            barcodeType,
            storageMethod,
            nextScanType,
            nextScanLabel,
            GetString(rdr, "PackSpecID"),
            message);
    }

    private static PutAwayLocationRow? SuggestPutAwayLocation(SqlConnection conn, SqlTransaction? tx, string itemNo, string? customerCode, decimal qty)
    {
        using var cmd = new SqlCommand("""
            WITH LocationBase AS
            (
                SELECT L.LocationID, L.LocationName, L.ZoneCode, L.Aisle, L.Bay, L.Slot,
                       CAST(ISNULL(NULLIF(L.Capacity, 0), 999999) AS DECIMAL(14,3)) AS Capacity,
                       L.LocationType,
                       CASE WHEN UPPER(ISNULL(L.LocationType, '')) IN ('FG', 'FINISHED_GOODS', 'FINISHED GOODS')
                                 OR UPPER(L.LocationID) LIKE 'FG%' THEN 1 ELSE 0 END AS IsFgLocation
                FROM dbo.MD_Location L
                WHERE ISNULL(L.ActiveFlag, 1) = 1
            ),
            HasFg AS
            (
                SELECT CASE WHEN EXISTS (SELECT 1 FROM LocationBase WHERE IsFgLocation = 1) THEN 1 ELSE 0 END AS Value
            ),
            StockByLocation AS
            (
                SELECT FS.Location,
                       CAST(SUM(ISNULL(FS.Qty, 0)) AS DECIMAL(14,3)) AS CurrentQty,
                       CASE
                           WHEN COUNT(DISTINCT NULLIF(FS.CustomerCode, '')) = 0 THEN NULL
                           WHEN COUNT(DISTINCT NULLIF(FS.CustomerCode, '')) = 1 THEN MAX(NULLIF(FS.CustomerCode, ''))
                           ELSE 'MIXED'
                       END AS CurrentCustomerCode
                FROM dbo.FG_Inventory FS
                WHERE UPPER(ISNULL(FS.Status, '')) IN ('AVAILABLE', 'RESERVED', 'HOLD')
                GROUP BY FS.Location
            )
            SELECT TOP (1)
                   L.LocationID, L.LocationName, L.ZoneCode, L.Aisle, L.Bay, L.Slot,
                   L.Capacity,
                   CAST(ISNULL(S.CurrentQty, 0) AS DECIMAL(14,3)) AS CurrentQty,
                   CAST(L.Capacity - ISNULL(S.CurrentQty, 0) AS DECIMAL(14,3)) AS AvailableQty,
                   S.CurrentCustomerCode
            FROM LocationBase L
            CROSS JOIN HasFg H
            LEFT JOIN StockByLocation S
                ON S.Location = L.LocationID
            WHERE (H.Value = 0 OR L.IsFgLocation = 1)
              AND (S.CurrentCustomerCode IS NULL OR S.CurrentCustomerCode = @CustomerCode OR NULLIF(@CustomerCode, '') IS NULL)
              AND L.Capacity - ISNULL(S.CurrentQty, 0) >= @Qty
            ORDER BY
                CASE WHEN S.CurrentCustomerCode = @CustomerCode THEN 0 WHEN S.CurrentCustomerCode IS NULL THEN 1 ELSE 2 END,
                L.Capacity - ISNULL(S.CurrentQty, 0) DESC,
                L.LocationID;
            """, conn, tx);
        cmd.Parameters.Add("@ItemNo", SqlDbType.NVarChar, 40).Value = itemNo;
        AddNullable(cmd, "@CustomerCode", SqlDbType.NVarChar, 40, customerCode);
        AddDecimal(cmd, "@Qty", qty);

        using var rdr = cmd.ExecuteReader();
        return rdr.Read() ? ReadPutAwayLocation(rdr, customerCode, qty) : null;
    }

    private static PutAwayLocationRow? ValidatePutAwayLocation(SqlConnection conn, SqlTransaction? tx, string locationId, string itemNo, string? customerCode, decimal qty, string? expectedScanType)
    {
        var parsed = ParseFgBarcode(locationId);
        var expected = NormalizeScanType(expectedScanType);
        var actual = parsed.Kind == BarcodeUnknown && expected == BarcodeLocation
            ? BarcodeLocation
            : parsed.Kind;

        if (IsStorageBarcode(parsed.Kind) && actual != expected)
        {
            return new PutAwayLocationRow(parsed.Value, null, null, null, null, null, 0, 0, 0, null, false,
                $"Scan {BarcodeKindLabel(expected)}. You scanned {BarcodeKindLabel(actual)}.",
                actual, parsed.Raw);
        }

        if (expected != BarcodeLocation && actual != expected)
        {
            return new PutAwayLocationRow(parsed.Value, null, null, null, null, null, 0, 0, 0, null, false,
                $"Scan {BarcodeKindLabel(expected)}. This barcode is not tagged as {BarcodeKindLabel(expected)}.",
                actual, parsed.Raw);
        }

        using var cmd = new SqlCommand("""
            WITH StockByLocation AS
            (
                SELECT FS.Location,
                       CAST(SUM(ISNULL(FS.Qty, 0)) AS DECIMAL(14,3)) AS CurrentQty,
                       CASE
                           WHEN COUNT(DISTINCT NULLIF(FS.CustomerCode, '')) = 0 THEN NULL
                           WHEN COUNT(DISTINCT NULLIF(FS.CustomerCode, '')) = 1 THEN MAX(NULLIF(FS.CustomerCode, ''))
                           ELSE 'MIXED'
                       END AS CurrentCustomerCode
                FROM dbo.FG_Inventory FS
                WHERE UPPER(ISNULL(FS.Status, '')) IN ('AVAILABLE', 'RESERVED', 'HOLD')
                GROUP BY FS.Location
            )
            SELECT L.LocationID, L.LocationName, L.ZoneCode, L.Aisle, L.Bay, L.Slot,
                   CAST(ISNULL(NULLIF(L.Capacity, 0), 999999) AS DECIMAL(14,3)) AS Capacity,
                   CAST(ISNULL(S.CurrentQty, 0) AS DECIMAL(14,3)) AS CurrentQty,
                   CAST(ISNULL(NULLIF(L.Capacity, 0), 999999) - ISNULL(S.CurrentQty, 0) AS DECIMAL(14,3)) AS AvailableQty,
                   S.CurrentCustomerCode,
                   CASE WHEN UPPER(ISNULL(L.LocationType, '')) IN ('FG', 'FINISHED_GOODS', 'FINISHED GOODS')
                             OR UPPER(L.LocationID) LIKE 'FG%' THEN 1 ELSE 0 END AS IsFgLocation
            FROM dbo.MD_Location L
            LEFT JOIN StockByLocation S
                ON S.Location = L.LocationID
            WHERE UPPER(L.LocationID) = UPPER(@LocationID)
              AND ISNULL(L.ActiveFlag, 1) = 1;
            """, conn, tx);
        cmd.Parameters.Add("@LocationID", SqlDbType.NVarChar, 80).Value = parsed.Value;

        using var rdr = cmd.ExecuteReader();
        if (!rdr.Read()) return null;

        var row = ReadPutAwayLocation(rdr, customerCode, qty, expected, parsed.Raw);
        if (!GetBool(rdr, "IsFgLocation"))
            return row with { IsValid = false, Message = "Scan an FG warehouse location.", ScanType = expected, ScannedBarcode = parsed.Raw };
        if (string.Equals(row.CurrentCustomerCode, "MIXED", StringComparison.OrdinalIgnoreCase))
            return row with { IsValid = false, Message = "Location already contains mixed customer stock.", ScanType = expected, ScannedBarcode = parsed.Raw };
        if (!string.IsNullOrWhiteSpace(row.CurrentCustomerCode) &&
            !string.IsNullOrWhiteSpace(customerCode) &&
            !string.Equals(row.CurrentCustomerCode, customerCode, StringComparison.OrdinalIgnoreCase))
            return row with { IsValid = false, Message = $"Customer mix is blocked. Current customer is {row.CurrentCustomerCode}.", ScanType = expected, ScannedBarcode = parsed.Raw };
        if (row.AvailableQty < qty)
            return row with { IsValid = false, Message = $"Location capacity is short by {qty - row.AvailableQty:0.###}.", ScanType = expected, ScannedBarcode = parsed.Raw };

        return row with { IsValid = true, Message = $"{BarcodeKindLabel(expected)} ready for FG Put-Away.", ScanType = expected, ScannedBarcode = parsed.Raw };
    }

    private static (int PalletCount, int PalletQty) ResolvePalletSplit(SqlConnection conn, SqlTransaction tx, string itemNo, decimal qty, int? requestedPalletCount, int? requestedPalletQty)
    {
        var palletQty = requestedPalletQty.GetValueOrDefault();
        if (palletQty <= 0)
        {
            using var cmd = new SqlCommand("""
                SELECT TOP (1)
                       ISNULL(NULLIF(QtyPerInner, 0), 1)
                     * ISNULL(NULLIF(InnerPerOuter, 0), 1)
                     * ISNULL(NULLIF(OuterPerPallet, 0), 1) AS PalletQty
                FROM dbo.MD_PackagingSpec
                WHERE ItemID = @ItemNo
                  AND UPPER(ISNULL(Status, 'ACTIVE')) IN ('ACTIVE', 'USE', 'Y')
                ORDER BY PackSpecID;
                """, conn, tx);
            cmd.Parameters.Add("@ItemNo", SqlDbType.NVarChar, 40).Value = itemNo;
            var value = cmd.ExecuteScalar();
            palletQty = value == null || value == DBNull.Value ? 0 : Convert.ToInt32(value);
        }

        if (palletQty <= 0)
            palletQty = Math.Max(1, Convert.ToInt32(Math.Ceiling(qty)));

        var palletCount = requestedPalletCount.GetValueOrDefault();
        if (palletCount <= 0)
            palletCount = Math.Max(1, Convert.ToInt32(Math.Ceiling(qty / palletQty)));

        return (palletCount, palletQty);
    }

    private static int InsertPutAwayStock(SqlConnection conn, SqlTransaction tx, PutAwayScanRow row, PutAwayLocationRow location,
        string? suggestedLocation, string? overrideReason, int palletCount, int palletQty, string operatorId,
        string storageMethod, string containerType, string containerBarcode)
    {
        int stockId;
        using (var cmd = new SqlCommand("""
            INSERT INTO dbo.FG_Inventory
                (StockNumber, FgTriggerID, WoID, ItemNo, LotID, CustomerCode, Qty, Location,
                 Status, HoldFlag, StockTS, CreatedBy, CreatedTS)
            OUTPUT INSERTED.StockID
            VALUES (CONCAT('FG-', FORMAT(SYSDATETIME(), 'yyyyMMdd-HHmmss')),
                    NULL, @WoID, @ItemNo, @LotID, @CustomerCode, @Qty, @Location,
                    'Available', 0, SYSDATETIME(), @OperatorID, SYSDATETIME());
            """, conn, tx))
        {
            AddNullable(cmd, "@WoID", SqlDbType.Int, row.WoId);
            cmd.Parameters.Add("@ItemNo", SqlDbType.NVarChar, 40).Value = row.ItemNo;
            AddNullable(cmd, "@LotID", SqlDbType.Int, row.LotId);
            AddNullable(cmd, "@CustomerCode", SqlDbType.NVarChar, 40, row.CustomerCode);
            AddDecimal(cmd, "@Qty", row.Qty);
            cmd.Parameters.Add("@Location", SqlDbType.NVarChar, 80).Value = location.LocationId;
            cmd.Parameters.Add("@OperatorID", SqlDbType.NVarChar, 450).Value = operatorId;
            stockId = Convert.ToInt32(cmd.ExecuteScalar());
        }

        var hasContainerColumns = TableHasColumn(conn, tx, "FG_PutAway", "ContainerBarcode");
        var putAwaySql = hasContainerColumns
            ? """
              INSERT INTO dbo.FG_PutAway
                  (StockID, WoID, ItemNo, Qty, SuggestedLoc, ActualLoc, LocOverrideReason,
                   PalletCount, PalletQty, StorageMethod, ContainerType, ContainerBarcode,
                   LabelPrintedTS, OperatorID, Status, CreatedBy, CreatedTS)
              VALUES (@StockID, @WoID, @ItemNo, @Qty, @SuggestedLoc, @ActualLoc, @OverrideReason,
                      @PalletCount, @PalletQty, @StorageMethod, @ContainerType, @ContainerBarcode,
                      SYSDATETIME(), @OperatorID, 'Confirmed', @OperatorID, SYSDATETIME());
              """
            : """
              INSERT INTO dbo.FG_PutAway
                  (StockID, WoID, ItemNo, Qty, SuggestedLoc, ActualLoc, LocOverrideReason,
                   PalletCount, PalletQty, LabelPrintedTS, OperatorID, Status, CreatedBy, CreatedTS)
              VALUES (@StockID, @WoID, @ItemNo, @Qty, @SuggestedLoc, @ActualLoc, @OverrideReason,
                      @PalletCount, @PalletQty, SYSDATETIME(), @OperatorID, 'Confirmed', @OperatorID, SYSDATETIME());
              """;

        using (var cmd = new SqlCommand(putAwaySql, conn, tx))
        {
            cmd.Parameters.Add("@StockID", SqlDbType.Int).Value = stockId;
            AddNullable(cmd, "@WoID", SqlDbType.Int, row.WoId);
            cmd.Parameters.Add("@ItemNo", SqlDbType.NVarChar, 40).Value = row.ItemNo;
            AddDecimal(cmd, "@Qty", row.Qty);
            AddNullable(cmd, "@SuggestedLoc", SqlDbType.NVarChar, 80, suggestedLocation);
            cmd.Parameters.Add("@ActualLoc", SqlDbType.NVarChar, 80).Value = location.LocationId;
            AddNullable(cmd, "@OverrideReason", SqlDbType.NVarChar, 120, overrideReason);
            cmd.Parameters.Add("@PalletCount", SqlDbType.Int).Value = palletCount;
            cmd.Parameters.Add("@PalletQty", SqlDbType.Int).Value = palletQty;
            cmd.Parameters.Add("@OperatorID", SqlDbType.NVarChar, 450).Value = operatorId;
            if (hasContainerColumns)
            {
                cmd.Parameters.Add("@StorageMethod", SqlDbType.NVarChar, 20).Value = storageMethod;
                cmd.Parameters.Add("@ContainerType", SqlDbType.NVarChar, 20).Value = containerType;
                cmd.Parameters.Add("@ContainerBarcode", SqlDbType.NVarChar, 80).Value = containerBarcode;
            }
            cmd.ExecuteNonQuery();
        }

        if (row.LotId.HasValue)
        {
            using var cmd = new SqlCommand("""
                UPDATE dbo.tbl_Lot
                   SET CurrentLocationID = @Location,
                       Status = 'Stocked',
                       QualityFlag = 'PASS',
                       ModifiedBy = @OperatorID,
                       ModifiedTS = SYSDATETIME()
                 WHERE LotID = @LotID;
                """, conn, tx);
            cmd.Parameters.Add("@Location", SqlDbType.NVarChar, 80).Value = location.LocationId;
            cmd.Parameters.Add("@OperatorID", SqlDbType.NVarChar, 450).Value = operatorId;
            cmd.Parameters.Add("@LotID", SqlDbType.Int).Value = row.LotId.Value;
            cmd.ExecuteNonQuery();
        }

        if (row.WoId.HasValue)
        {
            using var cmd = new SqlCommand("""
                UPDATE dbo.PP_WorkOrder
                   SET Status = 'Stocked',
                       ModifiedBy = @OperatorID,
                       ModifiedTS = SYSDATETIME()
                 WHERE WoID = @WoID;
                """, conn, tx);
            cmd.Parameters.Add("@OperatorID", SqlDbType.NVarChar, 450).Value = operatorId;
            cmd.Parameters.Add("@WoID", SqlDbType.Int).Value = row.WoId.Value;
            cmd.ExecuteNonQuery();
        }

        return stockId;
    }

    private static PutAwayLocationRow ReadPutAwayLocation(SqlDataReader rdr, string? customerCode, decimal qty,
        string scanType = BarcodeLocation, string? scannedBarcode = null)
    {
        var capacity = GetDecimal(rdr, "Capacity");
        var currentQty = GetDecimal(rdr, "CurrentQty");
        var availableQty = GetDecimal(rdr, "AvailableQty");
        var currentCustomer = GetString(rdr, "CurrentCustomerCode");
        var valid = availableQty >= qty &&
                    (string.IsNullOrWhiteSpace(currentCustomer) ||
                     string.IsNullOrWhiteSpace(customerCode) ||
                     string.Equals(currentCustomer, customerCode, StringComparison.OrdinalIgnoreCase));

        return new PutAwayLocationRow(
            GetString(rdr, "LocationID") ?? "",
            GetString(rdr, "LocationName"),
            GetString(rdr, "ZoneCode"),
            GetString(rdr, "Aisle"),
            GetString(rdr, "Bay"),
            GetString(rdr, "Slot"),
            capacity,
            currentQty,
            availableQty,
            currentCustomer,
            valid,
            valid ? $"{BarcodeKindLabel(scanType)} ready for FG Put-Away." : "Location cannot accept this FG LOT.",
            scanType,
            scannedBarcode ?? GetString(rdr, "LocationID") ?? "");
    }

    private static ParsedFgBarcode ParseFgBarcode(string? barcode)
    {
        var raw = (barcode ?? "")
            .Replace("\u0002", "")
            .Replace("\u0003", "")
            .Trim();

        if (string.IsNullOrWhiteSpace(raw))
            return new ParsedFgBarcode("", "", BarcodeUnknown);

        var parts = raw.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 2)
        {
            var prefix = parts[0].Trim().ToUpperInvariant();
            var value = parts[1].Trim();
            var kind = prefix switch
            {
                "FGLOT" or "LOT" => BarcodeLot,
                "FGWO" or "WO" or "WORKORDER" => BarcodeWo,
                "FGLOC" or "LOC" or "LOCATION" => BarcodeLocation,
                "FGBOX" or "BOX" => BarcodeBox,
                "FGPAL" or "PAL" or "PALLET" => BarcodePallet,
                "FGRACK" or "RACK" => BarcodeRack,
                _ => BarcodeUnknown
            };

            if (kind != BarcodeUnknown && !string.IsNullOrWhiteSpace(value))
                return new ParsedFgBarcode(raw, value, kind);
        }

        if (raw.Contains('\u001d'))
        {
            var gsLot = raw.Split('\u001d', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(x => x.StartsWith("T", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(gsLot) && gsLot.Length > 12)
                return new ParsedFgBarcode(raw, gsLot[12..].Trim(), BarcodeLot);
        }

        if (raw.StartsWith("WO-", StringComparison.OrdinalIgnoreCase))
            return new ParsedFgBarcode(raw, raw, BarcodeWo);
        if (raw.StartsWith("LOT-", StringComparison.OrdinalIgnoreCase))
            return new ParsedFgBarcode(raw, raw, BarcodeLot);
        if (raw.StartsWith("FG-", StringComparison.OrdinalIgnoreCase))
            return new ParsedFgBarcode(raw, raw, BarcodeLocation);

        return new ParsedFgBarcode(raw, raw, BarcodeUnknown);
    }

    private static bool IsStorageBarcode(string? kind)
        => string.Equals(kind, BarcodeLocation, StringComparison.OrdinalIgnoreCase)
           || string.Equals(kind, BarcodeBox, StringComparison.OrdinalIgnoreCase)
           || string.Equals(kind, BarcodePallet, StringComparison.OrdinalIgnoreCase)
           || string.Equals(kind, BarcodeRack, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeStorageMethod(string? storageMethod)
    {
        var value = (storageMethod ?? "").Trim().ToUpperInvariant().Replace(" ", "_");
        return value switch
        {
            "BOX" or "CASE" or "TRAY" => BarcodeBox,
            "PALLET" or "PALLETE" or "PALETTE" => BarcodePallet,
            "RACK" or "RACK_LOCATION" => BarcodeRack,
            "LOCATION" or "LOCATION_ONLY" or "LOC" => BarcodeLocation,
            _ => BarcodeLocation
        };
    }

    private static string NormalizeScanType(string? scanType)
    {
        var value = (scanType ?? "").Trim().ToUpperInvariant().Replace(" ", "_");
        return value switch
        {
            BarcodeBox => BarcodeBox,
            BarcodePallet => BarcodePallet,
            BarcodeRack => BarcodeRack,
            BarcodeLocation or "LOC" or "LOCATION_ONLY" => BarcodeLocation,
            _ => BarcodeLocation
        };
    }

    private static string NextScanTypeForStorage(string storageMethod)
        => NormalizeScanType(storageMethod);

    private static string NextScanLabel(string scanType)
        => NormalizeScanType(scanType) switch
        {
            BarcodeBox => "BOX NO SCAN",
            BarcodePallet => "PALLET NO SCAN",
            BarcodeRack => "RACK NO SCAN",
            _ => "LOCATION NO SCAN"
        };

    private static string BarcodeKindLabel(string? kind)
        => (kind ?? "").Trim().ToUpperInvariant().Replace(" ", "_") switch
        {
            BarcodeBox => "Box barcode",
            BarcodePallet => "Pallet barcode",
            BarcodeRack => "Rack barcode",
            BarcodeLocation => "Location barcode",
            BarcodeLot => "FG LOT barcode",
            BarcodeWo => "WO barcode",
            _ => "FG LOT or WO"
        };

    private static bool TableHasColumn(SqlConnection conn, SqlTransaction? tx, string tableName, string columnName)
    {
        using var cmd = new SqlCommand("""
            SELECT CASE WHEN EXISTS
            (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(@TableName)
                  AND name = @ColumnName
            ) THEN 1 ELSE 0 END;
            """, conn, tx);
        cmd.Parameters.Add("@TableName", SqlDbType.NVarChar, 128).Value = $"dbo.{tableName}";
        cmd.Parameters.Add("@ColumnName", SqlDbType.NVarChar, 128).Value = columnName;
        return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
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

    private static DateTime? GetDate(SqlDataReader rdr, string name)
    {
        if (!HasColumn(rdr, name)) return null;
        var value = rdr[name];
        if (value == DBNull.Value) return null;
        if (value is DateTime dt) return dt;
        return DateTime.TryParse(Convert.ToString(value), out var parsed) ? parsed : null;
    }

    private static bool GetBool(SqlDataReader rdr, string name)
    {
        if (!HasColumn(rdr, name)) return false;
        var value = rdr[name];
        if (value == DBNull.Value) return false;
        return value switch
        {
            bool b => b,
            byte b => b != 0,
            short s => s != 0,
            int i => i != 0,
            long l => l != 0,
            _ => bool.TryParse(Convert.ToString(value), out var parsed) && parsed
        };
    }

    private static string ValueOrDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

    private static void AddNullable(SqlCommand cmd, string name, SqlDbType type, int? value)
    {
        var p = cmd.Parameters.Add(name, type);
        p.Value = value.HasValue ? value.Value : DBNull.Value;
    }

    private static void AddNullable(SqlCommand cmd, string name, SqlDbType type, int size, string? value)
    {
        var p = cmd.Parameters.Add(name, type, size);
        p.Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    }

    private static void AddDecimal(SqlCommand cmd, string name, decimal value)
    {
        var p = cmd.Parameters.Add(name, SqlDbType.Decimal);
        p.Precision = 14;
        p.Scale = 3;
        p.Value = value;
    }
}
