using System.Net.Http.Headers;
using System.Net.Http.Json;
using AMES.Contracts.Dto;

namespace AMES.Tablet.Services;

public sealed class TabletAuthService(HttpClient http, TabletAuthState auth)
{
    private sealed record LoginRequest(string EmployeeNo, string Pin, string TerminalId, string LineId, string ShiftCode);
    private sealed record LoginResponse(string Token, string? Reason);

    public async Task<string?> LoginAsync(string employeeNo, string pin)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync("/api/auth/login",
                new LoginRequest(employeeNo, pin, "TABLET-01", "WAREHOUSE", "A"));
        }
        catch
        {
            return "Authentication service is unavailable.";
        }

        if (!response.IsSuccessStatusCode)
            return "Check Employee No and PIN.";

        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        if (string.IsNullOrWhiteSpace(login?.Token))
            return login?.Reason ?? "Check Employee No and PIN.";

        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        var session = await http.GetFromJsonAsync<PopSessionDto>("/api/auth/me");
        if (session is null)
            return "Session could not be loaded after sign-in.";

        auth.SignIn(login.Token, session);
        return null;
    }
}
