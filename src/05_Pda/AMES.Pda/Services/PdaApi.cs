using System.Net.Http.Headers;
using System.Net.Http.Json;
using AMES.Contracts.Dto;

namespace AMES.Pda.Services;

/// <summary>
/// Thin HTTP client wrapper for the AMES.Api endpoints. One instance per
/// process — registered as singleton in MauiProgram. Stamps every request
/// with the current bearer token from AuthState.
/// </summary>
public sealed class PdaApi
{
    private readonly HttpClient _http;
    private readonly AuthState  _auth;

    public PdaApi(HttpClient http, AuthState auth)
    {
        _http = http;
        _auth = auth;
    }

    // ── Auth ─────────────────────────────────────────────────────────────
    public sealed record LoginReq(string EmployeeNo, string Pin, string TerminalId, string LineId, string ShiftCode);
    public sealed record LoginRes(string Token, int Result, string? Reason,
                                   string? EmployeeNo, string? EmployeeName,
                                   string? LineId, string? ShiftCode, DateTime? ExpiresAt);

    /// <summary>
    /// Login + session fetch in one call. The API hands back an opaque
    /// bearer token; we stamp it locally and immediately call /me so the
    /// caller gets a fully-populated PopSessionDto in one await. Avoids
    /// the timing bug where AuthState.Token wasn't set yet when MeAsync
    /// rebuilt the Authorization header.
    /// Returns null on any auth failure or unreachable API.
    /// </summary>
    public async Task<(string Token, PopSessionDto Session, string? Reason)?> LoginAsync(
        string employeeNo, string pin,
        string terminalId = "PDA-DEV-01",
        string lineId = "LINE-INJ-01",
        string shiftCode = "DAY")
    {
        HttpResponseMessage resp;
        try
        {
            resp = await _http.PostAsJsonAsync("/api/auth/login",
                new LoginReq(employeeNo, pin, terminalId, lineId, shiftCode));
        }
        catch (Exception ex) { return (Token: "", Session: null!, Reason: ex.Message); }

        if (!resp.IsSuccessStatusCode)
            return (Token: "", Session: null!, Reason: $"HTTP {(int)resp.StatusCode}");

        var login = await resp.Content.ReadFromJsonAsync<LoginRes>();
        if (login is null || string.IsNullOrEmpty(login.Token))
            return (Token: "", Session: null!, Reason: login?.Reason ?? "no token");

        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.Token);

        var meResp = await _http.GetAsync("/api/auth/me");
        if (!meResp.IsSuccessStatusCode)
            return (Token: "", Session: null!, Reason: $"/me HTTP {(int)meResp.StatusCode}");

