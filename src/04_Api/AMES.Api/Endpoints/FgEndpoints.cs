using AMES.Api.Auth;
using AMES.Api.Logging;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using System.Data;
using Microsoft.Data.SqlClient;

namespace AMES.Api.Endpoints;

public static class FgEndpoints
{

    // ── DTOs ─────────────────────────────────────────────────────────────
    public sealed record StockRow(string LotNo, string ItemNo, string? ItemName,
        int? LotId, string? CustomerCode, decimal Qty, string? Unit,
        string? Location, string? Status, DateTime? StockTs);
    public sealed record DashboardDto(int ShipmentsToday, int LotsShippedToday, decimal QtyShippedToday,
        int PendingReturns, int StockLots, decimal StockOnHand);
    public sealed record PutAwayWaitingRow(int LotId, string LotNo, string? WoNumber, string ItemNo,
        string? ItemName, string? CustomerCode, decimal Qty, string? Unit, DateTime? ProducedAt,
        DateTime? ReadyAt);
    public sealed record ReturnRow(int ReturnId, string CustomerId, int ShipmentId,
        string PartNo, string PartName, string? LotNo, decimal Qty, string? ReturnReason, string? Status, DateTime? ReceivedAt);
    public sealed record ReturnScanRow(string Barcode, int? LotId, string LotNo,
        int ShipmentId, string? OutboundBarcode, string CustomerId, string? CustomerName,
        string PartNo, string? PartName, DateTime ShippedAt, decimal Qty);
    public sealed record ReturnResult(bool Success, string Message, int? ReturnId, ReturnScanRow? Row);
    public sealed record AdjustScanRow(string ReceiveType, string? Yn, string LotNo, string Barcode,
        string? SourceTable, string? NoteNo, string? CaseBarcode, string? CaseNo, string? InvoiceNo,
        string? ContainerNo, string? PartNo, string? PartName, decimal Qty, string? Unit, string? PoNo,
        int? PoSeq, string? VendorId, string? VendorName, DateTime? ProductionDate, DateTime? DeliveryDate,
        DateTime? ArrivalDate, DateTime? ShipDate, DateTime? PackDate, string? ReceivedLocation,
        string? ReceivedStatus);
    public sealed record AdjustSaveReq(string? Mode, string Barcode, decimal DeltaQty, string ReasonCode,
        string? ReasonNote, bool SimulateFailure = false, decimal? ExpectedQty = null, string? ExpectedLocation = null);
    public sealed record AdjustResult(bool Success, string Message, AdjustScanRow? Row);

    public sealed record PutAwayScanRow(int? LotId, string LotNo, int? WoId,
        string ItemNo, string? ItemName, string? CustomerCode, decimal Qty, string? Unit,
        DateTime? MfgDate, DateTime? ExpiryDate, DateTime? ReadyAt,
        bool IsProductionCompleted, bool AlreadyStocked, string? ExistingLotNo, string? ExistingLocation,
        string? ExistingStatus, string BarcodeType, string StorageMethod, string NextScanType,
        string NextScanLabel, string? PackSpecId, string Message);
    public sealed record PutAwayLocationRow(string LocationId, string? LocationName, string? ZoneCode,
        string? Aisle, string? Bay, string? Slot, decimal Capacity, decimal CurrentQty,
        decimal AvailableQty, string? CurrentCustomerCode, bool IsValid, string Message,
        string ScanType, string ScannedBarcode, string? AreaCode = null);
    public sealed record PutAwayConfirmReq(string Barcode, string LocationId);
    public sealed record PutAwayResult(bool Success, string Message, string? InventoryLotNo, PutAwayScanRow? Row,
        PutAwayLocationRow? Location);
    public sealed record OutboundUnitItemRow(string LotNo, string UnitType, string PartNo, string? PartName,
        decimal Qty, string? LocationNo);
    public sealed record OutboundUnitRow(string OutboundBarcode, string UnitType, string? LocationNo,
        decimal TotalQty, int ItemCount, List<OutboundUnitItemRow> Items, string? CustomerId = null, string? CustomerName = null);
    public sealed record OutboundUnitResult(bool Success, string Message, OutboundUnitRow? Unit,
        int ProcessedCount = 0, int? ShipmentId = null);
    public sealed record OutboundUnitReq(string Barcode, string? CustomerId = null);
    public sealed record OutboundCustomerRow(string CustomerId, string CustomerName);
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

