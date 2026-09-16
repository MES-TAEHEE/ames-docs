namespace AMES.Pda.Services;

public sealed class WarehouseApi(PdaApi api)
{
    public Task<List<PdaApi.CodeOption>> CodeItemsAsync(string groupCode) => api.CodeItemsAsync(groupCode);
    public Task<List<PdaApi.InboundRow>> WhInboundTodayAsync() => api.WhInboundTodayAsync();
    public Task<List<PdaApi.InventoryRow>> WhInventoryAsync(string? q = null, DateTime? dateFrom = null,
        DateTime? dateTo = null, bool simulateFailure = false, string? areaCode = null)
        => api.WhInventoryAsync(q, dateFrom, dateTo, simulateFailure, areaCode);
    public Task<PdaApi.InventoryTestChangeResult> WhToggleInventoryTestQtyAsync() => api.WhToggleInventoryTestQtyAsync();
    public Task<PdaApi.InventoryScanLookupRow?> WhInventoryScanAsync(string? scanText, string? areaCode = null)
        => api.WhInventoryScanAsync(scanText, areaCode);
    public Task<List<PdaApi.InventoryLocationRow>> WhInventoryLocationsAsync(string itemNo, DateTime? dateFrom = null,
        DateTime? dateTo = null, string? areaCode = null)
        => api.WhInventoryLocationsAsync(itemNo, dateFrom, dateTo, areaCode);
    public Task<List<PdaApi.LocationRow>> WhLocationsAsync(string? areaCode = null) => api.WhLocationsAsync(areaCode);
    public Task<List<PdaApi.LocationRow>> WhLocationMapAsync() => api.WhLocationMapAsync();
    public Task<List<PdaApi.LocationMapItemRow>> WhLocationMapItemsAsync(string locationId, DateTime? dateFrom = null,
        DateTime? dateTo = null, string? areaCode = null)
        => api.WhLocationMapItemsAsync(locationId, dateFrom, dateTo, areaCode);
    public Task<PdaApi.LocationRow?> WhScanLocationAsync(string locationId, string? areaCode = null)
        => api.WhScanLocationAsync(locationId, areaCode);
    public Task<PdaApi.ReleaseSlipStatusRow?> WhReleaseSlipStatusAsync(string pickSlipNo) => api.WhReleaseSlipStatusAsync(pickSlipNo);
    public Task<List<PdaApi.ReleasePickLineRow>> WhReleaseLinesAsync(string pickSlipNo) => api.WhReleaseLinesAsync(pickSlipNo);
    public Task<List<PdaApi.ReleaseFifoLotRow>> WhReleaseFifoLotsAsync(string pickSlipNo, string? areaCode = null)
        => api.WhReleaseFifoLotsAsync(pickSlipNo, areaCode);
    public Task<PdaApi.ReleaseLotRow?> WhReleaseLotAsync(string pickSlipNo, string lotNo, string? areaCode = null)
        => api.WhReleaseLotAsync(pickSlipNo, lotNo, areaCode);
    public Task<List<PdaApi.LotStatusRow>> WhLotStatusesAsync(string? q = null, string? areaCode = null)
        => api.WhLotStatusesAsync(q, areaCode);
    public Task<HttpResponseMessage> WhReleaseCompleteAsync(PdaApi.ReleaseCompleteReq body) => api.WhReleaseCompleteAsync(body);
    public Task<List<PdaApi.OutgoingVendorRow>> WhOutgoingVendorsAsync() => api.WhOutgoingVendorsAsync();
    public Task<PdaApi.DirectOutgoingLotRow?> WhDirectOutgoingLotAsync(string lotNo, string? areaCode = null)
        => api.WhDirectOutgoingLotAsync(lotNo, areaCode);
    public Task<HttpResponseMessage> WhDirectOutgoingAsync(PdaApi.DirectOutgoingReq body) => api.WhDirectOutgoingAsync(body);
    public Task<List<PdaApi.TransactionRow>> WhTransactionsAsync(int days = 7) => api.WhTransactionsAsync(days);
    public Task<HttpResponseMessage> WhReceiveAsync(PdaApi.ReceiveReq body) => api.WhReceiveAsync(body);
    public Task<PdaApi.InboundDocumentResult> WhInboundDocumentAsync(string mode, string barcode)
        => api.WhInboundDocumentAsync(mode, barcode);
    public Task<PdaApi.InboundScanRow?> WhScanInboundAsync(string mode, string barcode) => api.WhScanInboundAsync(mode, barcode);
    public Task<PdaApi.AdjustmentLocation?> ScanAdjustmentLocationAsync(string barcode, bool finishedGoods, string? areaCode = null)
        => api.ScanAdjustmentLocationAsync(barcode, finishedGoods, areaCode);
    public Task<PdaApi.InboundScanRow?> WhScanAdjustAsync(string scanText, string? areaCode = null)
        => api.WhScanAdjustAsync(scanText, areaCode);
    public Task<List<PdaApi.WarehouseTransactionRow>> WhWarehouseTransactionsAsync(string? search = null,
        DateTime? dateFrom = null, DateTime? dateTo = null, bool finishedGoods = false, string? areaCode = null)
        => api.WhWarehouseTransactionsAsync(search, dateFrom, dateTo, finishedGoods, areaCode);
    public Task<PdaApi.InboundReceiveResult> WhReceiveInboundAsync(PdaApi.InboundReceiveReq body) => api.WhReceiveInboundAsync(body);
    public Task<PdaApi.InboundReceiveResult> WhMoveInboundLocationAsync(PdaApi.InboundReceiveReq body)
        => api.WhMoveInboundLocationAsync(body);
    public Task<PdaApi.InboundReceiveResult> WhCancelInboundAsync(PdaApi.InboundCancelReq body) => api.WhCancelInboundAsync(body);
    public Task<PdaApi.InboundReceiveResult> WhSaveAdjustQtyAsync(PdaApi.AdjustSaveReq body) => api.WhSaveAdjustQtyAsync(body);
    public Task WhResetSimpleInboundTestAsync() => api.WhResetSimpleInboundTestAsync();
    public Task WhResetPptTestAsync(string screen) => api.WhResetPptTestAsync(screen);
    public Task WhResetHistoryTestAsync() => api.WhResetHistoryTestAsync();
    public Task<PdaApi.AdjustTestResetResult> WhResetAdjustTestAsync() => api.WhResetAdjustTestAsync();
    public Task<HttpResponseMessage> WhPickAsync(PdaApi.PickReq body) => api.WhPickAsync(body);
}
