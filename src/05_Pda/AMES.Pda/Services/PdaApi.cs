using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace AMES.Pda.Services;

/// <summary>
/// Shared PDA DTOs and HTTP transport used by the domain API clients.
/// Each domain client stamps requests with the bearer token from AuthState.
/// </summary>
public abstract class PdaApi
{
    public const string SparePartsAreaCode = "SPARE_PARTS_AREA";
    protected readonly HttpClient _http;
    protected readonly AuthState  _auth;

    protected PdaApi(HttpClient http, AuthState auth)
    {
        _http = http;
        _auth = auth;
    }

    // ── Auth ─────────────────────────────────────────────────────────────
    public sealed record LoginReq(string EmployeeNo, string Pin, string TerminalId, string LineId, string ShiftCode);
    public sealed record BarcodeLoginReq(string Barcode, string TerminalId, string LineId, string ShiftCode);
    public sealed record LoginRes(string Token, int Result, string? Reason,
                                   string? EmployeeNo, string? EmployeeName,
                                   string? LineId, string? ShiftCode, DateTime? ExpiresAt);

    public sealed record CodeOption(string CodeValue, string? CodeName, string? CodeNameEn, string? Attribute1)
    {
        public string Name => string.IsNullOrWhiteSpace(CodeNameEn) ? CodeName ?? CodeValue : CodeNameEn;
    }

    public async Task<List<CodeOption>> CodeItemsAsync(string groupCode)
    {
        Authorize();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            using var response = await _http.GetAsync(
                $"/api/sys/code-items/{Uri.EscapeDataString(groupCode)}", timeout.Token);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new InvalidOperationException("Your session has expired. Go back and sign in again.");
            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw new InvalidOperationException("You do not have permission to load this code list.");
            response.EnsureSuccessStatusCode();
            var items = await response.Content.ReadFromJsonAsync<List<CodeOption>>(cancellationToken: timeout.Token);
            if (items is not { Count: > 0 })
                throw new InvalidOperationException($"No active codes are configured for {groupCode}. Contact an administrator.");
            return items;
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException("Code service is unavailable. Check the API connection and reopen this screen.", ex);
        }
        catch (OperationCanceledException ex)
        {
            throw new InvalidOperationException("Code service timed out. Check the API connection and reopen this screen.", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Code service returned an invalid response. Contact an administrator.", ex);
        }
    }

