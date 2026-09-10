using AMES.Api.Auth;
using AMES.Api.Logging;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using AMES.Data.Repositories;
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
    public sealed record OutgoingSlipRow(int OutgoingSlipId, string OutgoingSlipBarcode,
        string? CustomerCode, DateTime? OutgoingDate, string? Destination, string? Status, int LineCount);
    public sealed record OutgoingSlipLineRow(int OutgoingSlipLineId, int OutgoingSlipId, int LineSeq,
        string PartNo, string? PartName, decimal RequiredQty, decimal ScannedQty, string? Status);
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
    public sealed record ReturnScanRow(string Barcode, int StockId, string? StockNumber, int? LotId, string? LotNo,
        int ShipmentOrderId, string? ShipOrderNumber, string CustomerCode,
        string ItemNo, string? ItemName, DateTime ShippedAt, decimal Qty);
    public sealed record ReturnResult(bool Success, string Message, int? ReturnId, ReturnScanRow? Row);
    public sealed record AdjustScanRow(string ReceiveType, string? Yn, string LotNo, string Barcode,
        string? SourceTable, string? NoteNo, string? CaseBarcode, string? CaseNo, string? InvoiceNo,
        string? ContainerNo, string? PartNo, string? PartName, decimal Qty, string? Unit, string? PoNo,
        int? PoSeq, string? VendorId, string? VendorName, DateTime? ProductionDate, DateTime? DeliveryDate,
        DateTime? ArrivalDate, DateTime? ShipDate, DateTime? PackDate, string? ReceivedLocation,
        string? ReceivedStatus);
    public sealed record AdjustSaveReq(string? Mode, string Barcode, decimal DeltaQty, string ReasonCode,
        string? ReasonNote, string? SupervisorPin = null, string? SupervisorEmployeeNo = null);
    public sealed record AdjustResult(bool Success, string Message, AdjustScanRow? Row);

    public sealed record PutAwayScanRow(int? LotId, string LotNo, int? WoId,
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
    public sealed record ReleaseLotReq(int OutgoingSlipLineId, int StockId, decimal Qty);
    public sealed record ReleaseLotScanReq(int OutgoingSlipId, string Barcode, List<ReleaseLotReq>? ScannedLots);
    public sealed record ReleaseLotScanResult(bool Success, string Code, string Message,
        StockRow? Stock, int? OutgoingSlipLineId);
    public sealed record CompleteReleaseReq(int OutgoingSlipId, List<ReleaseLotReq> Lots);
    public sealed record CompleteReleaseResult(bool Success, string Message, int? PickId);
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
    public sealed record ReturnReq(string Barcode, string ReturnReason, string? Note);

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
        var master = new MasterDataRepository(factory);
        var g = app.MapGroup("/api/fg").WithTags("Finished Goods");
        g.MapAdjustmentLocation(factory, finishedGoods: true);

        g.MapPost("/test/ppt-reset/{screen}", (HttpContext ctx, string screen) =>
        {
            if (ctx.GetSession() is not { } session) return Results.Unauthorized();
            if (!PdaScenarioUsers.IsAny(session.EmployeeNo))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (screen is not ("qc" or "putaway" or "inventory" or "release" or "loading" or "return" or "adjust" or "history"))
                return Results.BadRequest(new { Message = "Unknown PPT test screen." });
            try
            {
                using var connection = factory.OpenConnection();
                using var command = new SqlCommand(screen == "history" ? "dbo.FG_PDA_HISTORY_TEST_RESET" : "dbo.FG_PDA_PPT_TEST_RESET", connection) { CommandType = CommandType.StoredProcedure };
                if (screen != "history") command.Parameters.Add("@Screen", SqlDbType.VarChar, 10).Value = screen;
                command.ExecuteNonQuery();
                return Results.Ok(new { Success = true });
            }
            catch (SqlException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

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
            if (master.FindActiveCodeItem("INV_ADJUST_REASON", body.ReasonCode?.Trim() ?? "") is null)
                return Results.BadRequest(new AdjustResult(false, "Select a valid reason code.", null));

            var result = ExecuteAdjustSave(factory, body, s.EmployeeNo, s.OperatorId);
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
                SELECT
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
                    SELECT TOP (1) QI.CustomerCode, QI.BatchQty, QI.InsEndTS, QI.Verdict
                    FROM dbo.QC_Inspection QI
                    WHERE QI.LotID = L.LotID
                    ORDER BY COALESCE(QI.InsEndTS, QI.InsStartTS, QI.CreatedTS) DESC, QI.InspectionID DESC
                ) Q
                WHERE UPPER(ISNULL(Q.Verdict, '')) IN ('PASS', 'PASSED', 'OK')
                  AND NOT EXISTS
                (
                    SELECT 1
                    FROM dbo.FG_Inventory S
                    WHERE S.LotID = L.LotID
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

        g.MapGet("/transactions", (HttpContext ctx, string? search, DateTime? dateFrom, DateTime? dateTo) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            if (dateFrom.HasValue && dateTo.HasValue && dateFrom.Value.Date > dateTo.Value.Date)
                return Results.BadRequest(new { Message = "From date cannot be after To date." });
            using var connection = factory.OpenConnection();
            using var command = new SqlCommand("dbo.FG_PDA_TRANSACTION_LIST", connection) { CommandType = CommandType.StoredProcedure };
            command.Parameters.Add("@SearchText", SqlDbType.NVarChar, 120).Value = (object?)search?.Trim() ?? DBNull.Value;
            command.Parameters.Add("@DateFrom", SqlDbType.Date).Value = (object?)dateFrom?.Date ?? DBNull.Value;
            command.Parameters.Add("@DateTo", SqlDbType.Date).Value = (object?)dateTo?.Date ?? DBNull.Value;
            using var reader = command.ExecuteReader();
            var rows = new List<WhEndpoints.WarehouseTransactionRow>();
            while (reader.Read()) rows.Add(WhEndpoints.ReadWarehouseTransactionRow(reader));
            return Results.Ok(rows);
        });

        // FG-02 PDA Put-Away - scan a QC-passed FG LOT.
        g.MapGet("/putaway/scan", (HttpContext ctx, string barcode) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(barcode))
                return Results.BadRequest(new PutAwayResult(false, "Scan FG LOT first.", null, null, null));

            var parsed = ParseFgBarcode(barcode);
            if (parsed.Kind == BarcodeWo || IsStorageBarcode(parsed.Kind))
            {
                var message = $"Scan FG LOT first. You scanned {BarcodeKindLabel(parsed.Kind)}.";
                WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                    s, "SCAN_FG_LOT", "FG002", parsed.Kind, parsed.Raw, "FAIL", message,
                    lotNo: parsed.Value));
                return Results.BadRequest(new PutAwayResult(false, message, null, null, null));
            }

            using var conn = factory.OpenConnection();
            var row = FindPutAwayScanRow(conn, null, parsed.Value);
            if (row is null)
            {
                WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                    s, "SCAN_FG_LOT", "FG002", parsed.Kind, parsed.Raw, "FAIL", "FG LOT was not found.",
                    lotNo: parsed.Value));
                return Results.NotFound(new PutAwayResult(false, "FG LOT was not found.", null, null, null));
            }

            var success = row.IsQcPassed && !row.AlreadyStocked;
            WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                s, "SCAN_FG_LOT", "FG002", row.BarcodeType, parsed.Raw, success ? "SUCCESS" : "FAIL", row.Message,
                lotNo: row.LotNo, partNo: row.ItemNo, locationId: row.ExistingLocation, qty: row.Qty));

            return Results.Ok(new PutAwayResult(success, row.Message, row.ExistingStockId, row, null));
        });

        g.MapGet("/putaway/container", (HttpContext ctx, string storageMethod, string barcode) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            var method = storageMethod.Trim().ToUpperInvariant();
            var error = master.FindActiveCodeItem("FG_STORAGE_METHOD", method) is null
                ? "Select a valid storage method."
                : method == BarcodeLocation
                ? "Location Only does not require a container barcode."
                : ValidatePutAwayContainer(method, barcode);
            return error is null
                ? Results.Ok(new PutAwayResult(true, "Container scanned. Scan Location Barcode.", null, null, null))
                : Results.BadRequest(new PutAwayResult(false, error, null, null, null));
        });

        // FG-02 PDA Put-Away - validate scanned FG location.
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

        // FG-02 PDA Put-Away - confirm Stock insert + PutAway history.
        g.MapPost("/putaway/confirm", (HttpContext ctx, PutAwayConfirmReq body) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(body.Barcode))
                return Results.BadRequest(new PutAwayResult(false, "Scan FG LOT first.", null, null, null));
            if (string.IsNullOrWhiteSpace(body.LocationId))
                return Results.BadRequest(new PutAwayResult(false, "Scan Location No first.", null, null, null));

            var storageMethod = body.StorageMethod?.Trim().ToUpperInvariant() ?? "";
            if (master.FindActiveCodeItem("FG_STORAGE_METHOD", storageMethod) is null)
                return Results.BadRequest(new PutAwayResult(false, "Select a valid storage method.", null, null, null));
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
                if (parsedLot.Kind == BarcodeWo || IsStorageBarcode(parsedLot.Kind))
                {
                    tx.Rollback();
                    return Results.BadRequest(new PutAwayResult(false, "Scan FG LOT first.", null, null, null));
                }
                if (!AcquirePutAwayLock(conn, tx, "LOT", parsedLot.Value))
                {
                    tx.Rollback();
                    return Results.Conflict(new PutAwayResult(false, "This FG LOT is being processed. Scan it again.", null, null, null));
                }
                var row = FindPutAwayScanRow(conn, tx, parsedLot.Value);
                if (row is null)
                {
                    tx.Rollback();
                    return Results.NotFound(new PutAwayResult(false, "FG LOT was not found.", null, null, null));
                }
                if (!row.IsQcPassed)
                {
                    tx.Rollback();
                    WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                        s, "FG_PUTAWAY", "FG002", "LOT", body.Barcode, "FAIL", row.Message,
                        lotNo: row.LotNo, partNo: row.ItemNo, qty: row.Qty));
                    return Results.BadRequest(new PutAwayResult(false, row.Message, null, row, null));
                }
                if (row.AlreadyStocked)
                {
                    tx.Rollback();
                    WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                        s, "FG_PUTAWAY", "FG002", "LOT", body.Barcode, "FAIL", row.Message,
                        lotNo: row.LotNo, partNo: row.ItemNo, locationId: row.ExistingLocation, qty: row.Qty));
                    return Results.BadRequest(new PutAwayResult(false, row.Message, row.ExistingStockId, row, null));
                }

                if (storageMethod != BarcodeLocation && !TableHasColumn(conn, tx, "FG_PutAway", "ContainerBarcode"))
                {
                    tx.Rollback();
                    return Results.BadRequest(new PutAwayResult(false, "Container storage is not configured. Apply the FG Put-Away database migration.", null, row, null));
                }

                var parsedLocation = ParseFgBarcode(body.LocationId);
                if (!AcquirePutAwayLock(conn, tx, "LOCATION", parsedLocation.Value))
                {
                    tx.Rollback();
                    return Results.Conflict(new PutAwayResult(false, "This FG location is being updated. Scan it again.", null, row, null));
                }
                var location = ValidatePutAwayLocation(conn, tx, body.LocationId.Trim(), row.ItemNo, row.CustomerCode, row.Qty, BarcodeLocation);
                if (location is null || !location.IsValid)
                {
                    tx.Rollback();
                    var message = location?.Message ?? "FG location was not found.";
                    WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                        s, "FG_PUTAWAY", "FG002", "LOCATION", body.LocationId, "FAIL", message,
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
                    s, "FG_PUTAWAY", "FG002", location.ScanType, location.ScannedBarcode, "SUCCESS", "FG Put-Away confirmed.",
                    lotNo: row.LotNo, partNo: row.ItemNo, locationId: location.LocationId, qty: row.Qty));

                return Results.Ok(new PutAwayResult(true, "FG Put-Away confirmed.", stockId, updated ?? row, location));
            }
            catch
            {
                tx.Rollback();
                throw;
            }
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

        // FG-04 Release starts from the outgoing-slip barcode, not the downstream shipment number.
        g.MapGet("/release/outgoing-slips/{barcode}", (HttpContext ctx, string barcode) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            const string sql = """
                SELECT o.ShipmentOrderID AS OutgoingSlipID, o.OutgoingSlipNumber,
                       o.CustomerCode, o.ShipDate AS OutgoingDate, o.DestPlant AS Destination,
                       ISNULL(o.Status,'Open') AS Status,
                       (SELECT COUNT(*) FROM dbo.FG_ShipmentOrderLine l
                        WHERE l.ShipmentOrderID=o.ShipmentOrderID) AS LineCount
                FROM dbo.FG_ShipmentOrder o
                WHERE o.OutgoingSlipNumber=@Q;
                """;
            return QueryWithParam(factory, sql, "@Q", barcode, r => new OutgoingSlipRow(
                (int)r["OutgoingSlipID"], r["OutgoingSlipNumber"] as string ?? "",
                r["CustomerCode"] as string, r["OutgoingDate"] as DateTime?,
                r["Destination"] as string, r["Status"] as string, (int)r["LineCount"]));
        });

        g.MapGet("/release/outgoing-slips/{barcode}/lines", (HttpContext ctx, string barcode) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            const string sql = """
                SELECT l.ShipmentOrderLineID AS OutgoingSlipLineID,
                       l.ShipmentOrderID AS OutgoingSlipID, ISNULL(l.LineSeq,0) AS LineSeq,
                       l.ItemNo AS PartNo, i.ItemName AS PartName,
                       ISNULL(l.OrderedQty,0) AS RequiredQty,
                       ISNULL(l.AllocatedQty,0) AS ScannedQty,
                       l.ReservationStatus AS Status
                FROM dbo.FG_ShipmentOrderLine l
                JOIN dbo.FG_ShipmentOrder o ON o.ShipmentOrderID=l.ShipmentOrderID
                LEFT JOIN dbo.MD_Item i ON i.ItemNo=l.ItemNo
                WHERE o.OutgoingSlipNumber=@Q
                ORDER BY l.LineSeq, l.ShipmentOrderLineID;
                """;
            return QueryWithParam(factory, sql, "@Q", barcode, r => new OutgoingSlipLineRow(
                (int)r["OutgoingSlipLineID"], (int)r["OutgoingSlipID"], (int)r["LineSeq"],
                r["PartNo"] as string ?? "", r["PartName"] as string,
                r.GetDecimal(r.GetOrdinal("RequiredQty")), r.GetDecimal(r.GetOrdinal("ScannedQty")),
                r["Status"] as string));
        });

        // Validate each LOT against the released outgoing slip and the full FIFO queue.
        g.MapPost("/release/lot/scan", (HttpContext ctx, ReleaseLotScanReq body) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            if (body.OutgoingSlipId <= 0 || string.IsNullOrWhiteSpace(body.Barcode))
                return Results.BadRequest(new ReleaseLotScanResult(false, "INVALID_SCAN",
                    "Scan the outgoing slip and FG LOT barcode first.", null, null));
            if (body.ScannedLots is { Count: > 1000 })
                return Results.BadRequest(new ReleaseLotScanResult(false, "INVALID_SCAN",
                    "The scanned LOT list is too large.", null, null));

            using var conn = factory.OpenConnection();
            using var cmd = new SqlCommand("dbo.FG_PDA_PICKING_SCAN", conn)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 15
            };
            cmd.Parameters.Add("@OutgoingSlipID", SqlDbType.Int).Value = body.OutgoingSlipId;
            cmd.Parameters.Add("@LotBarcode", SqlDbType.VarChar, 80).Value = body.Barcode.Trim();
            cmd.Parameters.Add("@ScannedLots", SqlDbType.NVarChar, -1).Value =
                JsonSerializer.Serialize(body.ScannedLots ?? []);
            try
            {
                using var r = cmd.ExecuteReader();
                if (!r.Read())
                    return Results.Problem("FG Picking scan returned no result.");
                var stock = new StockRow(
                    GetInt(r, "StockID") ?? 0,
                    GetString(r, "StockNumber"),
                    GetString(r, "ItemNo") ?? "",
                    GetString(r, "ItemName"),
                    GetInt(r, "LotID"),
                    GetString(r, "LotNo"),
                    GetString(r, "CustomerCode"),
                    GetDecimal(r, "Qty"),
                    GetString(r, "Unit"),
                    GetString(r, "Location"),
                    GetString(r, "Status"),
                    GetDate(r, "StockTS"));
                return Results.Ok(new ReleaseLotScanResult(true, "OK", "FG LOT is ready to pick.",
                    stock, GetInt(r, "OutgoingSlipLineID")));
            }
            catch (SqlException ex) when (ex.Number is >= 51800 and <= 51819)
            {
                return Results.BadRequest(new ReleaseLotScanResult(false, ReleaseScanErrorCode(ex.Number),
                    ex.Message, null, null));
            }
        });

        // COMPLETE reserves every listed LOT atomically and preserves the original request lines.
        g.MapPost("/release/complete", (HttpContext ctx, CompleteReleaseReq body) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();
            if (body.OutgoingSlipId <= 0 || body.Lots is not { Count: > 0 })
                return Results.BadRequest(new CompleteReleaseResult(false,
                    "Scan every listed LOT before completing.", null));
            if (body.Lots.Count > 1000)
                return Results.BadRequest(new CompleteReleaseResult(false,
                    "The scanned LOT list is too large.", null));

            using var conn = factory.OpenConnection();
            using var cmd = new SqlCommand("dbo.FG_PDA_PICKING_COMPLETE", conn)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 30
            };
            cmd.Parameters.Add("@OutgoingSlipID", SqlDbType.Int).Value = body.OutgoingSlipId;
            cmd.Parameters.Add("@Lots", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(body.Lots);
            cmd.Parameters.Add("@OperatorID", SqlDbType.NVarChar, 450).Value = s.OperatorId;
            try
            {
                var id = Convert.ToInt32(cmd.ExecuteScalar());
                return Results.Ok(new CompleteReleaseResult(true,
                    "Release picking completed.", id));
            }
            catch (SqlException ex) when (ex.Number is >= 51820 and <= 51839)
            {
                return Results.BadRequest(new CompleteReleaseResult(false, ex.Message, null));
            }
        });

        // FG-05 Truck and picked-product loading
        g.MapGet("/loading/order/scan", (HttpContext ctx, string barcode) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            if (!TryNormalizeLoadingOrderBarcode(barcode, out var orderNumber, out var error))
                return Results.BadRequest(new LoadingOrderResult(false, error, null));
            try
            {
                using var conn = factory.OpenConnection();
                using var cmd = new SqlCommand("dbo.FG_PDA_LOADING_ORDER_SCAN", conn) { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.Add("@OrderNumber", SqlDbType.VarChar, 40).Value = orderNumber;
                using var rdr = cmd.ExecuteReader();
                if (!rdr.Read())
                    return Results.Json(new LoadingOrderResult(false, "Truck loading service returned no shipment order.", null), statusCode: 503);
                var orderId = GetInt(rdr, "ShipmentOrderID") ?? 0;
                var shipOrderNumber = GetString(rdr, "ShipOrderNumber") ?? "";
                var customerCode = GetString(rdr, "CustomerCode") ?? "";
                var shipDate = GetDate(rdr, "ShipDate");
                var destination = GetString(rdr, "Destination");
                var items = new List<LoadingItemRow>();
                if (rdr.NextResult())
                {
                    while (rdr.Read())
                    {
                        items.Add(new LoadingItemRow(
                            GetInt(rdr, "StockID") ?? 0,
                            GetInt(rdr, "ShipmentOrderLineID") ?? 0,
                            GetInt(rdr, "ShipmentOrderID") ?? 0,
                            GetString(rdr, "ShipOrderNumber") ?? "",
                            GetString(rdr, "CustomerCode") ?? "",
                            GetString(rdr, "ItemNo") ?? "",
                            GetString(rdr, "ItemName"),
                            GetString(rdr, "LotNo"),
                            GetString(rdr, "StockNumber"),
                            GetDecimal(rdr, "Qty"),
                            GetString(rdr, "Unit"),
                            GetString(rdr, "Location")));
                    }
                }
                if (items.Count == 0)
                    return Results.Json(new LoadingOrderResult(false, "No picked products are assigned to this shipment order.", null), statusCode: 503);
                var row = new LoadingOrderRow(orderId, shipOrderNumber, shipOrderNumber,
                    customerCode, shipDate, destination, items);
                return Results.Ok(new LoadingOrderResult(true,
                    $"Shipment order loaded. Scan {items.Count} product(s).", row));
            }
            catch (SqlException ex) when (ex.Number is >= 51900 and <= 51919)
            {
                return Results.BadRequest(new LoadingOrderResult(false, ex.Message, null));
            }
            catch
            {
                return Results.Json(new LoadingOrderResult(false,
                    "Truck loading service is unavailable. Check the API and database connection.", null), statusCode: 503);
            }
        });

        g.MapGet("/loading/truck/scan", (HttpContext ctx, string barcode) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            if (!TryNormalizeTruckBarcode(barcode, out var truck, out var error))
                return Results.BadRequest(new LoadingResult(false, error, null, null, null));
            var row = new LoadingTruckRow($"TRUCK:{truck}", truck, true, "Truck is ready for loading.");
            return Results.Ok(new LoadingResult(true, row.Message, null, row, null));
        });

        g.MapGet("/loading/item/scan", (HttpContext ctx, string barcode, int? shipmentOrderId) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            if (shipmentOrderId is not > 0)
                return Results.BadRequest(new LoadingResult(false, "Scan the shipment order before the stock barcode.", null, null, null));
            if (!TryNormalizeLoadingItemBarcode(barcode, out var normalized, out var error))
                return Results.BadRequest(new LoadingResult(false, error, null, null, null));
            try
            {
                using var conn = factory.OpenConnection();
                using var cmd = new SqlCommand("dbo.FG_PDA_LOADING_STOCK_SCAN", conn) { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.Add("@Barcode", SqlDbType.VarChar, 80).Value = normalized;
                cmd.Parameters.Add("@ShipmentOrderID", SqlDbType.Int).Value = shipmentOrderId.Value;
                using var rdr = cmd.ExecuteReader();
                if (!rdr.Read())
                    return Results.Json(new LoadingResult(false, "Truck loading service returned no product.", null, null, null), statusCode: 503);
                var item = new LoadingItemRow(
                    GetInt(rdr, "StockID") ?? 0,
                    GetInt(rdr, "ShipmentOrderLineID") ?? 0,
                    GetInt(rdr, "ShipmentOrderID") ?? 0,
                    GetString(rdr, "ShipOrderNumber") ?? "",
                    GetString(rdr, "CustomerCode") ?? "",
                    GetString(rdr, "ItemNo") ?? "",
                    GetString(rdr, "ItemName"),
                    GetString(rdr, "LotNo"),
                    GetString(rdr, "StockNumber"),
                    GetDecimal(rdr, "Qty"),
                    GetString(rdr, "Unit"),
                    GetString(rdr, "Location"));
                return Results.Ok(new LoadingResult(true, "Picked product is ready for truck loading.", null, null, item));
            }
            catch (SqlException ex) when (ex.Number is >= 51900 and <= 51919)
            {
                return Results.BadRequest(new LoadingResult(false, ex.Message, null, null, null));
            }
            catch
            {
                return Results.Json(new LoadingResult(false,
                    "Truck loading service is unavailable. Check the API and database connection.", null, null, null), statusCode: 503);
            }
        });

        g.MapPost("/loading", (HttpContext ctx, LoadingReq body) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();
            if (!TryNormalizeTruckBarcode(body.TruckBarcode, out var truck, out var truckError))
                return Results.BadRequest(new LoadingResult(false, truckError, null, null, null));
            if (body.ShipmentOrderId <= 0 || body.StockIds is null || body.StockIds.Count == 0)
                return Results.BadRequest(new LoadingResult(false,
                    "Scan a truck and at least one picked product.", null, null, null));
            if (body.StockIds.Distinct().Count() != body.StockIds.Count)
                return Results.BadRequest(new LoadingResult(false,
                    "The loading list contains duplicate product scans.", null, null, null));
            try
            {
                using var conn = factory.OpenConnection();
                using var cmd = new SqlCommand("dbo.FG_PDA_LOADING_COMPLETE", conn) { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.Add("@LicensePlate", SqlDbType.VarChar, 20).Value = truck;
                cmd.Parameters.Add("@ShipmentOrderID", SqlDbType.Int).Value = body.ShipmentOrderId;
                cmd.Parameters.Add("@StockIDs", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(body.StockIds);
                cmd.Parameters.Add("@OperatorID", SqlDbType.NVarChar, 450).Value = s.OperatorId;
                using var rdr = cmd.ExecuteReader();
                if (!rdr.Read())
                    return Results.Json(new LoadingResult(false, "Truck loading service returned no confirmation.", null, null, null), statusCode: 503);
                var id = GetInt(rdr, "LoadingID");
                var loadedCount = GetInt(rdr, "LoadedCount") ?? body.StockIds.Count;
                return Results.Ok(new LoadingResult(true,
                    $"{loadedCount} product(s) were loaded onto truck {truck}.", id,
                    new LoadingTruckRow($"TRUCK:{truck}", truck, true, "Loading confirmed."), null));
            }
            catch (SqlException ex) when (ex.Number is >= 51900 and <= 51919)
            {
                return Results.BadRequest(new LoadingResult(false, ex.Message, null, null, null));
            }
            catch
            {
                return Results.Json(new LoadingResult(false,
                    "Truck loading service is unavailable. Check the API and database connection.", null, null, null), statusCode: 503);
            }
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
            if (!TryNormalizeReturnBarcode(barcode, out var normalized, out var error))
                return Results.BadRequest(new ReturnResult(false, error, null, null));
            try
            {
                using var conn = factory.OpenConnection();
                using var cmd = new SqlCommand("dbo.FG_PDA_RETURN_SCAN", conn) { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.Add("@Barcode", SqlDbType.VarChar, 80).Value = normalized;
                using var rdr = cmd.ExecuteReader();
                if (!rdr.Read())
                    return Results.Json(new ReturnResult(false, "Customer return service returned no product.", null, null), statusCode: 503);
                return Results.Ok(new ReturnResult(true, "Product is eligible for customer return.", null, ReadReturnScanRow(rdr)));
            }
            catch (SqlException ex) when (ex.Number is >= 52000 and <= 52019)
            {
                return Results.BadRequest(new ReturnResult(false, ex.Message, null, null));
            }
            catch
            {
                return Results.Json(new ReturnResult(false,
                    "Customer return service is unavailable. Check the API and database connection.", null, null), statusCode: 503);
            }
        });

        g.MapPost("/return", (HttpContext ctx, ReturnReq body) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();

            if (!TryNormalizeReturnBarcode(body.Barcode, out var normalized, out var barcodeError))
                return Results.BadRequest(new ReturnResult(false, barcodeError, null, null));

            var returnReason = master.FindActiveCodeItem("FG_RETURN_REASON", body.ReturnReason?.Trim() ?? "")?.CodeValue;
            if (string.IsNullOrWhiteSpace(returnReason))
            {
                return Results.BadRequest(new ReturnResult(false,
                    "Select a valid return reason.", null, null));
            }
            var note = string.IsNullOrWhiteSpace(body.Note) ? null : body.Note.Trim();
            if (note?.Length > 500)
            {
                return Results.BadRequest(new ReturnResult(false,
                    "Return note must be 500 characters or fewer.", null, null));
            }

            try
            {
                using var conn = factory.OpenConnection();
                using var cmd = new SqlCommand("dbo.FG_PDA_RETURN_RECEIVE", conn) { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.Add("@Barcode", SqlDbType.VarChar, 80).Value = normalized;
                cmd.Parameters.Add("@ReturnReason", SqlDbType.VarChar, 60).Value = returnReason;
                cmd.Parameters.Add("@Note", SqlDbType.NVarChar, 500).Value = (object?)note ?? DBNull.Value;
                cmd.Parameters.Add("@OperatorID", SqlDbType.NVarChar, 450).Value = s.OperatorId;
                using var rdr = cmd.ExecuteReader();
                if (!rdr.Read())
                    return Results.Json(new ReturnResult(false, "Customer return service returned no confirmation.", null, null), statusCode: 503);
                var id = GetInt(rdr, "ReturnID");
                return Results.Ok(new ReturnResult(true, "Customer return received.", id, ReadReturnScanRow(rdr)));
            }
            catch (SqlException ex) when (ex.Number is >= 52000 and <= 52019)
            {
                return Results.BadRequest(new ReturnResult(false, ex.Message, null, null));
            }
            catch
            {
                return Results.Json(new ReturnResult(false,
                    "Customer return service is unavailable. Check the API and database connection.", null, null), statusCode: 503);
            }
        });

        g.MapGet("/returns", (HttpContext ctx) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            const string sql = """
                SELECT TOP 50
                    ReturnID, ReturnNumber, CustomerCode,
                    COALESCE(ItemNo,JSON_VALUE(ItemsJSON, '$[0].itemNo')) AS ItemNo,
                    COALESCE(ReturnQty,TRY_CONVERT(decimal(12,3), JSON_VALUE(ItemsJSON, '$[0].qty'))) AS Qty,
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
    private static string ReleaseScanErrorCode(int number) => number switch
    {
        51800 => "SLIP_NOT_FOUND",
        51801 => "SLIP_NOT_RELEASED",
        51802 => "LOT_NOT_FOUND",
        51803 => "LOT_ALREADY_SCANNED",
        51804 => "LOT_NOT_AVAILABLE",
        51805 => "LOT_ON_HOLD",
        51806 => "WRONG_CUSTOMER",
        51807 => "WRONG_PART",
        51808 => "QUANTITY_EXCEEDS_REMAINING",
        51809 => "FIFO_ORDER",
        _ => "INVALID_SCAN"
    };

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
        if (separator <= 0 || !value[..separator].Trim().Equals("TRUCK", StringComparison.OrdinalIgnoreCase))
        {
            error = "Invalid truck barcode. Expected TRUCK:<license plate>.";
            return false;
        }
        value = value[(separator + 1)..].Trim();

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

    private static ReturnScanRow ReadReturnScanRow(SqlDataReader rdr) => new(
        GetString(rdr, "Barcode") ?? "",
        GetInt(rdr, "StockID") ?? 0,
        GetString(rdr, "StockNumber"),
        GetInt(rdr, "LotID"),
        GetString(rdr, "LotNo"),
        GetInt(rdr, "ShipmentOrderID") ?? 0,
        GetString(rdr, "ShipOrderNumber"),
        GetString(rdr, "CustomerCode") ?? "",
        GetString(rdr, "ItemNo") ?? "",
        GetString(rdr, "ItemName"),
        GetDate(rdr, "ShippedAt") ?? DateTime.MinValue,
        GetDecimal(rdr, "Qty"));
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
                L.WoID,
                L.ItemNo,
                I.ItemName,
                COALESCE(NULLIF(Q.CustomerCode, ''), NULLIF(S.CustomerCode, '')) AS CustomerCode,
                CAST(COALESCE(NULLIF(Q.BatchQty, 0), NULLIF(L.RemainingQty, 0), NULLIF(L.BatchSize, 0), 0) AS DECIMAL(14,3)) AS Qty,
                I.DefaultUOM AS Unit,
                L.ProducedAt AS MfgDate,
                L.ExpiryDate,
                Q.InspectionNo AS QcInspectionNo,
                CASE WHEN UPPER(ISNULL(Q.Verdict, '')) IN ('PASS', 'PASSED', 'OK') THEN Q.InsEndTS END AS QcPassTs,
                CASE WHEN UPPER(ISNULL(Q.Verdict, '')) IN ('PASS', 'PASSED', 'OK') THEN 1 ELSE 0 END AS IsQcPassed,
                S.StockID AS ExistingStockId,
                S.Location AS ExistingLocation,
                S.Status AS ExistingStatus,
                P.PackSpecID,
                P.StorageMethod
            FROM dbo.tbl_Lot L
            LEFT JOIN dbo.MD_Item I
                ON I.ItemNo = L.ItemNo
            OUTER APPLY
            (
                SELECT TOP (1) QI.InspectionID, QI.InspectionNo, QI.CustomerCode, QI.BatchQty, QI.InsEndTS, QI.Verdict
                FROM dbo.QC_Inspection QI
                WHERE QI.LotID = L.LotID
                ORDER BY COALESCE(QI.InsEndTS, QI.InsStartTS, QI.CreatedTS) DESC, QI.InspectionID DESC
            ) Q
            OUTER APPLY
            (
                SELECT TOP (1) FS.StockID, FS.CustomerCode, FS.Location, FS.Status
                FROM dbo.FG_Inventory FS WITH (UPDLOCK, HOLDLOCK)
                WHERE FS.LotID = L.LotID
                  AND UPPER(ISNULL(FS.Status, '')) NOT IN ('CANCELED', 'CANCELLED')
                ORDER BY FS.StockTS DESC, FS.StockID DESC
            ) S
            OUTER APPLY
            (
                SELECT TOP (1)
                    PS.PackSpecID,
                    UPPER(ISNULL(NULLIF(PS.PackType, ''), 'LOCATION')) AS StorageMethod
                FROM dbo.MD_PackagingSpec PS
                WHERE PS.ItemID = L.ItemNo
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
            ORDER BY L.LotID DESC;
            """, conn, tx);
        cmd.Parameters.Add("@Barcode", SqlDbType.NVarChar, 80).Value = barcode;

        using var rdr = cmd.ExecuteReader();
        if (!rdr.Read()) return null;

        var existingStockId = GetInt(rdr, "ExistingStockId");
        var existingLocation = GetString(rdr, "ExistingLocation");
        var isQcPassed = GetBool(rdr, "IsQcPassed");
        var lotNo = GetString(rdr, "LotCode") ?? barcode;
        const string barcodeType = BarcodeLot;
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
                  AND ISNULL(ActiveFlag, 1) = 1
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
                    'AVAILABLE', 0, SYSDATETIME(), @OperatorID, SYSDATETIME());
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

        return stockId;
    }

    private static bool AcquirePutAwayLock(SqlConnection conn, SqlTransaction tx, string scope, string value)
    {
        using var cmd = new SqlCommand("""
            DECLARE @Result int;
            EXEC @Result = sys.sp_getapplock
                @Resource = @Resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 5000;
            SELECT @Result;
            """, conn, tx);
        cmd.Parameters.Add("@Resource", SqlDbType.NVarChar, 255).Value =
            $"FG_PUTAWAY_{scope}:{value.Trim().ToUpperInvariant()}";
        return Convert.ToInt32(cmd.ExecuteScalar()) >= 0;
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

    private static AdjustResult ExecuteAdjustSave(AmesConnectionFactory factory, AdjustSaveReq body, string userId, string operatorId)
    {
        try
        {
            using var conn = factory.OpenConnection();

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
            cmd.Parameters.Add("@SupervisorUserId", SqlDbType.NVarChar, 450).Value = operatorId;
            cmd.Parameters.Add("@SupervisorEmployeeNo", SqlDbType.NVarChar, 40).Value = userId;
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