        if (PdaTestDatabaseGuard.AllowsReset(app, factory)) g.MapPost("/test/ppt-reset/{screen}", (HttpContext ctx, string screen) =>
        {
            if (ctx.GetSession() is not { } session) return Results.Unauthorized();
            if (!PdaScenarioUsers.IsAny(session.EmployeeNo))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (screen is not ("qc" or "putaway" or "inventory" or "outbound" or "return" or "adjust" or "history"))
                return Results.BadRequest(new { Message = "Unknown PPT test screen." });
            try
            {
                using var connection = factory.OpenConnection();
                var procedure = screen switch
                {
                    "history" => "dbo.FG_PDA_HISTORY_TEST_RESET",
                    "outbound" => "dbo.FG_PDA_OUTBOUND_TEST_RESET",
                    _ => "dbo.FG_PDA_PPT_TEST_RESET"
                };
                using var command = new SqlCommand(procedure, connection) { CommandType = CommandType.StoredProcedure };
                if (screen is not ("history" or "outbound"))
                    command.Parameters.Add("@Screen", SqlDbType.VarChar, 10).Value = screen;
                command.ExecuteNonQuery();
                return Results.Ok(new { Success = true });
            }
            catch (SqlException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).WithTags("PDA Test Scenarios");

        g.MapGet("/transactions", (HttpContext ctx, string? search, DateTime? dateFrom, DateTime? dateTo) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            if (dateFrom.HasValue && dateTo.HasValue && dateFrom.Value.Date > dateTo.Value.Date)
                return Results.BadRequest(new { Message = "From date cannot be after To date." });

            using var connection = factory.OpenConnection();
            using var command = new SqlCommand(PdaFinishedGoodsSql.Transactions, connection)
            {
                CommandType = CommandType.Text
            };
            command.Parameters.Add("@SearchText", SqlDbType.NVarChar, 120).Value = (object?)search?.Trim() ?? DBNull.Value;
            command.Parameters.Add("@DateFrom", SqlDbType.Date).Value = (object?)dateFrom?.Date ?? DBNull.Value;
            command.Parameters.Add("@DateTo", SqlDbType.Date).Value = (object?)dateTo?.Date ?? DBNull.Value;
            using var reader = command.ExecuteReader();
            var rows = new List<WhEndpoints.WarehouseTransactionRow>();
            while (reader.Read()) rows.Add(WhEndpoints.ReadWarehouseTransactionRow(reader));
            return Results.Ok(rows);
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
            if (string.IsNullOrWhiteSpace(body.Barcode) || body.Barcode.Length>50
                || body.DeltaQty==0 || decimal.Truncate(body.DeltaQty)!=body.DeltaQty
                || body.ExpectedQty is null || body.ExpectedQty<0 || body.ExpectedLocation is null)
                return Results.BadRequest(new AdjustResult(false, "Scan stock again. A whole-number adjustment and current quantity/location are required.", null));
            if (master.FindActiveCodeItem("INV_ADJUST_REASON", body.ReasonCode?.Trim() ?? "") is null)
                return Results.BadRequest(new AdjustResult(false, "Select a valid reason code.", null));

            var result = ExecuteAdjustSave(factory, body, s.EmployeeNo);
            WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                s, "ADJUST_SAVE", "FG007", "LOT", body.Barcode,
                result.Success ? "SUCCESS" : "FAIL", result.Message,
                lotNo: result.Row?.LotNo ?? body.Barcode, partNo: result.Row?.PartNo,
                locationId: result.Row?.ReceivedLocation, qty: body.DeltaQty));
            return Results.Ok(result);
        });

        // FG-01 POP-completed FG LOTs waiting for Put-Away.
        g.MapGet("/putaway/waiting", (HttpContext ctx) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            const string sql = """
                SELECT
                    L.LotID,
                    L.LotCode AS LotNo,
                    W.WoNumber,
                    COALESCE(NULLIF(L.ItemNo, ''), W.ItemNo) AS ItemNo,
                    I.ItemName,
                    IMG.CustomerCode,
                    CAST(COALESCE(NULLIF(PR.GoodQty, 0), NULLIF(L.RemainingQty, 0),
                         NULLIF(L.BatchSize, 0), NULLIF(W.CompletedQty, 0), 0) AS DECIMAL(14,3)) AS Qty,
                    I.DefaultUOM AS Unit,
                    L.ProducedAt,
                    COALESCE(PR.EntryAt, IMG.ConfirmedAt, L.ProducedAt, L.CreatedTS) AS ReadyAt
                FROM dbo.tbl_Lot L
                LEFT JOIN dbo.PP_WorkOrder W ON W.WoID = L.WoID
                LEFT JOIN dbo.MD_Item I ON I.ItemNo = COALESCE(NULLIF(L.ItemNo, ''), W.ItemNo)
                LEFT JOIN dbo.PR_ImgLot IMG ON IMG.LotID = L.LotID
                CROSS APPLY
                (
                    SELECT SUM(COALESCE(R.GoodQty, 0)) AS GoodQty, MAX(R.EntryAt) AS EntryAt
                    FROM dbo.PR_ProductionResult R
                    WHERE R.LotID = L.LotID
                      AND UPPER(ISNULL(R.ProcessCode, '')) = UPPER(ISNULL(L.ProcessCode, ''))
                      AND ISNULL(R.DefectFlag, 0) = 0
                ) PR
                WHERE UPPER(ISNULL(L.Status, '')) = 'CONFIRMED'
                  AND UPPER(ISNULL(I.ItemType, '')) = 'ASSY'
                  AND COALESCE(PR.GoodQty, 0) > 0
                  AND NOT EXISTS
                (
                    SELECT 1
                    FROM dbo.WH_Inventory S
                    WHERE S.LotNo = L.LotCode
                      AND S.Qty > 0
                )
                ORDER BY COALESCE(IMG.ConfirmedAt, PR.EntryAt, L.ProducedAt, L.CreatedTS), L.LotID;
                """;
            return Query(factory, sql, r => new PutAwayWaitingRow(
                (int)r["LotID"], r["LotNo"] as string ?? "", r["WoNumber"] as string,
                r["ItemNo"] as string ?? "", r["ItemName"] as string, r["CustomerCode"] as string,
                r.GetDecimal(r.GetOrdinal("Qty")), r["Unit"] as string,
                r["ProducedAt"] as DateTime?, r["ReadyAt"] as DateTime?));
        });

        // FG-02 PDA Put-Away - scan a POP-completed FG LOT.
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

            var success = row.IsProductionCompleted && !row.AlreadyStocked && row.Qty > 0;
            WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                s, "SCAN_FG_LOT", "FG002", row.BarcodeType, parsed.Raw, success ? "SUCCESS" : "FAIL", row.Message,
                lotNo: row.LotNo, partNo: row.ItemNo, locationId: row.ExistingLocation, qty: row.Qty));

            return Results.Ok(new PutAwayResult(success, row.Message, row.ExistingLotNo, row, null));
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

        // FG Put-away writes inventory and its transaction together.
        g.MapPost("/putaway/confirm", (HttpContext ctx, PutAwayConfirmReq body) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(body.Barcode))
                return Results.BadRequest(new PutAwayResult(false, "Scan FG LOT first.", null, null, null));
            if (string.IsNullOrWhiteSpace(body.LocationId))
                return Results.BadRequest(new PutAwayResult(false, "Scan Location No first.", null, null, null));

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
                if (!row.IsProductionCompleted)
                {
                    tx.Rollback();
                    WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                        s, "FG_PUTAWAY", "FG002", "LOT", body.Barcode, "FAIL", row.Message,
                        lotNo: row.LotNo, partNo: row.ItemNo, qty: row.Qty));
                    return Results.BadRequest(new PutAwayResult(false, row.Message, null, row, null));
                }
                if (row.Qty <= 0)
                {
                    tx.Rollback();
                    return Results.BadRequest(new PutAwayResult(false, row.Message, null, row, null));
                }
                if (row.AlreadyStocked)
                {
                    tx.Rollback();
                    WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                        s, "FG_PUTAWAY", "FG002", "LOT", body.Barcode, "FAIL", row.Message,
                        lotNo: row.LotNo, partNo: row.ItemNo, locationId: row.ExistingLocation, qty: row.Qty));
                    return Results.BadRequest(new PutAwayResult(false, row.Message, row.ExistingLotNo, row, null));
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

                var stockId = SavePutAwayStock(conn, tx, row.LotNo, location.LocationId, s.EmployeeNo);

                var updated = row with
                {
                    AlreadyStocked = true,
                    ExistingLotNo = stockId,
                    ExistingLocation = location.LocationId,
                    ExistingStatus = "AVAILABLE",
                    Message = "FG Put-Away confirmed."
                };
                var response = Results.Ok(new PutAwayResult(true, "FG Put-Away confirmed.", stockId, updated, location));
                tx.Commit();
                return response;
            }
            catch (SqlException ex) when (ex.Number is >= 52510 and <= 52518)
            {
                if (tx.Connection is not null) tx.Rollback();
                return Results.BadRequest(new PutAwayResult(false, ex.Message, null, null, null));
            }
            catch
            {
                if (tx.Connection is not null) tx.Rollback();
                throw;
            }
        });

        // FG-02 Inventory
        g.MapGet("/inventory", (HttpContext ctx, string? q) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            using var connection = factory.OpenConnection();
            using var command = new SqlCommand(PdaFinishedGoodsSql.Inventory, connection)
            {
                CommandType = CommandType.Text,
                CommandTimeout = 15
            };
            command.Parameters.Add("@SearchText", SqlDbType.NVarChar, 120).Value =
                string.IsNullOrWhiteSpace(q) ? DBNull.Value : q.Trim();
            using var reader = command.ExecuteReader();
            var rows = new List<StockRow>();
            while (reader.Read())
            {
                rows.Add(new StockRow(
                    GetString(reader, "LotNo") ?? "",
                    GetString(reader, "ItemNo") ?? "",
                    GetString(reader, "ItemName"),
                    GetInt(reader, "LotID"),
                    GetString(reader, "CustomerCode"),
                    GetDecimal(reader, "Qty"),
                    GetString(reader, "Unit"),
                    GetString(reader, "Location"),
                    GetString(reader, "Status"),
                    GetDate(reader, "StockTS")));
            }
            return Results.Ok(rows);
        });

        g.MapGet("/outbound/customers", (HttpContext ctx) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            using var conn = factory.OpenConnection();
            using var cmd = new SqlCommand("SELECT CustomerID,COALESCE(NULLIF(CustomerNameEn,''),CustomerName) CustomerName FROM dbo.MD_Customer WHERE UPPER(ISNULL(Status,'ACTIVE')) NOT IN('INACTIVE','DISABLED') ORDER BY CustomerID;", conn) { CommandTimeout = 10 };
            using var reader = cmd.ExecuteReader();
            var rows = new List<OutboundCustomerRow>();
            while (reader.Read()) rows.Add(new OutboundCustomerRow((string)reader["CustomerID"],(string)reader["CustomerName"]));
            return Results.Ok(rows);
        });

        g.MapGet("/outbound/scan", (HttpContext ctx, string barcode) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            if (!FgOutboundBarcode.TryNormalize(barcode, out var normalized, out var error))
                return Results.BadRequest(new OutboundUnitResult(false, error, null));
            try
            {
                using var conn = factory.OpenConnection();
                if (FindOutboundLocationError(conn, null, normalized) is { } locationError)
                    return Results.BadRequest(new OutboundUnitResult(false, locationError, null));
                using var cmd = new SqlCommand(PdaFinishedGoodsSql.OutboundScan, conn)
                {
                    CommandType = CommandType.Text,
                    CommandTimeout = 15
                };
                cmd.Parameters.Add("@Barcode", SqlDbType.NVarChar, 50).Value = normalized;
                using var rdr = cmd.ExecuteReader();
                if (!rdr.Read())
                    return Results.Json(new OutboundUnitResult(false, "Outbound service returned no inventory unit.", null), statusCode: 503);

                var outboundBarcode = GetString(rdr, "OutboundBarcode") ?? "";
                var unitType = GetString(rdr, "UnitType") ?? "PART";
                var locationNo = GetString(rdr, "LocationNo");
                var totalQty = GetDecimal(rdr, "TotalQty");
                var itemCount = GetInt(rdr, "ItemCount") ?? 0;
                var customerId = GetString(rdr, "CustomerID");
                var customerName = GetString(rdr, "CustomerName");
                var items = new List<OutboundUnitItemRow>();
                if (rdr.NextResult())
                {
                    while (rdr.Read())
                    {
                        items.Add(new OutboundUnitItemRow(
                            GetString(rdr, "LotNo") ?? "",
                            GetString(rdr, "UnitType") ?? "PART",
                            GetString(rdr, "PartNo") ?? "",
                            GetString(rdr, "PartName"),
                            GetDecimal(rdr, "Qty"),
                            GetString(rdr, "LocationNo")));
                    }
                }
                return Results.Ok(new OutboundUnitResult(true,
                    $"{unitType} loaded. {itemCount} inventory LOT(s) are ready for outbound.",
                    new OutboundUnitRow(outboundBarcode, unitType, locationNo, totalQty, itemCount, items, customerId, customerName)));
            }
            catch (SqlException ex) when (ex.Number is >= 52000 and <= 52019)
            {
                return Results.BadRequest(new OutboundUnitResult(false,
                    ex.Number is 52000 or 52001 ? ExplainOutboundFailure(factory, normalized, ex.Message) : ex.Message, null));
            }
            catch
            {
                return Results.Json(new OutboundUnitResult(false,
                    "Outbound service is unavailable. Check the API and database connection.", null),
                    statusCode: 503);
            }
        });

        g.MapPost("/outbound", (HttpContext ctx, OutboundUnitReq body) =>
        {
            if (ctx.GetSession() is not { } session) return Results.Unauthorized();
            if (!FgOutboundBarcode.TryNormalize(body.Barcode, out var normalized, out var error))
                return Results.BadRequest(new OutboundUnitResult(false, error, null));
            if (body.CustomerId is not null && !LoginInput.Valid(body.CustomerId, 20))
                return Results.BadRequest(new OutboundUnitResult(false, "Select a valid customer ID within 20 bytes.", null));
            try
            {
                using var conn = factory.OpenConnection();
                using var tx = conn.BeginTransaction();
                if (FindOutboundLocationError(conn, tx, normalized) is { } locationError)
                    return Results.BadRequest(new OutboundUnitResult(false, locationError, null));
                using var cmd = new SqlCommand(PdaFinishedGoodsSql.OutboundComplete, conn)
                {
                    Transaction = tx,
                    CommandType = CommandType.Text,
                    CommandTimeout = 30
                };
                cmd.Parameters.Add("@Barcode", SqlDbType.NVarChar, 50).Value = normalized;
                ColumnParams.Add(cmd, "@OperatorID", "WH_InventoryTransaction.CreatedBy", session.OperatorId);
                ColumnParams.Add(cmd, "@CustomerID", "MD_Customer.CustomerID", body.CustomerId);
                using var rdr = cmd.ExecuteReader();
                if (!rdr.Read())
                    return Results.Json(new OutboundUnitResult(false, "Outbound service returned no result.", null), statusCode: 503);
                var processed = GetInt(rdr, "ProcessedCount") ?? 0;
                var totalQty = GetDecimal(rdr, "TotalQty");
                var unitType = GetString(rdr, "UnitType") ?? "UNIT";
                var shipmentId = GetInt(rdr, "ShipmentID");
                // Finish all result sets before committing the outer transaction.
                while (rdr.NextResult()) { }
                rdr.Close();
                tx.Commit();
                return Results.Ok(new OutboundUnitResult(true,
                    $"Shipment #{shipmentId}: {unitType}, {processed} inventory LOT(s), {totalQty:N0} EA processed.", null, processed, shipmentId));
            }
            catch (SqlException ex) when (ex.Number is >= 52000 and <= 52019)
            {
                return Results.BadRequest(new OutboundUnitResult(false,
                    ex.Number is 52000 or 52001 ? ExplainOutboundFailure(factory, normalized, ex.Message) : ex.Message, null));
            }
            catch
            {
                return Results.Json(new OutboundUnitResult(false,
                    "Outbound confirmation could not be verified. Check transaction history before retrying; the stock may already have been processed.", null),
                    statusCode: 503);
            }
        });

        // FG-09 Dashboard rollup
        g.MapGet("/dashboard", (HttpContext ctx) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            using var conn = factory.OpenConnection();
            using var cmd = new SqlCommand("""
                DECLARE @Today date=CONVERT(date,SYSDATETIME());
                SELECT
                    (SELECT COUNT(*) FROM dbo.FG_Shipment WHERE ShippedAt>=@Today) ShipmentsToday,
                    (SELECT COUNT(*) FROM dbo.WH_InventoryTransaction T JOIN dbo.FG_Shipment S ON S.ShipmentID=T.SourceID
                        WHERE T.SourceType='FG_OUTBOUND' AND T.TransactionType='OUT' AND S.ShippedAt>=@Today) LotsShippedToday,
                    (SELECT COALESCE(SUM(-T.QtyChange),0) FROM dbo.WH_InventoryTransaction T JOIN dbo.FG_Shipment S ON S.ShipmentID=T.SourceID
                        WHERE T.SourceType='FG_OUTBOUND' AND T.TransactionType='OUT' AND S.ShippedAt>=@Today) QtyShippedToday,
                    (SELECT COUNT(*) FROM dbo.FG_CustomerReturn WHERE Status IN('Open','Inspecting')) PendingReturns,
                    COUNT(*) StockLots,COALESCE(SUM(W.Qty),0) StockOnHand
                FROM dbo.WH_Inventory W JOIN dbo.MD_Location L ON L.LocationID=W.LocationNo
                WHERE W.Qty>0 AND UPPER(ISNULL(L.LocationType,''))='FG';
                """, conn) { CommandTimeout = 15 };
            using var rdr=cmd.ExecuteReader();
            rdr.Read();
            return Results.Ok(new DashboardDto((int)rdr["ShipmentsToday"],(int)rdr["LotsShippedToday"],
                Convert.ToDecimal(rdr["QtyShippedToday"]),(int)rdr["PendingReturns"],(int)rdr["StockLots"],Convert.ToDecimal(rdr["StockOnHand"])));
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
                using var cmd = new SqlCommand(PdaFinishedGoodsSql.ReturnScan, conn) { CommandType = CommandType.Text };
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
                using var cmd = new SqlCommand(PdaFinishedGoodsSql.ReturnReceive, conn) { CommandType = CommandType.Text };
                cmd.Parameters.Add("@Barcode", SqlDbType.VarChar, 80).Value = normalized;
                cmd.Parameters.Add("@ReturnReason", SqlDbType.VarChar, 60).Value = returnReason;
                cmd.Parameters.Add("@Note", SqlDbType.NVarChar, 500).Value = (object?)note ?? DBNull.Value;
                ColumnParams.Add(cmd, "@OperatorID", "WH_InventoryTransaction.CreatedBy", s.OperatorId);
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
                SELECT TOP 50 ReturnID,CustomerID,ShipmentID,PartNo,PartName,LotNo,
                    ReturnQty AS Qty,ReturnReason,Status,ReceivedAt
                FROM dbo.FG_CustomerReturn ORDER BY ReceivedAt DESC,ReturnID DESC;
                """;
            return Query(factory,sql,r=>new ReturnRow((int)r["ReturnID"],(string)r["CustomerID"],
                (int)r["ShipmentID"],(string)r["PartNo"],(string)r["PartName"],r["LotNo"] as string,
                Convert.ToDecimal(r["Qty"]),r["ReturnReason"] as string,r["Status"] as string,r["ReceivedAt"] as DateTime?));
        });
    }

    // ── helpers ──────────────────────────────────────────────────────────
    private static ReturnScanRow ReadReturnScanRow(SqlDataReader rdr) => new(
        GetString(rdr, "Barcode") ?? "",
        GetInt(rdr, "LotID"),
        GetString(rdr, "LotNo") ?? "",
        GetInt(rdr, "ShipmentID") ?? 0,
        GetString(rdr, "OutboundBarcode"),
        GetString(rdr, "CustomerID") ?? "",
        GetString(rdr, "CustomerName"),
        GetString(rdr, "PartNo") ?? "",
        GetString(rdr, "PartName"),
        GetDate(rdr, "ShippedAt") ?? DateTime.MinValue,
        GetDecimal(rdr, "Qty"));
    private static string? FindOutboundLocationError(SqlConnection conn, SqlTransaction? tx, string barcode)
    {
        using var cmd = new SqlCommand("""
            ;WITH UnitTree AS
            (
                SELECT LotNo,ParentLotNo,UnitType,PartNo,LocationNo,Qty FROM dbo.WH_Inventory WITH(UPDLOCK,HOLDLOCK) WHERE LotNo=@Barcode
                UNION ALL
                SELECT C.LotNo,C.ParentLotNo,C.UnitType,C.PartNo,C.LocationNo,C.Qty
                FROM dbo.WH_Inventory C WITH(UPDLOCK,HOLDLOCK) JOIN UnitTree P ON C.ParentLotNo=P.LotNo
            )
            SELECT TOP(1) T.LotNo,T.UnitType,T.PartNo,T.LocationNo,L.LocationID AS MasterLocation,L.LocationType
            FROM UnitTree T LEFT JOIN dbo.MD_Location L WITH(HOLDLOCK) ON L.LocationID=T.LocationNo
            WHERE T.Qty>0 AND (T.PartNo IS NOT NULL OR UPPER(ISNULL(T.UnitType,'')) NOT IN('PALLET','CASE'))
              AND NOT EXISTS(SELECT 1 FROM UnitTree C WHERE C.ParentLotNo=T.LotNo AND C.Qty>0)
              AND (NULLIF(LTRIM(RTRIM(T.PartNo)),'') IS NULL OR NULLIF(LTRIM(RTRIM(T.UnitType)),'') IS NULL
                   OR NULLIF(LTRIM(RTRIM(T.LocationNo)),'') IS NULL OR L.LocationID IS NULL OR UPPER(ISNULL(L.LocationType,''))<>'FG')
            ORDER BY T.LotNo
            OPTION(MAXRECURSION 100);
            """, conn, tx) { CommandTimeout = 15 };
        cmd.Parameters.Add("@Barcode", SqlDbType.NVarChar, 50).Value = barcode;
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        var lot = GetString(reader, "LotNo") ?? barcode;
        var unit = GetString(reader, "UnitType");
        var missingPart = string.IsNullOrWhiteSpace(GetString(reader, "PartNo"));
        if (missingPart || string.IsNullOrWhiteSpace(unit))
            return FgOutboundBarcode.Failure(lot, true, unit, false, null, false, false, missingPart);
        return FgOutboundBarcode.LocationFailure(lot, GetString(reader, "LocationNo"),
            GetString(reader, "MasterLocation"), GetString(reader, "LocationType"));
    }

    private static string ExplainOutboundFailure(AmesConnectionFactory factory, string barcode, string fallback)
    {
        try
        {
            using var conn = factory.OpenConnection();
            using var cmd = new SqlCommand("""
                ;WITH UnitTree AS
                (
                    SELECT LotNo, ParentLotNo, UnitType, PartNo, Qty FROM dbo.WH_Inventory WHERE LotNo=@Barcode
                    UNION ALL
                    SELECT C.LotNo,C.ParentLotNo,C.UnitType,C.PartNo,C.Qty
                    FROM dbo.WH_Inventory C JOIN UnitTree P ON C.ParentLotNo=P.LotNo
                )
                SELECT
                    CASE WHEN EXISTS(SELECT 1 FROM dbo.WH_Inventory WHERE LotNo=@Barcode) THEN 1 ELSE 0 END AS InventoryExists,
                    (SELECT TOP(1) UnitType FROM dbo.WH_Inventory WHERE LotNo=@Barcode) AS UnitType,
                    CASE WHEN EXISTS(SELECT 1 FROM dbo.tbl_Lot WHERE LotCode=@Barcode) THEN 1 ELSE 0 END AS ProductionLotExists,
                    (SELECT TOP(1) Status FROM dbo.tbl_Lot WHERE LotCode=@Barcode) AS ProductionStatus,
                    CASE WHEN EXISTS(SELECT 1 FROM dbo.MD_Location WHERE LocationID=@Barcode) THEN 1 ELSE 0 END AS LocationBarcode,
                    CASE WHEN EXISTS(SELECT 1 FROM dbo.WH_InventoryTransaction T
                         WHERE T.SourceType='FG_OUTBOUND' AND
                         (T.LotNo=@Barcode OR EXISTS(SELECT 1 FROM UnitTree U WHERE U.LotNo=T.LotNo)))
                         THEN 1 ELSE 0 END AS PreviouslyOutbound,
                    CASE WHEN EXISTS(SELECT 1 FROM UnitTree T WHERE T.Qty>0 AND UPPER(ISNULL(T.UnitType,'')) NOT IN('PALLET','CASE')
                         AND NULLIF(LTRIM(RTRIM(T.PartNo)),'') IS NULL
                         AND NOT EXISTS(SELECT 1 FROM UnitTree C WHERE C.ParentLotNo=T.LotNo AND C.Qty>0))
                         THEN 1 ELSE 0 END AS MissingPart
                OPTION(MAXRECURSION 100);
                """, conn) { CommandTimeout = 5 };
            cmd.Parameters.Add("@Barcode", SqlDbType.NVarChar, 50).Value = barcode;
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return fallback;
            return FgOutboundBarcode.Failure(barcode, GetInt(reader, "InventoryExists") == 1,
                GetString(reader, "UnitType"), GetInt(reader, "ProductionLotExists") == 1,
                GetString(reader, "ProductionStatus"), GetInt(reader, "LocationBarcode") == 1,
                GetInt(reader, "PreviouslyOutbound") == 1, GetInt(reader, "MissingPart") == 1);
        }
        catch
        {
            return "Outbound was rejected, but barcode details could not be loaded. Check the API/database connection and scan again.";
        }
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

        if (value.Length is < 3 or > 50)
        {
            error = "Barcode length must be between 3 and 50 characters.";
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
                NULLIF(IMG.CustomerCode, '') AS CustomerCode,
                CAST(COALESCE(S.Qty, NULLIF(PR.GoodQty, 0), NULLIF(L.RemainingQty, 0), NULLIF(L.BatchSize, 0), 0) AS DECIMAL(14,3)) AS Qty,
                I.DefaultUOM AS Unit,
                L.ProducedAt AS MfgDate,
                L.ExpiryDate,
                COALESCE(PR.EntryAt, IMG.ConfirmedAt, L.ProducedAt, L.CreatedTS) AS ReadyAt,
                CASE WHEN UPPER(ISNULL(L.Status, '')) IN ('CONFIRMED', 'STOCKED')
                           AND I.ItemType='ASSY' AND ISNULL(I.ActiveFlag,1)=1
                           AND COALESCE(PR.GoodQty, 0) > 0 THEN 1 ELSE 0 END AS IsProductionCompleted,
                S.LotNo AS ExistingLotNo,
                S.LocationNo AS ExistingLocation,
                S.Status AS ExistingStatus,
                CASE WHEN (SELECT TOP(1) T.SourceType FROM dbo.WH_InventoryTransaction T
                    WHERE T.LotNo COLLATE DATABASE_DEFAULT=L.LotCode COLLATE DATABASE_DEFAULT AND T.QtyChange<>0
                    ORDER BY T.TransactionTime DESC,T.TransactionID DESC)='FG_OUTBOUND'
                    THEN 1 ELSE 0 END AS AlreadyShipped
            FROM dbo.tbl_Lot L
            LEFT JOIN dbo.MD_Item I
                ON I.ItemNo = L.ItemNo
            LEFT JOIN dbo.PR_ImgLot IMG
                ON IMG.LotID = L.LotID
            OUTER APPLY
            (
                SELECT SUM(COALESCE(R.GoodQty, 0)) AS GoodQty, MAX(R.EntryAt) AS EntryAt
                FROM dbo.PR_ProductionResult R
                WHERE R.LotID = L.LotID
                  AND UPPER(ISNULL(R.ProcessCode, '')) = UPPER(ISNULL(L.ProcessCode, ''))
                  AND ISNULL(R.DefectFlag, 0) = 0
            ) PR
            OUTER APPLY
            (
                SELECT TOP (1)
                    FS.LotNo,
                    FS.Qty,
                    FS.LocationNo,
                    'AVAILABLE' AS Status
                FROM dbo.WH_Inventory FS WITH (UPDLOCK, HOLDLOCK)
                WHERE FS.LotNo = L.LotCode
                ORDER BY FS.ReceivedAt DESC, FS.LotNo DESC
            ) S
            WHERE UPPER(L.LotCode) = UPPER(@Barcode)
            ORDER BY L.LotID DESC;
            """, conn, tx);
        cmd.Parameters.Add("@Barcode", SqlDbType.NVarChar, 80).Value = barcode;

        using var rdr = cmd.ExecuteReader();
        if (!rdr.Read()) return null;

        var existingLotNo = GetString(rdr, "ExistingLotNo");
        var existingLocation = GetString(rdr, "ExistingLocation");
        var isProductionCompleted = GetBool(rdr, "IsProductionCompleted");
        var lotNo = GetString(rdr, "LotCode") ?? barcode;
        const string barcodeType = BarcodeLot;
        const string storageMethod = BarcodeLocation;
        var nextScanType = NextScanTypeForStorage(storageMethod);
        var nextScanLabel = NextScanLabel(nextScanType);
        var message = !isProductionCompleted
            ? "POP production completion is required before FG Put-Away."
            : GetDecimal(rdr, "Qty") <= 0
                ? GetBool(rdr, "AlreadyShipped")
                    ? "This product has already been shipped. Put-away is not allowed."
                    : "This LOT has no available quantity for put-away."
                : !string.IsNullOrWhiteSpace(existingLocation)
                    ? $"This FG LOT is already stocked at {existingLocation}."
                    : $"FG LOT matched. Scan {BarcodeKindLabel(nextScanType)}.";

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
            GetDate(rdr, "ReadyAt"),
            isProductionCompleted,
            !string.IsNullOrWhiteSpace(existingLocation) && GetDecimal(rdr, "Qty") > 0,
            existingLotNo,
            existingLocation,
            GetString(rdr, "ExistingStatus"),
            barcodeType,
            storageMethod,
            nextScanType,
            nextScanLabel,
            null,
            message);
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
                SELECT FS.LocationNo,
                       CAST(SUM(ISNULL(FS.Qty, 0)) AS DECIMAL(14,3)) AS CurrentQty,
                       CAST(NULL AS varchar(40)) AS CurrentCustomerCode
                FROM dbo.WH_Inventory FS
                WHERE FS.Qty > 0
                GROUP BY FS.LocationNo
            )
            SELECT L.LocationID, L.LocationName, L.ZoneCode, L.AreaCode, L.Aisle, L.Bay, L.Slot,
                   CAST(ISNULL(L.Capacity, 0) AS DECIMAL(14,3)) AS Capacity,
                   CAST(ISNULL(S.CurrentQty, 0) AS DECIMAL(14,3)) AS CurrentQty,
                   CAST(CASE WHEN L.Capacity > 0 THEN L.Capacity - ISNULL(S.CurrentQty, 0) ELSE 0 END AS DECIMAL(14,3)) AS AvailableQty,
                   S.CurrentCustomerCode,
                   CASE WHEN UPPER(ISNULL(L.LocationType, '')) = 'FG' THEN 1 ELSE 0 END AS IsFgLocation
            FROM dbo.MD_Location L
            LEFT JOIN StockByLocation S
                ON S.LocationNo = L.LocationID
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
        if (row.Capacity > 0 && row.AvailableQty < qty)
            return row with { IsValid = false, Message = $"Location capacity is short by {qty - row.AvailableQty:0.###}.", ScanType = expected, ScannedBarcode = parsed.Raw };

        return row with { IsValid = true, Message = $"{BarcodeKindLabel(expected)} ready for FG Put-Away.", ScanType = expected, ScannedBarcode = parsed.Raw };
    }

    private static string SavePutAwayStock(SqlConnection conn, SqlTransaction tx, string lotNo, string location, string operatorId)
    {
        using var cmd = new SqlCommand(PdaFinishedGoodsSql.PutAway, conn, tx)
        {
            CommandType = CommandType.Text,
            CommandTimeout = 15
        };
        cmd.Parameters.Add("@Barcode", SqlDbType.NVarChar, 50).Value = lotNo;
        cmd.Parameters.Add("@Location", SqlDbType.VarChar, 50).Value = location;
        cmd.Parameters.Add("@OperatorID", SqlDbType.VarChar, 20).Value = operatorId;
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) throw new InvalidOperationException("Put-away returned no confirmation.");
        return GetString(reader, "LotNo") ?? lotNo;
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
        var valid = (capacity <= 0 || availableQty >= qty) &&
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
            scannedBarcode ?? GetString(rdr, "LocationID") ?? "",
            GetString(rdr, "AreaCode"));
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

    private static IResult Query<T>(AmesConnectionFactory factory, string sql, Func<SqlDataReader, T> map)
    {
        using var conn = factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        using var rdr  = cmd.ExecuteReader();
        var list = new List<T>();
        while (rdr.Read()) list.Add(map(rdr));
        return Results.Ok(list);
    }
    private static IResult QueryWithParam<T>(AmesConnectionFactory factory, string sql, string p, string? column, object v,
                                              Func<SqlDataReader, T> map)
    {
        using var conn = factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        ColumnParams.AddFilter(cmd, p, column, v);
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
        using var cmd = new SqlCommand(PdaFinishedGoodsSql.AdjustScan, conn)
        {
            CommandType = CommandType.Text,
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
            using var conn = factory.OpenConnection();
            using var tx = conn.BeginTransaction();
            using (var check = new SqlCommand("SELECT Qty,LocationNo FROM dbo.WH_Inventory WITH(UPDLOCK,ROWLOCK) WHERE LotNo=@Lot;", conn, tx))
            {
                ColumnParams.AddFilter(check,"@Lot","WH_Inventory.LotNo",body.Barcode.Trim());
                using var current = check.ExecuteReader();
                if (!current.Read() || current.GetDecimal(0)!=body.ExpectedQty
                    || !string.Equals(current[1] as string ?? "",body.ExpectedLocation,StringComparison.OrdinalIgnoreCase))
                    return new(false,"Stock quantity or location changed. Scan again.",null);
            }
            using var cmd = new SqlCommand(PdaFinishedGoodsSql.AdjustSave, conn, tx)
            {
                CommandType = CommandType.Text,
                CommandTimeout = 15
            };
            cmd.Parameters.Add("@ScanText", SqlDbType.NVarChar, 80).Value = body.Barcode.Trim();
            AddDecimal(cmd, "@DeltaQty", body.DeltaQty);
            cmd.Parameters.Add("@ReasonCode", SqlDbType.NVarChar, 30).Value = body.ReasonCode.Trim();
            cmd.Parameters.Add("@ReasonNote", SqlDbType.NVarChar, 500).Value =
                string.IsNullOrWhiteSpace(body.ReasonNote) ? DBNull.Value : body.ReasonNote.Trim();
            ColumnParams.Add(cmd,"@UserId","WH_InventoryTransaction.CreatedBy",userId);
            AdjustScanRow? row;
            using (var rdr = cmd.ExecuteReader()) row = rdr.Read() ? ReadAdjustScanRow(rdr) : null;
            tx.Commit();
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