    // ── WH ───────────────────────────────────────────────────────────────
    public sealed record InboundRow(int LotId, string LotCode, string? ItemNo, string? ItemName,
        decimal Qty, string? Vendor, DateTime? ArrivedAt);
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
    public sealed record WarehouseTransactionRow(long RowNo, string? LotNo, string? PartNo,
        string? WorkDate, string? WorkTime, string? LocationId, decimal Qty, string Status,
        string Direction, string? WorkerId, string? ReasonCode, string? ReasonNote,
        string? Supervisor, decimal? BeforeQty, decimal? DeltaQty, decimal? AfterQty,
        string? BeforeStatus, string? AfterStatus, string? BeforeLocation, string? AfterLocation,
        string? Source, string? Note, string? Unit = null);
    public sealed record InventoryRow(int InventoryId, string ItemNo, string? ItemName, string LocationId,
        int? LotId, decimal OnHandQty, decimal ReservedQty, DateTime? ExpiryDate,
        string? CarCode = null, string? Unit = null, decimal? MinDays = null, decimal? MinQty = null,
        decimal? MaxDays = null, decimal? MaxQty = null, int LotCount = 0, int LocationCount = 0,
        string? Status = null, string? StatusName = null, DateTime? LastReceivedDate = null,
        string? LotNo = null);
    public sealed record LotStatusRow(int LotId, string LotNo, string? ItemNo, string? ItemName,
        string InventoryStatus, decimal RemainingQty, string? LocationId, DateTime? ProductionDate,
        DateTime? LastChangedAt);
    public sealed record InventoryLocationRow(int RowNo, string ItemNo, string LocationId, string? LocationName,
        string? WarehouseCode, string? WarehouseName, string? AreaCode, string? AreaName,
        string? ZoneCode, string? ZoneName, string? RackX, string? RackY, string? RackZ, decimal Qty);
    public sealed record InventoryScanLookupRow(string SearchKind, string SearchText, string? DisplayText);
    public sealed record LocationRow(string LocationId, string? LocationName, string? Zone, int LineCount, decimal TotalQty,
        string? WarehouseCode = null, string? WarehouseName = null, string? AreaCode = null, string? AreaName = null,
        string? ZoneName = null, string? X = null, string? Y = null, string? Z = null,
        string? PlantCode = null, string? LocationType = null, decimal? Capacity = null, string? Unit = null);
    public sealed record LocationMapItemRow(string LotNo, string? PartNo, string? PartName, decimal Qty, string? Unit,
        string? InventoryStatus, string? WorkDate, string? WorkTime);
    public sealed record InventoryTestChangeResult(bool Success, string Message, string LotNo, decimal Qty);
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
    public sealed record OutgoingVendorRow(string VendorId, string? VendorName)
    {
        public string DisplayName => string.IsNullOrWhiteSpace(VendorName)
            ? VendorId
            : $"{VendorId} / {VendorName}";
    }
    public sealed record SparePartRow(string EosSpNo, string? Category,
        string? ApplicableEquipment, string? PartName, string? PartNo, string? Maker,
        string? Vendor, decimal Qty, string? Unit, string? StorageLocation, string? AreaCode,
        string InventoryStatus, bool IsReleaseEligible, string? ImageDataUrl, bool HasReceived = false);
    public sealed record SparePartMoveReq(string EosSpNo, int Qty = 1,
        string? LocationId = null, string? Note = null);
    public sealed record SparePartMoveResult(bool Success, string Message, SparePartRow? Row = null);
    public sealed record TransactionRow(long TxnId, DateTime TxnTime, string TxnType, string? ItemNo,
        string? LocationId, decimal QtyBefore, decimal Delta, decimal QtyAfter, string? ReasonCode);

    public sealed record ReceiveReq(string LotCode, decimal Qty, string LocationId);
    public sealed record InboundReceiveReq(string Mode, string Barcode, string LocationId, bool SimulateFailure = false);
    public sealed record InboundCancelReq(string Mode, string Barcode);
    public sealed record AdjustSaveReq(string? Mode, string Barcode, decimal DeltaQty, string ReasonCode,
        string? ReasonNote, bool SimulateFailure = false);
    public sealed record AdjustTestResetResult(bool Success, string Message, string LotNo, decimal Qty);
    public sealed record InboundReceiveResult(bool Success, string Message, InboundScanRow? Row);
    public sealed record PickReq(string PickSlipNo, string LotNo, decimal Qty);
    public sealed record PickResult(bool Success, string Message, ReleaseLotRow? Row);

    public sealed record AdjustmentStock(string Barcode, string LotNo, string PartNo, string? PartName, decimal Qty, string? Unit);
    public sealed record AdjustmentLocation(string LocationId, List<AdjustmentStock> Items);

