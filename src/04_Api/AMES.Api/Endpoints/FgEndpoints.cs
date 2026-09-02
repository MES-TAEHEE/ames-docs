using AMES.Api.Auth;
using AMES.Api.Logging;
using AMES.Data.Connection;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Text.Json;

namespace AMES.Api.Endpoints;

public static class FgEndpoints
{
    private const string PdaAdjustScanStockProcedure = "FG_PDA_ADJUST_SCAN_STOCK";
    private const string PdaAdjustSaveQtyProcedure = "FG_PDA_ADJUST_SAVE_QTY";

    // ── DTOs ─────────────────────────────────────────────────────────────
    public sealed record StockRow(int StockId, string? StockNumber, string ItemNo, string? ItemName,
        int? LotId, string? LotNo, string? CustomerCode, decimal Qty, string? Unit,
        string? Location, string? Status, DateTime? StockTs);
    public sealed record OrderRow(int ShipmentOrderId, string? ShipOrderNumber, string? CustomerCode,
        string? CustomerPo, DateTime? ShipDate, string? CarrierCode, string? DestPlant, string? Status, int LineCount);
    public sealed record OrderLineRow(int ShipmentOrderLineId, int ShipmentOrderId, int LineSeq,
        string ItemNo, string? ItemName, decimal OrderedQty, decimal AllocatedQty,
        int? StockId, string? LotNo, string? Location, string? ReservationStatus);
    public sealed record HistoryRow(int LoadingId, string? LoadingNumber, int? ShipmentOrderId,
        string? ShipOrderNumber, string? CustomerCode, string? LicensePlate, string? DriverName,
        DateTime? DepartureTs, string? OTDStatus);
    public sealed record DashboardDto(int OpenOrders, int ReadyToShip, int InTransit, int DeliveredToday,
        int PendingReturns, decimal StockOnHand);
    public sealed record QcCompletedRow(int LotId, string LotNo, string? WoNumber, string ItemNo,
        string? ItemName, string? CustomerCode, decimal Qty, string? Unit, DateTime? ProducedAt,
        DateTime? QcPassTs);
    public sealed record ReturnRow(int ReturnId, string? ReturnNumber, string? CustomerCode,
        string? ItemNo, decimal Qty, string? ReturnReason, string? Status, DateTime? ReceivedAt);
    public sealed record ReturnScanRow(string Barcode, string? StockNumber, string? LotNo,
        int ShipmentOrderId, string? ShipOrderNumber, string CustomerCode,
        string ItemNo, string? ItemName, DateTime ShippedAt);
    public sealed record ReturnResult(bool Success, string Message, int? ReturnId, ReturnScanRow? Row);
    public sealed record AdjustScanRow(string ReceiveType, string? Yn, string LotNo, string Barcode,
        string? SourceTable, string? NoteNo, string? CaseBarcode, string? CaseNo, string? InvoiceNo,
        string? ContainerNo, string? PartNo, string? PartName, decimal Qty, string? Unit, string? PoNo,
        int? PoSeq, string? VendorId, string? VendorName, DateTime? ProductionDate, DateTime? DeliveryDate,
        DateTime? ArrivalDate, DateTime? ShipDate, DateTime? PackDate, string? ReceivedLocation,
        string? ReceivedStatus);
    public sealed record AdjustSaveReq(string? Mode, string Barcode, decimal DeltaQty, string ReasonCode,
        string? ReasonNote, string SupervisorPin, string? SupervisorEmployeeNo = null);
    public sealed record AdjustResult(bool Success, string Message, AdjustScanRow? Row);

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
    public sealed record LoadingTruckRow(string Barcode, string LicensePlate, bool Ready, string Message);
    public sealed record LoadingItemRow(int StockId, int ShipmentOrderLineId, int ShipmentOrderId,
        string ShipOrderNumber, string CustomerCode, string ItemNo, string? ItemName,
        string? LotNo, string? StockNumber, decimal Qty, string? Unit, string? Location);
    public sealed record LoadingOrderRow(int ShipmentOrderId, string Barcode, string ShipOrderNumber,
        string CustomerCode, DateTime? ShipDate, string? Destination, List<LoadingItemRow> Items);
    public sealed record LoadingOrderResult(bool Success, string Message, LoadingOrderRow? Order);
    public sealed record LoadingReq(string TruckBarcode, int ShipmentOrderId, List<int> StockIds);
    public sealed record LoadingResult(bool Success, string Message, int? LoadingId,
        LoadingTruckRow? Truck, LoadingItemRow? Item);
    public sealed record DeliveryReq(int ShipmentOrderId, int? LoadingId);
    public sealed record DayEndReq(string CloseMode, string? Note);
    public sealed record ReturnReq(string Barcode, string ReturnReason);

    private const string BarcodeLot = "LOT";
    private const string BarcodeWo = "WORK_ORDER";
    private const string BarcodeLocation = "LOCATION";
    private const string BarcodeBox = "BOX";
    private const string BarcodePallet = "PALLET";
    private const string BarcodeRack = "RACK";
    private const string BarcodeUnknown = "UNKNOWN";
    private static readonly string[] ReturnReasons =
        ["Defect", "Wrong item", "Damaged in transit", "Customer change", "Other"];

    private sealed record ParsedFgBarcode(string Raw, string Value, string Kind);

