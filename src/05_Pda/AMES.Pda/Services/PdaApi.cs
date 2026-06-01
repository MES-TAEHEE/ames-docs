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

    public async Task<List<InboundRow>> WhInboundTodayAsync()
    {
        Authorize();
        var resp = await _http.GetAsync("/api/wh/inbound/today");
        if (!resp.IsSuccessStatusCode) return new();
        return await resp.Content.ReadFromJsonAsync<List<InboundRow>>() ?? new();
    }

    // ── helper ──────────────────────────────────────────────────────────
    private void Authorize()
    {
        _http.DefaultRequestHeaders.Authorization =
            _auth.Token is null ? null : new AuthenticationHeaderValue("Bearer", _auth.Token);
    }
}
