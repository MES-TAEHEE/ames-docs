using AMES.Api.Auth;
using AMES.Api.Logging;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using System.Data;
using Microsoft.Data.SqlClient;

namespace AMES.Api.Endpoints;

public static class WhEndpoints
{
    private const string WhLocationCorcd = "5010";
    private const string WhLocationBizcd = "5011";
    private const string PdaScheduleInboundProcedure = "WH_PDA_SCHEDULE_INBOUND_LIST";
    private const string PdaScheduleReleaseProcedure = "WH_PDA_SCHEDULE_RELEASE_LIST";
    private const string PdaInboundDocumentInfoProcedure = "WH_PDA_INBOUND_DOCUMENT_INFO";
    private const string PdaInboundScanLotProcedure = "WH_PDA_INBOUND_SCAN_LOT";
    private const string PdaInboundReceiveLotProcedure = "WH_PDA_INBOUND_RECEIVE_LOT";
    private const string PdaInboundMoveLocationProcedure = "WH_PDA_INBOUND_MOVE_LOCATION";
    private const string PdaInboundCancelReceiptProcedure = "WH_PDA_INBOUND_CANCEL_RECEIPT";
    private const string PdaAdjustScanStockProcedure = "WH_PDA_ADJUST_SCAN_STOCK";
    private const string PdaAdjustSaveQtyProcedure = "WH_PDA_ADJUST_SAVE_QTY";
    private const string PdaReleaseSlipStatusProcedure = "WH_PDA_RELEASE_SLIP_STATUS";
    private const string PdaReleasePickLinesProcedure = "WH_PDA_RELEASE_PICK_LINES";
    private const string PdaReleaseScanLotProcedure = "WH_PDA_RELEASE_SCAN_LOT";
    private const string PdaReleasePickLotProcedure = "WH_PDA_RELEASE_PICK_LOT";
    private const string PdaSparePartStockMoveProcedure = "SP_PDA_STOCK_MOVE";

    // ── DTOs ─────────────────────────────────────────────────────────────
    public sealed record Wh001ScheduleInboundItem(int ScheduleItemId, string PurchaseOrderNo, int? PurchaseOrderLineNo,
        string? SupplierName, string? MaterialNo, string? MaterialName, string? CarCode, string? UnitOfMeasure,
        decimal PurchaseOrderQty, decimal ReceivedQty, decimal RemainingQty, DateTime? ExpectedArrivalDate,
        DateTime? PurchaseOrderCreatedDate, string ReceiptStatus);

    public sealed record InventoryRow(int InventoryId, string ItemNo, string? ItemName, string LocationId,
        int? LotId, decimal OnHandQty, decimal ReservedQty, DateTime? ExpiryDate,
        string? CarCode = null, string? Unit = null, decimal? MinDays = null, decimal? MinQty = null,
        decimal? MaxDays = null, decimal? MaxQty = null, int LotCount = 0, int LocationCount = 0,
        string? Status = null, string? StatusName = null, DateTime? LastReceivedDate = null,
        string? LotNo = null);
    public sealed record LotStatusRow(int LotId, string LotNo, string? ItemNo, string? ItemName,
        string InventoryStatus, decimal RemainingQty, string? LocationId, DateTime? ProductionDate,
        DateTime? LastChangedAt);

    public sealed record LocationRow(string LocationId, string? LocationName, string? Zone, int LineCount, decimal TotalQty,
        string? WarehouseCode = null, string? WarehouseName = null, string? AreaCode = null, string? AreaName = null,
        string? ZoneName = null, string? X = null, string? Y = null, string? Z = null,
        string? PlantCode = null, string? LocationType = null, decimal? Capacity = null, string? Unit = null);
    public sealed record LocationMapItemRow(string LotNo, string? PartNo, string? PartName, decimal Qty, string? Unit,
        string? InventoryStatus, string? WorkDate, string? WorkTime);
    public sealed record InventoryLocationRow(int RowNo, string ItemNo, string LocationId, string? LocationName,
        string? WarehouseCode, string? WarehouseName, string? AreaCode, string? AreaName,
        string? ZoneCode, string? ZoneName, string? RackX, string? RackY, string? RackZ, decimal Qty);
    public sealed record InventoryScanLookupRow(string SearchKind, string SearchText, string? DisplayText);
    public sealed record InventoryTestChangeResult(bool Success, string Message, string LotNo, decimal Qty);

    public sealed record InboundScanRow(string ReceiveType, string? Yn, string LotNo, string Barcode,
        string? SourceTable, string? NoteNo, string? CaseBarcode, string? CaseNo, string? InvoiceNo,
        string? ContainerNo, string? PartNo, string? PartName, decimal Qty, string? Unit, string? PoNo,
        int? PoSeq, string? VendorId, string? VendorName, DateTime? ProductionDate, DateTime? DeliveryDate,
        DateTime? ArrivalDate, DateTime? ShipDate, DateTime? PackDate, string? ReceivedLocation,
        string? ReceivedStatus);

    public sealed record InboundDocumentRow(string ReceiveType, int InboundDocumentId,
        string DocumentBarcode, string? DocumentNo, string? VendorId, string? VendorName,
        string? CaseNo, string? InvoiceNo, string? ContainerNo, DateTime? ShipDate,
        DateTime? PackDate, DateTime? DeliveryDate, DateTime? ArrivalDate,
        int TotalBoxes, int ScannedBoxes, string? Yn);
    public sealed record InboundDocumentLineRow(string PartNo, string? PartName,
        int BoxCount, int ScanCount, string? Yn);
    public sealed record InboundDocumentBoxRow(string PartNo, string BoxBarcode,
        string? LotNo, decimal Qty, string? Unit, string? Yn);
    public sealed record InboundDocumentResult(InboundDocumentRow? Document,
        List<InboundDocumentLineRow> Lines, List<InboundDocumentBoxRow> Boxes);

    public sealed record InboundReceiveReq(string Mode, string Barcode, string LocationId, bool SimulateFailure = false);
    public sealed record InboundCancelReq(string Mode, string Barcode);
    public sealed record AdjustSaveReq(string? Mode, string Barcode, decimal DeltaQty, string ReasonCode,
        string? ReasonNote, bool SimulateFailure = false);
    public sealed record AdjustTestResetResult(bool Success, string Message, string LotNo, decimal Qty);
    public sealed record InboundReceiveResult(bool Success, string Message, InboundScanRow? Row);

    public sealed record Wh001ScheduleReleaseItem(int WorkOrderId, string? WorkOrderNo,
        string PartNo, string? PartName, decimal OrderQty, string? Unit,
        DateTime? DueDate, string WorkOrderStatus, DateTime? ReleasedAt, string? LineId);

    public sealed record ReleaseSlipStatusRow(string PickSlipNo, bool Exists, bool IsClosed, int LineCount,
        string? RequestLocation, DateTime? RequestDate, DateTime? CloseDate, string Message);

    public sealed record ReleasePickLineRow(string PickSlipNo, string ItemNo, string? ItemName,
        decimal RequestBoxQty, decimal PickedBoxQty, decimal PickedQty, string? RequestUserId,
        string? SuggestedLocation1, string? SuggestedLocation2, string? SuggestedLocation3, string Status);

    public sealed record ReleaseLotRow(string PickSlipNo, string LotNo, string? ItemNo, string? ItemName,
        decimal Qty, string? Unit, string? LocationNo, string? LocationName, string? ZoneCode,
        string? InvStatus, string? ProdDate, string? RcvDate, bool IsFifoSuggested, bool IsValid,
        string? Message);
    public sealed record ReleaseFifoLotRow(string PickSlipNo, string ItemNo, string LotNo, string? LocationNo,
        decimal Qty, string? ProductionDate);
    public sealed record ReleasePickInput(string LotNo, decimal Qty);
    public sealed record ReleaseCompleteReq(string PickSlipNo, List<ReleasePickInput>? Lots = null,
        string? OutgoingType = null, bool SimulateFailure = false);
    public sealed record ReleaseCompleteResult(bool Success, string Message);
    public sealed record DirectOutgoingLotRow(int LotId, string LotNo, string? ItemNo, string? ItemName,
        decimal Qty, string? Unit, string? LocationId, string InventoryStatus, bool IsValid, string Message);
    public sealed record DirectOutgoingReq(string OutgoingType, string LotNo, decimal Qty,
        string? TargetCode = null, string? Note = null);
    public sealed record DirectOutgoingResult(bool Success, string Message, DirectOutgoingLotRow? Row = null);
    public sealed record OutgoingVendorRow(string VendorId, string? VendorName);
    public sealed record SparePartRow(string EosSpNo, string? Category,
        string? ApplicableEquipment, string? PartName, string? PartNo, string? Maker,
        string? Vendor, decimal Qty, string? Unit, string? StorageLocation, string? AreaCode,
        string InventoryStatus, bool IsReleaseEligible, string? ImageDataUrl, bool HasReceived = false,
        int? SafetyStock = null, string? SerialNo = null, string? ItemStatus = null,
        string? ExtraLocation = null, int? LastTxnId = null);
    public sealed record SparePartMoveReq(string EosSpNo, int Qty = 1,
        string? LocationId = null, string? Note = null);
    public sealed record SparePartSerialRow(string SerialNo, string StatusCode, DateTime CreatedAt);
    public sealed record SparePartLabelCreateReq(string EosSpNo, int Count);
    public sealed record SparePartCancelReq(string SerialNo, string MoveType, string? Note = null);
    public sealed record SparePartMoveResult(bool Success, string Message, SparePartRow? Row = null);
    public sealed record SparePartAdjustmentStock(string Barcode, string LotNo, string PartNo,
        string? PartName, decimal Qty, string? Unit);
    public sealed record SparePartAdjustmentLocation(string LocationId, List<SparePartAdjustmentStock> Items);

    public sealed record TransactionRow(long TxnId, DateTime TxnTime, string TxnType, string? ItemNo,
        string? LocationId, decimal QtyBefore, decimal Delta, decimal QtyAfter, string? ReasonCode);

    public sealed record WarehouseTransactionRow(long RowNo, string? LotNo, string? PartNo,
        string? WorkDate, string? WorkTime, string? LocationId, decimal Qty, string Status,
        string Direction, string? WorkerId, string? ReasonCode, string? ReasonNote,
        string? Supervisor, decimal? BeforeQty, decimal? DeltaQty, decimal? AfterQty,
        string? BeforeStatus, string? AfterStatus, string? BeforeLocation, string? AfterLocation,
        string? Source, string? Note, string? Unit = null);

    public sealed record ReceiveReq(string LotCode, decimal Qty, string LocationId);
    public sealed record PickReq(string PickSlipNo, string LotNo, decimal Qty);
    public sealed record PickResult(bool Success, string Message, ReleaseLotRow? Row);

    // ── Routes ───────────────────────────────────────────────────────────
    public static void MapWh(this WebApplication app, AmesConnectionFactory factory)
    {
        var master = new MasterDataRepository(factory);
        var maintenance = new MntRepository(factory);
        var g = app.MapGroup("/api/wh").WithTags("Warehouse");
        g.MapAdjustmentLocation(factory, finishedGoods: false);

        // WH-001 Schedule - inbound tab
        IResult GetWh001ScheduleInbound(HttpContext ctx, int? year, int? quarter, string? vendorId, string? lang)
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            return Results.Ok(QueryWh001ScheduleInbound(factory, year, quarter, vendorId, lang));
        }