    // ── FG ───────────────────────────────────────────────────────────────
    public sealed record FgStockRow(int StockId, string? StockNumber, string ItemNo, string? ItemName,
        int? LotId, string? LotNo, string? CustomerCode, decimal Qty, string? Unit,
        string? Location, string? Status, DateTime? StockTs);
    public sealed record FgOrderRow(int ShipmentOrderId, string? ShipOrderNumber, string? CustomerCode,
        string? CustomerPo, DateTime? ShipDate, string? CarrierCode, string? DestPlant, string? Status, int LineCount);
    public sealed record FgOrderLineRow(int ShipmentOrderLineId, int ShipmentOrderId, int LineSeq,
        string ItemNo, string? ItemName, decimal OrderedQty, decimal AllocatedQty,
        int? StockId, string? LotNo, string? Location, string? ReservationStatus);
    public sealed record FgOutgoingSlipRow(int OutgoingSlipId, string OutgoingSlipBarcode,
        string? CustomerCode, DateTime? OutgoingDate, string? Destination, string? Status, int LineCount);
    public sealed record FgOutgoingSlipLineRow(int OutgoingSlipLineId, int OutgoingSlipId, int LineSeq,
        string PartNo, string? PartName, decimal RequiredQty, decimal ScannedQty, string? Status);
    public sealed record FgHistoryRow(int LoadingId, string? LoadingNumber, int? ShipmentOrderId,
        string? ShipOrderNumber, string? CustomerCode, string? LicensePlate, string? DriverName,
        DateTime? DepartureTs, string? OTDStatus);
    public sealed record FgDashboard(int OpenOrders, int ReadyToShip, int InTransit, int DeliveredToday,
        int PendingReturns, decimal StockOnHand);
    public sealed record FgQcCompletedRow(int LotId, string LotNo, string? WoNumber, string ItemNo,
        string? ItemName, string? CustomerCode, decimal Qty, string? Unit, DateTime? ProducedAt,
        DateTime? QcPassTs);
    public sealed record FgReturnRow(int ReturnId, string? ReturnNumber, string? CustomerCode,
        string? ItemNo, decimal Qty, string? ReturnReason, string? Status, DateTime? ReceivedAt);
    public sealed record FgReturnScanRow(string Barcode, int StockId, string? StockNumber, int? LotId, string? LotNo,
        int ShipmentOrderId, string? ShipOrderNumber, string CustomerCode,
        string ItemNo, string? ItemName, DateTime ShippedAt, decimal Qty);
    public sealed record FgReturnResult(bool Success, string Message, int? ReturnId, FgReturnScanRow? Row);

    public sealed record FgPutAwayScanRow(int? LotId, string LotNo, int? WoId,
        string ItemNo, string? ItemName, string? CustomerCode, decimal Qty, string? Unit,
        DateTime? MfgDate, DateTime? ExpiryDate, string? QcInspectionNo, DateTime? QcPassTs,
        bool IsQcPassed, bool AlreadyStocked, int? ExistingStockId, string? ExistingLocation,
        string? ExistingStatus, string BarcodeType, string StorageMethod, string NextScanType,
        string NextScanLabel, string? PackSpecId, string Message);
    public sealed record FgPutAwayLocationRow(string LocationId, string? LocationName, string? ZoneCode,
        string? Aisle, string? Bay, string? Slot, decimal Capacity, decimal CurrentQty,
        decimal AvailableQty, string? CurrentCustomerCode, bool IsValid, string Message,
        string ScanType, string ScannedBarcode);
    public sealed record FgPutAwayConfirmReq(string Barcode, string LocationId, string? SuggestedLocation,
        string? OverrideReason, int? PalletCount, int? PalletQty, string? StorageMethod,
        string? ContainerType, string? ContainerBarcode);
    public sealed record FgPutAwayResult(bool Success, string Message, int? StockId, FgPutAwayScanRow? Row,
        FgPutAwayLocationRow? Location);
    public sealed record FgReleaseLotReq(int OutgoingSlipLineId, int StockId, decimal Qty);
    public sealed record FgReleaseLotScanReq(int OutgoingSlipId, string Barcode, List<FgReleaseLotReq>? ScannedLots);
    public sealed record FgReleaseLotScanResult(bool Success, string Code, string Message,
        FgStockRow? Stock, int? OutgoingSlipLineId);
    public sealed record FgCompleteReleaseReq(int OutgoingSlipId, List<FgReleaseLotReq> Lots);
    public sealed record FgCompleteReleaseResult(bool Success, string Message, int? PickId);
    public sealed record FgLoadingTruckRow(string Barcode, string LicensePlate, bool Ready, string Message);
    public sealed record FgLoadingItemRow(int StockId, int ShipmentOrderLineId, int ShipmentOrderId,
        string ShipOrderNumber, string CustomerCode, string ItemNo, string? ItemName,
        string? LotNo, string? StockNumber, decimal Qty, string? Unit, string? Location);
    public sealed record FgLoadingOrderRow(int ShipmentOrderId, string Barcode, string ShipOrderNumber,
        string CustomerCode, DateTime? ShipDate, string? Destination, List<FgLoadingItemRow> Items);
    public sealed record FgLoadingOrderResult(bool Success, string Message, FgLoadingOrderRow? Order);
    public sealed record FgLoadingReq(string TruckBarcode, int ShipmentOrderId, List<int> StockIds);
    public sealed record FgLoadingResult(bool Success, string Message, int? LoadingId,
        FgLoadingTruckRow? Truck, FgLoadingItemRow? Item);
    public sealed record FgDeliveryReq(int ShipmentOrderId, int? LoadingId);
    public sealed record FgDayEndReq(string CloseMode, string? Note);
    public sealed record FgReturnReq(string Barcode, string ReturnReason, string? Note);

