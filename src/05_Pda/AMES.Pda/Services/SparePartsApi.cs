namespace AMES.Pda.Services;

public sealed class SparePartsApi(PdaApi api)
{
    public const string AreaCode = PdaApi.SparePartsAreaCode;

    public Task<PdaApi.SparePartRow?> SpItemAsync(string eosSpNo) => api.SpItemAsync(eosSpNo);
    public Task<PdaApi.SparePartMoveResult> SpReceiveAsync(PdaApi.SparePartMoveReq body) => api.SpReceiveAsync(body);
    public Task<PdaApi.SparePartMoveResult> SpReleaseAsync(PdaApi.SparePartMoveReq body) => api.SpReleaseAsync(body);
    public Task SpResetTestAsync() => api.SpResetTestAsync();
    public Task<PdaApi.InboundReceiveResult> SpMoveLocationAsync(PdaApi.InboundReceiveReq body) => api.SpMoveLocationAsync(body);
    public Task<PdaApi.InboundReceiveResult> SpSaveAdjustQtyAsync(PdaApi.AdjustSaveReq body) => api.SpSaveAdjustQtyAsync(body);
    public Task<PdaApi.LocationRow?> WhScanLocationAsync(string locationId, string? areaCode = AreaCode)
        => api.WhScanLocationAsync(locationId, areaCode);
}
