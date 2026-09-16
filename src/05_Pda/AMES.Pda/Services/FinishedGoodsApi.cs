namespace AMES.Pda.Services;

public sealed class FinishedGoodsApi(PdaApi api)
{
    public Task<List<PdaApi.CodeOption>> CodeItemsAsync(string groupCode) => api.CodeItemsAsync(groupCode);
    public Task FgResetPptTestAsync(string screen) => api.FgResetPptTestAsync(screen);
    public Task<PdaApi.InboundScanRow?> FgScanAdjustAsync(string scanText) => api.FgScanAdjustAsync(scanText);
    public Task<PdaApi.InboundReceiveResult> FgSaveAdjustQtyAsync(PdaApi.AdjustSaveReq body) => api.FgSaveAdjustQtyAsync(body);
    public Task<List<PdaApi.FgStockRow>> FgInventoryAsync(string? q = null) => api.FgInventoryAsync(q);
    public Task<List<PdaApi.FgQcCompletedRow>> FgQcCompletedAsync() => api.FgQcCompletedAsync();
    public Task<List<PdaApi.FgOrderRow>> FgOrdersAsync() => api.FgOrdersAsync();
    public Task<List<PdaApi.FgOrderLineRow>> FgOrderLinesAsync(string shipOrderNumber) => api.FgOrderLinesAsync(shipOrderNumber);
    public Task<PdaApi.FgOutgoingSlipRow?> FgOutgoingSlipAsync(string barcode) => api.FgOutgoingSlipAsync(barcode);
    public Task<List<PdaApi.FgOutgoingSlipLineRow>> FgOutgoingSlipLinesAsync(string barcode) => api.FgOutgoingSlipLinesAsync(barcode);
    public Task<List<PdaApi.FgHistoryRow>> FgHistoryAsync() => api.FgHistoryAsync();
    public Task<List<PdaApi.FgReturnRow>> FgReturnsAsync() => api.FgReturnsAsync();
    public Task<PdaApi.FgReturnResult> FgReturnScanAsync(string barcode) => api.FgReturnScanAsync(barcode);
    public Task<PdaApi.FgDashboard> FgDashboardAsync() => api.FgDashboardAsync();
    public Task<PdaApi.FgPutAwayResult> FgPutAwayScanAsync(string barcode) => api.FgPutAwayScanAsync(barcode);
    public Task<PdaApi.FgPutAwayResult> FgValidatePutAwayContainerAsync(string storageMethod, string barcode)
        => api.FgValidatePutAwayContainerAsync(storageMethod, barcode);
    public Task<PdaApi.FgPutAwayLocationRow?> FgValidatePutAwayLocationAsync(string locationId, string itemNo,
        string? customerCode, decimal qty, string? expectedScanType)
        => api.FgValidatePutAwayLocationAsync(locationId, itemNo, customerCode, qty, expectedScanType);
    public Task<PdaApi.FgPutAwayResult> FgConfirmPutAwayAsync(PdaApi.FgPutAwayConfirmReq body) => api.FgConfirmPutAwayAsync(body);
    public Task<PdaApi.FgReleaseLotScanResult> FgReleaseLotScanAsync(PdaApi.FgReleaseLotScanReq body)
        => api.FgReleaseLotScanAsync(body);
    public Task<PdaApi.FgCompleteReleaseResult> FgCompleteReleaseAsync(PdaApi.FgCompleteReleaseReq body)
        => api.FgCompleteReleaseAsync(body);
    public Task<PdaApi.FgLoadingOrderResult> FgLoadingOrderScanAsync(string barcode) => api.FgLoadingOrderScanAsync(barcode);
    public Task<PdaApi.FgLoadingResult> FgLoadingTruckScanAsync(string barcode) => api.FgLoadingTruckScanAsync(barcode);
    public Task<PdaApi.FgLoadingResult> FgLoadingItemScanAsync(string barcode, int? shipmentOrderId)
        => api.FgLoadingItemScanAsync(barcode, shipmentOrderId);
    public Task<PdaApi.FgLoadingResult> FgLoadingAsync(PdaApi.FgLoadingReq body) => api.FgLoadingAsync(body);
    public Task<HttpResponseMessage> FgDeliveryAsync(PdaApi.FgDeliveryReq body) => api.FgDeliveryAsync(body);
    public Task<HttpResponseMessage> FgDayEndAsync(PdaApi.FgDayEndReq body) => api.FgDayEndAsync(body);
    public Task<PdaApi.FgReturnResult> FgReturnAsync(PdaApi.FgReturnReq body) => api.FgReturnAsync(body);
}