    // ── Routes ───────────────────────────────────────────────────────────
    public static void MapFg(this WebApplication app, AmesConnectionFactory factory)
    {
        var g = app.MapGroup("/api/fg").WithTags("Finished Goods");

        g.MapGet("/adjust/scan", (HttpContext ctx, string scanText) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();

            try
            {
                var row = ExecuteAdjustScan(factory, scanText);
                WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                    s, "SCAN_STOCK", "FG007", "LOT", scanText, "SUCCESS", "Finished goods adjustment stock scanned",
                    lotNo: row?.LotNo, partNo: row?.PartNo, locationId: row?.ReceivedLocation, qty: row?.Qty));
                return Results.Ok(row);
            }
            catch (Exception ex)
            {
                var message = AdjustMessage(ex);
                var isValidationError = ex is SqlException sqlEx
                    && sqlEx.Errors.Count > 0
                    && sqlEx.Errors[0].Number >= 51600
                    && sqlEx.Errors[0].Number < 51700;
                WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                    s, "SCAN_STOCK", "FG007", "LOT", scanText, "FAIL", message, lotNo: scanText));
                return Results.Problem(message, statusCode: isValidationError
                    ? StatusCodes.Status400BadRequest
                    : StatusCodes.Status503ServiceUnavailable);
            }
        });

        g.MapPost("/adjust/save", (HttpContext ctx, AdjustSaveReq body) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();

            var result = ExecuteAdjustSave(factory, body, s.EmployeeNo);
            WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                s, "ADJUST_SAVE", "FG007", "LOT", body.Barcode,
                result.Success ? "SUCCESS" : "FAIL", result.Message,
                lotNo: result.Row?.LotNo ?? body.Barcode, partNo: result.Row?.PartNo,
                locationId: result.Row?.ReceivedLocation, qty: body.DeltaQty));
            return Results.Ok(result);
        });

        // FG-01 QC Complete List - passed FG LOTs waiting for Put-Away.
        g.MapGet("/qc-completed", (HttpContext ctx) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            const string sql = """
                SELECT TOP 100
                    L.LotID,
                    L.LotCode AS LotNo,
                    W.WoNumber,
                    COALESCE(NULLIF(L.ItemNo, ''), W.ItemNo) AS ItemNo,
                    I.ItemName,
                    Q.CustomerCode,
                    CAST(COALESCE(NULLIF(Q.BatchQty, 0), NULLIF(L.RemainingQty, 0),
                         NULLIF(L.BatchSize, 0), NULLIF(W.CompletedQty, 0), 0) AS DECIMAL(14,3)) AS Qty,
                    I.DefaultUOM AS Unit,
                    L.ProducedAt,
                    Q.InsEndTS AS QcPassTs
                FROM dbo.tbl_Lot L
                LEFT JOIN dbo.PP_WorkOrder W ON W.WoID = L.WoID
                LEFT JOIN dbo.MD_Item I ON I.ItemNo = COALESCE(NULLIF(L.ItemNo, ''), W.ItemNo)
                CROSS APPLY
                (
                    SELECT TOP (1) QI.CustomerCode, QI.BatchQty, QI.InsEndTS
                    FROM dbo.QC_Inspection QI
                    WHERE (QI.LotID = L.LotID OR (L.WoID IS NOT NULL AND QI.WoID = L.WoID))
                      AND UPPER(ISNULL(QI.Verdict, '')) IN ('PASS', 'PASSED', 'OK')
                    ORDER BY QI.InsEndTS DESC, QI.InspectionID DESC
                ) Q
                WHERE NOT EXISTS
                (
                    SELECT 1
                    FROM dbo.FG_Inventory S
                    WHERE (S.LotID = L.LotID OR (L.WoID IS NOT NULL AND S.WoID = L.WoID))
                      AND UPPER(ISNULL(S.Status, '')) NOT IN ('CANCELED', 'CANCELLED')
                )
                ORDER BY CASE WHEN Q.InsEndTS IS NULL THEN 1 ELSE 0 END,
                         Q.InsEndTS, L.ProducedAt, L.LotID;
                """;
            return Query(factory, sql, r => new QcCompletedRow(
                (int)r["LotID"], r["LotNo"] as string ?? "", r["WoNumber"] as string,
                r["ItemNo"] as string ?? "", r["ItemName"] as string, r["CustomerCode"] as string,
                r.GetDecimal(r.GetOrdinal("Qty")), r["Unit"] as string,
                r["ProducedAt"] as DateTime?, r["QcPassTs"] as DateTime?));
        });

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

        g.MapGet("/putaway/container", (HttpContext ctx, string storageMethod, string barcode) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            var method = storageMethod.Trim().ToUpperInvariant();
            var error = method == BarcodeLocation
                ? "Location Only does not require a container barcode."
                : ValidatePutAwayContainer(method, barcode);
            return error is null
                ? Results.Ok(new PutAwayResult(true, "Container scanned. Scan Location Barcode.", null, null, null))
                : Results.BadRequest(new PutAwayResult(false, error, null, null, null));
        });

        // FG-01 PDA Put-Away - validate scanned FG location.
        g.MapGet("/putaway/location", (HttpContext ctx, string locationId, string itemNo, string? customerCode, decimal qty, string? expectedScanType) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(locationId))
                return Results.BadRequest(new PutAwayResult(false, "Scan Location No first.", null, null, null));

            using var conn = factory.OpenConnection();
            var location = ValidatePutAwayLocation(conn, null, locationId.Trim(), itemNo, customerCode, qty, BarcodeLocation);
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

            var storageMethod = body.StorageMethod?.Trim().ToUpperInvariant() ?? "";
            var containerError = ValidatePutAwayContainer(storageMethod, body.ContainerBarcode);
            if (containerError is not null)
                return Results.BadRequest(new PutAwayResult(false, containerError, null, null, null));
            if (!string.IsNullOrWhiteSpace(body.ContainerType) &&
                !string.Equals(storageMethod, body.ContainerType, StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new PutAwayResult(false, "Container type must match Storage.", null, null, null));

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

                if (storageMethod != BarcodeLocation && !TableHasColumn(conn, tx, "FG_PutAway", "ContainerBarcode"))
                {
                    tx.Rollback();
                    return Results.BadRequest(new PutAwayResult(false, "Container storage is not configured. Apply the FG Put-Away database migration.", null, row, null));
                }

                var location = ValidatePutAwayLocation(conn, tx, body.LocationId.Trim(), row.ItemNo, row.CustomerCode, row.Qty, BarcodeLocation);
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
                var containerBarcode = storageMethod == BarcodeLocation ? "" : ParseFgBarcode(body.ContainerBarcode).Raw;
                var stockId = InsertPutAwayStock(conn, tx, row, location, body.SuggestedLocation, body.OverrideReason,
                    pack.PalletCount, pack.PalletQty, s.OperatorId, storageMethod, storageMethod, containerBarcode);

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
                       s.LotID, l.LotCode AS LotNo, s.CustomerCode, ISNULL(s.Qty,0) AS Qty,
                       m.DefaultUOM AS Unit,
                       s.Location, s.Status, s.StockTS
                FROM   dbo.FG_Inventory s
                LEFT JOIN dbo.MD_Item m ON m.ItemNo = s.ItemNo
                LEFT JOIN dbo.tbl_Lot l ON l.LotID = s.LotID
                WHERE  (@Q = ''
                    OR s.ItemNo LIKE '%' + @Q + '%'
                    OR m.ItemName LIKE '%' + @Q + '%'
                    OR l.LotCode LIKE '%' + @Q + '%'
                    OR s.Location LIKE '%' + @Q + '%')
                ORDER BY s.StockTS DESC;
                """;
            return QueryWithParam(factory, sql, "@Q", q ?? "", r => new StockRow(
                (int)r["StockID"], r["StockNumber"] as string,
                r["ItemNo"] as string ?? "", r["ItemName"] as string,
                r["LotID"] as int?, r["LotNo"] as string, r["CustomerCode"] as string,
                r.GetDecimal(r.GetOrdinal("Qty")),
                r["Unit"] as string,
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

        g.MapGet("/orders/{shipOrderNumber}/lines", (HttpContext ctx, string shipOrderNumber) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            const string sql = """
                SELECT l.ShipmentOrderLineID, l.ShipmentOrderID, ISNULL(l.LineSeq, 0) AS LineSeq,
                       l.ItemNo, i.ItemName, ISNULL(l.OrderedQty, 0) AS OrderedQty,
                       ISNULL(l.AllocatedQty, 0) AS AllocatedQty, l.StockID,
                       lot.LotCode AS LotNo, l.Location, l.ReservationStatus
                FROM dbo.FG_ShipmentOrderLine l
                JOIN dbo.FG_ShipmentOrder o ON o.ShipmentOrderID = l.ShipmentOrderID
                LEFT JOIN dbo.MD_Item i ON i.ItemNo = l.ItemNo
                LEFT JOIN dbo.tbl_Lot lot ON lot.LotID = l.LotID
                WHERE o.ShipOrderNumber = @Q
                ORDER BY l.LineSeq, l.ShipmentOrderLineID;
                """;
            return QueryWithParam(factory, sql, "@Q", shipOrderNumber, r => new OrderLineRow(
                (int)r["ShipmentOrderLineID"], (int)r["ShipmentOrderID"], (int)r["LineSeq"],
                r["ItemNo"] as string ?? "", r["ItemName"] as string,
                r.GetDecimal(r.GetOrdinal("OrderedQty")), r.GetDecimal(r.GetOrdinal("AllocatedQty")),
                r["StockID"] as int?, r["LotNo"] as string, r["Location"] as string,
                r["ReservationStatus"] as string));
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

                UPDATE dbo.FG_ShipmentOrderLine
                SET ReservationStatus='Picked', AllocatedQty=@Q, ReservedAt=SYSDATETIME(),
                    ModifiedBy=@Op, ModifiedTS=SYSDATETIME()
                WHERE ShipmentOrderID=@So AND StockID=@Stk;

                UPDATE dbo.FG_ShipmentOrder
                SET Status='Picked', ModifiedBy=@Op, ModifiedTS=SYSDATETIME()
                WHERE ShipmentOrderID=@So
                  AND UPPER(ISNULL(Status,'')) NOT IN ('SHIPPED','CANCELLED','CLOSED');
                """, conn);
            cmd.Parameters.AddWithValue("@So",  body.ShipmentOrderId);
            cmd.Parameters.AddWithValue("@Stk", body.StockId);
            cmd.Parameters.AddWithValue("@Q",   body.Qty);
            cmd.Parameters.AddWithValue("@Op",  s.OperatorId);
            var id = (int)cmd.ExecuteScalar()!;
            return Results.Ok(new { PickId = id });
        });

        // FG-05 Truck and picked-product loading
        g.MapGet("/loading/order/scan", (HttpContext ctx, string barcode) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            if (!TryNormalizeLoadingOrderBarcode(barcode, out var orderNumber, out var error))
                return Results.BadRequest(new LoadingOrderResult(false, error, null));

            using var conn = factory.OpenConnection();
            using var cmd = new SqlCommand("""
                SELECT TOP (1)
                    ShipmentOrderID, ShipOrderNumber, CustomerCode, ShipDate, DestPlant, Status
                FROM dbo.FG_ShipmentOrder
                WHERE UPPER(ISNULL(ShipOrderNumber,''))=UPPER(@OrderNumber)
                ORDER BY ShipmentOrderID DESC;
                """, conn);
            cmd.Parameters.Add("@OrderNumber", SqlDbType.NVarChar, 40).Value = orderNumber;

            int orderId;
            string shipOrderNumber;
            string customerCode;
            DateTime? shipDate;
            string? destination;
            string orderStatus;
            using (var rdr = cmd.ExecuteReader())
            {
                if (!rdr.Read())
                    return Results.NotFound(new LoadingOrderResult(false,
                        "Shipment order barcode was not found.", null));
                orderId = GetInt(rdr, "ShipmentOrderID") ?? 0;
                shipOrderNumber = GetString(rdr, "ShipOrderNumber") ?? "";
                customerCode = GetString(rdr, "CustomerCode") ?? "";
                shipDate = GetDate(rdr, "ShipDate");
                destination = GetString(rdr, "DestPlant");
                orderStatus = (GetString(rdr, "Status") ?? "").ToUpperInvariant();
            }

            if (orderStatus is "SHIPPED" or "LOADED" or "CANCELLED" or "CLOSED")
                return Results.BadRequest(new LoadingOrderResult(false,
                    $"This shipment order is already {orderStatus.ToLowerInvariant()}.", null));
            if (orderStatus is not ("READY" or "PICKED" or "RELEASED"))
                return Results.BadRequest(new LoadingOrderResult(false,
                    "This shipment order is not ready for truck loading.", null));

            var items = ReadPickedLoadingItems(conn, null, orderId);
            if (items.Count == 0)
                return Results.BadRequest(new LoadingOrderResult(false,
                    "No picked products are assigned to this shipment order.", null));

            var row = new LoadingOrderRow(orderId, shipOrderNumber, shipOrderNumber,
                customerCode, shipDate, destination, items);
            return Results.Ok(new LoadingOrderResult(true,
                $"Shipment order loaded. Scan the truck for {items.Count} product(s).", row));
        });

        g.MapGet("/loading/truck/scan", (HttpContext ctx, string barcode) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            if (!TryNormalizeTruckBarcode(barcode, out var truck, out var error))
                return Results.BadRequest(new LoadingResult(false, error, null, null, null));

            using var conn = factory.OpenConnection();
            using var cmd = new SqlCommand("""
                SELECT TOP (1) LoadingNumber
                FROM dbo.FG_LoadingConfirm
                WHERE UPPER(ISNULL(LicensePlate,'')) = UPPER(@LicensePlate)
                  AND DepartureTS IS NULL
                ORDER BY LoadingID DESC;
                """, conn);
            cmd.Parameters.Add("@LicensePlate", SqlDbType.NVarChar, 20).Value = truck;
            var openLoading = Convert.ToString(cmd.ExecuteScalar());
            if (!string.IsNullOrWhiteSpace(openLoading))
            {
                return Results.BadRequest(new LoadingResult(false,
                    $"This truck already has open loading {openLoading}.", null, null, null));
            }

            var row = new LoadingTruckRow(truck, truck, true, "Truck is ready for loading.");
            return Results.Ok(new LoadingResult(true, row.Message, null, row, null));
        });

        g.MapGet("/loading/item/scan", (HttpContext ctx, string barcode, int? shipmentOrderId) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            using var conn = factory.OpenConnection();
            var validation = ValidateLoadingItem(conn, null, barcode, shipmentOrderId);
            return validation.Item is null
                ? Results.BadRequest(new LoadingResult(false, validation.Message, null, null, null))
                : Results.Ok(new LoadingResult(true, validation.Message, null, null, validation.Item));
        });

        g.MapPost("/loading", (HttpContext ctx, LoadingReq body) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();
            if (!TryNormalizeTruckBarcode(body.TruckBarcode, out var truck, out var truckError))
                return Results.BadRequest(new LoadingResult(false, truckError, null, null, null));
            if (body.ShipmentOrderId <= 0 || body.StockIds is null || body.StockIds.Count == 0)
                return Results.BadRequest(new LoadingResult(false,
                    "Scan a truck and at least one picked product.", null, null, null));
            if (body.StockIds.Count > 100 || body.StockIds.Distinct().Count() != body.StockIds.Count)
                return Results.BadRequest(new LoadingResult(false,
                    "The loading list contains duplicate or excessive product scans.", null, null, null));

            using var conn = factory.OpenConnection();
            using var tx = conn.BeginTransaction(IsolationLevel.Serializable);

            var expected = ReadPickedLoadingItems(conn, tx, body.ShipmentOrderId);
            if (expected.Count == 0)
            {
                tx.Rollback();
                return Results.BadRequest(new LoadingResult(false,
                    "No picked products are available for this shipment order.", null, null, null));
            }

            var expectedIds = expected.Select(x => x.StockId).OrderBy(x => x).ToArray();
            var scannedIds = body.StockIds.OrderBy(x => x).ToArray();
            if (!expectedIds.SequenceEqual(scannedIds))
            {
                tx.Rollback();
                var missing = expected.Count(x => !body.StockIds.Contains(x.StockId));
                return Results.BadRequest(new LoadingResult(false,
                    missing > 0
                        ? $"Scan all picked products before confirming. {missing} product(s) remain."
                        : "The loading list contains a product that is not assigned to this shipment order.",
                    null, null, null));
            }

            using (var duplicateCmd = new SqlCommand("""
                SELECT TOP (1) LoadingNumber
                FROM dbo.FG_LoadingConfirm WITH (UPDLOCK, HOLDLOCK)
                WHERE ShipmentOrderID=@So
                  AND UPPER(ISNULL(OTDStatus,'')) <> 'CANCELLED'
                ORDER BY LoadingID DESC;
                """, conn, tx))
            {
                duplicateCmd.Parameters.Add("@So", SqlDbType.Int).Value = body.ShipmentOrderId;
                var priorLoading = Convert.ToString(duplicateCmd.ExecuteScalar());
                if (!string.IsNullOrWhiteSpace(priorLoading))
                {
                    tx.Rollback();
                    return Results.BadRequest(new LoadingResult(false,
                        $"This shipment order was already loaded under {priorLoading}.", null, null, null));
                }
            }

            var loadedJson = JsonSerializer.Serialize(expected.Select(x => new
            {
                stockId = x.StockId,
                shipmentOrderLineId = x.ShipmentOrderLineId,
                itemNo = x.ItemNo,
                lotNo = x.LotNo,
                stockNumber = x.StockNumber,
                qty = x.Qty,
                unit = x.Unit,
                location = x.Location
            }));

            using var cmd  = new SqlCommand("""
                INSERT INTO dbo.FG_LoadingConfirm
                    (LoadingNumber, ShipmentOrderID, LicensePlate, CarrierCode,
                     DockNo, ArrivalTS, PalletsLoadedJSON, OTDStatus,
                     OperatorID, ConfirmedAt, CreatedBy, CreatedTS)
                OUTPUT INSERTED.LoadingID
                VALUES (CONCAT('LDG-', FORMAT(SYSDATETIME(),'yyMMddHHmmss')),
                        @So, @Lp,
                        (SELECT TOP (1) CarrierCode FROM dbo.FG_ShipmentOrder WHERE ShipmentOrderID=@So),
                        'PDA', SYSDATETIME(), @LoadedJson,
                        'Pending', @Op, SYSDATETIME(), 'pda', SYSDATETIME());

                UPDATE dbo.FG_ShipmentOrderLine
                SET ReservationStatus='Loaded', ReleasedAt=SYSDATETIME(),
                    ModifiedBy=@Op, ModifiedTS=SYSDATETIME()
                WHERE ShipmentOrderID=@So
                  AND StockID IN (SELECT TRY_CONVERT(int, [value]) FROM OPENJSON(@StockIds));

                UPDATE dbo.FG_Inventory
                SET Status='Loaded', ModifiedBy=@Op, ModifiedTS=SYSDATETIME()
                WHERE StockID IN (SELECT TRY_CONVERT(int, [value]) FROM OPENJSON(@StockIds));

                UPDATE dbo.FG_ShipmentOrder SET Status='Loaded', ModifiedBy=@Op, ModifiedTS=SYSDATETIME()
                WHERE  ShipmentOrderID = @So;
                """, conn, tx);
            cmd.Parameters.AddWithValue("@So", body.ShipmentOrderId);
            cmd.Parameters.AddWithValue("@Lp", truck);
            cmd.Parameters.Add("@LoadedJson", SqlDbType.NVarChar, -1).Value = loadedJson;
            cmd.Parameters.Add("@StockIds", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(body.StockIds);
            cmd.Parameters.AddWithValue("@Op", s.OperatorId);
            var id = (int)cmd.ExecuteScalar()!;
            tx.Commit();
            return Results.Ok(new LoadingResult(true,
                $"{expected.Count} product(s) were loaded onto truck {truck}.", id,
                new LoadingTruckRow(truck, truck, true, "Loading confirmed."), null));
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
        g.MapGet("/return/scan", (HttpContext ctx, string barcode) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();

            using var conn = factory.OpenConnection();
            var validation = ValidateReturnProduct(conn, null, barcode, lockReturnHistory: false);
            return validation.Product is null
                ? Results.BadRequest(new ReturnResult(false, validation.Message, null, null))
                : Results.Ok(new ReturnResult(true, "Product is eligible for customer return.", null, validation.Product.Row));
        });

        g.MapPost("/return", (HttpContext ctx, ReturnReq body) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();

            var returnReason = ReturnReasons.FirstOrDefault(x =>
                string.Equals(x, body.ReturnReason?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (returnReason is null)
            {
                return Results.BadRequest(new ReturnResult(false,
                    "Select a valid return reason.", null, null));
            }

            using var conn = factory.OpenConnection();
            using var tx = conn.BeginTransaction(IsolationLevel.Serializable);
            var validation = ValidateReturnProduct(conn, tx, body.Barcode, lockReturnHistory: true);
            if (validation.Product is null)
            {
                tx.Rollback();
                return Results.BadRequest(new ReturnResult(false,
                    validation.Message, null, null));
            }
            var product = validation.Product;

            using var cmd  = new SqlCommand("""
                INSERT INTO dbo.FG_CustomerReturn
                    (ReturnNumber, CustomerCode, OriginalShipmentOrderID,
                     ReturnReason, ItemsJSON, Status, ReceivedAt, ReceivedBy,
                     CapaTriggered, CreatedBy, CreatedTS)
                OUTPUT INSERTED.ReturnID
                VALUES (CONCAT('RMA-', FORMAT(SYSDATETIME(),'yyMMddHHmmss')),
                        @C, @So, @R,
                        (SELECT @I AS itemNo, @Lot AS lotNo, @Stock AS stockNumber,
                                @Barcode AS barcode, @Q AS qty FOR JSON PATH),
                        'Open', SYSDATETIME(), @By, 0,
                        'pda', SYSDATETIME());
                """, conn, tx);
            cmd.Parameters.AddWithValue("@C", product.Row.CustomerCode);
            cmd.Parameters.AddWithValue("@So", product.Row.ShipmentOrderId);
            cmd.Parameters.AddWithValue("@R", returnReason);
            cmd.Parameters.AddWithValue("@I", product.Row.ItemNo);
            cmd.Parameters.AddWithValue("@Lot", (object?)product.Row.LotNo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Stock", (object?)product.Row.StockNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Barcode", product.Row.Barcode);
            cmd.Parameters.AddWithValue("@Q", product.Qty);
            cmd.Parameters.AddWithValue("@By", s.OperatorId);
            var id = (int)cmd.ExecuteScalar()!;
            tx.Commit();
            return Results.Ok(new ReturnResult(true, "Customer return received.", id, product.Row));
        });

        g.MapGet("/returns", (HttpContext ctx) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            const string sql = """
                SELECT TOP 50
                    ReturnID, ReturnNumber, CustomerCode,
                    JSON_VALUE(ItemsJSON, '$[0].itemNo') AS ItemNo,
                    TRY_CONVERT(decimal(12,3), JSON_VALUE(ItemsJSON, '$[0].qty')) AS Qty,
                    ReturnReason, Status, ReceivedAt
                FROM dbo.FG_CustomerReturn
                ORDER BY ReceivedAt DESC, ReturnID DESC;
                """;
            return Query(factory, sql, r => new ReturnRow(
                (int)r["ReturnID"], r["ReturnNumber"] as string, r["CustomerCode"] as string,
                r["ItemNo"] as string, r["Qty"] == DBNull.Value ? 0 : Convert.ToDecimal(r["Qty"]),
                r["ReturnReason"] as string, r["Status"] as string, r["ReceivedAt"] as DateTime?));
        });
    }

    // ── helpers ──────────────────────────────────────────────────────────
    private sealed record LoadingItemValidation(LoadingItemRow? Item, string Message);

    private static bool TryNormalizeTruckBarcode(
        string? barcode, out string licensePlate, out string error)
    {
        var value = (barcode ?? "").Replace("\u0002", "").Replace("\u0003", "").Trim();
        licensePlate = "";
        error = "";
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Scan the truck barcode first.";
            return false;
        }

        var separator = value.IndexOf(':');
        if (separator >= 0)
        {
            var prefix = value[..separator].Trim();
            if (!new[] { "TRUCK", "PLATE", "VEHICLE" }.Contains(prefix, StringComparer.OrdinalIgnoreCase))
            {
                error = "Unsupported barcode type. Scan a truck barcode.";
                return false;
            }
            value = value[(separator + 1)..].Trim();
        }

        if (value.Length is < 3 or > 20)
        {
            error = "Truck barcode length must be between 3 and 20 characters.";
            return false;
        }
        if (value.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '-' or '_' or ' ')))
        {
            error = "Truck barcode contains unsupported characters.";
            return false;
        }

        licensePlate = value.ToUpperInvariant();
        return true;
    }

    private static LoadingItemValidation ValidateLoadingItem(
        SqlConnection conn, SqlTransaction? tx, string barcode, int? shipmentOrderId)
    {
        if (!TryNormalizeLoadingItemBarcode(barcode, out var normalized, out var formatError))
            return new LoadingItemValidation(null, formatError);

        using var cmd = new SqlCommand("""
            SELECT TOP (20)
                S.StockID,
                S.StockNumber,
                S.ItemNo,
                I.ItemName,
                S.Qty,
                I.DefaultUOM AS Unit,
                S.Location,
                S.Status AS StockStatus,
                LOT.LotCode,
                L.ShipmentOrderLineID,
                L.ShipmentOrderID,
                L.ReservationStatus,
                O.ShipOrderNumber,
                O.CustomerCode,
                O.Status AS OrderStatus
            FROM dbo.FG_Inventory S
            LEFT JOIN dbo.tbl_Lot LOT ON LOT.LotID=S.LotID
            LEFT JOIN dbo.MD_Item I ON I.ItemNo=S.ItemNo
            LEFT JOIN dbo.FG_ShipmentOrderLine L ON L.StockID=S.StockID
            LEFT JOIN dbo.FG_ShipmentOrder O ON O.ShipmentOrderID=L.ShipmentOrderID
            WHERE UPPER(ISNULL(LOT.LotCode,''))=UPPER(@Barcode)
               OR UPPER(ISNULL(S.StockNumber,''))=UPPER(@Barcode)
            ORDER BY CASE WHEN UPPER(ISNULL(L.ReservationStatus,''))='PICKED'
                                AND UPPER(ISNULL(S.Status,''))='RESERVED' THEN 0 ELSE 1 END,
                     L.ShipmentOrderLineID DESC;
            """, conn, tx);
        cmd.Parameters.Add("@Barcode", SqlDbType.NVarChar, 80).Value = normalized;

        var foundInventory = false;
        var foundPicked = false;
        var completedShipment = false;
        var belongsToOtherOrder = false;
        LoadingItemRow? selected = null;
        using (var rdr = cmd.ExecuteReader())
        {
            while (rdr.Read())
            {
                foundInventory = true;
                var orderId = GetInt(rdr, "ShipmentOrderID") ?? 0;
                var lineId = GetInt(rdr, "ShipmentOrderLineID") ?? 0;
                var picked = lineId > 0 && orderId > 0
                    && string.Equals(GetString(rdr, "ReservationStatus"), "Picked", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(GetString(rdr, "StockStatus"), "Reserved", StringComparison.OrdinalIgnoreCase);
                if (!picked) continue;

                foundPicked = true;
                var orderStatus = (GetString(rdr, "OrderStatus") ?? "").ToUpperInvariant();
                if (orderStatus is "SHIPPED" or "CANCELLED" or "CLOSED")
                {
                    completedShipment = true;
                    continue;
                }
                if (orderStatus is not ("READY" or "PICKED" or "RELEASED")) continue;
                if (shipmentOrderId is > 0 && orderId != shipmentOrderId.Value)
                {
                    belongsToOtherOrder = true;
                    continue;
                }

                selected = new LoadingItemRow(
                    GetInt(rdr, "StockID") ?? 0,
                    lineId,
                    orderId,
                    GetString(rdr, "ShipOrderNumber") ?? "",
                    GetString(rdr, "CustomerCode") ?? "",
                    GetString(rdr, "ItemNo") ?? "",
                    GetString(rdr, "ItemName"),
                    GetString(rdr, "LotCode"),
                    GetString(rdr, "StockNumber"),
                    GetDecimal(rdr, "Qty"),
                    GetString(rdr, "Unit"),
                    GetString(rdr, "Location"));
                break;
            }
        }

        if (!foundInventory)
            return new LoadingItemValidation(null, "This barcode does not match an FG LOT or stock record.");
        if (selected is null && completedShipment)
            return new LoadingItemValidation(null, "This product belongs to a shipment order that is already completed.");
        if (selected is null && belongsToOtherOrder)
            return new LoadingItemValidation(null, "This product belongs to a different shipment order.");
        if (selected is null && !foundPicked)
            return new LoadingItemValidation(null, "This product has not completed release picking.");
        if (selected is null)
            return new LoadingItemValidation(null, "This product is not eligible for truck loading.");

        using var loadedCmd = new SqlCommand("""
            SELECT TOP (1) LoadingNumber
            FROM dbo.FG_LoadingConfirm
            WHERE ShipmentOrderID=@ShipmentOrderID
              AND UPPER(ISNULL(OTDStatus,'')) <> 'CANCELLED'
            ORDER BY LoadingID DESC;
            """, conn, tx);
        loadedCmd.Parameters.Add("@ShipmentOrderID", SqlDbType.Int).Value = selected.ShipmentOrderId;
        var loadingNumber = Convert.ToString(loadedCmd.ExecuteScalar());
        if (!string.IsNullOrWhiteSpace(loadingNumber))
            return new LoadingItemValidation(null,
                $"This shipment order was already loaded under {loadingNumber}.");

        return new LoadingItemValidation(selected, "Picked product is ready for truck loading.");
    }

    private static List<LoadingItemRow> ReadPickedLoadingItems(
        SqlConnection conn, SqlTransaction? tx, int shipmentOrderId)
    {
        using var cmd = new SqlCommand("""
            SELECT
                S.StockID,
                L.ShipmentOrderLineID,
                L.ShipmentOrderID,
                O.ShipOrderNumber,
                O.CustomerCode,
                S.ItemNo,
                I.ItemName,
                LOT.LotCode,
                S.StockNumber,
                S.Qty,
                I.DefaultUOM AS Unit,
                S.Location
            FROM dbo.FG_ShipmentOrderLine L WITH (UPDLOCK, HOLDLOCK)
            JOIN dbo.FG_ShipmentOrder O ON O.ShipmentOrderID=L.ShipmentOrderID
            JOIN dbo.FG_Inventory S WITH (UPDLOCK, HOLDLOCK) ON S.StockID=L.StockID
            LEFT JOIN dbo.tbl_Lot LOT ON LOT.LotID=S.LotID
            LEFT JOIN dbo.MD_Item I ON I.ItemNo=S.ItemNo
            WHERE L.ShipmentOrderID=@ShipmentOrderID
              AND UPPER(ISNULL(L.ReservationStatus,''))='PICKED'
              AND UPPER(ISNULL(S.Status,''))='RESERVED'
              AND UPPER(ISNULL(O.Status,'')) IN ('READY','PICKED','RELEASED')
            ORDER BY L.LineSeq, L.ShipmentOrderLineID;
            """, conn, tx);
        cmd.Parameters.Add("@ShipmentOrderID", SqlDbType.Int).Value = shipmentOrderId;
        using var rdr = cmd.ExecuteReader();
        var rows = new List<LoadingItemRow>();
        while (rdr.Read())
        {
            rows.Add(new LoadingItemRow(
                GetInt(rdr, "StockID") ?? 0,
                GetInt(rdr, "ShipmentOrderLineID") ?? 0,
                GetInt(rdr, "ShipmentOrderID") ?? 0,
                GetString(rdr, "ShipOrderNumber") ?? "",
                GetString(rdr, "CustomerCode") ?? "",
                GetString(rdr, "ItemNo") ?? "",
                GetString(rdr, "ItemName"),
                GetString(rdr, "LotCode"),
                GetString(rdr, "StockNumber"),
                GetDecimal(rdr, "Qty"),
                GetString(rdr, "Unit"),
                GetString(rdr, "Location")));
        }
        return rows;
    }

    private static bool TryNormalizeLoadingItemBarcode(
        string? barcode, out string normalized, out string error)
    {
        var value = (barcode ?? "").Replace("\u0002", "").Replace("\u0003", "").Trim();
        normalized = "";
        error = "";
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Scan the picked product barcode.";
            return false;
        }

        var separator = value.IndexOf(':');
        if (separator >= 0)
        {
            var prefix = value[..separator].Trim();
            if (!new[] { "FGLOT", "LOT", "STOCK" }.Contains(prefix, StringComparer.OrdinalIgnoreCase))
            {
                error = "Unsupported barcode type. Scan an FG LOT or stock barcode.";
                return false;
            }
            value = value[(separator + 1)..].Trim();
        }

        if (value.Length is < 3 or > 80 ||
            value.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' or '/')))
        {
            error = "The product barcode format is invalid.";
            return false;
        }

        normalized = value;
        return true;
    }

    private static bool TryNormalizeLoadingOrderBarcode(
        string? barcode, out string orderNumber, out string error)
    {
        var value = (barcode ?? "").Replace("\u0002", "").Replace("\u0003", "").Trim();
        orderNumber = "";
        error = "";
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Scan the shipment order barcode first.";
            return false;
        }

        var separator = value.IndexOf(':');
        if (separator >= 0)
        {
            var prefix = value[..separator].Trim();
            if (!new[] { "SHIPMENT", "ORDER", "SO", "LOAD" }.Contains(prefix, StringComparer.OrdinalIgnoreCase))
            {
                error = "Unsupported barcode type. Scan a shipment order barcode.";
                return false;
            }
            value = value[(separator + 1)..].Trim();
        }

        if (value.Length is < 3 or > 40 ||
            value.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' or '/')))
        {
            error = "The shipment order barcode format is invalid.";
            return false;
        }

        orderNumber = value;
        return true;
    }

    private sealed record ReturnProductData(ReturnScanRow Row, decimal Qty);
    private sealed record ReturnCandidate(int ShipmentOrderLineId, bool HasFutureShipmentDate, ReturnProductData Product);
    private sealed record ReturnValidation(ReturnProductData? Product, string Message);

    private static ReturnValidation ValidateReturnProduct(
        SqlConnection conn, SqlTransaction? tx, string barcode, bool lockReturnHistory)
    {
        if (!TryNormalizeReturnBarcode(barcode, out var normalized, out var formatError))
            return new ReturnValidation(null, formatError);

        using var cmd = new SqlCommand("""
            SELECT TOP (10)
                L.ShipmentOrderLineID,
                S.StockNumber,
                LOT.LotCode,
                O.ShipmentOrderID,
                O.ShipOrderNumber,
                O.CustomerCode,
                L.ItemNo,
                I.ItemName,
                LC.DepartureTS AS ShippedAt,
                CASE WHEN LC.DepartureTS > DATEADD(minute, 5, SYSDATETIME()) THEN 1 ELSE 0 END AS FutureShipmentFlag,
                CAST(COALESCE(NULLIF(L.AllocatedQty, 0), NULLIF(S.Qty, 0),
                              NULLIF(L.OrderedQty, 0), 1) AS decimal(12,3)) AS ReturnQty
            FROM dbo.FG_ShipmentOrderLine L
            JOIN dbo.FG_ShipmentOrder O
              ON O.ShipmentOrderID = L.ShipmentOrderID
            LEFT JOIN dbo.FG_Inventory S
              ON S.StockID = L.StockID
            LEFT JOIN dbo.tbl_Lot LOT
              ON LOT.LotID = COALESCE(L.LotID, S.LotID)
            LEFT JOIN dbo.MD_Item I
              ON I.ItemNo = L.ItemNo
            OUTER APPLY
            (
                SELECT TOP (1) X.DepartureTS
                FROM dbo.FG_LoadingConfirm X
                WHERE X.ShipmentOrderID = O.ShipmentOrderID
                  AND X.DepartureTS IS NOT NULL
                ORDER BY X.DepartureTS DESC, X.LoadingID DESC
            ) LC
            WHERE UPPER(ISNULL(LOT.LotCode, '')) = UPPER(@Barcode)
               OR UPPER(ISNULL(S.StockNumber, '')) = UPPER(@Barcode)
            ORDER BY LC.DepartureTS DESC, L.ShipmentOrderLineID DESC;
            """, conn, tx);
        cmd.Parameters.Add("@Barcode", SqlDbType.NVarChar, 80).Value = normalized;

        var candidates = new List<ReturnCandidate>();
        using (var rdr = cmd.ExecuteReader())
        {
            while (rdr.Read())
            {
                var candidateRow = new ReturnScanRow(
                    normalized,
                    GetString(rdr, "StockNumber"),
                    GetString(rdr, "LotCode"),
                    GetInt(rdr, "ShipmentOrderID") ?? 0,
                    GetString(rdr, "ShipOrderNumber"),
                    GetString(rdr, "CustomerCode") ?? "",
                    GetString(rdr, "ItemNo") ?? "",
                    GetString(rdr, "ItemName"),
                    GetDate(rdr, "ShippedAt") ?? DateTime.MinValue);
                candidates.Add(new ReturnCandidate(
                    GetInt(rdr, "ShipmentOrderLineID") ?? 0,
                    GetInt(rdr, "FutureShipmentFlag") == 1,
                    new ReturnProductData(candidateRow, GetDecimal(rdr, "ReturnQty"))));
            }
        }

        if (candidates.Count == 0)
            return new ReturnValidation(null, "This barcode does not match a finished-good LOT or stock record.");

        var shipped = candidates
            .Where(x => x.Product.Row.ShippedAt != DateTime.MinValue)
            .OrderByDescending(x => x.Product.Row.ShippedAt)
            .ThenByDescending(x => x.ShipmentOrderLineId)
            .ToList();
        if (shipped.Count == 0)
            return new ReturnValidation(null, "The product exists, but no completed shipment history was found.");

        var latestCandidate = shipped[0];
        var latest = latestCandidate.Product;
        var row = latest.Row;
        if (latestCandidate.HasFutureShipmentDate)
            return new ReturnValidation(null, "The shipment date is in the future. Verify the loading record.");
        if (row.ShipmentOrderId <= 0 || string.IsNullOrWhiteSpace(row.ShipOrderNumber))
            return new ReturnValidation(null, "The shipment record is incomplete. Shipment order information is required.");
        if (string.IsNullOrWhiteSpace(row.CustomerCode))
            return new ReturnValidation(null, "The shipment record is incomplete. Customer information is required.");
        if (string.IsNullOrWhiteSpace(row.ItemNo) || string.IsNullOrWhiteSpace(row.ItemName))
            return new ReturnValidation(null, "The shipment record is incomplete. Part master information is required.");
        if (latest.Qty <= 0)
            return new ReturnValidation(null, "The shipment quantity is invalid. Verify the shipment line.");

        var sameDepartureMatches = shipped.Count(x =>
            x.Product.Row.ShippedAt == row.ShippedAt &&
            x.Product.Row.ShipmentOrderId != row.ShipmentOrderId);
        if (sameDepartureMatches > 0)
            return new ReturnValidation(null, "This barcode matches multiple shipments. Contact a supervisor before receiving it.");

        var lockHint = lockReturnHistory ? "WITH (UPDLOCK, HOLDLOCK)" : "";
        using var returnCmd = new SqlCommand($$"""
            SELECT TOP (1) ReturnNumber, Status, ReceivedAt
            FROM dbo.FG_CustomerReturn {{lockHint}}
            CROSS APPLY
            (
                SELECT CASE WHEN ISJSON(ItemsJSON) = 1 THEN ItemsJSON ELSE N'{}' END AS SafeItemsJSON
            ) J
            WHERE UPPER(ISNULL(Status, '')) NOT IN ('CANCELLED', 'REJECTED')
              AND
              (
                   UPPER(ISNULL(JSON_VALUE(J.SafeItemsJSON, '$[0].barcode'), '')) = UPPER(@Barcode)
                OR (@LotNo <> '' AND UPPER(ISNULL(JSON_VALUE(J.SafeItemsJSON, '$[0].lotNo'), '')) = UPPER(@LotNo))
                OR (@StockNumber <> '' AND UPPER(ISNULL(JSON_VALUE(J.SafeItemsJSON, '$[0].stockNumber'), '')) = UPPER(@StockNumber))
                OR
                (
                    OriginalShipmentOrderID = @ShipmentOrderID
                    AND UPPER(ISNULL(JSON_VALUE(J.SafeItemsJSON, '$[0].itemNo'), '')) = UPPER(@ItemNo)
                    AND NULLIF(JSON_VALUE(J.SafeItemsJSON, '$[0].barcode'), '') IS NULL
                    AND NULLIF(JSON_VALUE(J.SafeItemsJSON, '$[0].lotNo'), '') IS NULL
                    AND NULLIF(JSON_VALUE(J.SafeItemsJSON, '$[0].stockNumber'), '') IS NULL
                )
              )
            ORDER BY ReceivedAt DESC, ReturnID DESC;
            """, conn, tx);
        returnCmd.Parameters.Add("@Barcode", SqlDbType.NVarChar, 80).Value = normalized;
        returnCmd.Parameters.Add("@LotNo", SqlDbType.NVarChar, 80).Value = (object?)row.LotNo ?? "";
        returnCmd.Parameters.Add("@StockNumber", SqlDbType.NVarChar, 80).Value = (object?)row.StockNumber ?? "";
        returnCmd.Parameters.Add("@ShipmentOrderID", SqlDbType.Int).Value = row.ShipmentOrderId;
        returnCmd.Parameters.Add("@ItemNo", SqlDbType.NVarChar, 40).Value = row.ItemNo;
        using var returnReader = returnCmd.ExecuteReader();
        if (returnReader.Read())
        {
            var returnNumber = GetString(returnReader, "ReturnNumber") ?? "an existing return";
            var status = GetString(returnReader, "Status") ?? "Open";
            return new ReturnValidation(null,
                $"This product was already received under {returnNumber} ({status}).");
        }

        return new ReturnValidation(latest, "Product is eligible for customer return.");
    }

    private static bool TryNormalizeReturnBarcode(
        string? barcode, out string normalized, out string error)
    {
        var value = (barcode ?? "").Replace("\u0002", "").Replace("\u0003", "").Trim();
        normalized = "";
        error = "";
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Scan the returned product barcode.";
            return false;
        }

        var separator = value.IndexOf(':');
        if (separator >= 0)
        {
            var prefix = value[..separator].Trim();
            if (!new[] { "FGLOT", "LOT", "STOCK" }.Contains(prefix, StringComparer.OrdinalIgnoreCase))
            {
                error = "Unsupported barcode type. Scan an FG LOT or stock barcode.";
                return false;
            }
            value = value[(separator + 1)..].Trim();
        }

        if (value.Length is < 3 or > 80)
        {
            error = "Barcode length must be between 3 and 80 characters.";
            return false;
        }
        if (value.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' or '/')))
        {
            error = "Barcode contains unsupported characters.";
            return false;
        }

        normalized = value;
        return true;
    }

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
                  AND ISNULL(PS.ActiveFlag, 1) = 1
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

    private static string? ValidatePutAwayContainer(string storageMethod, string? barcode)
    {
        if (storageMethod is not (BarcodeBox or BarcodePallet or BarcodeRack or BarcodeLocation))
            return "Select Storage first.";
        if (storageMethod == BarcodeLocation)
            return string.IsNullOrWhiteSpace(barcode) ? null : "Location Only does not require a container barcode.";

        var parsed = ParseFgBarcode(barcode);
        if (parsed.Raw.Length == 0)
            return $"Scan {BarcodeKindLabel(storageMethod)} first.";
        if (parsed.Raw.Length > 80 || parsed.Value.Length == 0 ||
            parsed.Value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.'))
            return "The container barcode format is invalid.";
        if (parsed.Kind != storageMethod)
            return $"Scan {BarcodeKindLabel(storageMethod)} using the {storageMethod}: prefix.";
        return null;
    }

    private static PutAwayLocationRow? ValidatePutAwayLocation(SqlConnection conn, SqlTransaction? tx, string locationId, string itemNo, string? customerCode, decimal qty, string? expectedScanType)
    {
        var parsed = ParseFgBarcode(locationId);
        var expected = NormalizeScanType(expectedScanType);
        var actual = parsed.Kind == BarcodeUnknown && expected == BarcodeLocation
            ? BarcodeLocation
            : parsed.Kind;

        if (parsed.Kind != BarcodeUnknown && actual != expected)
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

    private static AdjustScanRow? ExecuteAdjustScan(AmesConnectionFactory factory, string scanText)
    {
        using var conn = factory.OpenConnection();
        using var cmd = new SqlCommand($"[dbo].[{PdaAdjustScanStockProcedure}]", conn)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 15
        };
        cmd.Parameters.Add("@ScanText", SqlDbType.NVarChar, 80).Value = scanText.Trim();
        using var rdr = cmd.ExecuteReader();
        return rdr.Read() ? ReadAdjustScanRow(rdr) : null;
    }

    private static AdjustResult ExecuteAdjustSave(AmesConnectionFactory factory, AdjustSaveReq body, string userId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(body.SupervisorEmployeeNo))
                return new AdjustResult(false, "Select a supervisor.", null);

            using var conn = factory.OpenConnection();
            var supervisor = WhEndpoints.FindSupervisorByPin(conn, body.SupervisorEmployeeNo, body.SupervisorPin);
            if (supervisor is null)
                return new AdjustResult(false, "The PIN does not match the selected supervisor.", null);

            using var cmd = new SqlCommand($"[dbo].[{PdaAdjustSaveQtyProcedure}]", conn)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 15
            };
            cmd.Parameters.Add("@ScanText", SqlDbType.NVarChar, 80).Value = body.Barcode.Trim();
            AddDecimal(cmd, "@DeltaQty", body.DeltaQty);
            cmd.Parameters.Add("@ReasonCode", SqlDbType.NVarChar, 30).Value = body.ReasonCode.Trim();
            cmd.Parameters.Add("@ReasonNote", SqlDbType.NVarChar, 500).Value =
                string.IsNullOrWhiteSpace(body.ReasonNote) ? DBNull.Value : body.ReasonNote.Trim();
            cmd.Parameters.Add("@SupervisorUserId", SqlDbType.NVarChar, 450).Value = supervisor.UserId;
            cmd.Parameters.Add("@SupervisorEmployeeNo", SqlDbType.NVarChar, 40).Value = supervisor.EmployeeNo;
            cmd.Parameters.Add("@UserId", SqlDbType.NVarChar, 40).Value = userId;

            using var rdr = cmd.ExecuteReader();
            var row = rdr.Read() ? ReadAdjustScanRow(rdr) : null;
            return new AdjustResult(true, "Finished goods quantity adjusted", row);
        }
        catch (Exception ex)
        {
            return new AdjustResult(false, AdjustMessage(ex), null);
        }
    }

    private static AdjustScanRow ReadAdjustScanRow(SqlDataReader rdr) => new(
        GetString(rdr, "RECEIVE_TYPE") ?? "FG",
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

    private static string AdjustMessage(Exception ex)
        => ex is SqlException sqlEx && sqlEx.Errors.Count > 0
            ? sqlEx.Errors[0].Message
            : ex.GetBaseException().Message;

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
