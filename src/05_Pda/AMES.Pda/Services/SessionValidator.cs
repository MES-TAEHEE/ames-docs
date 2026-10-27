using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AMES.Contracts.Dto;

namespace AMES.Pda.Services;

public sealed class SessionValidator(HttpClient http, AuthState auth)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Task CheckOnActivationAsync()
    {
        // Activation can follow a brief interruption without a Stopped event.
        auth.RequireSessionCheck();
        return CheckAsync();
    }

    public async Task CheckAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var token = auth.Token;
            if (token is null || !auth.SessionCheckRequired) return;
            var version = auth.SessionCheckVersion;
            auth.CompleteSessionCheck(token, version, null, null);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var response = await http.SendAsync(request, timeout.Token);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    auth.ExpireSession(token);
                    return;
                }
                response.EnsureSuccessStatusCode();
                var session = await response.Content.ReadFromJsonAsync<PopSessionDto>(cancellationToken: timeout.Token);
                if (session is null) throw new JsonException("Empty session response.");
                auth.CompleteSessionCheck(token, version, session, null);
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
            {
                auth.CompleteSessionCheck(token, version, null,
                    "Cannot verify your session. Check the connection and retry.");
            }
        }
        finally { _gate.Release(); }
    }
}
