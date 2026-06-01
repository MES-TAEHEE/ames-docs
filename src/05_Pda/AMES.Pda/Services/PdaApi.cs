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

    public async Task<LoginRes?> LoginAsync(string employeeNo, string pin,
                                             string terminalId = "PDA-DEV-01",
                                             string lineId = "LINE-INJ-01",
                                             string shiftCode = "DAY")
    {
        var resp = await _http.PostAsJsonAsync("/api/auth/login",
            new LoginReq(employeeNo, pin, terminalId, lineId, shiftCode));
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<LoginRes>();
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