        var session = await meResp.Content.ReadFromJsonAsync<PopSessionDto>();
        return session is null
            ? (Token: "", Session: null!, Reason: "/me empty body")
            : (login.Token, session, null);
    }

    public async Task LogoutAsync()
    {
        Authorize();
        await _http.PostAsync("/api/auth/logout", null);
    }

    public async Task<PopSessionDto?> MeAsync()
    {
        Authorize();
        var resp = await _http.GetAsync("/api/auth/me");
        return resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<PopSessionDto>() : null;
    }

    // ── WH ───────────────────────────────────────────────────────────────
    public sealed record InboundRow(int LotId, string LotCode, string? ItemNo, string? ItemName,
        decimal Qty, string? Vendor, DateTime? ArrivedAt);
    public sealed record InboundScheduleRow(int PoId, string PoNumber, int? PoLineNo, string? VendorId,
        string? ItemNo, string? ItemName, string? CarCode, string? Unit, decimal OrderQty, decimal ReceivedQty,
        decimal NonDeliverQty, DateTime? DueDate, DateTime? PoCreateDate, string? Status);
    public sealed record InventoryRow(int InventoryId, string ItemNo, string? ItemName, string LocationId,
        int? LotId, decimal OnHandQty, decimal ReservedQty, DateTime? ExpiryDate);
    public sealed record LocationRow(string LocationId, string? LocationName, string? Zone, int LineCount, decimal TotalQty);
    public sealed record ReleaseScheduleRow(int ReleaseScheduleId, int? WoId, string? WoNumber,
        string ItemNo, string? ItemName, decimal DemandQty, decimal PickedQty, DateTime? RequiredAt, string? Status);
    public sealed record TransactionRow(long TxnId, DateTime TxnTime, string TxnType, string? ItemNo,
        string? LocationId, decimal QtyBefore, decimal Delta, decimal QtyAfter, string? ReasonCode);

    public sealed record ReceiveReq(string LotCode, decimal Qty, string LocationId);
    public sealed record AdjustReq(string ItemNo, string LocationId, decimal Delta, string ReasonCode, string? Note);
    public sealed record PickReq(int ReleaseScheduleId, string LotCode, decimal Qty);

    public Task<List<InboundRow>>         WhInboundTodayAsync()    => Get<List<InboundRow>>("/api/wh/inbound/today");
    public Task<List<InboundScheduleRow>> WhInboundScheduleAsync() => Get<List<InboundScheduleRow>>("/api/wh/inbound/schedule");
    public Task<List<InventoryRow>>       WhInventoryAsync(string? q = null)
        => Get<List<InventoryRow>>("/api/wh/inventory" + (string.IsNullOrEmpty(q) ? "" : $"?q={Uri.EscapeDataString(q)}"));
    public Task<List<LocationRow>>        WhLocationsAsync()       => Get<List<LocationRow>>("/api/wh/locations");
    public Task<List<ReleaseScheduleRow>> WhReleaseScheduleAsync() => Get<List<ReleaseScheduleRow>>("/api/wh/release/schedule");
    public Task<List<TransactionRow>>     WhTransactionsAsync(int days = 7) => Get<List<TransactionRow>>($"/api/wh/transactions?days={days}");

    public Task<HttpResponseMessage> WhReceiveAsync(ReceiveReq body) => Post("/api/wh/inbound/receive", body);
    public Task<HttpResponseMessage> WhAdjustAsync (AdjustReq  body) => Post("/api/wh/inventory/adjust", body);
    public Task<HttpResponseMessage> WhPickAsync   (PickReq    body) => Post("/api/wh/release/pick",      body);

    // ── FG ───────────────────────────────────────────────────────────────
    public sealed record FgStockRow(int StockId, string? StockNumber, string ItemNo, string? ItemName,
        int? LotId, string? CustomerCode, decimal Qty, string? Location, string? Status, DateTime? StockTs);
    public sealed record FgOrderRow(int ShipmentOrderId, string? ShipOrderNumber, string? CustomerCode,
        string? CustomerPo, DateTime? ShipDate, string? CarrierCode, string? DestPlant, string? Status, int LineCount);
    public sealed record FgHistoryRow(int LoadingId, string? LoadingNumber, int? ShipmentOrderId,
        string? ShipOrderNumber, string? CustomerCode, string? LicensePlate, string? DriverName,
        DateTime? DepartureTs, string? OTDStatus);
    public sealed record FgDashboard(int OpenOrders, int ReadyToShip, int InTransit, int DeliveredToday,
        int PendingReturns, decimal StockOnHand);

    public sealed record FgPutAwayReq(int WoId, string ItemNo, decimal Qty, string ActualLoc, int PalletCount);
    public sealed record FgPickReq(int ShipmentOrderId, int StockId, decimal Qty);
    public sealed record FgLoadingReq(int ShipmentOrderId, string LicensePlate, string DriverName, string DockNo, string? SealNo);
    public sealed record FgDeliveryReq(int ShipmentOrderId, int? LoadingId);
    public sealed record FgDayEndReq(string CloseMode, string? Note);
    public sealed record FgReturnReq(string CustomerCode, int? OriginalShipmentOrderId, string ReturnReason, decimal Qty, string ItemNo);

    public Task<List<FgStockRow>>   FgInventoryAsync(string? q = null)
        => Get<List<FgStockRow>>("/api/fg/inventory" + (string.IsNullOrEmpty(q) ? "" : $"?q={Uri.EscapeDataString(q)}"));
    public Task<List<FgOrderRow>>   FgOrdersAsync()  => Get<List<FgOrderRow>>("/api/fg/orders");
    public Task<List<FgHistoryRow>> FgHistoryAsync() => Get<List<FgHistoryRow>>("/api/fg/history");
    public async Task<FgDashboard>  FgDashboardAsync()
    {
        Authorize();
        try
        {
            var r = await _http.GetAsync("/api/fg/dashboard");
            if (!r.IsSuccessStatusCode) return new FgDashboard(0,0,0,0,0,0);
            return await r.Content.ReadFromJsonAsync<FgDashboard>() ?? new FgDashboard(0,0,0,0,0,0);
        }
        catch { return new FgDashboard(0,0,0,0,0,0); }
    }

    public Task<HttpResponseMessage> FgPutAwayAsync (FgPutAwayReq body)  => Post("/api/fg/putaway",  body);
    public Task<HttpResponseMessage> FgPickAsync    (FgPickReq    body)  => Post("/api/fg/pick",     body);
    public Task<HttpResponseMessage> FgLoadingAsync (FgLoadingReq body)  => Post("/api/fg/loading",  body);
    public Task<HttpResponseMessage> FgDeliveryAsync(FgDeliveryReq body) => Post("/api/fg/delivery", body);
    public Task<HttpResponseMessage> FgDayEndAsync  (FgDayEndReq  body)  => Post("/api/fg/dayend",   body);
    public Task<HttpResponseMessage> FgReturnAsync  (FgReturnReq  body)  => Post("/api/fg/return",   body);

    // ── private HTTP helpers ────────────────────────────────────────────
    private async Task<T> Get<T>(string url) where T : new()
    {
        Authorize();
        try
        {
            var resp = await _http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return new T();
            return await resp.Content.ReadFromJsonAsync<T>() ?? new T();
        }
        catch { return new T(); }
    }
    private async Task<HttpResponseMessage> Post<TBody>(string url, TBody body)
    {
        Authorize();
        return await _http.PostAsJsonAsync(url, body);
    }

    // ── helper ──────────────────────────────────────────────────────────
    private void Authorize()
    {
        _http.DefaultRequestHeaders.Authorization =
            _auth.Token is null ? null : new AuthenticationHeaderValue("Bearer", _auth.Token);
    }
}
