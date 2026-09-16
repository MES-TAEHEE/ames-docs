using System.Net.Http.Headers;
using System.Net.Http.Json;
using AMES.Contracts.Dto;

namespace AMES.Pda.Services;

public sealed class AuthApi(HttpClient http, AuthState auth) : PdaApi(http, auth)
{
    /// <summary>
    /// Login + session fetch in one call. The API hands back an opaque
    /// bearer token; we stamp it locally and immediately call /me so the
    /// caller gets a fully-populated PopSessionDto in one await. Avoids
    /// the timing bug where AuthState.Token wasn't set yet when MeAsync
    /// rebuilt the Authorization header.
    /// Returns null on any auth failure or unreachable API.
    /// </summary>
    public Task<(string Token, PopSessionDto Session, string? Reason)?> LoginAsync(
        string employeeNo, string pin,
        string terminalId = "PDA-DEV-01",
        string lineId = "LINE-INJ-01",
        string shiftCode = "A")
        => LoginCoreAsync("/api/auth/login",
            new LoginReq(employeeNo, pin, terminalId, lineId, shiftCode),
            "Check Employee No and PIN.");

    public Task<(string Token, PopSessionDto Session, string? Reason)?> LoginByBarcodeAsync(
        string barcode,
        string terminalId = "PDA-DEV-01",
        string lineId = "LINE-INJ-01",
        string shiftCode = "A")
        => LoginCoreAsync("/api/auth/barcode-login",
            new BarcodeLoginReq(barcode, terminalId, lineId, shiftCode),
            "Scan or enter a valid employee barcode.");

    private async Task<(string Token, PopSessionDto Session, string? Reason)?> LoginCoreAsync<T>(
        string path, T request, string defaultReason)
    {
        HttpResponseMessage resp;
        try
        {
            resp = await _http.PostAsJsonAsync(path, request);
        }
        catch (Exception)
        {
            return (Token: "", Session: null!, Reason: "Authentication service is unavailable.");
        }

        if (!resp.IsSuccessStatusCode)
            return (Token: "", Session: null!, Reason: await LoginHttpErrorAsync(resp, defaultReason));

        LoginRes? login;
        try
        {
            login = await resp.Content.ReadFromJsonAsync<LoginRes>();
        }
        catch
        {
            return (Token: "", Session: null!, Reason: "Authentication service returned an invalid response.");
        }

        if (login is null || string.IsNullOrEmpty(login.Token))
            return (Token: "", Session: null!, Reason: NormalizeLoginReason(login?.Reason, defaultReason));

        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.Token);

        var meResp = await _http.GetAsync("/api/auth/me");
        if (!meResp.IsSuccessStatusCode)
            return (Token: "", Session: null!, Reason: "Session could not be loaded after sign-in.");

        var session = await meResp.Content.ReadFromJsonAsync<PopSessionDto>();
        return session is null
            ? (Token: "", Session: null!, Reason: "Session response was empty.")
            : (login.Token, session, null);
    }

    private static async Task<string> LoginHttpErrorAsync(HttpResponseMessage resp, string defaultReason)
    {
        if ((int)resp.StatusCode >= 500)
            return "Authentication service failed. Check API database connection.";

        var body = "";
        try { body = await resp.Content.ReadAsStringAsync(); }
        catch { /* ignore body read failures */ }

        return string.IsNullOrWhiteSpace(body)
            ? $"Authentication request failed ({(int)resp.StatusCode})."
            : NormalizeLoginReason(body, defaultReason);
    }

    private static string NormalizeLoginReason(string? reason, string defaultReason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return defaultReason;

        var text = reason.Trim();
        if (text.Contains("SqlException", StringComparison.OrdinalIgnoreCase)
            || text.Contains("network-related", StringComparison.OrdinalIgnoreCase)
            || text.Contains("SQL Server", StringComparison.OrdinalIgnoreCase))
            return "Authentication database is unavailable.";

        if (text.Equals("bad pin", StringComparison.OrdinalIgnoreCase)
            || text.Equals("unknown employee", StringComparison.OrdinalIgnoreCase))
            return defaultReason;

        if (text.Length > 160)
            text = text[..160] + "...";

        return text;
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

}