    protected async Task<T> GetRequiredAsync<T>(string url, string fallback)
    {
        Authorize();
        using var response = await _http.GetAsync(url);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadServiceErrorAsync(response, fallback));
        return await response.Content.ReadFromJsonAsync<T>()
            ?? throw new InvalidOperationException(fallback);
    }

    protected Task<T> Get<T>(string url) where T : new()
        => GetRequiredAsync<T>(url, "PDA service is unavailable.");
    protected async Task<HttpResponseMessage> Post<TBody>(string url, TBody body)
    {
        Authorize();
        return await _http.PostAsJsonAsync(url, body);
    }

    // ── helper ──────────────────────────────────────────────────────────
    protected static async Task<SparePartMoveResult> ReadSparePartMoveResultAsync(HttpResponseMessage response, string fallback)
    {
        if (!response.IsSuccessStatusCode)
            return new SparePartMoveResult(false, await ReadServiceErrorAsync(response, fallback));

        return await response.Content.ReadFromJsonAsync<SparePartMoveResult>()
               ?? new SparePartMoveResult(false, fallback);
    }

    protected static async Task<InboundReceiveResult> ReadInboundReceiveResultAsync(HttpResponseMessage resp)
    {
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
            return new InboundReceiveResult(false, "Session expired. Sign in again.", null);

        if (!resp.IsSuccessStatusCode)
            return new InboundReceiveResult(false, await ReadServiceErrorAsync(resp, "Warehouse service is unavailable."), null);

        return await resp.Content.ReadFromJsonAsync<InboundReceiveResult>()
               ?? new InboundReceiveResult(false, "Warehouse service returned an empty response.", null);
    }

    protected static async Task<string> ReadServiceErrorAsync(HttpResponseMessage resp, string fallback)
    {
        string body;
        try
        {
            body = await resp.Content.ReadAsStringAsync();
        }
        catch
        {
            return fallback;
        }

        if (string.IsNullOrWhiteSpace(body))
            return fallback;

        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("detail", out var detail)
                && detail.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(detail.GetString()))
                return detail.GetString()!;

            if (json.RootElement.TryGetProperty("title", out var title)
                && title.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(title.GetString()))
                return title.GetString()!;
        }
        catch
        {
            // Fall back to a short generic message rather than showing raw JSON.
        }

        return fallback;
    }

    protected static List<T> FilterByArea<T>(IEnumerable<T> rows, Func<T, string?> areaSelector,
        string? areaCode)
    {
        var list = rows.ToList();
        return string.IsNullOrWhiteSpace(areaCode)
            ? list
            : list.Where(row => string.Equals(areaSelector(row), areaCode, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    protected static void EnsureArea(string? actualAreaCode, string? expectedAreaCode)
    {
        if (string.IsNullOrWhiteSpace(expectedAreaCode)) return;
        if (string.Equals(actualAreaCode, expectedAreaCode, StringComparison.OrdinalIgnoreCase)) return;

        throw new InvalidOperationException(string.Equals(expectedAreaCode, SparePartsAreaCode, StringComparison.OrdinalIgnoreCase)
            ? "This location does not belong to the Spare Parts Area."
            : $"This location does not belong to {expectedAreaCode}.");
    }

    protected void Authorize()
    {
        _http.DefaultRequestHeaders.Authorization =
            _auth.Token is null ? null : new AuthenticationHeaderValue("Bearer", _auth.Token);
    }
}