        g.MapGet("/schedule/inbound", GetWh001ScheduleInbound);
        g.MapGet("/inbound/schedule", GetWh001ScheduleInbound);

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
            cmd.Parameters.AddWithValue("@By", s.EmployeeNo);
            cmd.Parameters.AddWithValue("@T", s.TerminalId);
            var id = (int)cmd.ExecuteScalar()!;
            return Results.Ok(new { ReceivingId = id });
        });

        g.MapGet("/inbound/document", (HttpContext ctx, string mode, string barcode) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();

            try
            {
                var result = ExecuteInboundDocument(factory, mode, barcode);
                if (result.Document is not null)
                {
                    WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                        s, "SCAN_DOCUMENT", "WH002", mode, barcode, "SUCCESS", "Inbound document scanned"));
                }
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                var isValidationError = ex is SqlException sqlEx
                    && sqlEx.Errors.Count > 0
                    && sqlEx.Errors[0].Number >= 51400
                    && sqlEx.Errors[0].Number < 51500;
                return Results.Problem(WarehouseProcedureMessage(ex), statusCode: isValidationError
                    ? StatusCodes.Status400BadRequest
                    : StatusCodes.Status503ServiceUnavailable);
            }
        });

        g.MapGet("/inbound/scan", (HttpContext ctx, string mode, string barcode) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();

            try
            {
                var row = ExecuteInboundScan(factory, mode, barcode);
                WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                    s, "SCAN_BOX", "WH002", "BOX", barcode, "SUCCESS", "Inbound box scanned",
                    lotNo: row?.LotNo ?? barcode, partNo: row?.PartNo, locationId: row?.ReceivedLocation, qty: row?.Qty));
                return Results.Ok(row);
            }
            catch (Exception ex)
            {
                var isValidationError =
                    ex is SqlException sqlEx
                    && sqlEx.Errors.Count > 0
                    && sqlEx.Errors[0].Number >= 51400
                    && sqlEx.Errors[0].Number < 51500;

                WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                    s, "SCAN_BOX", "WH002", "BOX", barcode, "FAIL", WarehouseProcedureMessage(ex),
                    lotNo: barcode));
                return Results.Problem(
                    WarehouseProcedureMessage(ex),
                    statusCode: isValidationError
                        ? StatusCodes.Status400BadRequest
                        : StatusCodes.Status503ServiceUnavailable);
            }
        });

        IResult ReceiveInboundLot(HttpContext ctx, InboundReceiveReq body)
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();

            var simulateFailure = body.SimulateFailure
                && (PdaScenarioUsers.IsDetailed(s.EmployeeNo)
                    || (PdaScenarioUsers.IsSimple(s.EmployeeNo)
                        && body.Barcode is "5011LL260908800001" or "5011LL260908800002" or "5011LL260908800003"
                            or "CKD260908800000001" or "CKD260908800000002" or "CKD260908800000003"));
            var result = ExecuteInboundReceive(
                factory, body, s.EmployeeNo, PdaInboundReceiveLotProcedure, "Received", simulateFailure);
            WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                s, "RECEIVE", "WH002", "LOT", body.Barcode, result.Success ? "SUCCESS" : "FAIL", result.Message,
                lotNo: result.Row?.LotNo ?? body.Barcode, partNo: result.Row?.PartNo, locationId: body.LocationId, qty: result.Row?.Qty));
            return simulateFailure && !result.Success
                ? Results.Problem(result.Message, statusCode: StatusCodes.Status503ServiceUnavailable)
                : Results.Ok(result);
        }

        g.MapPost("/inbound/receive-lot", ReceiveInboundLot);
        g.MapPost("/inbound/receive-sis", ReceiveInboundLot);

        IResult GetSparePart(HttpContext ctx, string eosSpNo)
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            var row = QuerySparePart(factory, eosSpNo);
            return row is null
                ? Results.Problem("EOS SP No was not found.", statusCode: StatusCodes.Status404NotFound)
                : Results.Ok(row);
        }

        g.MapGet("/sp/item", GetSparePart);

        g.MapGet("/sp/master", (HttpContext ctx, string? q) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(QuerySpareParts(factory, search: q)))
            .WithSummary("List active spare part masters for PDA label printing")
            .WithDescription("Includes zero-stock parts. Printing creates a unique SerialNo for each QR label; Inbound and Release scan that SerialNo.")
            .Produces<List<SparePartRow>>()
            .Produces(StatusCodes.Status401Unauthorized);

        g.MapPost("/sp/labels", (HttpContext ctx, SparePartLabelCreateReq body) =>
        {
            if (ctx.GetSession() is not { } session) return Results.Unauthorized();
            try { return Results.Ok(CreateSparePartLabels(factory, body, session.EmployeeNo)); }
            catch (Exception ex) { return Results.Problem(WarehouseProcedureMessage(ex), statusCode: StatusCodes.Status400BadRequest); }
        });
        g.MapGet("/sp/labels/{eosSpNo}", (HttpContext ctx, string eosSpNo) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(QuerySparePartSerials(factory, eosSpNo)));

        IResult MoveSparePart(HttpContext ctx, SparePartMoveReq body, string moveType)
        {
            if (ctx.GetSession() is not { } session) return Results.Unauthorized();
            var result = ExecuteSparePartMove(factory, body, moveType, session.EmployeeNo);
            return result.Success
                ? Results.Ok(result)
                : Results.Problem(result.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        g.MapPost("/sp/inbound", (HttpContext ctx, SparePartMoveReq body) => MoveSparePart(ctx, body, "IN"));
        g.MapPost("/sp/release", (HttpContext ctx, SparePartMoveReq body) => MoveSparePart(ctx, body, "OUT"));
        g.MapPost("/sp/cancel", (HttpContext ctx, SparePartCancelReq body) =>
        {
            if (ctx.GetSession() is not { } session) return Results.Unauthorized();
            var result = CancelSparePartMove(factory, body, session.EmployeeNo);
            return result.Success ? Results.Ok(result)
                : Results.Problem(result.Message, statusCode: StatusCodes.Status400BadRequest);
        });

        g.MapGet("/sp/inventory", (HttpContext ctx, string? q) =>
            ctx.GetSession() is null ? Results.Unauthorized() : Results.Ok(QuerySparePartInventory(factory, q)));
        g.MapGet("/sp/inventory/lots", (HttpContext ctx, string? q) =>
            ctx.GetSession() is null ? Results.Unauthorized() : Results.Ok(QuerySparePartLotStatuses(factory, q)));
        g.MapGet("/sp/inventory/locations", (HttpContext ctx, string eosSpNo) =>
            ctx.GetSession() is null ? Results.Unauthorized() : Results.Ok(QuerySparePartInventoryLocations(factory, eosSpNo)));
        g.MapGet("/sp/inventory/location/{locationId}/items", (HttpContext ctx, string locationId) =>
            ctx.GetSession() is null ? Results.Unauthorized() : Results.Ok(QuerySparePartLocationItems(factory, locationId)));
        g.MapGet("/sp/locations", (HttpContext ctx) =>
            ctx.GetSession() is null ? Results.Unauthorized() : Results.Ok(QuerySparePartLocations(factory)));

        g.MapGet("/sp/adjust/location", (HttpContext ctx, string barcode) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            return Results.Ok(QuerySparePartAdjustmentLocation(factory, barcode));
        });
        g.MapGet("/sp/adjust/scan", (HttpContext ctx, string scanText) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            var row = QuerySparePartInboundRow(factory, scanText);
            if (row?.ReceivedStatus == "NOT RECEIVED")
                return Results.Problem("This spare part has not been received yet. Receive it before adjustment or locating.", statusCode: StatusCodes.Status400BadRequest);
            return row is null
                ? Results.Problem("EOS SP No was not found.", statusCode: StatusCodes.Status404NotFound)
                : Results.Ok(row);
        });
        g.MapPost("/sp/adjust/save", (HttpContext ctx, AdjustSaveReq body) =>
        {
            if (ctx.GetSession() is not { } session) return Results.Unauthorized();
            var reasonCode = body.ReasonCode?.Trim() ?? "";
            if (master.FindActiveCodeItem("INV_ADJUST_REASON", reasonCode) is null)
                return Results.BadRequest(new InboundReceiveResult(false, "Select a valid reason code.", null));
            if (body.DeltaQty == 0 || body.DeltaQty != decimal.Truncate(body.DeltaQty))
                return Results.BadRequest(new InboundReceiveResult(false, "Enter a non-zero whole-number adjustment.", null));
            try
            {
                maintenance.AdjustSparePartStock(body.Barcode.Trim(), "ADJ", decimal.ToInt32(body.DeltaQty),
                    "PDA_ADJUST", reasonCode, body.ReasonNote, session.EmployeeNo);
                return Results.Ok(new InboundReceiveResult(true, "Quantity adjusted",
                    QuerySparePartInboundRow(factory, body.Barcode)));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new InboundReceiveResult(false, WarehouseProcedureMessage(ex), null));
            }
        });
        g.MapPost("/sp/move-location", (HttpContext ctx, InboundReceiveReq body) =>
        {
            if (ctx.GetSession() is not { } session) return Results.Unauthorized();
            var result = ExecuteSparePartLocationMove(factory, body.Barcode, body.LocationId, session.EmployeeNo);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });
        g.MapGet("/sp/transactions", (HttpContext ctx, string? search, DateTime? dateFrom, DateTime? dateTo) =>
            ctx.GetSession() is null
                ? Results.Unauthorized()
                : Results.Ok(QuerySparePartTransactions(factory, search, dateFrom, dateTo)));

        g.MapPost("/sp/test/reset", (HttpContext ctx) =>
        {
            if (ctx.GetSession() is not { } session) return Results.Unauthorized();
            if (!PdaScenarioUsers.IsSimple(session.EmployeeNo) && !PdaScenarioUsers.IsDetailed(session.EmployeeNo))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            try
            {
                using var connection = factory.OpenConnection();
                using var command = new SqlCommand("dbo.SP_PDA_SIMPLE_TEST_RESET", connection)
                {
                    CommandType = CommandType.StoredProcedure
                };
                command.ExecuteNonQuery();
                return Results.Ok(new { Success = true });
            }
            catch (Exception ex)
            {
                return Results.Problem(WarehouseProcedureMessage(ex), statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).WithTags("PDA Test Scenarios");

        g.MapPost("/inbound/test/simple-reset", (HttpContext ctx) =>
        {
            if (ctx.GetSession() is not { } session) return Results.Unauthorized();
            if (!PdaScenarioUsers.IsSimple(session.EmployeeNo))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            try
            {
                using var connection = factory.OpenConnection();
                using var command = new SqlCommand("dbo.WH_PDA_INBOUND_SIMPLE_TEST_RESET", connection)
                {
                    CommandType = CommandType.StoredProcedure
                };
                command.ExecuteNonQuery();
                return Results.Ok(new { Success = true });
            }
            catch (Exception ex)
            {
                return Results.Problem(WarehouseProcedureMessage(ex), statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).WithTags("PDA Test Scenarios");

        g.MapPost("/inbound/move-location", (HttpContext ctx, InboundReceiveReq body) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();

            try
            {
                var result = ExecuteInboundReceive(factory, body, s.EmployeeNo, PdaInboundMoveLocationProcedure, "Location changed");
                WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                    s, "MOVE_LOCATION", "WH002", "LOCATION", body.LocationId, result.Success ? "SUCCESS" : "FAIL", result.Message,
                    lotNo: result.Row?.LotNo ?? body.Barcode, partNo: result.Row?.PartNo, locationId: body.LocationId, qty: result.Row?.Qty));
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                var isValidationError =
                    ex is SqlException sqlEx
                    && sqlEx.Errors.Count > 0
                    && sqlEx.Errors[0].Number >= 51400
                    && sqlEx.Errors[0].Number < 51500;

                WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                    s, "MOVE_LOCATION", "WH002", "LOCATION", body.LocationId, "FAIL", WarehouseProcedureMessage(ex),
                    lotNo: body.Barcode, locationId: body.LocationId));
                return Results.Problem(
                    WarehouseProcedureMessage(ex),
                    statusCode: isValidationError
                        ? StatusCodes.Status400BadRequest
                        : StatusCodes.Status503ServiceUnavailable);
            }
        });

        g.MapPost("/inbound/cancel", (HttpContext ctx, InboundCancelReq body) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();

            var result = ExecuteInboundCancel(factory, body, s.EmployeeNo);
            WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                s, "CANCEL_RECEIPT", "WH002", "LOT", body.Barcode, result.Success ? "SUCCESS" : "FAIL", result.Message,
                lotNo: result.Row?.LotNo ?? body.Barcode, partNo: result.Row?.PartNo, locationId: result.Row?.ReceivedLocation, qty: result.Row?.Qty));
            return Results.Ok(result);
        });


        g.MapGet("/adjust/scan", (HttpContext ctx, string scanText) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();

            try
            {
                var row = ExecuteAdjustScan(factory, scanText);
                WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                    s, "SCAN_STOCK", "WH005", "LOT", scanText, "SUCCESS", "Adjustment stock scanned",
                    lotNo: row?.LotNo, partNo: row?.PartNo, locationId: row?.ReceivedLocation, qty: row?.Qty));
                return Results.Ok(row);
            }
            catch (Exception ex)
            {
                var isValidationError =
                    ex is SqlException sqlEx
                    && sqlEx.Errors.Count > 0
                    && sqlEx.Errors[0].Number >= 51500
                    && sqlEx.Errors[0].Number < 51600;

                WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                    s, "SCAN_STOCK", "WH005", "LOT", scanText, "FAIL", WarehouseProcedureMessage(ex),
                    lotNo: scanText));
                return Results.Problem(
                    WarehouseProcedureMessage(ex),
                    statusCode: isValidationError
                        ? StatusCodes.Status400BadRequest
                        : StatusCodes.Status503ServiceUnavailable);
            }
        });

        IResult SaveAdjustQuantity(HttpContext ctx, AdjustSaveReq body)
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();
            if (master.FindActiveCodeItem("INV_ADJUST_REASON", body.ReasonCode?.Trim() ?? "") is null)
                return Results.BadRequest(new InboundReceiveResult(false, "Select a valid reason code.", null));

            var simulateFailure = body.SimulateFailure && PdaScenarioUsers.IsDetailed(s.EmployeeNo);
            var result = ExecuteAdjustSave(factory, body, s.EmployeeNo, simulateFailure);
            WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                s, "ADJUST_SAVE", "WH005", "LOT", body.Barcode, result.Success ? "SUCCESS" : "FAIL", result.Message,
                lotNo: result.Row?.LotNo ?? body.Barcode, partNo: result.Row?.PartNo,
                locationId: result.Row?.ReceivedLocation, qty: body.DeltaQty));
            return simulateFailure && !result.Success
                ? Results.Problem(result.Message, statusCode: StatusCodes.Status503ServiceUnavailable)
                : Results.Ok(result);
        }

        g.MapPost("/adjust/save", SaveAdjustQuantity);
        g.MapPost("/inbound/adjust-qty", SaveAdjustQuantity);

        g.MapPost("/adjust/test/reset", (HttpContext ctx) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();
            if (!PdaScenarioUsers.IsDetailed(s.EmployeeNo))
                return Results.Forbid();

            const string lotNo = "5011LL260904500001";
            using var conn = factory.OpenConnection();
            using var tx = conn.BeginTransaction();
            try
            {
                using var cmd = new SqlCommand("""
                    DECLARE @LotID int = (SELECT TOP (1) LotID FROM dbo.tbl_Lot WHERE LotCode = @LotNo);
                    IF @LotID IS NULL
                        THROW 51000, 'Adjust scenario test data was not found.', 1;

                    DELETE FROM dbo.WH_InventoryTransaction
                    WHERE LotID = @LotID AND CreatedBy = N'TEST';

                    UPDATE dbo.WH_Inventory
                    SET OnHandQty = 10, Status = N'Received', ModifiedBy = N'TEST', ModifiedTS = SYSDATETIME()
                    WHERE LotID = @LotID;

                    UPDATE dbo.tbl_Lot
                    SET RemainingQty = 10, Status = N'Received', InventoryStatus = N'STORED',
                        ModifiedBy = N'TEST', ModifiedTS = SYSDATETIME()
                    WHERE LotID = @LotID;
                    """, conn, tx);
                cmd.Parameters.Add("@LotNo", SqlDbType.NVarChar, 50).Value = lotNo;
                cmd.ExecuteNonQuery();
                tx.Commit();
                return Results.Ok(new AdjustTestResetResult(true, "Adjust test stock reset to 10 EA.", lotNo, 10));
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return Results.Problem(WarehouseProcedureMessage(ex), statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).WithTags("PDA Test Scenarios");

        g.MapGet("/location/scan", (HttpContext ctx, string locationId) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(locationId))
                return Results.Problem("Scan Location No.", statusCode: StatusCodes.Status400BadRequest);

            try
            {
                var location = QuerySisLocation(factory, locationId);
                WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                    s, "SCAN_LOCATION", "WH002", "LOCATION", locationId,
                    location is null ? "FAIL" : "SUCCESS",
                    location is null ? "Location not found." : "Location scanned",
                    locationId: location?.LocationId ?? locationId, qty: location?.TotalQty));
                return location is null
                    ? Results.Problem("Location not found.", statusCode: StatusCodes.Status404NotFound)
                    : Results.Ok(location);
            }
            catch (Exception ex)
            {
                WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                    s, "SCAN_LOCATION", "WH002", "LOCATION", locationId, "FAIL", WarehouseProcedureMessage(ex),
                    locationId: locationId));
                return Results.Problem("Warehouse database is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

        // WH-03 Inventory Status
        g.MapGet("/inventory", (HttpContext ctx, string? q, DateTime? dateFrom, DateTime? dateTo,
            string? areaCode, bool? simulateFailure) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();
            if (simulateFailure == true && PdaScenarioUsers.IsDetailed(s.EmployeeNo))
                return Results.Problem("Simulated Inventory API failure.", statusCode: StatusCodes.Status503ServiceUnavailable);
            return Results.Ok(QueryInventory(factory, q, dateFrom, dateTo, areaCode));
        });

        g.MapGet("/inventory/scan", (HttpContext ctx, string scanText) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            return Results.Ok(QueryInventoryScan(factory, scanText));
        });

        g.MapGet("/inventory/locations", (HttpContext ctx, string itemNo, DateTime? dateFrom,
            DateTime? dateTo, string? areaCode) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            return Results.Ok(QueryInventoryLocations(factory, itemNo, dateFrom, dateTo, areaCode));
        });

        g.MapPost("/inventory/test/toggle-qty", (HttpContext ctx) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();
            if (!PdaScenarioUsers.IsDetailed(s.EmployeeNo))
                return Results.Forbid();

            const string lotNo = "5011LL260804000001";
            using var conn = factory.OpenConnection();
            using var tx = conn.BeginTransaction();
            try
            {
                using var cmd = new SqlCommand("""
                    DECLARE @LotID int;
                    DECLARE @Current decimal(18,3);
                    DECLARE @Next decimal(18,3);

                    SELECT TOP (1) @LotID = L.LotID
                    FROM dbo.tbl_Lot L WITH (UPDLOCK, HOLDLOCK)
                    WHERE L.LotCode = @LotNo;

                    SELECT TOP (1) @Current = I.OnHandQty
                    FROM dbo.WH_Inventory I WITH (UPDLOCK, HOLDLOCK)
                    WHERE I.LotID = @LotID AND I.LocationID = N'B0-09-D2';

                    IF @LotID IS NULL OR @Current IS NULL
                        THROW 51000, 'Inventory refresh test data was not found.', 1;

                    SET @Next = CASE WHEN @Current = 120 THEN 121 ELSE 120 END;

                    UPDATE dbo.WH_Inventory
                    SET OnHandQty = @Next, ModifiedTS = SYSDATETIME(), ModifiedBy = N'TEST'
                    WHERE LotID = @LotID AND LocationID = N'B0-09-D2';

                    UPDATE dbo.tbl_Lot
                    SET RemainingQty = (
                        SELECT COALESCE(SUM(I.OnHandQty), 0)
                        FROM dbo.WH_Inventory I
                        WHERE I.LotID = @LotID
                    )
                    WHERE LotID = @LotID;

                    SELECT @Next;
                    """, conn, tx);
                cmd.Parameters.Add("@LotNo", SqlDbType.NVarChar, 50).Value = lotNo;
                var qty = Convert.ToDecimal(cmd.ExecuteScalar());
                tx.Commit();
                return Results.Ok(new InventoryTestChangeResult(true,
                    $"Test stock changed to {qty:#,##0.###} EA. Press REFRESH to load the latest quantity.", lotNo, qty));
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return Results.Problem(WarehouseProcedureMessage(ex), statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).WithTags("PDA Test Scenarios");

        g.MapGet("/inventory/location/{locationId}", (HttpContext ctx, string locationId, DateTime? dateFrom, DateTime? dateTo) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();

            using var conn = factory.OpenConnection();
            using var cmd = new SqlCommand("""
                SELECT
                    COALESCE(LOT.LotCode, CONCAT(N'LOT-', W.LotID), N'-') AS LOTNO,
                    W.ItemNo AS PARTNO,
                    I.ItemName AS PARTNM,
                    SUM(COALESCE(W.OnHandQty, 0)) AS QTY,
                    I.DefaultUOM AS UNIT,
                    COALESCE(W.Status, N'Received') AS INV_STATUS,
                    CONVERT(nvarchar(10), MAX(W.LastReceivedAt), 23) AS WORK_DATE,
                    CONVERT(nvarchar(8), MAX(W.LastReceivedAt), 108) AS WORK_TIME
                FROM dbo.WH_Inventory W
                LEFT JOIN dbo.tbl_Lot LOT ON LOT.LotID = W.LotID
                LEFT JOIN dbo.MD_Item I ON I.ItemNo = W.ItemNo
                WHERE UPPER(W.LocationID) = UPPER(@LocationID)
                  AND COALESCE(W.OnHandQty, 0) > 0
                  AND UPPER(COALESCE(W.Status, N'Received')) NOT IN (N'CANCELED', N'RELEASED', N'PICKED')
                  AND (@DateFrom IS NULL OR CONVERT(date, W.LastReceivedAt) >= @DateFrom)
                  AND (@DateTo IS NULL OR CONVERT(date, W.LastReceivedAt) <= @DateTo)
                GROUP BY
                    COALESCE(LOT.LotCode, CONCAT(N'LOT-', W.LotID), N'-'),
                    W.ItemNo, I.ItemName, I.DefaultUOM, COALESCE(W.Status, N'Received')
                ORDER BY W.ItemNo, LOTNO;
                """, conn);
            cmd.Parameters.Add("@LocationID", SqlDbType.NVarChar, 40).Value = locationId.Trim();
            cmd.Parameters.Add("@DateFrom", SqlDbType.Date).Value = dateFrom?.Date ?? (object)DBNull.Value;
            cmd.Parameters.Add("@DateTo", SqlDbType.Date).Value = dateTo?.Date ?? (object)DBNull.Value;

            using var rdr = cmd.ExecuteReader();
            var rows = new List<LocationMapItemRow>();
            while (rdr.Read())
            {
                rows.Add(new LocationMapItemRow(
                    GetString(rdr, "LOTNO") ?? "-",
                    GetString(rdr, "PARTNO"),
                    GetString(rdr, "PARTNM"),
                    GetDecimal(rdr, "QTY"),
                    GetString(rdr, "UNIT"),
                    GetString(rdr, "INV_STATUS"),
                    GetString(rdr, "WORK_DATE"),
                    GetString(rdr, "WORK_TIME")));
            }

            return Results.Ok(rows);
        });

        // WH-04 Location Map
        g.MapGet("/locations", (HttpContext ctx) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            const string sql = """
                SELECT l.LocationID, l.LocationName, l.ZoneCode,
                       COUNT(i.InventoryID) AS LineCount,
                       COALESCE(SUM(i.OnHandQty),0) AS TotalQty,
                       l.WhCode AS WarehouseCode,
                       wm.WhName AS WarehouseName,
                       l.AreaCode AS AreaCode,
                       am.AreaName AS AreaName,
                       l.ZoneCode AS ZoneName,
                       l.Aisle, l.Bay, l.Slot,
                       l.PlantCode, l.LocationType, l.Capacity,
                       CASE WHEN COUNT(DISTINCT NULLIF(mi.DefaultUOM,N'')) = 1
                            THEN MAX(mi.DefaultUOM)
                            WHEN COUNT(DISTINCT NULLIF(mi.DefaultUOM,N'')) > 1 THEN N'MIXED'
                            ELSE NULL END AS Unit
                FROM dbo.MD_Location l
                LEFT JOIN dbo.WH_WarehouseMaster wm
                  ON wm.WhCode = l.WhCode
                LEFT JOIN dbo.WH_AreaMaster am
                  ON am.WhCode = l.WhCode
                 AND am.AreaCode = l.AreaCode
                LEFT JOIN dbo.WH_Inventory i
                  ON i.LocationID = l.LocationID
                 AND COALESCE(i.OnHandQty,0) > 0
                 AND UPPER(COALESCE(i.Status,N'Received')) NOT IN (N'CANCELED',N'RELEASED',N'PICKED')
                LEFT JOIN dbo.MD_Item mi ON mi.ItemNo = i.ItemNo
                WHERE ISNULL(l.ActiveFlag,1) = 1
                GROUP BY l.LocationID, l.LocationName, l.ZoneCode, l.WhCode, l.AreaCode, l.PlantCode,
                         wm.WhName, am.AreaName,
                         l.Aisle, l.Bay, l.Slot, l.LocationType, l.Capacity
                ORDER BY l.ZoneCode, l.LocationID;
                """;
            return Query(factory, sql, r => new LocationRow(
                r["LocationID"] as string ?? "", r["LocationName"] as string,
                r["ZoneCode"] as string, (int)r["LineCount"],
                r.GetDecimal(r.GetOrdinal("TotalQty")),
                GetString(r, "WarehouseCode"), GetString(r, "WarehouseName"),
                GetString(r, "AreaCode"), GetString(r, "AreaName"), GetString(r, "ZoneName"),
                GetString(r, "Aisle"), GetString(r, "Bay"), GetString(r, "Slot"),
                GetString(r, "PlantCode"), GetString(r, "LocationType"), GetNullableDecimal(r, "Capacity"),
                GetString(r, "Unit")));
        });


        // WH-001 Schedule - release tab
        IResult GetWh001ScheduleRelease(HttpContext ctx, DateTime? dateFrom, DateTime? dateTo)
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            if (dateFrom.HasValue && dateTo.HasValue && dateFrom.Value.Date > dateTo.Value.Date)
                return Results.BadRequest("From date cannot be after To date.");
            return Results.Ok(QueryWh001ScheduleRelease(factory, dateFrom, dateTo));
        }

        g.MapGet("/schedule/release", GetWh001ScheduleRelease);
        g.MapGet("/release/schedule", GetWh001ScheduleRelease);

        g.MapGet("/release/schedule/{pickSlipNo}/status", (HttpContext ctx, string pickSlipNo) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();
            var status = QueryReleaseSlipStatus(factory, pickSlipNo);
            WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                s, "SCAN_PICK_SLIP", "WH003", "PICK_SLIP", pickSlipNo,
                status.Exists ? "SUCCESS" : "FAIL", status.Message,
                refDocType: "PICK_SLIP", refDocNo: status.PickSlipNo));
            return Results.Ok(status);
        });

        g.MapGet("/release/schedule/{pickSlipNo}/lines", (HttpContext ctx, string pickSlipNo) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            return Results.Ok(QueryReleasePickLines(factory, pickSlipNo));
        });

        g.MapGet("/inventory/lots", (HttpContext ctx, string? q) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            const string sql = """
                SELECT TOP 300 L.LotID, COALESCE(L.LotCode, CONCAT('LOT-',L.LotID)) AS LotNo,
                       L.ItemNo, I.ItemName,
                       COALESCE(NULLIF(L.InventoryStatus,''),
                           CASE WHEN W.InventoryID IS NULL THEN 'CREATED'
                                WHEN COALESCE(W.OnHandQty,0)<=0 THEN 'RELEASED'
                                WHEN NULLIF(W.LocationID,'') IS NULL THEN 'RECEIVED'
                                ELSE 'STORED' END) AS InventoryStatus,
                       COALESCE(L.RemainingQty,W.OnHandQty,0) AS RemainingQty,
                       W.LocationID, L.ProducedAt,
                       COALESCE(L.ModifiedTS,L.CreatedTS) AS LastChangedAt
                FROM dbo.tbl_Lot L
                LEFT JOIN dbo.MD_Item I ON I.ItemNo=L.ItemNo
                OUTER APPLY
                (
                    SELECT TOP (1) X.InventoryID,X.OnHandQty,X.LocationID
                    FROM dbo.WH_Inventory X WHERE X.LotID=L.LotID
                    ORDER BY X.InventoryID DESC
                ) W
                WHERE @Q='' OR L.LotCode LIKE '%'+@Q+'%' OR L.ItemNo LIKE '%'+@Q+'%' OR I.ItemName LIKE '%'+@Q+'%'
                ORDER BY COALESCE(L.ModifiedTS,L.CreatedTS) DESC,L.LotID DESC;
                """;
            return QueryWithParam(factory, sql, "@Q", q ?? "", r => new LotStatusRow(
                (int)r["LotID"], r["LotNo"] as string ?? "", r["ItemNo"] as string, r["ItemName"] as string,
                r["InventoryStatus"] as string ?? "CREATED", r.GetDecimal(r.GetOrdinal("RemainingQty")),
                r["LocationID"] as string, r["ProducedAt"] as DateTime?, r["LastChangedAt"] as DateTime?));
        });

        g.MapGet("/release/schedule/{pickSlipNo}/fifo-lots", (HttpContext ctx, string pickSlipNo) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            return Results.Ok(QueryReleaseFifoLots(factory, pickSlipNo));
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
            if (ProcedureExists(conn, "dbo", PdaReleasePickLotProcedure))
            {
                var result = ExecuteReleasePickStoredProcedure(conn, body, s.EmployeeNo, s.TerminalId);
                WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                    s, "RELEASE_PICK", "WH003", "LOT", body.LotNo,
                    result.Success ? "SUCCESS" : "FAIL", result.Message,
                    refDocType: "PICK_SLIP", refDocNo: body.PickSlipNo,
                    lotNo: result.Row?.LotNo ?? body.LotNo, partNo: result.Row?.ItemNo,
                    locationId: result.Row?.LocationNo, qty: result.Row?.Qty ?? body.Qty));
                return result.Success ? Results.Ok(result) : Results.BadRequest(result);
            }

            using var tx = conn.BeginTransaction();

            var row = ValidateReleaseLot(conn, tx, body.PickSlipNo, body.LotNo);
            if (!row.IsValid)
            {
                tx.Rollback();
                WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                    s, "RELEASE_PICK", "WH003", "LOT", body.LotNo, "FAIL", row.Message,
                    refDocType: "PICK_SLIP", refDocNo: body.PickSlipNo,
                    lotNo: row.LotNo ?? body.LotNo, partNo: row.ItemNo, locationId: row.LocationNo, qty: body.Qty));
                return Results.BadRequest(new PickResult(false, row.Message ?? "LOT cannot be picked.", row));
            }

            var pickQty = body.Qty <= 0 ? row.Qty : body.Qty;
            if (pickQty != row.Qty)
            {
                tx.Rollback();
                WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                    s, "RELEASE_PICK", "WH003", "LOT", body.LotNo, "FAIL",
                    "Partial LOT split is not supported in this PDA flow yet. Pick the full LOT quantity.",
                    refDocType: "PICK_SLIP", refDocNo: body.PickSlipNo,
                    lotNo: row.LotNo ?? body.LotNo, partNo: row.ItemNo, locationId: row.LocationNo, qty: pickQty));
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

                IF OBJECT_ID(N'dbo.WH_InventoryTransaction', N'U') IS NOT NULL
                BEGIN
                    INSERT INTO dbo.WH_InventoryTransaction
                    (
                        TransactionType, ItemNo, LocationID, LotID, QtyBefore, QtyChange, QtyAfter,
                        ReasonCode, RefDocType, RefDocID, OperatorID, Note, CreatedBy, CreatedTS
                    )
                    SELECT TOP (1)
                        'OUT', @PartNo, @LocationNo, NULL, @Qty, -@Qty, 0,
                        'RELEASE_PICK', 'PICK_SLIP', TRY_CONVERT(int, REPLACE(@PickSlipNo, N'RS-', N'')), @By,
                        CONCAT('PDA release pick ', @PickSlipNo), @By, SYSDATETIME();
                END
                """, conn, tx);
            cmd.Parameters.AddWithValue("@PickSlipNo", body.PickSlipNo.Trim());
            cmd.Parameters.AddWithValue("@LotNo", body.LotNo.Trim());
            cmd.Parameters.AddWithValue("@PartNo", (object?)row.ItemNo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Qty", pickQty);
            cmd.Parameters.AddWithValue("@LocationNo", (object?)row.LocationNo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@BeforeStatus", (object?)row.InvStatus ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@By", s.EmployeeNo);
            cmd.Parameters.AddWithValue("@TerminalId", s.TerminalId);
            var affected = cmd.ExecuteNonQuery();

            if (affected < 2)
            {
                tx.Rollback();
                WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                    s, "RELEASE_PICK", "WH003", "LOT", body.LotNo, "FAIL", "LOT status changed before picking. Scan again.",
                    refDocType: "PICK_SLIP", refDocNo: body.PickSlipNo,
                    lotNo: row.LotNo ?? body.LotNo, partNo: row.ItemNo, locationId: row.LocationNo, qty: pickQty));
                return Results.BadRequest(new PickResult(false, "LOT status changed before picking. Scan again.", row));
            }

            tx.Commit();
            var pickResult = new PickResult(true, "Release pick completed.", row with
            {
                InvStatus = "O1",
                Message = "Release pick completed."
            });
            WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                s, "RELEASE_PICK", "WH003", "LOT", body.LotNo, "SUCCESS", pickResult.Message,
                refDocType: "PICK_SLIP", refDocNo: body.PickSlipNo,
                lotNo: row.LotNo ?? body.LotNo, partNo: row.ItemNo, locationId: row.LocationNo, qty: pickQty));
            return Results.Ok(pickResult);
        });

        g.MapPost("/release/complete", (HttpContext ctx, ReleaseCompleteReq body) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();
            var pickSlipNo = body.PickSlipNo?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(pickSlipNo))
                return Results.BadRequest(new ReleaseCompleteResult(false, "Pick Slip No is required."));
            if (body.Lots is not { Count: > 0 })
                return Results.BadRequest(new ReleaseCompleteResult(false, "Scan every requested LOT before Release."));

            var outgoingType = master.FindActiveCodeItem("WH_OUTGOING_TYPE", body.OutgoingType?.Trim() ?? "");
            var reasonCode = outgoingType?.Attribute1;
            if (string.IsNullOrWhiteSpace(reasonCode))
                return Results.BadRequest(new ReleaseCompleteResult(false, "Select an outgoing type."));

            var simulateFailure = body.SimulateFailure && PdaScenarioUsers.IsDetailed(s.EmployeeNo);
            var result = ExecuteReleaseBatch(factory, pickSlipNo, body.Lots, reasonCode,
                s.EmployeeNo, s.TerminalId, simulateFailure);
            if (!result.Success)
                return simulateFailure
                    ? Results.Json(result, statusCode: StatusCodes.Status503ServiceUnavailable)
                    : Results.BadRequest(result);

            var message = $"Release completed as {body.OutgoingType}. Inventory was updated for every scanned LOT.";
            WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                s, "RELEASE_COMPLETE", "WH003", "PICK_SLIP", pickSlipNo, "SUCCESS", message,
                refDocType: "PICK_SLIP", refDocNo: pickSlipNo));
            return Results.Ok(result with { Message = message });
        });

        g.MapGet("/release/outgoing/vendors", (HttpContext ctx) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            const string sql = """
                SELECT VendorID, VendorName
                FROM dbo.MD_Vendor
                WHERE COALESCE(ActiveFlag,1)=1
                ORDER BY VendorName, VendorID;
                """;
            return Results.Ok(Query(factory, sql, r => new OutgoingVendorRow(
                r["VendorID"] as string ?? "", r["VendorName"] as string)));
        });

        g.MapGet("/release/outgoing/lot", (HttpContext ctx, string lotNo) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();
            var row = QueryDirectOutgoingLot(factory, lotNo);
            WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                s, "SCAN_OUTGOING_LOT", "WH003", "LOT", lotNo,
                row.IsValid ? "SUCCESS" : "FAIL", row.Message,
                refDocType: "DIRECT_OUTGOING", lotNo: row.LotNo, partNo: row.ItemNo,
                locationId: row.LocationId, qty: row.Qty));
            return Results.Ok(row);
        });

        g.MapPost("/release/outgoing", (HttpContext ctx, DirectOutgoingReq body) =>
        {
            if (ctx.GetSession() is not { } s) return Results.Unauthorized();
            var outgoingType = master.FindActiveCodeItem("WH_OUTGOING_TYPE", body.OutgoingType?.Trim() ?? "");
            var result = string.IsNullOrWhiteSpace(outgoingType?.Attribute1)
                ? new DirectOutgoingResult(false, "Select a valid outgoing type.")
                : ExecuteDirectOutgoing(factory, body, outgoingType.Attribute1, s.EmployeeNo);
            WarehouseOperationLogger.TryWrite(factory, ctx, WarehouseOperationLogger.FromSession(
                s, "DIRECT_OUTGOING", "WH003", "LOT", body.LotNo,
                result.Success ? "SUCCESS" : "FAIL", result.Message,
                refDocType: body.OutgoingType, refDocNo: body.TargetCode,
                lotNo: result.Row?.LotNo ?? body.LotNo, partNo: result.Row?.ItemNo,
                locationId: result.Row?.LocationId, qty: body.Qty));
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        // WH-06 Inventory Transactions
        g.MapGet("/transactions", (HttpContext ctx, int? days) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            var d = days ?? 7;
            var sql = $$"""
                SELECT TOP 100 TransactionID AS TxnID,
                       TransactionTime AS TxnTime,
                       ISNULL(TransactionType,'?') AS TxnType,
                       ItemNo, LocationID,
                       ISNULL(QtyBefore,0) AS QtyBefore,
                       ISNULL(QtyChange,0) AS Delta,
                       ISNULL(QtyAfter,0)  AS QtyAfter,
                       ReasonCode
                FROM   dbo.WH_InventoryTransaction
                WHERE  TransactionTime > DATEADD(day, -{{d}}, SYSDATETIME())
                ORDER BY TransactionTime DESC;
                """;
            return Query(factory, sql, r => new TransactionRow(
                (long)r["TxnID"], (DateTime)r["TxnTime"], r["TxnType"] as string ?? "?",
                r["ItemNo"] as string, r["LocationID"] as string,
                r.GetDecimal(r.GetOrdinal("QtyBefore")),
                r.GetDecimal(r.GetOrdinal("Delta")),
                r.GetDecimal(r.GetOrdinal("QtyAfter")),
                r["ReasonCode"] as string));
        });

        g.MapPost("/test/ppt-reset/{screen}", (HttpContext ctx, string screen) =>
        {
            if (ctx.GetSession() is not { } session) return Results.Unauthorized();
            if (!PdaScenarioUsers.IsSimple(session.EmployeeNo))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (screen is not ("release" or "inventory" or "adjust" or "history"))
                return Results.BadRequest(new { Message = "Unknown PPT test screen." });
            try
            {
                using var connection = factory.OpenConnection();
                using var command = new SqlCommand("dbo.WH_PDA_PPT_TEST_RESET", connection)
                {
                    CommandType = CommandType.StoredProcedure
                };
                command.Parameters.Add("@Screen", SqlDbType.VarChar, 10).Value = screen;
                command.ExecuteNonQuery();
                return Results.Ok(new { Success = true });
            }
            catch (Exception ex)
            {
                return Results.Problem(WarehouseProcedureMessage(ex), statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).WithTags("PDA Test Scenarios");

        g.MapPost("/transactions/test/reset", (HttpContext ctx) =>
        {
            if (ctx.GetSession() is not { } session) return Results.Unauthorized();
            if (!PdaScenarioUsers.IsDetailed(session.EmployeeNo))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            try
            {
                using var connection = factory.OpenConnection();
                using var command = new SqlCommand("dbo.WH_PDA_PPT_TEST_RESET", connection)
                {
                    CommandType = CommandType.StoredProcedure
                };
                command.Parameters.Add("@Screen", SqlDbType.VarChar, 10).Value = "history";
                command.ExecuteNonQuery();
                return Results.Ok(new { Success = true });
            }
            catch (Exception ex)
            {
                return Results.Problem(WarehouseProcedureMessage(ex), statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).WithTags("PDA Test Scenarios");

        g.MapGet("/warehouse-transactions", (
            HttpContext ctx,
            string? search,
            DateTime? dateFrom,
            DateTime? dateTo) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            return Results.Ok(QueryWarehouseTransactions(factory, search, dateFrom, dateTo));
        });
    }

    // Backwards-compat DTO from the v0 sample endpoint
    public sealed record InboundRowDto(int LotId, string LotCode, string? ItemNo, string? ItemName,
        decimal Qty, string? Vendor, DateTime? ArrivedAt);

    private static List<Wh001ScheduleInboundItem> QueryWh001ScheduleInbound(
        AmesConnectionFactory factory,
        int? year,
        int? quarter,
        string? vendorId,
        string? lang)
    {
        var today = DateTime.Today;
        var queryYear = year ?? today.Year;
        var queryQuarter = quarter ?? ((today.Month - 1) / 3) + 1;
        var language = string.IsNullOrWhiteSpace(lang) ? "EN" : lang;

        using var conn = factory.OpenConnection();
        var hasScheduleProcedure = ProcedureExists(conn, "dbo", PdaScheduleInboundProcedure);
        if (!hasScheduleProcedure)
            return new List<Wh001ScheduleInboundItem>();

        using var cmd = new SqlCommand($"[dbo].[{PdaScheduleInboundProcedure}]", conn)
        {
            CommandType = CommandType.StoredProcedure
        };

        cmd.Parameters.AddWithValue("@CompanyCode", "1000");
        cmd.Parameters.AddWithValue("@BusinessCode", "5011");
        cmd.Parameters.AddWithValue("@ScheduleYear", queryYear.ToString());
        cmd.Parameters.AddWithValue("@ScheduleQuarter", queryQuarter.ToString());
        cmd.Parameters.Add("@SupplierCode", SqlDbType.NVarChar, 10).Value =
            string.IsNullOrWhiteSpace(vendorId) ? DBNull.Value : vendorId;
        cmd.Parameters.AddWithValue("@LanguageCode", language);

        using var rdr = cmd.ExecuteReader();
        var rows = new List<Wh001ScheduleInboundItem>();
        var scheduleItemId = 1;
        while (rdr.Read())
        {
            rows.Add(ReadWh001ScheduleInboundItem(rdr, today, scheduleItemId++));
        }

        return rows;
    }

    private static List<Wh001ScheduleReleaseItem> QueryWh001ScheduleRelease(
        AmesConnectionFactory factory,
        DateTime? dateFrom,
        DateTime? dateTo)
    {
        using var conn = factory.OpenConnection();
        if (ProcedureExists(conn, "dbo", PdaScheduleReleaseProcedure))
        {
            using var wh001Cmd = new SqlCommand($"[dbo].[{PdaScheduleReleaseProcedure}]", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            wh001Cmd.Parameters.Add("@DueDateFrom", SqlDbType.Date).Value = (object?)dateFrom?.Date ?? DBNull.Value;
            wh001Cmd.Parameters.Add("@DueDateTo", SqlDbType.Date).Value = (object?)dateTo?.Date ?? DBNull.Value;

            using var wh001Rdr = wh001Cmd.ExecuteReader();
            var wh001Rows = new List<Wh001ScheduleReleaseItem>();
            while (wh001Rdr.Read()) wh001Rows.Add(ReadWh001ScheduleReleaseItem(wh001Rdr));
            return wh001Rows;
        }

        if (!TableExists(conn, null, "dbo", "PP_WorkOrder"))
            return [];

        using var cmd = new SqlCommand("""
            SELECT TOP (100)
                W.WoID AS WorkOrderId,
                W.WoNumber AS WorkOrderNo,
                W.ItemNo AS PartNo,
                I.ItemName AS PartName,
                COALESCE(W.OrderQty, 0) AS OrderQty,
                COALESCE(I.DefaultUOM, N'EA') AS Unit,
                W.DueDate,
                COALESCE(W.Status, N'Released') AS WorkOrderStatus,
                W.ReleasedAt,
                RL.LineID
            FROM dbo.PP_WorkOrder W
            LEFT JOIN dbo.MD_Item I ON I.ItemNo = W.ItemNo
            OUTER APPLY (SELECT TOP 1 r.LineID FROM dbo.PP_WorkOrderRouting r
                         WHERE r.WoID = W.WoID AND r.LineID IS NOT NULL
                         ORDER BY r.StepSeq) RL
            WHERE W.Status IN (N'Released', N'In Progress')
              AND (@DueDateFrom IS NULL OR W.DueDate >= @DueDateFrom)
              AND (@DueDateTo IS NULL OR W.DueDate <= @DueDateTo)
            ORDER BY ISNULL(W.DueDate, CONVERT(date, '9999-12-31')),
                     ISNULL(W.Priority, 5),
                     W.WoID;
            """, conn);
        cmd.Parameters.Add("@DueDateFrom", SqlDbType.Date).Value = (object?)dateFrom?.Date ?? DBNull.Value;
        cmd.Parameters.Add("@DueDateTo", SqlDbType.Date).Value = (object?)dateTo?.Date ?? DBNull.Value;

        using var rdr = cmd.ExecuteReader();
        var rows = new List<Wh001ScheduleReleaseItem>();
        while (rdr.Read()) rows.Add(ReadWh001ScheduleReleaseItem(rdr));
        return rows;
    }

    private static List<ReleasePickLineRow> QueryReleasePickLines(AmesConnectionFactory factory, string pickSlipNo)
    {
        using var conn = factory.OpenConnection();
        if (ProcedureExists(conn, "dbo", PdaReleasePickLinesProcedure))
        {
            using var pdaCmd = new SqlCommand($"[dbo].[{PdaReleasePickLinesProcedure}]", conn)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 15
            };
            pdaCmd.Parameters.Add("@PickSlipNo", SqlDbType.NVarChar, 40).Value = pickSlipNo.Trim();

            using var pdaRdr = pdaCmd.ExecuteReader();
            var pdaRows = new List<ReleasePickLineRow>();
            while (pdaRdr.Read())
            {
                pdaRows.Add(new ReleasePickLineRow(
                    GetString(pdaRdr, "PICK_SLIPNO") ?? "",
                    GetString(pdaRdr, "PARTNO") ?? "",
                    GetString(pdaRdr, "PARTNM"),
                    GetDecimal(pdaRdr, "REQ_BOX_QTY"),
                    GetDecimal(pdaRdr, "PICKED_BOX_QTY"),
                    GetDecimal(pdaRdr, "PICKED_QTY"),
                    GetString(pdaRdr, "REQ_USERID"),
                    GetString(pdaRdr, "LOC_01"),
                    GetString(pdaRdr, "LOC_02"),
                    GetString(pdaRdr, "LOC_03"),
                    GetString(pdaRdr, "STATUS") ?? "Open"));
            }

            return pdaRows;
        }

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

    private static List<ReleaseFifoLotRow> QueryReleaseFifoLots(AmesConnectionFactory factory, string pickSlipNo)
    {
        pickSlipNo = pickSlipNo.Trim();
        if (string.IsNullOrWhiteSpace(pickSlipNo)) return new List<ReleaseFifoLotRow>();

        using var conn = factory.OpenConnection();
        using var cmd = new SqlCommand("""
            ;WITH ReleaseLines AS
            (
                SELECT
                    RS.ReleaseScheduleID,
                    COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.ReleaseScheduleID)) AS PickSlipNo,
                    RS.ItemNo,
                    CASE
                        WHEN COALESCE(RS.DemandQty, 0) > COALESCE(RS.PickedQty, 0)
                            THEN COALESCE(RS.DemandQty, 0) - COALESCE(RS.PickedQty, 0)
                        ELSE 0
                    END AS RemainingBoxQty
                FROM dbo.WH_ReleaseSchedule RS
                WHERE UPPER(COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.ReleaseScheduleID))) = UPPER(@PickSlipNo)
                  AND UPPER(COALESCE(RS.Status, N'OPEN')) NOT IN (N'CLOSED', N'RELEASED', N'CANCELED', N'CANCELLED')
            ),
            RankedLots AS
            (
                SELECT
                    R.PickSlipNo,
                    R.ReleaseScheduleID,
                    R.ItemNo,
                    R.RemainingBoxQty,
                    L.LotCode,
                    W.LocationID,
                    COALESCE(W.OnHandQty, 0) AS Qty,
                    L.ProducedAt,
                    W.LastReceivedAt,
                    ROW_NUMBER() OVER
                    (
                        PARTITION BY R.ReleaseScheduleID
                        ORDER BY COALESCE(L.ProducedAt, CONVERT(datetime2, '9999-12-31')),
                                 COALESCE(W.LastReceivedAt, CONVERT(datetime2, '9999-12-31')),
                                 L.LotID
                    ) AS FifoSeq
                FROM ReleaseLines R
                INNER JOIN dbo.WH_Inventory W ON W.ItemNo = R.ItemNo
                INNER JOIN dbo.tbl_Lot L ON L.LotID = W.LotID
                WHERE COALESCE(W.OnHandQty, 0) > 0
                  AND UPPER(COALESCE(W.Status, N'RECEIVED')) NOT IN (N'CANCELED', N'RELEASED', N'PICKED')
            )
            SELECT
                PickSlipNo AS PICK_SLIPNO,
                ItemNo AS PARTNO,
                LotCode AS LOTNO,
                LocationID AS LOCATION_NO,
                Qty AS QTY,
                CONVERT(nvarchar(20), ProducedAt, 23) AS PROD_DATE
            FROM RankedLots
            WHERE FifoSeq <= RemainingBoxQty
            ORDER BY ReleaseScheduleID, FifoSeq;
            """, conn);
        cmd.Parameters.AddWithValue("@PickSlipNo", pickSlipNo);

        using var rdr = cmd.ExecuteReader();
        var rows = new List<ReleaseFifoLotRow>();
        while (rdr.Read())
        {
            rows.Add(new ReleaseFifoLotRow(
                GetString(rdr, "PICK_SLIPNO") ?? pickSlipNo,
                GetString(rdr, "PARTNO") ?? "",
                GetString(rdr, "LOTNO") ?? "",
                GetString(rdr, "LOCATION_NO"),
                GetDecimal(rdr, "QTY"),
                GetString(rdr, "PROD_DATE")));
        }
        return rows;
    }

    private static List<InventoryRow> QueryInventory(AmesConnectionFactory factory, string? search,
        DateTime? dateFrom, DateTime? dateTo, string? areaCode)
    {
        using var conn = factory.OpenConnection();
        var usePdaProcedure = ProcedureExists(conn, "dbo", "WH_PDA_INVENTORY_STATUS_LIST");
        using var cmd = new SqlCommand(
            usePdaProcedure ? "[dbo].[WH_PDA_INVENTORY_STATUS_LIST]" : "[SIS_TEST].[PDA_WH03_INVENTORY_STATUS]", conn)
        {
            CommandType = CommandType.StoredProcedure
        };
        if (usePdaProcedure)
        {
            cmd.Parameters.Add("@SearchText", SqlDbType.NVarChar, 80).Value =
                string.IsNullOrWhiteSpace(search) ? DBNull.Value : search.Trim();
            cmd.Parameters.Add("@StockDateFrom", SqlDbType.Date).Value = dateFrom.HasValue ? dateFrom.Value.Date : (object)DBNull.Value;
            cmd.Parameters.Add("@StockDateTo", SqlDbType.Date).Value = dateTo.HasValue ? dateTo.Value.Date : (object)DBNull.Value;
            cmd.Parameters.Add("@AreaCode", SqlDbType.NVarChar, 20).Value =
                string.IsNullOrWhiteSpace(areaCode) ? DBNull.Value : areaCode.Trim();
        }
        else
        {
            cmd.Parameters.Add("@IN_CORCD", SqlDbType.NVarChar, 10).Value = WhLocationCorcd;
            cmd.Parameters.Add("@IN_BIZCD", SqlDbType.NVarChar, 10).Value = WhLocationBizcd;
            cmd.Parameters.Add("@IN_Q", SqlDbType.NVarChar, 80).Value =
                string.IsNullOrWhiteSpace(search) ? DBNull.Value : search.Trim();
            cmd.Parameters.Add("@IN_DATE_FROM", SqlDbType.Date).Value = dateFrom.HasValue ? dateFrom.Value.Date : (object)DBNull.Value;
            cmd.Parameters.Add("@IN_DATE_TO", SqlDbType.Date).Value = dateTo.HasValue ? dateTo.Value.Date : (object)DBNull.Value;
            cmd.Parameters.Add("@IN_LANG_SET", SqlDbType.NVarChar, 10).Value = "EN";
        }

        using var rdr = cmd.ExecuteReader();
        var rows = new List<InventoryRow>();
        while (rdr.Read())
        {
            rows.Add(new InventoryRow(
                GetInt(rdr, "INVENTORY_ID") ?? 0,
                GetString(rdr, "PARTNO") ?? "",
                GetString(rdr, "PARTNM"),
                GetString(rdr, "PRIMARY_LOCATION") ?? "-",
                null,
                GetDecimal(rdr, "SUM_QTY"),
                GetDecimal(rdr, "RESERVED_QTY"),
                GetDate(rdr, "LAST_RECEIVED_DATE"),
                GetString(rdr, "VINCD"),
                GetString(rdr, "UNIT"),
                GetNullableDecimal(rdr, "MIN_INV_DAY"),
                GetNullableDecimal(rdr, "MIN_INV_QTY"),
                GetNullableDecimal(rdr, "MAX_INV_DAY"),
                GetNullableDecimal(rdr, "MAX_INV_QTY"),
                GetInt(rdr, "LOT_COUNT") ?? 0,
                GetInt(rdr, "LOCATION_COUNT") ?? 0,
                GetString(rdr, "STATUS"),
                GetString(rdr, "STATUSNM"),
                GetDate(rdr, "LAST_RECEIVED_DATE"),
                GetString(rdr, "LOTNO")));
        }
        return rows;
    }

    private static InventoryScanLookupRow QueryInventoryScan(AmesConnectionFactory factory, string scanText)
    {
        var value = scanText.Trim();
        using var conn = factory.OpenConnection();
        if (ProcedureExists(conn, "dbo", "WH_PDA_INVENTORY_SCAN_LOOKUP"))
        {
            using var cmd = new SqlCommand("[dbo].[WH_PDA_INVENTORY_SCAN_LOOKUP]", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.Add("@ScanText", SqlDbType.NVarChar, 80).Value = value;
            using var rdr = cmd.ExecuteReader();
            if (rdr.Read())
                return new InventoryScanLookupRow(
                    GetString(rdr, "SEARCH_KIND") ?? "TEXT",
                    GetString(rdr, "SEARCH_TEXT") ?? value,
                    GetString(rdr, "DISPLAY_TEXT"));
        }

        var lookups = new (string Kind, string Schema, string Table, string Sql)[]
        {
            ("LOCATION", "dbo", "MD_Location", "SELECT TOP (1) LocationID FROM dbo.MD_Location WHERE UPPER(LocationID)=UPPER(@ScanText) AND COALESCE(ActiveFlag,1)=1"),
            ("PART", "dbo", "tbl_Lot", "SELECT TOP (1) ItemNo FROM dbo.tbl_Lot WHERE UPPER(LotCode)=UPPER(@ScanText) OR UPPER(ItemNo)=UPPER(@ScanText) ORDER BY CASE WHEN UPPER(LotCode)=UPPER(@ScanText) THEN 0 ELSE 1 END, LotID DESC"),
            ("PART", "dbo", "MD_Item", "SELECT TOP (1) ItemNo FROM dbo.MD_Item WHERE UPPER(ItemNo)=UPPER(@ScanText) AND COALESCE(ActiveFlag,1)=1"),
            ("LOCATION", "SIS_TEST", "WMS1040", "SELECT TOP (1) LOCATION_NO FROM SIS_TEST.WMS1040 WHERE UPPER(LOCATION_NO)=UPPER(@ScanText) AND COALESCE(USE_YN,N'Y')=N'Y'"),
            ("LOCATION", "SIS_TEST", "WMS2000", "SELECT TOP (1) LOCATION_NO FROM SIS_TEST.WMS2000 WHERE UPPER(LOCATION_NO)=UPPER(@ScanText) AND COALESCE(QTY,0)>0"),
            ("PART", "SIS_TEST", "WMS2020", "SELECT TOP (1) PARTNO FROM SIS_TEST.WMS2020 WHERE UPPER(LOTNO)=UPPER(@ScanText) OR UPPER(PARTNO)=UPPER(@ScanText) ORDER BY CASE WHEN UPPER(LOTNO)=UPPER(@ScanText) THEN 0 ELSE 1 END"),
            ("PART", "SIS_TEST", "WMS2010", "SELECT TOP (1) PARTNO FROM SIS_TEST.WMS2010 WHERE UPPER(LOTNO)=UPPER(@ScanText) OR UPPER(PARTNO)=UPPER(@ScanText) ORDER BY CASE WHEN UPPER(LOTNO)=UPPER(@ScanText) THEN 0 ELSE 1 END"),
            ("PART", "SIS_TEST", "ACD0020", "SELECT TOP (1) PARTNO FROM SIS_TEST.ACD0020 WHERE UPPER(PARTNO)=UPPER(@ScanText)")
        };
        foreach (var lookup in lookups)
        {
            if (!TableExists(conn, null, lookup.Schema, lookup.Table)) continue;
            var result = ScalarString(conn, null, lookup.Sql, ("@ScanText", value));
            if (!string.IsNullOrWhiteSpace(result))
                return new InventoryScanLookupRow(lookup.Kind, result, null);
        }
        return new InventoryScanLookupRow("TEXT", value, null);
    }

    private static List<InventoryLocationRow> QueryInventoryLocations(AmesConnectionFactory factory, string itemNo,
        DateTime? dateFrom, DateTime? dateTo, string? areaCode)
    {
        using var conn = factory.OpenConnection();
        var usePdaProcedure = ProcedureExists(conn, "dbo", "WH_PDA_INVENTORY_LOCATION_LIST");
        using var cmd = new SqlCommand(
            usePdaProcedure ? "[dbo].[WH_PDA_INVENTORY_LOCATION_LIST]" : "[SIS_TEST].[PDA_WH03_INVENTORY_LOCATIONS]", conn)
        {
            CommandType = CommandType.StoredProcedure
        };
        if (usePdaProcedure)
        {
            cmd.Parameters.Add("@ItemNo", SqlDbType.NVarChar, 40).Value = itemNo.Trim();
            cmd.Parameters.Add("@StockDateFrom", SqlDbType.Date).Value = dateFrom.HasValue ? dateFrom.Value.Date : (object)DBNull.Value;
            cmd.Parameters.Add("@StockDateTo", SqlDbType.Date).Value = dateTo.HasValue ? dateTo.Value.Date : (object)DBNull.Value;
            cmd.Parameters.Add("@AreaCode", SqlDbType.NVarChar, 20).Value =
                string.IsNullOrWhiteSpace(areaCode) ? DBNull.Value : areaCode.Trim();
        }
        else
        {
            cmd.Parameters.Add("@IN_CORCD", SqlDbType.NVarChar, 10).Value = WhLocationCorcd;
            cmd.Parameters.Add("@IN_BIZCD", SqlDbType.NVarChar, 10).Value = WhLocationBizcd;
            cmd.Parameters.Add("@IN_PARTNO", SqlDbType.NVarChar, 40).Value = itemNo.Trim();
            cmd.Parameters.Add("@IN_DATE_FROM", SqlDbType.Date).Value = dateFrom.HasValue ? dateFrom.Value.Date : (object)DBNull.Value;
            cmd.Parameters.Add("@IN_DATE_TO", SqlDbType.Date).Value = dateTo.HasValue ? dateTo.Value.Date : (object)DBNull.Value;
            cmd.Parameters.Add("@IN_LANG_SET", SqlDbType.NVarChar, 10).Value = "EN";
        }

        using var rdr = cmd.ExecuteReader();
        var rows = new List<InventoryLocationRow>();
        while (rdr.Read())
        {
            rows.Add(new InventoryLocationRow(
                GetInt(rdr, "ROW_NO") ?? 0,
                GetString(rdr, "PARTNO") ?? "",
                GetString(rdr, "LOCATION_NO") ?? "-",
                GetString(rdr, "LOCATION_NM"),
                GetString(rdr, "WHCD"),
                GetString(rdr, "WHNM"),
                GetString(rdr, "AREACD"),
                GetString(rdr, "AREANM"),
                GetString(rdr, "ZONECD"),
                GetString(rdr, "ZONENM"),
                GetString(rdr, "RACK_X"),
                GetString(rdr, "RACK_Y"),
                GetString(rdr, "RACK_Z"),
                GetDecimal(rdr, "SUM_QTY")));
        }
        return rows;
    }

    private static List<WarehouseTransactionRow> QueryWarehouseTransactions(
        AmesConnectionFactory factory,
        string? search,
        DateTime? dateFrom,
        DateTime? dateTo)
    {
        using var conn = factory.OpenConnection();
        if (!ProcedureExists(conn, "dbo", "WH_PDA_TRANSACTION_LIST"))
            return new List<WarehouseTransactionRow>();

        using var cmd = new SqlCommand("[dbo].[WH_PDA_TRANSACTION_LIST]", conn)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 15
        };
        cmd.Parameters.Add("@SearchText", SqlDbType.NVarChar, 120).Value =
            string.IsNullOrWhiteSpace(search) ? DBNull.Value : search.Trim();
        cmd.Parameters.Add("@DateFrom", SqlDbType.Date).Value =
            dateFrom.HasValue ? dateFrom.Value.Date : DateTime.Today.AddDays(-30);
        cmd.Parameters.Add("@DateTo", SqlDbType.Date).Value =
            dateTo.HasValue ? dateTo.Value.Date : DateTime.Today;

        using var rdr = cmd.ExecuteReader();
        var rows = new List<WarehouseTransactionRow>();
        while (rdr.Read())
            rows.Add(ReadWarehouseTransactionRow(rdr));

        return rows;
    }

    private static ReleaseSlipStatusRow QueryReleaseSlipStatus(AmesConnectionFactory factory, string pickSlipNo)
    {
        pickSlipNo = pickSlipNo.Trim();

        if (string.IsNullOrWhiteSpace(pickSlipNo))
            return new ReleaseSlipStatusRow("", false, false, 0, null, null, null, "Pick Slip No is required.");

        using var conn = factory.OpenConnection();
        if (ProcedureExists(conn, "dbo", PdaReleaseSlipStatusProcedure))
        {
            using var pdaCmd = new SqlCommand($"[dbo].[{PdaReleaseSlipStatusProcedure}]", conn)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 15
            };
            pdaCmd.Parameters.Add("@PickSlipNo", SqlDbType.NVarChar, 40).Value = pickSlipNo;

            using var pdaRdr = pdaCmd.ExecuteReader();
            if (!pdaRdr.Read())
                return new ReleaseSlipStatusRow(pickSlipNo, false, false, 0, null, null, null, "Pick Slip was not found.");

            return new ReleaseSlipStatusRow(
                GetString(pdaRdr, "PICK_SLIPNO") ?? pickSlipNo,
                GetInt(pdaRdr, "EXISTS_FLAG") == 1,
                GetInt(pdaRdr, "IS_CLOSED") == 1,
                GetInt(pdaRdr, "LINE_COUNT") ?? 0,
                GetString(pdaRdr, "REQ_LOCATION"),
                GetDate(pdaRdr, "REQ_DATE"),
                GetDate(pdaRdr, "CLOSE_DATE"),
                GetString(pdaRdr, "MESSAGE") ?? "Pick Slip is ready.");
        }

        if (!HasReleaseTables(conn))
            return new ReleaseSlipStatusRow(pickSlipNo, false, false, 0, null, null, null,
                "Release tables are not available in SIS_TEST.");

        using var cmd = new SqlCommand("""
            SELECT
                COUNT(*) AS LINE_COUNT,
                MIN(REQ_LOCATION) AS REQ_LOCATION,
                MAX(TRY_CONVERT(date, REQ_DATE)) AS REQ_DATE_DT,
                MAX(TRY_CONVERT(datetime2, CLOSE_DATE)) AS CLOSE_DATE,
                MAX(CASE WHEN ISNULL(CLOSE_YN, N'N') = N'Y' THEN 1 ELSE 0 END) AS CLOSE_YN_FLAG
            FROM SIS_TEST.WMS3050
            WHERE PICK_SLIPNO = @PickSlipNo;
            """, conn);
        cmd.Parameters.AddWithValue("@PickSlipNo", pickSlipNo);

        using var rdr = cmd.ExecuteReader();
        if (!rdr.Read())
            return new ReleaseSlipStatusRow(pickSlipNo, false, false, 0, null, null, null, "Pick Slip was not found.");

        var lineCount = GetInt(rdr, "LINE_COUNT") ?? 0;
        if (lineCount <= 0)
            return new ReleaseSlipStatusRow(pickSlipNo, false, false, 0, null, null, null, "Pick Slip was not found.");

        var closeDate = GetDate(rdr, "CLOSE_DATE");
        var isClosed = closeDate.HasValue || GetInt(rdr, "CLOSE_YN_FLAG") == 1;
        var message = isClosed ? "Pick Slip is already closed." : "Pick Slip is ready.";

        return new ReleaseSlipStatusRow(
            pickSlipNo,
            true,
            isClosed,
            lineCount,
            GetString(rdr, "REQ_LOCATION"),
            GetDate(rdr, "REQ_DATE_DT"),
            closeDate,
            message);
    }

    private static ReleaseLotRow ValidateReleaseLot(SqlConnection conn, SqlTransaction? tx, string pickSlipNo, string lotNo)
    {
        pickSlipNo = pickSlipNo.Trim();
        lotNo = lotNo.Trim();

        if (ProcedureExists(conn, tx, "dbo", PdaReleaseScanLotProcedure))
        {
            using var pdaCmd = new SqlCommand($"[dbo].[{PdaReleaseScanLotProcedure}]", conn, tx)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 15
            };
            pdaCmd.Parameters.Add("@PickSlipNo", SqlDbType.NVarChar, 40).Value = pickSlipNo;
            pdaCmd.Parameters.Add("@LotNo", SqlDbType.NVarChar, 50).Value = lotNo;

            using var pdaRdr = pdaCmd.ExecuteReader();
            return pdaRdr.Read()
                ? ReadReleaseLotRow(pdaRdr, pickSlipNo, lotNo)
                : InvalidReleaseLot(pickSlipNo, lotNo, "LOT was not found.");
        }

        if (!HasReleaseTables(conn, tx))
            return InvalidReleaseLot(pickSlipNo, lotNo, "Release tables are not available in SIS_TEST.");

        if (string.IsNullOrWhiteSpace(pickSlipNo))
            return InvalidReleaseLot(pickSlipNo, lotNo, "Pick Slip No is required.");

        if (string.IsNullOrWhiteSpace(lotNo))
            return InvalidReleaseLot(pickSlipNo, lotNo, "LOT No is required.");

        using (var slipCmd = new SqlCommand("""
            SELECT TOP (1) CLOSE_DATE, CLOSE_YN
            FROM SIS_TEST.WMS3050
            WHERE PICK_SLIPNO = @PickSlipNo;
            """, conn, tx))
        {
            slipCmd.Parameters.AddWithValue("@PickSlipNo", pickSlipNo);
            using var slipRdr = slipCmd.ExecuteReader();
            if (!slipRdr.Read())
                return InvalidReleaseLot(pickSlipNo, lotNo, "Pick Slip was not found.");

            var closeDate = GetDate(slipRdr, "CLOSE_DATE");
            var closeYn = GetString(slipRdr, "CLOSE_YN");
            if (closeDate.HasValue || string.Equals(closeYn, "Y", StringComparison.OrdinalIgnoreCase))
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

    private static DirectOutgoingLotRow QueryDirectOutgoingLot(AmesConnectionFactory factory, string lotNo)
    {
        var normalizedLot = lotNo?.Trim() ?? "";
        if (normalizedLot.Length == 0)
            return new DirectOutgoingLotRow(0, "", null, null, 0, null, null, "-", false, "LOT No is required.");

        using var conn = factory.OpenConnection();
        using var cmd = new SqlCommand("""
            SELECT TOP (1)
                L.LotID, L.LotCode, L.ItemNo, I.ItemName, I.DefaultUOM,
                W.LocationID, COALESCE(W.OnHandQty, L.RemainingQty, 0) AS Qty,
                COALESCE(NULLIF(L.InventoryStatus,''), NULLIF(W.Status,''), 'CREATED') AS InventoryStatus
            FROM dbo.tbl_Lot L
            LEFT JOIN dbo.MD_Item I ON I.ItemNo=L.ItemNo
            OUTER APPLY
            (
                SELECT TOP (1) X.InventoryID, X.LocationID, X.OnHandQty, X.Status
                FROM dbo.WH_Inventory X
                WHERE X.LotID=L.LotID
                ORDER BY CASE WHEN COALESCE(X.OnHandQty,0)>0 THEN 0 ELSE 1 END, X.InventoryID DESC
            ) W
            WHERE UPPER(L.LotCode)=UPPER(@LotNo);
            """, conn);
        cmd.Parameters.AddWithValue("@LotNo", normalizedLot);
        using var rdr = cmd.ExecuteReader();
        if (!rdr.Read())
            return new DirectOutgoingLotRow(0, normalizedLot, null, null, 0, null, null, "-", false, "LOT was not found.");

        var qty = GetDecimal(rdr, "Qty");
        var status = GetString(rdr, "InventoryStatus") ?? "CREATED";
        var validStatus = new[] { "RECEIVED", "STORED", "RETURN_RECEIVED", "RELEASE_CANCELLED" }
            .Contains(status, StringComparer.OrdinalIgnoreCase);
        var valid = qty > 0 && validStatus;
        return new DirectOutgoingLotRow(
            rdr.GetInt32(rdr.GetOrdinal("LotID")),
            GetString(rdr, "LotCode") ?? normalizedLot,
            GetString(rdr, "ItemNo"),
            GetString(rdr, "ItemName"),
            qty,
            GetString(rdr, "DefaultUOM"),
            GetString(rdr, "LocationID"),
            status,
            valid,
            valid ? "LOT is ready for outgoing." : $"LOT cannot be released. Current status: {status}.");
    }

    private static SparePartRow? QuerySparePart(AmesConnectionFactory factory, string eosSpNo)
        => string.IsNullOrWhiteSpace(eosSpNo) ? null : QuerySpareParts(factory, eosSpNo.Trim()).FirstOrDefault();

    private static List<SparePartRow> QuerySpareParts(AmesConnectionFactory factory, string? eosSpNo = null, string? search = null)
    {
        using var conn = factory.OpenConnection();
        using var cmd = new SqlCommand("""
            SELECT
                P.SparePartNo AS EosSpNo,
                COALESCE(C.CodeNameEn, C.CodeName, P.Category) AS Category,
                COALESCE(E.CodeNameEn, E.CodeName, P.ApplicableEquip) AS ApplicableEquipment,
                P.PartName, P.PartNo, P.Maker,
                CASE WHEN @EosSpNo IS NOT NULL THEN P.SparePartImage END AS SparePartImage,
                COALESCE(V.VendorName, P.SupplierID) AS Vendor,
                P.OnHandQty AS Qty, P.SafetyStock, COALESCE(P.UOM, 'EA') AS Unit,
                COALESCE(I.LocationID, ML.LocationID) AS StorageLocation, CAST(NULL AS varchar(20)) AS AreaCode,
                CASE WHEN I.SparePartItemID IS NOT NULL THEN CASE WHEN I.StatusCode='CREATED' THEN 0 ELSE 1 END
                     WHEN EXISTS (SELECT 1 FROM dbo.MNT_SparePartsTxn T
                    WHERE T.SparePartNo=P.SparePartNo AND T.MoveType='IN' AND T.RefType='PDA') THEN 1 ELSE 0 END AS HasReceived,
                CASE WHEN I.SparePartItemID IS NOT NULL THEN REPLACE(I.StatusCode,'_',' ')
                     WHEN P.OnHandQty > 0 THEN 'IN STOCK' ELSE 'OUT OF STOCK' END AS InventoryStatus,
                I.SerialNo,I.StatusCode AS ItemStatus,
                CASE WHEN P.ZoneCode='SP_EXTRA' AND NULLIF(LTRIM(RTRIM(P.ExtraLocation)),'') IS NOT NULL THEN P.ExtraLocation END AS ExtraLocation,
                (SELECT TOP (1) T.SparePartsTxnID FROM dbo.MNT_SparePartsTxn T
                 WHERE T.SparePartItemID=I.SparePartItemID ORDER BY T.SparePartsTxnID DESC) AS LastTxnId
            FROM dbo.MD_SparePart P
            LEFT JOIN dbo.MD_CodeItem C
              ON C.GroupCode = 'SPAREPARTS_CATEGORY' AND C.CodeValue = P.Category AND COALESCE(C.UseFlag,1) = 1
            LEFT JOIN dbo.MD_CodeItem E
              ON E.GroupCode = 'SPAREPARTS_EQUIP' AND E.CodeValue = P.ApplicableEquip AND COALESCE(E.UseFlag,1) = 1
            LEFT JOIN dbo.MD_Vendor V ON V.VendorID = P.SupplierID
            LEFT JOIN dbo.MNT_SparePartItem I
              ON I.SparePartNo=P.SparePartNo AND UPPER(I.SerialNo)=UPPER(@EosSpNo)
            OUTER APPLY (SELECT CASE
                WHEN NULLIF(P.ZoneCode, '') IS NULL THEN NULL
                WHEN P.ZoneCode = 'SP_EXTRA' THEN 'SP-EXTRA'
                WHEN NULLIF(P.Slot, '') IS NULL OR P.Slot = 'EX' THEN REPLACE(P.ZoneCode, '_', '-')
                ELSE CONCAT(REPLACE(P.ZoneCode, '_', '-'), '-', P.Slot)
            END AS LocationID) ML
            WHERE (@EosSpNo IS NULL OR UPPER(P.SparePartNo) = UPPER(@EosSpNo) OR I.SparePartItemID IS NOT NULL)
              AND COALESCE(P.ActiveFlag,1) = 1
              AND (@Search = '' OR P.SparePartNo LIKE '%' + @Search + '%'
                   OR P.PartNo LIKE '%' + @Search + '%' OR P.PartName LIKE '%' + @Search + '%'
                   OR P.Maker LIKE '%' + @Search + '%' OR ML.LocationID LIKE '%' + @Search + '%')
            ORDER BY P.SparePartNo, ML.LocationID;
            """, conn);
        cmd.Parameters.Add("@EosSpNo", SqlDbType.VarChar, 80).Value = (object?)eosSpNo ?? DBNull.Value;
        cmd.Parameters.Add("@Search", SqlDbType.NVarChar, 120).Value = search?.Trim() ?? "";
        using var rdr = cmd.ExecuteReader();
        var rows = new List<SparePartRow>();
        while (rdr.Read())
        {
            var qty = GetDecimal(rdr, "Qty");
            var hasReceived = Convert.ToInt32(rdr["HasReceived"]) == 1;
            var itemStatus = GetString(rdr, "ItemStatus");
            rows.Add(new SparePartRow(
                GetString(rdr, "EosSpNo") ?? "",
                GetString(rdr, "Category"), GetString(rdr, "ApplicableEquipment"),
                GetString(rdr, "PartName"), GetString(rdr, "PartNo"), GetString(rdr, "Maker"),
                GetString(rdr, "Vendor"), qty, GetString(rdr, "Unit"), GetString(rdr, "StorageLocation"),
                GetString(rdr, "AreaCode"), hasReceived ? GetString(rdr, "InventoryStatus") ?? "OUT OF STOCK" : "NOT RECEIVED",
                string.Equals(itemStatus, "IN_STOCK", StringComparison.OrdinalIgnoreCase) || (itemStatus is null && hasReceived && qty > 0),
                GetImageDataUrl(rdr, "SparePartImage"), hasReceived, GetInt(rdr, "SafetyStock"),
                GetString(rdr, "SerialNo"), itemStatus, GetString(rdr, "ExtraLocation"), GetInt(rdr, "LastTxnId")));
        }
        return rows;
    }

    private static List<InventoryRow> QuerySparePartInventory(AmesConnectionFactory factory, string? search)
    {
        using var conn = factory.OpenConnection();
        using var cmd = new SqlCommand("""
            WITH Stock AS
            (
                SELECT SparePartNo, COUNT(*) AS OnHandQty, COUNT(DISTINCT LocationID) AS LocationCount,
                       CASE WHEN COUNT(DISTINCT LocationID)=1 THEN MAX(LocationID) ELSE '' END AS LocationID,
                       MAX(COALESCE(ModifiedTS,CreatedTS)) AS LastChangedAt
                FROM dbo.MNT_SparePartItem
                WHERE StatusCode='IN_STOCK'
                GROUP BY SparePartNo
            )
            SELECT CAST(ROW_NUMBER() OVER (ORDER BY P.SparePartNo) AS int) AS InventoryID,
                   P.PartNo AS ItemNo, P.SparePartNo AS LotNo, P.PartName AS ItemName, S.LocationID,
                   CAST(S.OnHandQty AS decimal(18,3)) AS OnHandQty, COALESCE(P.UOM, 'EA') AS Unit,
                   CAST(P.SafetyStock AS decimal(18,3)) AS MinQty,
                   CAST(NULL AS decimal(18,3)) AS MaxQty,
                   CASE WHEN P.SafetyStock IS NOT NULL AND S.OnHandQty < P.SafetyStock THEN 'BELOW_MIN'
                        ELSE 'NORMAL' END AS StockStatus,
                   S.LastChangedAt, S.LocationCount
            FROM dbo.MD_SparePart P
            JOIN Stock S ON S.SparePartNo=P.SparePartNo
            WHERE COALESCE(P.ActiveFlag,1) = 1
              AND (@Q = '' OR P.SparePartNo LIKE '%' + @Q + '%' OR P.PartNo LIKE '%' + @Q + '%'
                   OR P.PartName LIKE '%' + @Q + '%' OR EXISTS
                      (SELECT 1 FROM dbo.MNT_SparePartItem I WHERE I.SparePartNo=P.SparePartNo AND I.StatusCode='IN_STOCK'
                       AND (I.SerialNo LIKE '%' + @Q + '%' OR I.LocationID LIKE '%' + @Q + '%')))
            ORDER BY P.SparePartNo;
            """, conn);
        cmd.Parameters.Add("@Q", SqlDbType.NVarChar, 120).Value = search?.Trim() ?? "";
        using var rdr = cmd.ExecuteReader();
        var rows = new List<InventoryRow>();
        while (rdr.Read())
            rows.Add(new InventoryRow(
                rdr.GetInt32(rdr.GetOrdinal("InventoryID")), GetString(rdr, "ItemNo") ?? "",
                GetString(rdr, "ItemName"), GetString(rdr, "LocationID") ?? "", null,
                GetDecimal(rdr, "OnHandQty"), 0, null, Unit: GetString(rdr, "Unit"),
                MinQty: GetNullableDecimal(rdr, "MinQty"), MaxQty: GetNullableDecimal(rdr, "MaxQty"),
                LotCount: Convert.ToInt32(GetDecimal(rdr, "OnHandQty")), LocationCount: Convert.ToInt32(rdr["LocationCount"]),
                Status: GetString(rdr, "StockStatus"), StatusName: GetString(rdr, "StockStatus"),
                LastReceivedDate: GetDate(rdr, "LastChangedAt"), LotNo: GetString(rdr, "LotNo")));
        return rows;
    }

    private static List<LotStatusRow> QuerySparePartLotStatuses(AmesConnectionFactory factory, string? search)
    {
        using var conn = factory.OpenConnection();
        using var cmd = new SqlCommand("""
            SELECT CAST(ROW_NUMBER() OVER (ORDER BY I.SerialNo) AS int) AS RowNo,
                   I.SerialNo, P.PartNo, P.PartName, I.LocationID AS StorageLoc,
                   CAST('IN STOCK' AS varchar(20)) AS InventoryStatus,
                   COALESCE(I.ModifiedTS, I.CreatedTS) AS LastChangedAt
            FROM dbo.MNT_SparePartItem I
            JOIN dbo.MD_SparePart P ON P.SparePartNo=I.SparePartNo
            WHERE COALESCE(P.ActiveFlag,1) = 1
              AND I.StatusCode='IN_STOCK'
              AND (@Q = '' OR I.SerialNo LIKE '%' + @Q + '%' OR P.SparePartNo LIKE '%' + @Q + '%'
                   OR P.PartNo LIKE '%' + @Q + '%' OR P.PartName LIKE '%' + @Q + '%' OR I.LocationID LIKE '%' + @Q + '%')
            ORDER BY I.SerialNo;
            """, conn);
        cmd.Parameters.Add("@Q", SqlDbType.NVarChar, 120).Value = search?.Trim() ?? "";
        using var rdr = cmd.ExecuteReader();
        var rows = new List<LotStatusRow>();
        while (rdr.Read())
            rows.Add(new LotStatusRow(rdr.GetInt32(rdr.GetOrdinal("RowNo")),
                GetString(rdr, "SerialNo") ?? "", GetString(rdr, "PartNo"),
                GetString(rdr, "PartName"), GetString(rdr, "InventoryStatus") ?? "OUT OF STOCK",
                1, GetString(rdr, "StorageLoc"), null,
                GetDate(rdr, "LastChangedAt")));
        return rows;
    }

    private static List<InventoryLocationRow> QuerySparePartInventoryLocations(
        AmesConnectionFactory factory, string eosSpNo)
    {
        using var conn = factory.OpenConnection();
        using var cmd = new SqlCommand("""
            SELECT P.SparePartNo, P.PartNo, I.LocationID AS StorageLoc, COUNT(*) AS OnHandQty,
                   I.LocationID AS LocationName,
                   CAST(NULL AS varchar(20)) AS WhCode, CAST(NULL AS nvarchar(120)) AS WhName,
                   CAST(NULL AS varchar(20)) AS AreaCode, CAST(NULL AS nvarchar(120)) AS AreaName,
                   P.ZoneCode, COALESCE(Z.CodeNameEn, Z.CodeName, P.ZoneCode) AS ZoneName,
                   CAST(NULL AS varchar(10)) AS Aisle, CAST(NULL AS varchar(10)) AS Bay, P.Slot
            FROM dbo.MD_SparePart P
            JOIN dbo.MNT_SparePartItem I ON I.SparePartNo=P.SparePartNo AND I.StatusCode='IN_STOCK'
            LEFT JOIN dbo.MD_CodeItem Z
              ON Z.GroupCode='MNT_ZONE' AND Z.CodeValue=P.ZoneCode AND COALESCE(Z.UseFlag,1)=1
            WHERE (UPPER(P.SparePartNo) = UPPER(@SparePartNo) OR UPPER(P.PartNo) = UPPER(@SparePartNo))
              AND COALESCE(P.ActiveFlag,1) = 1
            GROUP BY P.SparePartNo,P.PartNo,I.LocationID,P.ZoneCode,Z.CodeNameEn,Z.CodeName,P.Slot
            ORDER BY I.LocationID;
            """, conn);
        cmd.Parameters.Add("@SparePartNo", SqlDbType.VarChar, 40).Value = eosSpNo.Trim();
        using var rdr = cmd.ExecuteReader();
        var rows = new List<InventoryLocationRow>();
        var rowNo = 0;
        while (rdr.Read() && !string.IsNullOrWhiteSpace(GetString(rdr, "StorageLoc")))
            rows.Add(new InventoryLocationRow(++rowNo, GetString(rdr, "PartNo") ?? "",
                GetString(rdr, "StorageLoc") ?? "", GetString(rdr, "LocationName"),
                GetString(rdr, "WhCode"), GetString(rdr, "WhName"), GetString(rdr, "AreaCode"),
                GetString(rdr, "AreaName"), GetString(rdr, "ZoneCode"), GetString(rdr, "ZoneName"),
                GetString(rdr, "Aisle"), GetString(rdr, "Bay"), GetString(rdr, "Slot"),
                GetDecimal(rdr, "OnHandQty")));
        return rows;
    }

    private static List<LocationMapItemRow> QuerySparePartLocationItems(
        AmesConnectionFactory factory, string locationId)
    {
        using var conn = factory.OpenConnection();
        using var cmd = new SqlCommand("""
            SELECT I.SerialNo, P.PartNo, P.PartName, COALESCE(P.UOM,'EA') AS Unit,
                   CAST('IN STOCK' AS varchar(20)) AS InventoryStatus,
                   CONVERT(char(10), COALESCE(I.ModifiedTS,I.CreatedTS), 23) AS WorkDate,
                   CONVERT(char(8), COALESCE(I.ModifiedTS,I.CreatedTS), 108) AS WorkTime
            FROM dbo.MNT_SparePartItem I
            JOIN dbo.MD_SparePart P ON P.SparePartNo=I.SparePartNo
            WHERE COALESCE(P.ActiveFlag,1) = 1
              AND I.StatusCode='IN_STOCK' AND UPPER(I.LocationID)=UPPER(@LocationId)
            ORDER BY I.SerialNo;
            """, conn);
        cmd.Parameters.Add("@LocationId", SqlDbType.VarChar, 20).Value = locationId.Trim();
        using var rdr = cmd.ExecuteReader();
        var rows = new List<LocationMapItemRow>();
        while (rdr.Read())
            rows.Add(new LocationMapItemRow(GetString(rdr, "SerialNo") ?? "", GetString(rdr, "PartNo"),
                GetString(rdr, "PartName"), 1, GetString(rdr, "Unit"),
                GetString(rdr, "InventoryStatus"), GetString(rdr, "WorkDate"), GetString(rdr, "WorkTime")));
        return rows;
    }

    private static List<LocationRow> QuerySparePartLocations(AmesConnectionFactory factory)
    {
        using var conn = factory.OpenConnection();
        using var cmd = new SqlCommand("""
            WITH SpareMaster AS
            (
                SELECT P.SparePartNo, P.ZoneCode, P.Slot,
                       CASE
                           WHEN P.ZoneCode = 'SP_EXTRA' THEN 'SP-EXTRA'
                           WHEN NULLIF(P.Slot, '') IS NULL OR P.Slot = 'EX' THEN REPLACE(P.ZoneCode, '_', '-')
                           ELSE CONCAT(REPLACE(P.ZoneCode, '_', '-'), '-', P.Slot)
                       END AS LocationID
                FROM dbo.MD_SparePart P
                WHERE COALESCE(P.ActiveFlag,1)=1 AND NULLIF(P.ZoneCode,'') IS NOT NULL
            )
            SELECT M.LocationID, M.LocationID AS LocationName, M.ZoneCode,
                   CAST(NULL AS varchar(20)) AS WhCode, CAST(NULL AS nvarchar(120)) AS WhName,
                   CAST(NULL AS varchar(20)) AS AreaCode, CAST(NULL AS nvarchar(120)) AS AreaName,
                   CAST(NULL AS varchar(10)) AS Aisle, CAST(NULL AS varchar(10)) AS Bay, M.Slot,
                   CAST(NULL AS varchar(20)) AS PlantCode, CAST('SPARE_PART' AS varchar(20)) AS LocationType,
                   CAST(NULL AS decimal(18,3)) AS Capacity,
                   COUNT(DISTINCT M.SparePartNo) AS LineCount,
                   COUNT(I.SparePartItemID) AS TotalQty,
                   CAST('EA' AS varchar(10)) AS Unit
            FROM SpareMaster M
            LEFT JOIN dbo.MNT_SparePartItem I
              ON I.SparePartNo=M.SparePartNo AND I.LocationID=M.LocationID AND I.StatusCode='IN_STOCK'
            GROUP BY M.LocationID,M.ZoneCode,M.Slot
            ORDER BY M.ZoneCode,M.LocationID;
            """, conn);
        using var rdr = cmd.ExecuteReader();
        var rows = new List<LocationRow>();
        while (rdr.Read())
            rows.Add(new LocationRow(GetString(rdr, "LocationID") ?? "", GetString(rdr, "LocationName"),
                GetString(rdr, "ZoneCode"), Convert.ToInt32(rdr["LineCount"]), GetDecimal(rdr, "TotalQty"),
                GetString(rdr, "WhCode"), GetString(rdr, "WhName"), GetString(rdr, "AreaCode"),
                GetString(rdr, "AreaName"), GetString(rdr, "ZoneCode"), GetString(rdr, "Aisle"),
                GetString(rdr, "Bay"), GetString(rdr, "Slot"), GetString(rdr, "PlantCode"),
                GetString(rdr, "LocationType"), GetNullableDecimal(rdr, "Capacity"), GetString(rdr, "Unit")));
        return rows;
    }

    private static SparePartAdjustmentLocation? QuerySparePartAdjustmentLocation(
        AmesConnectionFactory factory, string barcode)
    {
        using var conn = factory.OpenConnection();
        using var location = new SqlCommand("""
            SELECT TOP (1) V.LocationID
            FROM dbo.MD_SparePart P
            CROSS APPLY (SELECT CASE
                WHEN P.ZoneCode='SP_EXTRA' THEN 'SP-EXTRA'
                WHEN NULLIF(P.Slot,'') IS NULL OR P.Slot='EX' THEN REPLACE(P.ZoneCode,'_','-')
                ELSE CONCAT(REPLACE(P.ZoneCode,'_','-'),'-',P.Slot)
            END AS LocationID) V
            WHERE COALESCE(P.ActiveFlag,1)=1 AND NULLIF(P.ZoneCode,'') IS NOT NULL
              AND UPPER(V.LocationID)=UPPER(@Barcode);
            """, conn);
        location.Parameters.Add("@Barcode", SqlDbType.NVarChar, 80).Value = barcode.Trim();
        if (location.ExecuteScalar() is not string locationId) return null;

        using var cmd = new SqlCommand("""
            SELECT P.SparePartNo, P.PartNo, P.PartName, P.OnHandQty, COALESCE(P.UOM,'EA') AS Unit
            FROM dbo.MD_SparePart P
            CROSS APPLY (SELECT CASE
                WHEN P.ZoneCode='SP_EXTRA' THEN 'SP-EXTRA'
                WHEN NULLIF(P.Slot,'') IS NULL OR P.Slot='EX' THEN REPLACE(P.ZoneCode,'_','-')
                ELSE CONCAT(REPLACE(P.ZoneCode,'_','-'),'-',P.Slot)
            END AS LocationID) V
            WHERE COALESCE(P.ActiveFlag,1)=1
              AND UPPER(V.LocationID)=UPPER(@LocationId)
              AND EXISTS (SELECT 1 FROM dbo.MNT_SparePartsTxn T
                  WHERE T.SparePartNo=P.SparePartNo AND T.MoveType='IN' AND T.RefType='PDA')
            ORDER BY P.SparePartNo;
            """, conn);
        cmd.Parameters.Add("@LocationId", SqlDbType.VarChar, 20).Value = locationId;
        using var rdr = cmd.ExecuteReader();
        var items = new List<SparePartAdjustmentStock>();
        while (rdr.Read())
        {
            var eosSpNo = GetString(rdr, "SparePartNo") ?? "";
            items.Add(new SparePartAdjustmentStock(eosSpNo, eosSpNo, GetString(rdr, "PartNo") ?? "",
                GetString(rdr, "PartName"), GetDecimal(rdr, "OnHandQty"), GetString(rdr, "Unit")));
        }
        return new SparePartAdjustmentLocation(locationId, items);
    }

    private static InboundScanRow? QuerySparePartInboundRow(AmesConnectionFactory factory, string eosSpNo)
    {
        var row = QuerySparePart(factory, eosSpNo);
        return row is null ? null : new InboundScanRow(
            "SP", row.HasReceived ? "N" : "Y", row.EosSpNo, row.EosSpNo, "MD_SparePart",
            null, null, null, null, null, row.PartNo, row.PartName, row.Qty, row.Unit,
            null, null, null, row.Vendor, null, null, null, null, null,
            row.StorageLocation, row.InventoryStatus);
    }

    private static InboundReceiveResult ExecuteSparePartLocationMove(
        AmesConnectionFactory factory, string eosSpNo, string locationId, string userId)
    {
        return new InboundReceiveResult(false,
            "Manage spare-part locations in Spare Part Master.",
            QuerySparePartInboundRow(factory, eosSpNo));
    }

    private static List<WarehouseTransactionRow> QuerySparePartTransactions(
        AmesConnectionFactory factory, string? search, DateTime? dateFrom, DateTime? dateTo)
    {
        using var conn = factory.OpenConnection();
        using var cmd = new SqlCommand("""
            SELECT T.SparePartsTxnID, T.SparePartNo, I.SerialNo, P.PartNo,
                   COALESCE(I.LocationID,ML.LocationID,T.RefID) AS StorageLoc, T.MoveType, T.Qty,
                   T.BalanceBefore, T.BalanceAfter, T.RefType, T.RefID, T.Note, T.ActorID,
                   CONVERT(char(10),T.TxnAt,23) AS WorkDate, CONVERT(char(8),T.TxnAt,108) AS WorkTime
            FROM dbo.MNT_SparePartsTxn T
            LEFT JOIN dbo.MD_SparePart P ON P.SparePartNo=T.SparePartNo
            LEFT JOIN dbo.MNT_SparePartItem I ON I.SparePartItemID=T.SparePartItemID
            OUTER APPLY (SELECT CASE
                WHEN P.ZoneCode='SP_EXTRA' THEN 'SP-EXTRA'
                WHEN NULLIF(P.ZoneCode,'') IS NULL THEN NULL
                WHEN NULLIF(P.Slot,'') IS NULL OR P.Slot='EX' THEN REPLACE(P.ZoneCode,'_','-')
                ELSE CONCAT(REPLACE(P.ZoneCode,'_','-'),'-',P.Slot)
            END AS LocationID) ML
            WHERE (@Q='' OR T.SparePartNo LIKE '%'+@Q+'%' OR I.SerialNo LIKE '%'+@Q+'%' OR P.PartNo LIKE '%'+@Q+'%' OR P.PartName LIKE '%'+@Q+'%'
                         OR T.ActorID LIKE '%'+@Q+'%' OR T.RefID LIKE '%'+@Q+'%')
              AND (@DateFrom IS NULL OR T.TxnAt>=@DateFrom)
              AND (@DateTo IS NULL OR T.TxnAt<DATEADD(day,1,@DateTo))
            ORDER BY T.TxnAt DESC,T.SparePartsTxnID DESC;
            """, conn);
        cmd.Parameters.Add("@Q", SqlDbType.NVarChar, 120).Value = search?.Trim() ?? "";
        cmd.Parameters.Add("@DateFrom", SqlDbType.Date).Value = (object?)dateFrom?.Date ?? DBNull.Value;
        cmd.Parameters.Add("@DateTo", SqlDbType.Date).Value = (object?)dateTo?.Date ?? DBNull.Value;
        using var rdr = cmd.ExecuteReader();
        var rows = new List<WarehouseTransactionRow>();
        while (rdr.Read())
        {
            var move = GetString(rdr, "MoveType") ?? "";
            var reason = string.Equals(GetString(rdr, "RefType"), "PDA_ADJUST", StringComparison.OrdinalIgnoreCase)
                ? GetString(rdr, "RefID") : null;
            rows.Add(new WarehouseTransactionRow(Convert.ToInt64(rdr["SparePartsTxnID"]),
                GetString(rdr, "SerialNo") ?? GetString(rdr, "SparePartNo"), GetString(rdr, "PartNo"), GetString(rdr, "WorkDate"),
                GetString(rdr, "WorkTime"), GetString(rdr, "StorageLoc"), GetDecimal(rdr, "Qty"),
                move, move, GetString(rdr, "ActorID"), reason, GetString(rdr, "Note"), null,
                GetNullableDecimal(rdr, "BalanceBefore"),
                move is "OUT" or "IN_CANCEL" ? -GetNullableDecimal(rdr, "Qty") : move is "MOVE" or "LABEL" ? 0 : GetNullableDecimal(rdr, "Qty"),
                GetNullableDecimal(rdr, "BalanceAfter"), null, null, null,
                move == "MOVE" ? GetString(rdr, "RefID") : null, GetString(rdr, "RefType"),
                GetString(rdr, "Note"), "EA"));
        }
        return rows;
    }

    private static SparePartMoveResult ExecuteSparePartMove(
        AmesConnectionFactory factory, SparePartMoveReq body, string moveType, string userId)
    {
        if (string.IsNullOrWhiteSpace(body.EosSpNo))
            return new SparePartMoveResult(false, "Scan a Serial No first.");
        if (body.Qty != 1)
            return new SparePartMoveResult(false, "Serialized spare parts must move one item at a time.");

        try
        {
            using var conn = factory.OpenConnection();
            using var cmd = new SqlCommand(PdaSparePartStockMoveProcedure, conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.Add("@SerialNo", SqlDbType.VarChar, 24).Value = body.EosSpNo.Trim();
            cmd.Parameters.Add("@MoveType", SqlDbType.VarChar, 10).Value = moveType;
            cmd.Parameters.Add("@Qty", SqlDbType.Int).Value = body.Qty;
            cmd.Parameters.Add("@LocationId", SqlDbType.VarChar, 20).Value =
                string.IsNullOrWhiteSpace(body.LocationId) ? DBNull.Value : body.LocationId.Trim();
            cmd.Parameters.Add("@UserId", SqlDbType.NVarChar, 450).Value = userId;
            cmd.Parameters.Add("@Note", SqlDbType.NVarChar, 500).Value =
                string.IsNullOrWhiteSpace(body.Note) ? DBNull.Value : body.Note.Trim();
            cmd.ExecuteNonQuery();

            return new SparePartMoveResult(true,
                moveType == "IN" ? "Spare part received." : "Spare part released.",
                QuerySparePart(factory, body.EosSpNo));
        }
        catch (Exception ex)
        {
            return new SparePartMoveResult(false, WarehouseProcedureMessage(ex));
        }
    }

    private static List<SparePartRow> CreateSparePartLabels(
        AmesConnectionFactory factory, SparePartLabelCreateReq body, string userId)
    {
        if (string.IsNullOrWhiteSpace(body.EosSpNo)) throw new InvalidOperationException("Select a spare part first.");
        using var conn = factory.OpenConnection();
        using var cmd = new SqlCommand("dbo.SP_PDA_SP_SERIAL_CREATE", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.Add("@SparePartNo", SqlDbType.VarChar, 16).Value = body.EosSpNo.Trim();
        cmd.Parameters.Add("@Count", SqlDbType.Int).Value = body.Count;
        cmd.Parameters.Add("@UserId", SqlDbType.NVarChar, 450).Value = userId;
        var serials = new List<string>();
        using (var rdr = cmd.ExecuteReader())
            while (rdr.Read()) serials.Add(rdr.GetString(0));
        return serials.Select(serial => QuerySparePart(factory, serial)
                ?? throw new InvalidOperationException($"Created serial {serial} could not be loaded."))
            .ToList();
    }

    private static List<SparePartSerialRow> QuerySparePartSerials(AmesConnectionFactory factory, string eosSpNo)
    {
        using var conn = factory.OpenConnection();
        using var cmd = new SqlCommand("""
            SELECT SerialNo, StatusCode, CreatedTS
            FROM dbo.MNT_SparePartItem
            WHERE UPPER(SparePartNo)=UPPER(@SparePartNo)
            ORDER BY SparePartItemID DESC;
            """, conn);
        cmd.Parameters.Add("@SparePartNo", SqlDbType.VarChar, 16).Value = eosSpNo.Trim();
        using var rdr = cmd.ExecuteReader();
        var rows = new List<SparePartSerialRow>();
        while (rdr.Read())
            rows.Add(new(GetString(rdr, "SerialNo") ?? "", GetString(rdr, "StatusCode") ?? "CREATED",
                GetDate(rdr, "CreatedTS") ?? DateTime.MinValue));
        return rows;
    }

    private static SparePartMoveResult CancelSparePartMove(
        AmesConnectionFactory factory, SparePartCancelReq body, string userId)
    {
        if (string.IsNullOrWhiteSpace(body.SerialNo)) return new(false, "Scan a Serial No first.");
        var move = body.MoveType?.Trim().ToUpperInvariant() ?? "";
        if (move is not ("IN" or "OUT")) return new(false, "Cancellation type must be IN or OUT.");
        try
        {
            using var conn = factory.OpenConnection();
            using var cmd = new SqlCommand("dbo.SP_PDA_SP_STOCK_CANCEL", conn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.Add("@SerialNo", SqlDbType.VarChar, 24).Value = body.SerialNo.Trim();
            cmd.Parameters.Add("@MoveType", SqlDbType.VarChar, 10).Value = move;
            cmd.Parameters.Add("@UserId", SqlDbType.NVarChar, 450).Value = userId;
            cmd.Parameters.Add("@Note", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(body.Note) ? DBNull.Value : body.Note.Trim();
            cmd.ExecuteNonQuery();
            return new(true, move == "IN" ? "Inbound cancelled." : "Release cancelled.", QuerySparePart(factory, body.SerialNo));
        }
        catch (Exception ex) { return new(false, WarehouseProcedureMessage(ex)); }
    }

    private static DirectOutgoingResult ExecuteDirectOutgoing(
        AmesConnectionFactory factory, DirectOutgoingReq body, string reasonCode, string userId)
    {
        var type = body.OutgoingType?.Trim().ToUpperInvariant() ?? "";
        if (string.IsNullOrWhiteSpace(body.LotNo))
            return new DirectOutgoingResult(false, "Scan a LOT No first.");
        if (type == "SUPPLIER" && string.IsNullOrWhiteSpace(body.TargetCode))
            return new DirectOutgoingResult(false, "Select a supplier first.");

        using var conn = factory.OpenConnection();
        using var tx = conn.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            if (type == "SUPPLIER")
            {
                using var vendorCmd = new SqlCommand("""
                    SELECT COUNT(*) FROM dbo.MD_Vendor
                    WHERE VendorID=@VendorID AND COALESCE(ActiveFlag,1)=1;
                    """, conn, tx);
                vendorCmd.Parameters.AddWithValue("@VendorID", body.TargetCode!.Trim());
                if ((int)vendorCmd.ExecuteScalar()! == 0)
                    return new DirectOutgoingResult(false, "The selected supplier is not available.");
            }

            int lotId;
            int inventoryId;
            string lotCode;
            string itemNo;
            string? itemName;
            string? unit;
            string? locationId;
            string beforeStatus;
            decimal beforeQty;

            using (var lotCmd = new SqlCommand("""
                SELECT TOP (1)
                    L.LotID, W.InventoryID, L.LotCode, L.ItemNo, I.ItemName, I.DefaultUOM,
                    W.LocationID, COALESCE(W.OnHandQty,0) AS Qty,
                    COALESCE(NULLIF(L.InventoryStatus,''), NULLIF(W.Status,''), 'CREATED') AS InventoryStatus
                FROM dbo.tbl_Lot L WITH (UPDLOCK,HOLDLOCK)
                INNER JOIN dbo.WH_Inventory W WITH (UPDLOCK,HOLDLOCK) ON W.LotID=L.LotID
                LEFT JOIN dbo.MD_Item I ON I.ItemNo=L.ItemNo
                WHERE UPPER(L.LotCode)=UPPER(@LotNo)
                ORDER BY CASE WHEN COALESCE(W.OnHandQty,0)>0 THEN 0 ELSE 1 END, W.InventoryID DESC;
                """, conn, tx))
            {
                lotCmd.Parameters.AddWithValue("@LotNo", body.LotNo.Trim());
                using var rdr = lotCmd.ExecuteReader();
                if (!rdr.Read())
                    return new DirectOutgoingResult(false, "LOT inventory was not found.");

                lotId = rdr.GetInt32(rdr.GetOrdinal("LotID"));
                inventoryId = rdr.GetInt32(rdr.GetOrdinal("InventoryID"));
                lotCode = GetString(rdr, "LotCode") ?? body.LotNo.Trim();
                itemNo = GetString(rdr, "ItemNo") ?? "";
                itemName = GetString(rdr, "ItemName");
                unit = GetString(rdr, "DefaultUOM");
                locationId = GetString(rdr, "LocationID");
                beforeQty = GetDecimal(rdr, "Qty");
                beforeStatus = GetString(rdr, "InventoryStatus") ?? "CREATED";
            }

            var validStatus = new[] { "RECEIVED", "STORED", "RETURN_RECEIVED", "RELEASE_CANCELLED" }
                .Contains(beforeStatus, StringComparer.OrdinalIgnoreCase);
            if (beforeQty <= 0 || !validStatus)
                return new DirectOutgoingResult(false, $"LOT cannot be released. Current status: {beforeStatus}.");

            var outgoingQty = type == "DEFECT" ? body.Qty : beforeQty;
            if (outgoingQty <= 0 || outgoingQty > beforeQty)
                return new DirectOutgoingResult(false, $"Outgoing quantity must be between 1 and {beforeQty:N0}.");

            var afterQty = beforeQty - outgoingQty;
            var afterStatus = afterQty == 0 ? "RELEASED" : beforeStatus;
            var target = string.IsNullOrWhiteSpace(body.TargetCode) ? null : body.TargetCode.Trim();
            var note = string.IsNullOrWhiteSpace(body.Note)
                ? $"{reasonCode}{(target is null ? "" : $" / {target}")}"
                : body.Note.Trim();

            using (var saveCmd = new SqlCommand("""
                UPDATE dbo.WH_Inventory
                   SET OnHandQty=@AfterQty,
                       ReservedQty=CASE WHEN COALESCE(ReservedQty,0)>@AfterQty THEN @AfterQty ELSE ReservedQty END,
                       Status=CASE WHEN @AfterQty=0 THEN 'Released' ELSE 'Stored' END,
                       ModifiedBy=@User, ModifiedTS=SYSDATETIME()
                 WHERE InventoryID=@InventoryID AND COALESCE(OnHandQty,0)=@BeforeQty;
                IF @@ROWCOUNT<>1 THROW 51630, 'LOT inventory changed before outgoing.', 1;

                UPDATE dbo.tbl_Lot
                   SET RemainingQty=@AfterQty,
                       InventoryStatus=@AfterStatus,
                       Status=CASE WHEN @AfterQty=0 THEN 'Released' ELSE Status END,
                       CurrentLocationID=CASE WHEN @AfterQty=0 THEN NULL ELSE CurrentLocationID END,
                       ModifiedBy=@User, ModifiedTS=SYSDATETIME()
                 WHERE LotID=@LotID;

                INSERT dbo.WH_InventoryTransaction
                    (TransactionTime,TransactionType,ItemNo,LocationID,LotID,QtyBefore,QtyChange,QtyAfter,
                     ReasonCode,RefDocType,RefDocID,OperatorID,Note,CreatedBy,CreatedTS)
                VALUES
                    (SYSDATETIME(),'OUT',@ItemNo,@LocationID,@LotID,@BeforeQty,-@OutgoingQty,@AfterQty,
                     @ReasonCode,'DIRECT_OUTGOING',NULL,@User,@Note,@User,SYSDATETIME());
                """, conn, tx))
            {
                saveCmd.Parameters.AddWithValue("@LotID", lotId);
                saveCmd.Parameters.AddWithValue("@InventoryID", inventoryId);
                saveCmd.Parameters.AddWithValue("@LotNo", lotCode);
                saveCmd.Parameters.AddWithValue("@ItemNo", itemNo);
                saveCmd.Parameters.AddWithValue("@LocationID", (object?)locationId ?? DBNull.Value);
                saveCmd.Parameters.AddWithValue("@BeforeQty", beforeQty);
                saveCmd.Parameters.AddWithValue("@OutgoingQty", outgoingQty);
                saveCmd.Parameters.AddWithValue("@AfterQty", afterQty);
                saveCmd.Parameters.AddWithValue("@BeforeStatus", beforeStatus);
                saveCmd.Parameters.AddWithValue("@AfterStatus", afterStatus);
                saveCmd.Parameters.AddWithValue("@ReasonCode", reasonCode);
                saveCmd.Parameters.AddWithValue("@TargetCode", (object?)target ?? DBNull.Value);
                saveCmd.Parameters.AddWithValue("@Note", note);
                saveCmd.Parameters.AddWithValue("@User", userId);
                saveCmd.ExecuteNonQuery();
            }

            tx.Commit();
            var row = new DirectOutgoingLotRow(lotId, lotCode, itemNo, itemName, afterQty, unit,
                locationId, afterStatus, afterQty > 0, "Outgoing completed.");
            return new DirectOutgoingResult(true, "Outgoing completed.", row);
        }
        catch (Exception ex)
        {
            try { tx.Rollback(); } catch { }
            return new DirectOutgoingResult(false, ex.Message);
        }
    }

    private sealed record ReleaseBatchLot(
        int LotId, int InventoryId, int ReleaseScheduleId, string LotNo, string ItemNo,
        string? LocationId, decimal Qty, DateTime? ProductionDate, DateTime? ReceivedDate, string InventoryStatus);

    private static ReleaseCompleteResult ExecuteReleaseBatch(
        AmesConnectionFactory factory,
        string pickSlipNo,
        IReadOnlyCollection<ReleasePickInput> requestedLots,
        string reasonCode,
        string userId,
        string terminalId,
        bool simulateFailure = false)
    {
        var slip = pickSlipNo.Trim();
        var normalized = requestedLots
            .Where(x => !string.IsNullOrWhiteSpace(x.LotNo))
            .Select(x => new ReleasePickInput(x.LotNo.Trim(), x.Qty))
            .ToList();
        if (normalized.Count == 0)
            return new ReleaseCompleteResult(false, "No scanned LOTs were supplied.");
        if (normalized.Select(x => x.LotNo).Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalized.Count)
            return new ReleaseCompleteResult(false, "The same LOT was scanned more than once.");

        using var conn = factory.OpenConnection();
        using var tx = conn.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            var lines = new Dictionary<string, (int ScheduleId, decimal Demand, decimal Picked)>(StringComparer.OrdinalIgnoreCase);
            using (var lineCmd = new SqlCommand("""
                SELECT ReleaseScheduleID, ItemNo, COALESCE(DemandQty,0) AS DemandQty, COALESCE(PickedQty,0) AS PickedQty
                FROM dbo.WH_ReleaseSchedule WITH (UPDLOCK, HOLDLOCK)
                WHERE UPPER(COALESCE(NULLIF(PickSlipNo,N''), CONCAT(N'RS-',ReleaseScheduleID))) = UPPER(@Slip)
                  AND UPPER(COALESCE(Status,N'OPEN')) NOT IN (N'RELEASED',N'CLOSED',N'CANCELLED',N'CANCELED');
                """, conn, tx))
            {
                lineCmd.Parameters.AddWithValue("@Slip", slip);
                using var rdr = lineCmd.ExecuteReader();
                while (rdr.Read())
                    lines[rdr.GetString(rdr.GetOrdinal("ItemNo"))] = (
                        rdr.GetInt32(rdr.GetOrdinal("ReleaseScheduleID")),
                        rdr.GetDecimal(rdr.GetOrdinal("DemandQty")),
                        rdr.GetDecimal(rdr.GetOrdinal("PickedQty")));
            }
            if (lines.Count == 0)
            {
                return new ReleaseCompleteResult(false, "Pick Slip was not found or is already released.");
            }

            var lots = new List<ReleaseBatchLot>();
            foreach (var input in normalized)
            {
                using var lotCmd = new SqlCommand("""
                    SELECT TOP (1)
                        L.LotID, W.InventoryID, RS.ReleaseScheduleID, L.LotCode, L.ItemNo,
                        W.LocationID, COALESCE(W.OnHandQty,0) AS Qty, L.ProducedAt, W.LastReceivedAt,
                        COALESCE(NULLIF(L.InventoryStatus,''), CASE WHEN W.LocationID IS NULL THEN 'RECEIVED' ELSE 'STORED' END) AS InventoryStatus
                    FROM dbo.tbl_Lot L WITH (UPDLOCK, HOLDLOCK)
                    INNER JOIN dbo.WH_Inventory W WITH (UPDLOCK, HOLDLOCK) ON W.LotID = L.LotID
                    INNER JOIN dbo.WH_ReleaseSchedule RS WITH (UPDLOCK, HOLDLOCK)
                            ON RS.ItemNo = L.ItemNo
                           AND UPPER(COALESCE(NULLIF(RS.PickSlipNo,N''), CONCAT(N'RS-',RS.ReleaseScheduleID))) = UPPER(@Slip)
                    WHERE UPPER(L.LotCode) = UPPER(@LotNo)
                    ORDER BY RS.ReleaseScheduleID;
                    """, conn, tx);
                lotCmd.Parameters.AddWithValue("@Slip", slip);
                lotCmd.Parameters.AddWithValue("@LotNo", input.LotNo);
                using var rdr = lotCmd.ExecuteReader();
                if (!rdr.Read())
                {
                    return new ReleaseCompleteResult(false, $"LOT {input.LotNo} is not part of this Pick Slip.");
                }
                var lot = new ReleaseBatchLot(
                    rdr.GetInt32(rdr.GetOrdinal("LotID")),
                    rdr.GetInt32(rdr.GetOrdinal("InventoryID")),
                    rdr.GetInt32(rdr.GetOrdinal("ReleaseScheduleID")),
                    rdr.GetString(rdr.GetOrdinal("LotCode")),
                    rdr.GetString(rdr.GetOrdinal("ItemNo")),
                    rdr.IsDBNull(rdr.GetOrdinal("LocationID")) ? null : rdr.GetString(rdr.GetOrdinal("LocationID")),
                    rdr.GetDecimal(rdr.GetOrdinal("Qty")),
                    rdr.IsDBNull(rdr.GetOrdinal("ProducedAt")) ? null : rdr.GetDateTime(rdr.GetOrdinal("ProducedAt")),
                    rdr.IsDBNull(rdr.GetOrdinal("LastReceivedAt")) ? null : rdr.GetDateTime(rdr.GetOrdinal("LastReceivedAt")),
                    rdr.GetString(rdr.GetOrdinal("InventoryStatus")));
                if (lot.Qty <= 0 || !new[] { "RECEIVED", "STORED", "RETURN_RECEIVED", "RELEASE_CANCELLED" }.Contains(lot.InventoryStatus, StringComparer.OrdinalIgnoreCase))
                {
                    return new ReleaseCompleteResult(false, $"LOT {lot.LotNo} cannot be released. Current status: {lot.InventoryStatus}.");
                }
                lots.Add(lot);
            }

            foreach (var line in lines)
            {
                var selectedBoxes = lots.Count(x => string.Equals(x.ItemNo, line.Key, StringComparison.OrdinalIgnoreCase));
                var remainingBoxes = line.Value.Demand - line.Value.Picked;
                if (selectedBoxes != remainingBoxes)
                {
                    return new ReleaseCompleteResult(false,
                        $"{line.Key}: scan {remainingBoxes:N0} box(es) before Release. Current scan: {selectedBoxes:N0}.");
                }
            }

            var selectedLotNos = lots.Select(x => x.LotNo).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var lot in lots)
            {
                using var fifoCmd = new SqlCommand("dbo.WH_PDA_FIFO_VIEW", conn, tx) { CommandType = CommandType.StoredProcedure };
                fifoCmd.Parameters.AddWithValue("@LotNo", lot.LotNo);
                using var fifoRdr = fifoCmd.ExecuteReader();
                while (fifoRdr.Read())
                {
                    var olderLot = GetString(fifoRdr, "LOTNO");
                    if (!string.IsNullOrWhiteSpace(olderLot) && !selectedLotNos.Contains(olderLot))
                    {
                        return new ReleaseCompleteResult(false,
                            $"FIFO blocked. Release older LOT {olderLot} before {lot.LotNo}.");
                    }
                }
            }

            foreach (var lot in lots)
            {
                using var cmd = new SqlCommand("""
                    DECLARE @BeforeStatus varchar(30);
                    SELECT @BeforeStatus = InventoryStatus FROM dbo.tbl_Lot WHERE LotID=@LotID;

                    UPDATE dbo.WH_Inventory
                       SET OnHandQty=0, ReservedQty=0, Status='Released', ModifiedBy=@User, ModifiedTS=SYSDATETIME()
                     WHERE InventoryID=@InventoryID AND COALESCE(OnHandQty,0)>0;
                    IF @@ROWCOUNT<>1 THROW 51620, 'LOT inventory changed before Release.', 1;

                    UPDATE dbo.tbl_Lot
                       SET RemainingQty=0, InventoryStatus='RELEASED', Status='Released', CurrentLocationID=NULL,
                           ModifiedBy=@User, ModifiedTS=SYSDATETIME()
                     WHERE LotID=@LotID;

                    INSERT dbo.WH_ReleasePicking
                        (PickingNo,ReleaseScheduleID,ItemNo,LocationID,LotID,PickedQty,PickedAt,PickedBy,TerminalID,FifoOverride,CreatedBy,CreatedTS)
                    VALUES
                        (CONCAT('PICK-',FORMAT(SYSDATETIME(),'yyMMddHHmmssfff')),@ScheduleID,@ItemNo,@LocationID,@LotID,@Qty,
                         SYSDATETIME(),@User,@Terminal,0,@User,SYSDATETIME());

                    INSERT dbo.WH_InventoryTransaction
                        (TransactionTime,TransactionType,ItemNo,LocationID,LotID,QtyBefore,QtyChange,QtyAfter,
                         ReasonCode,RefDocType,RefDocID,OperatorID,Note,CreatedBy,CreatedTS)
                    VALUES
                        (SYSDATETIME(),'OUT',@ItemNo,@LocationID,@LotID,@Qty,-@Qty,0,
                         @ReasonCode,'PICK_SLIP',@ScheduleID,@User,CONCAT('Release ',@Slip,' / ',@ReasonCode),@User,SYSDATETIME());
                    """, conn, tx);
                cmd.Parameters.AddWithValue("@LotID", lot.LotId);
                cmd.Parameters.AddWithValue("@InventoryID", lot.InventoryId);
                cmd.Parameters.AddWithValue("@ScheduleID", lot.ReleaseScheduleId);
                cmd.Parameters.AddWithValue("@LotNo", lot.LotNo);
                cmd.Parameters.AddWithValue("@ItemNo", lot.ItemNo);
                cmd.Parameters.AddWithValue("@LocationID", (object?)lot.LocationId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ProductionDate", (object?)lot.ProductionDate ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ReceivedDate", (object?)lot.ReceivedDate ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Qty", lot.Qty);
                cmd.Parameters.AddWithValue("@Slip", slip);
                cmd.Parameters.AddWithValue("@ReasonCode", reasonCode);
                cmd.Parameters.AddWithValue("@User", userId);
                cmd.Parameters.AddWithValue("@Terminal", terminalId);
                cmd.ExecuteNonQuery();
            }

            using (var finish = new SqlCommand("""
                ;WITH PickCounts AS
                (
                    SELECT ItemNo, COUNT(*) AS BoxQty FROM dbo.WH_ReleasePicking
                    WHERE ReleaseScheduleID IN
                    (
                        SELECT ReleaseScheduleID FROM dbo.WH_ReleaseSchedule
                        WHERE UPPER(COALESCE(NULLIF(PickSlipNo,N''),CONCAT(N'RS-',ReleaseScheduleID)))=UPPER(@Slip)
                    )
                    GROUP BY ItemNo
                )
                UPDATE RS
                   SET PickedQty=COALESCE(P.BoxQty,0),
                       Status=CASE WHEN COALESCE(P.BoxQty,0)>=COALESCE(RS.DemandQty,0) THEN 'Closed' ELSE 'Partial' END,
                       CloseDate=CASE WHEN COALESCE(P.BoxQty,0)>=COALESCE(RS.DemandQty,0) THEN SYSDATETIME() ELSE CloseDate END,
                       CloseUserId=CASE WHEN COALESCE(P.BoxQty,0)>=COALESCE(RS.DemandQty,0) THEN @User ELSE CloseUserId END,
                       ModifiedBy=@User, ModifiedTS=SYSDATETIME()
                FROM dbo.WH_ReleaseSchedule RS
                LEFT JOIN PickCounts P ON P.ItemNo=RS.ItemNo
                WHERE UPPER(COALESCE(NULLIF(RS.PickSlipNo,N''),CONCAT(N'RS-',RS.ReleaseScheduleID)))=UPPER(@Slip);
                """, conn, tx))
            {
                finish.Parameters.AddWithValue("@Slip", slip);
                finish.Parameters.AddWithValue("@User", userId);
                finish.ExecuteNonQuery();
            }

            if (simulateFailure)
                throw new InvalidOperationException(
                    "Simulated Release API failure. Database transaction was rolled back.");

            tx.Commit();
            return new ReleaseCompleteResult(true, "Release completed.");
        }
        catch (Exception ex)
        {
            try { tx.Rollback(); } catch { }
            return new ReleaseCompleteResult(false, simulateFailure
                ? "Simulated Release API failure. Database transaction was rolled back."
                : WarehouseProcedureMessage(ex));
        }
    }

    private static PickResult ExecuteReleasePickStoredProcedure(SqlConnection conn, PickReq body, string userId, string terminalId)
    {
        using var cmd = new SqlCommand($"[dbo].[{PdaReleasePickLotProcedure}]", conn)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 15
        };
        cmd.Parameters.Add("@PickSlipNo", SqlDbType.NVarChar, 40).Value = body.PickSlipNo.Trim();
        cmd.Parameters.Add("@LotNo", SqlDbType.NVarChar, 50).Value = body.LotNo.Trim();
        cmd.Parameters.Add("@UserId", SqlDbType.NVarChar, 80).Value = userId;
        cmd.Parameters.Add("@TerminalId", SqlDbType.NVarChar, 80).Value = terminalId;

        using var rdr = cmd.ExecuteReader();
        var row = rdr.Read()
            ? ReadReleaseLotRow(rdr, body.PickSlipNo.Trim(), body.LotNo.Trim())
            : InvalidReleaseLot(body.PickSlipNo, body.LotNo, "Release pick service returned an empty response.");

        return new PickResult(row.IsValid, row.Message ?? (row.IsValid ? "Release pick completed." : "LOT cannot be picked."), row);
    }

    private static ReleaseLotRow ReadReleaseLotRow(SqlDataReader rdr, string pickSlipNo, string lotNo)
    {
        return new ReleaseLotRow(
            GetString(rdr, "PICK_SLIPNO") ?? pickSlipNo,
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
            GetInt(rdr, "IS_FIFO_SUGGESTED") == 1,
            GetInt(rdr, "IS_VALID") == 1,
            GetString(rdr, "MESSAGE"));
    }

    private static ReleaseLotRow InvalidReleaseLot(string pickSlipNo, string lotNo, string message)
        => new(pickSlipNo, lotNo, null, null, 0, null, null, null, null, null, null, null, false, false, message);

    private static bool HasReleaseTables(SqlConnection conn, SqlTransaction? tx = null)
        => TableExists(conn, tx, "SIS_TEST", "WMS3050")
           && TableExists(conn, tx, "SIS_TEST", "WMS2020")
           && TableExists(conn, tx, "SIS_TEST", "WMS1040")
           && TableExists(conn, tx, "SIS_TEST", "ACD0020")
           && TableExists(conn, tx, "SIS_TEST", "ACD0020L");

    private static Wh001ScheduleInboundItem ReadWh001ScheduleInboundItem(
        SqlDataReader rdr,
        DateTime today,
        int scheduleItemId)
    {
        var purchaseOrderQty = GetDecimal(rdr, "PurchaseOrderQty");
        if (!HasColumn(rdr, "PurchaseOrderQty")) purchaseOrderQty = GetDecimal(rdr, "PO_QTY");

        var receivedQty = GetDecimal(rdr, "ReceivedQty");
        if (!HasColumn(rdr, "ReceivedQty")) receivedQty = GetDecimal(rdr, "GRN_QTY");

        var remainingQty = GetDecimal(rdr, "RemainingQty");
        if (!HasColumn(rdr, "RemainingQty")) remainingQty = GetDecimal(rdr, "NON_DELI_QTY");

        var expectedArrivalDate = GetDate(rdr, "ExpectedArrivalDate") ?? GetDate(rdr, "PO_DELI_DATE");
        var receiptStatus = remainingQty <= 0
            ? "Complete"
            : expectedArrivalDate.HasValue && expectedArrivalDate.Value.Date < today ? "Late"
            : "In Progress";

        return new Wh001ScheduleInboundItem(
            scheduleItemId,
            GetString(rdr, "PurchaseOrderNo") ?? GetString(rdr, "PONO") ?? "",
            GetInt(rdr, "PurchaseOrderLineNo") ?? GetInt(rdr, "PONO_SEQ"),
            GetString(rdr, "SupplierName") ?? GetString(rdr, "VENDNM") ?? GetString(rdr, "VENDCD"),
            GetString(rdr, "MaterialNo") ?? GetString(rdr, "PARTNO"),
            GetString(rdr, "MaterialName") ?? GetString(rdr, "PARTNM"),
            GetString(rdr, "CarCode") ?? GetString(rdr, "VINCD"),
            GetString(rdr, "UnitOfMeasure") ?? GetString(rdr, "PO_UNIT"),
            purchaseOrderQty,
            receivedQty,
            remainingQty,
            expectedArrivalDate,
            GetDate(rdr, "PurchaseOrderCreatedDate") ?? GetDate(rdr, "PO_DATE"),
            GetString(rdr, "ReceiptStatus") ?? receiptStatus);
    }

    private static Wh001ScheduleReleaseItem ReadWh001ScheduleReleaseItem(SqlDataReader rdr)
        => new(
            GetInt(rdr, "WorkOrderId") ?? 0,
            GetString(rdr, "WorkOrderNo"),
            GetString(rdr, "PartNo") ?? "",
            GetString(rdr, "PartName"),
            GetDecimal(rdr, "OrderQty"),
            GetString(rdr, "Unit"),
            GetDate(rdr, "DueDate"),
            GetString(rdr, "WorkOrderStatus") ?? "Released",
            GetDate(rdr, "ReleasedAt"),
            GetString(rdr, "LineId"));

    private static bool TableExists(SqlConnection conn, SqlTransaction? tx, string schema, string table)
    {
        using var cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID(@ObjectName, N'U') IS NULL THEN 0 ELSE 1 END;", conn, tx);
        cmd.Parameters.Add("@ObjectName", SqlDbType.NVarChar, 256).Value = $"{schema}.{table}";
        return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
    }

    private static bool ProcedureExists(SqlConnection conn, string schema, string procedure)
        => ProcedureExists(conn, null, schema, procedure);

    private static bool ProcedureExists(SqlConnection conn, SqlTransaction? tx, string schema, string procedure)
    {
        using var cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID(@ObjectName, N'P') IS NULL THEN 0 ELSE 1 END;", conn, tx);
        cmd.Parameters.Add("@ObjectName", SqlDbType.NVarChar, 256).Value = $"{schema}.{procedure}";
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
    private static InboundDocumentResult ExecuteInboundDocument(AmesConnectionFactory factory, string mode, string barcode)
    {
        using var conn = factory.OpenConnection();
        using var cmd = new SqlCommand($"[dbo].[{PdaInboundDocumentInfoProcedure}]", conn)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 15
        };
        cmd.Parameters.Add("@ReceiveMode", SqlDbType.NVarChar, 10).Value = mode.Trim();
        cmd.Parameters.Add("@DocumentBarcode", SqlDbType.NVarChar, 50).Value = barcode.Trim();

        using var rdr = cmd.ExecuteReader();
        var document = rdr.Read() ? ReadInboundDocumentRow(rdr) : null;
        var lines = new List<InboundDocumentLineRow>();
        if (rdr.NextResult())
        {
            while (rdr.Read())
                lines.Add(ReadInboundDocumentLineRow(rdr));
        }
        var boxes = new List<InboundDocumentBoxRow>();
        if (rdr.NextResult())
        {
            while (rdr.Read())
                boxes.Add(ReadInboundDocumentBoxRow(rdr));
        }
        return new InboundDocumentResult(document, lines, boxes);
    }

    private static InboundScanRow? ExecuteInboundScan(AmesConnectionFactory factory, string mode, string barcode)
    {
        using var conn = factory.OpenConnection();
        using var cmd = new SqlCommand($"[dbo].[{PdaInboundScanLotProcedure}]", conn)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 15
        };
        cmd.Parameters.Add("@ReceiveMode", SqlDbType.NVarChar, 10).Value = mode.Trim();
        cmd.Parameters.Add("@LotBarcode", SqlDbType.NVarChar, 50).Value = barcode.Trim();

        using var rdr = cmd.ExecuteReader();
        return rdr.Read() ? ReadInboundScanRow(rdr) : null;
    }

    private static InboundScanRow? ExecuteAdjustScan(AmesConnectionFactory factory, string scanText)
    {
        using var conn = factory.OpenConnection();
        using var cmd = new SqlCommand($"[dbo].[{PdaAdjustScanStockProcedure}]", conn)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 15
        };
        cmd.Parameters.Add("@ScanText", SqlDbType.NVarChar, 80).Value = scanText.Trim();

        using var rdr = cmd.ExecuteReader();
        return rdr.Read() ? ReadInboundScanRow(rdr) : null;
    }

    private static InboundReceiveResult ExecuteInboundReceive(
        AmesConnectionFactory factory,
        InboundReceiveReq body,
        string userId,
        string proc,
        string successMessage,
        bool simulateFailure = false)
    {
        try
        {
            using var conn = factory.OpenConnection();
            using var cmd = new SqlCommand($"[dbo].[{proc}]", conn)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 15
            };
            cmd.Parameters.Add("@ReceiveMode", SqlDbType.NVarChar, 10).Value = body.Mode.Trim();
            cmd.Parameters.Add("@LotBarcode", SqlDbType.NVarChar, 50).Value = body.Barcode.Trim();
            cmd.Parameters.Add("@LocationId", SqlDbType.NVarChar, 30).Value = body.LocationId.Trim();
            cmd.Parameters.Add("@UserId", SqlDbType.NVarChar, 40).Value = userId;
            if (string.Equals(proc, PdaInboundReceiveLotProcedure, StringComparison.OrdinalIgnoreCase))
                cmd.Parameters.Add("@SimulateFailure", SqlDbType.Bit).Value = simulateFailure;

            using var rdr = cmd.ExecuteReader();
            var row = rdr.Read() ? ReadInboundScanRow(rdr) : null;
            return new InboundReceiveResult(true, successMessage, row);
        }
        catch (Exception ex)
        {
            return new InboundReceiveResult(false, WarehouseProcedureMessage(ex), null);
        }
    }

    private static InboundReceiveResult ExecuteInboundCancel(AmesConnectionFactory factory, InboundCancelReq body, string userId)
    {
        try
        {
            using var conn = factory.OpenConnection();
            using var cmd = new SqlCommand($"[dbo].[{PdaInboundCancelReceiptProcedure}]", conn)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 15
            };
            cmd.Parameters.Add("@ReceiveMode", SqlDbType.NVarChar, 10).Value = body.Mode.Trim();
            cmd.Parameters.Add("@LotBarcode", SqlDbType.NVarChar, 50).Value = body.Barcode.Trim();
            cmd.Parameters.Add("@UserId", SqlDbType.NVarChar, 40).Value = userId;

            using var rdr = cmd.ExecuteReader();
            var row = rdr.Read() ? ReadInboundScanRow(rdr) : null;
            return new InboundReceiveResult(true, "Incoming canceled", row);
        }
        catch (Exception ex)
        {
            return new InboundReceiveResult(false, WarehouseProcedureMessage(ex), null);
        }
    }

    private static InboundReceiveResult ExecuteAdjustSave(AmesConnectionFactory factory, AdjustSaveReq body, string userId,
        bool simulateFailure = false)
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
            cmd.Parameters.Add("@DeltaQty", SqlDbType.Decimal).Value = body.DeltaQty;
            cmd.Parameters["@DeltaQty"].Precision = 18;
            cmd.Parameters["@DeltaQty"].Scale = 3;
            cmd.Parameters.Add("@ReasonCode", SqlDbType.NVarChar, 30).Value = body.ReasonCode.Trim();
            cmd.Parameters.Add("@ReasonNote", SqlDbType.NVarChar, 500).Value =
                string.IsNullOrWhiteSpace(body.ReasonNote) ? DBNull.Value : body.ReasonNote.Trim();
            cmd.Parameters.Add("@UserId", SqlDbType.NVarChar, 40).Value = userId;
            cmd.Parameters.Add("@SimulateFailure", SqlDbType.Bit).Value = simulateFailure;

            using var rdr = cmd.ExecuteReader();
            var row = rdr.Read() ? ReadInboundScanRow(rdr) : null;
            return new InboundReceiveResult(true, "Quantity adjusted", row);
        }
        catch (Exception ex)
        {
            return new InboundReceiveResult(false, WarehouseProcedureMessage(ex), null);
        }
    }


    private static LocationRow? QuerySisLocation(AmesConnectionFactory factory, string locationId)
    {
        using var conn = factory.OpenConnection();
        var dboLocation = QueryDboLocation(conn, locationId);
        if (dboLocation is not null)
            return dboLocation;

        if (!TableExists(conn, null, "SIS_TEST", "WMS1040"))
            return null;

        var hasWarehouseMaster = TableExists(conn, null, "SIS_TEST", "WMS1010");
        var hasAreaMaster = TableExists(conn, null, "SIS_TEST", "WMS1020");
        var hasZoneMaster = TableExists(conn, null, "SIS_TEST", "WMS1030");
        var hasStock = TableExists(conn, null, "SIS_TEST", "WMS2020");

        var joins = "";
        var warehouseName = "CAST(NULL AS nvarchar(80)) AS WarehouseName";
        var areaName = "CAST(NULL AS nvarchar(80)) AS AreaName";
        var zoneName = "CAST(NULL AS nvarchar(80)) AS ZoneName";
        var stockJoin = "";
        var lineCount = "CAST(0 AS int) AS LineCount";
        var totalQty = "CAST(0 AS decimal(18,3)) AS TotalQty";
        var groupNames = "";

        if (hasWarehouseMaster)
        {
            joins += """

            LEFT JOIN SIS_TEST.WMS1010 W
                ON W.CORCD = L.CORCD
               AND W.BIZCD = L.BIZCD
               AND W.WHCD = L.WHCD
            """;
            warehouseName = "W.WHNM AS WarehouseName";
            groupNames += ", W.WHNM";
        }

        if (hasAreaMaster)
        {
            joins += """

            LEFT JOIN SIS_TEST.WMS1020 A
                ON A.CORCD = L.CORCD
               AND A.BIZCD = L.BIZCD
               AND A.AREACD = L.AREACD
            """;
            areaName = "A.AREANM AS AreaName";
            groupNames += ", A.AREANM";
        }

        if (hasZoneMaster)
        {
            joins += """

            LEFT JOIN SIS_TEST.WMS1030 Z
                ON Z.CORCD = L.CORCD
               AND Z.BIZCD = L.BIZCD
               AND Z.ZONECD = L.ZONECD
            """;
            zoneName = "Z.ZONENM AS ZoneName";
            groupNames += ", Z.ZONENM";
        }

        if (hasStock)
        {
            stockJoin = """

            LEFT JOIN SIS_TEST.WMS2020 S
                ON S.LOCATION_NO = L.LOCATION_NO
            """;
            lineCount = "COUNT(S.LOTNO) AS LineCount";
            totalQty = "COALESCE(SUM(S.QTY), 0) AS TotalQty";
        }

        var sql = $"""
            SELECT TOP (1)
                L.LOCATION_NO AS LocationID,
                L.LOCATION_NM AS LocationName,
                L.ZONECD AS ZoneCode,
                L.WHCD AS WarehouseCode,
                {warehouseName},
                L.AREACD AS AreaCode,
                {areaName},
                {zoneName},
                L.RACK_X AS Aisle,
                L.RACK_Y AS Bay,
                L.RACK_Z AS Slot,
                CAST(NULL AS nvarchar(20)) AS PlantCode,
                CAST(N'SIS' AS nvarchar(20)) AS LocationType,
                CAST(NULL AS decimal(18,3)) AS Capacity,
                {lineCount},
                {totalQty}
            FROM SIS_TEST.WMS1040 L
            {joins}
            {stockJoin}
            WHERE L.CORCD = @LocationCorcd
              AND L.BIZCD = @LocationBizcd
              AND COALESCE(L.USE_YN, N'Y') = N'Y'
              AND UPPER(L.LOCATION_NO) = UPPER(@LocationID)
            GROUP BY L.LOCATION_NO, L.LOCATION_NM, L.ZONECD, L.WHCD,
                L.AREACD, L.RACK_X, L.RACK_Y, L.RACK_Z{groupNames}
            ORDER BY L.LOCATION_NO;
            """;

        using var cmd = new SqlCommand(sql, conn)
        {
            CommandTimeout = 15
        };
        cmd.Parameters.Add("@LocationCorcd", SqlDbType.NVarChar, 10).Value = WhLocationCorcd;
        cmd.Parameters.Add("@LocationBizcd", SqlDbType.NVarChar, 10).Value = WhLocationBizcd;
        cmd.Parameters.Add("@LocationID", SqlDbType.NVarChar, 30).Value = locationId.Trim();

        using var rdr = cmd.ExecuteReader();
        return rdr.Read() ? ReadLocationRow(rdr) : null;
    }

    private static LocationRow? QueryDboLocation(SqlConnection conn, string locationId)
    {
        if (!TableExists(conn, null, "dbo", "MD_Location"))
            return null;

        using var dboCmd = new SqlCommand("""
            SELECT TOP (1)
                L.LocationID,
                L.LocationName,
                L.ZoneCode,
                L.WhCode AS WarehouseCode,
                COALESCE(NULLIF(W.WhName, N''), L.WhCode) AS WarehouseName,
                L.AreaCode AS AreaCode,
                COALESCE(NULLIF(A.AreaName, N''), L.AreaCode) AS AreaName,
                L.ZoneCode AS ZoneName,
                L.Aisle,
                L.Bay,
                L.Slot,
                L.PlantCode,
                L.LocationType,
                L.Capacity,
                COUNT(I.InventoryID) AS LineCount,
                COALESCE(SUM(I.OnHandQty), 0) AS TotalQty
            FROM dbo.MD_Location L
            LEFT JOIN dbo.WH_WarehouseMaster W ON W.WhCode = L.WhCode
            LEFT JOIN dbo.WH_AreaMaster A ON A.WhCode = L.WhCode AND A.AreaCode = L.AreaCode
            LEFT JOIN dbo.WH_Inventory I
                ON I.LocationID = L.LocationID
               AND COALESCE(I.Status, 'Received') <> 'Canceled'
               AND COALESCE(I.OnHandQty, 0) > 0
            WHERE COALESCE(L.ActiveFlag, 1) = 1
              AND UPPER(L.LocationID) = UPPER(@LocationID)
            GROUP BY L.LocationID, L.LocationName, L.WhCode, W.WhName, L.AreaCode, A.AreaName,
                L.ZoneCode, L.Aisle, L.Bay, L.Slot, L.PlantCode, L.LocationType, L.Capacity
            ORDER BY L.LocationID;
            """, conn)
        {
            CommandTimeout = 15
        };
        dboCmd.Parameters.Add("@LocationID", SqlDbType.NVarChar, 30).Value = locationId.Trim();

        using var dboRdr = dboCmd.ExecuteReader();
        return dboRdr.Read() ? ReadLocationRow(dboRdr) : null;
    }

    private static string WarehouseProcedureMessage(Exception ex)
    {
        if (ex is InvalidOperationException && ex.Message.StartsWith("This spare part has not been received yet.", StringComparison.Ordinal))
            return ex.Message;
        if (ex is SqlException sqlEx && sqlEx.Errors.Count > 0)
            return sqlEx.Errors[0].Message;

        return "Warehouse database is unavailable.";
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

    private static InboundDocumentRow ReadInboundDocumentRow(SqlDataReader rdr)
    {
        return new InboundDocumentRow(
            GetString(rdr, "RECEIVE_TYPE") ?? "",
            GetInt(rdr, "INBOUND_DOCUMENT_ID") ?? 0,
            GetString(rdr, "DOCUMENT_BARCODE") ?? "",
            GetString(rdr, "DOCUMENT_NO"),
            GetString(rdr, "VENDCD"),
            GetString(rdr, "VENDNM"),
            GetString(rdr, "CASE_NO"),
            GetString(rdr, "INVOICE_NO"),
            GetString(rdr, "CONTAINER_NO"),
            GetDate(rdr, "SHIP_DATE"),
            GetDate(rdr, "PACK_DATE"),
            GetDate(rdr, "DELI_DATE"),
            GetDate(rdr, "ARRIV_DATE"),
            GetInt(rdr, "TOTAL_BOXES") ?? 0,
            GetInt(rdr, "SCANNED_BOXES") ?? 0,
            GetString(rdr, "YN"));
    }

    private static InboundDocumentLineRow ReadInboundDocumentLineRow(SqlDataReader rdr)
    {
        return new InboundDocumentLineRow(
            GetString(rdr, "PARTNO") ?? "",
            GetString(rdr, "PARTNM"),
            GetInt(rdr, "BOX_COUNT") ?? 0,
            GetInt(rdr, "SCAN_COUNT") ?? 0,
            GetString(rdr, "YN"));
    }

    private static InboundDocumentBoxRow ReadInboundDocumentBoxRow(SqlDataReader rdr)
    {
        return new InboundDocumentBoxRow(
            GetString(rdr, "PARTNO") ?? "",
            GetString(rdr, "BOX_BARCODE") ?? "",
            GetString(rdr, "LOTNO"),
            GetDecimal(rdr, "QTY"),
            GetString(rdr, "UNIT"),
            GetString(rdr, "YN"));
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
            GetNullableDecimal(rdr, "Capacity"),
            GetString(rdr, "Unit"));
    }

    internal static WarehouseTransactionRow ReadWarehouseTransactionRow(SqlDataReader rdr)
    {
        return new WarehouseTransactionRow(
            GetLong(rdr, "ROW_NO") ?? 0,
            GetString(rdr, "LOTNO"),
            GetString(rdr, "PARTNO"),
            GetString(rdr, "WDATE"),
            GetString(rdr, "WTIME"),
            GetString(rdr, "LOCATION_NO"),
            GetDecimal(rdr, "QTY"),
            GetString(rdr, "STATUS") ?? "-",
            GetString(rdr, "DIRECTION") ?? "OTHER",
            GetString(rdr, "WORKER_ID"),
            GetString(rdr, "REASON_CODE"),
            GetString(rdr, "REASON_NOTE"),
            GetString(rdr, "SUPERVISOR"),
            GetNullableDecimal(rdr, "BEFORE_QTY"),
            GetNullableDecimal(rdr, "DELTA_QTY"),
            GetNullableDecimal(rdr, "AFTER_QTY"),
            GetString(rdr, "BEFORE_STATUS"),
            GetString(rdr, "AFTER_STATUS"),
            GetString(rdr, "BEFORE_LOCATION"),
            GetString(rdr, "AFTER_LOCATION"),
            GetString(rdr, "SOURCE"),
            GetString(rdr, "NOTE"),
            GetString(rdr, "UNIT"));
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

    private static string? GetImageDataUrl(SqlDataReader rdr, string name)
    {
        if (!HasColumn(rdr, name) || rdr[name] is not byte[] { Length: > 0 } bytes) return null;
        var contentType = bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
            ? "image/png"
            : "image/jpeg";
        return $"data:{contentType};base64,{Convert.ToBase64String(bytes)}";
    }

    private static int? GetInt(SqlDataReader rdr, string name)
    {
        if (!HasColumn(rdr, name)) return null;
        var value = rdr[name];
        return value == DBNull.Value ? null : Convert.ToInt32(value);
    }

    private static long? GetLong(SqlDataReader rdr, string name)
    {
        if (!HasColumn(rdr, name)) return null;
        var value = rdr[name];
        return value == DBNull.Value ? null : Convert.ToInt64(value);
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
