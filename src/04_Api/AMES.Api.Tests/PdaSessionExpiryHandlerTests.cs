using System.Net;
using System.Net.Http.Headers;
using AMES.Contracts.Dto;
using AMES.Contracts.Enums;
using AMES.Pda.Services;
using Xunit;

namespace AMES.Api.Tests;

public class PdaSessionExpiryHandlerTests
{
    [Theory]
    [InlineData("/api/wh/inventory", HttpStatusCode.Unauthorized, true)]
    [InlineData("/api/wh/inventory", HttpStatusCode.ServiceUnavailable, false)]
    [InlineData("/api/auth/barcode-login", HttpStatusCode.Unauthorized, false)]
    [InlineData("/api/auth/employees", HttpStatusCode.Unauthorized, false)]
    public async Task OnlyExpiredAuthenticatedRequestsClearSession(string path, HttpStatusCode status, bool signedOut)
    {
        var auth = new AuthState();
        auth.SignIn("old-token", new PopSessionDto
        {
            SessionId = 1, OperatorId = "TEST", EmployeeNo = "TEST", EmployeeName = "Test",
            TerminalId = "PDA", LineId = "01", ShiftCode = "A", AuthMethod = AuthMethod.Badge,
            StartedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddHours(1)
        });
        using var validationClient = new HttpClient(new ResponseHandler(HttpStatusCode.Unauthorized));
        using var client = new HttpClient(new SessionExpiryHandler(auth, new SessionValidator(validationClient, auth))
        {
            InnerHandler = new ResponseHandler(status)
        }) { BaseAddress = new Uri("https://example.test") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        using var response = await client.GetAsync(path);

        Assert.Equal(signedOut, auth.Session is null);
    }

    [Fact]
    public async Task EmployeePickerRemainsAccessibleWhenSessionRevalidationIsPending()
    {
        var auth = new AuthState();
        auth.SignIn("old-token", new PopSessionDto
        {
            SessionId = 1, OperatorId = "TEST", EmployeeNo = "TEST", EmployeeName = "Test",
            TerminalId = "PDA", LineId = "", ShiftCode = "", AuthMethod = AuthMethod.Badge,
            StartedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddHours(1)
        });
        auth.RequireSessionCheck();
        Assert.True(auth.SessionCheckRequired);
        using var validationClient = new HttpClient(new ResponseHandler(HttpStatusCode.Unauthorized));
        using var client = new HttpClient(new SessionExpiryHandler(auth, new SessionValidator(validationClient, auth))
        {
            InnerHandler = new ResponseHandler(HttpStatusCode.OK)
        }) { BaseAddress = new Uri("https://example.test") };

        using var response = await client.GetAsync("/api/auth/employees");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed class ResponseHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status));
    }
}
