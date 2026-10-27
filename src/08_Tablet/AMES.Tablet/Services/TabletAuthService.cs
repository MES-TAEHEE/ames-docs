using System.Net.Http.Headers;
using System.Net.Http.Json;
using AMES.Contracts.Dto;

namespace AMES.Tablet.Services;

public sealed class TabletAuthService(HttpClient http, TabletAuthState auth)
{
    private sealed record LoginRequest(string EmployeeNo, string Pin, string TerminalId);
    private sealed record LoginResponse(string Token, string? Reason);

    public async Task<List<LoginEmployeeDto>> EmployeesAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var response = await http.GetAsync("/api/auth/employees", timeout.Token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<LoginEmployeeDto>>(cancellationToken: timeout.Token) ?? [];
    }

    public async Task<string?> LoginAsync(string employeeNo, string pin)
    {
        try
        {
            http.DefaultRequestHeaders.Authorization = null;
            auth.SignOut();
            using var response = await http.PostAsJsonAsync("/api/auth/login",
                new LoginRequest(employeeNo, pin, "TABLET-01"));
            if (!response.IsSuccessStatusCode)
                return response.StatusCode switch
                {
                    System.Net.HttpStatusCode.Forbidden => "This account is not allowed to sign in on this server.",
                    System.Net.HttpStatusCode.TooManyRequests => "Too many sign-in attempts. Please wait and retry.",
                    _ => $"Sign-in failed (HTTP {(int)response.StatusCode}). Check your ID and PIN, or retry."
                };

            var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
            if (string.IsNullOrWhiteSpace(login?.Token))
                return login?.Reason ?? "Check Employee No and PIN.";

            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
            using var me = await http.GetAsync("/api/auth/me");
            if (!me.IsSuccessStatusCode)
            {
                http.DefaultRequestHeaders.Authorization = null;
                return "Session could not be loaded after sign-in. Please retry.";
            }
            var session = await me.Content.ReadFromJsonAsync<PopSessionDto>();
            if (session is null) { http.DefaultRequestHeaders.Authorization = null; return "Session could not be loaded after sign-in."; }
            auth.SignIn(login.Token, session);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            http.DefaultRequestHeaders.Authorization = null;
            return "Authentication service is unavailable. Check Tailscale and retry.";
        }
    }

    public async Task LogoutAsync()
    {
        var token = http.DefaultRequestHeaders.Authorization;
        http.DefaultRequestHeaders.Authorization = null;
        auth.SignOut();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Authorization = token;
        try { using var response = await http.SendAsync(request); }
        catch (HttpRequestException) { /* Local sign-out must still work offline. */ }
        catch (TaskCanceledException) { /* Local sign-out must still work offline. */ }
    }
}
