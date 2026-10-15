using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AMES.Contracts.Dto;
using AMES.Contracts.Enums;
using AMES.Pda.Services;
using Xunit;

namespace AMES.Api.Tests;

public class PdaForegroundSessionTests
{
    private static PopSessionDto Session() => new()
    {
        SessionId = 1, OperatorId = "TEST", EmployeeNo = "TEST", EmployeeName = "Test",
        TerminalId = "PDA", LineId = "01", ShiftCode = "A", AuthMethod = AuthMethod.Badge,
        StartedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddHours(1)
    };

    private static AuthState SignedIn()
    {
        var auth = new AuthState();
        auth.SignIn("old-token", Session());
        return auth;
    }

    [Fact]
    public async Task ConcurrentResumeChecksValidateOnceAndUnblockValidSession()
    {
        var auth = SignedIn();
        auth.RequireSessionCheck();
        var calls = 0;
        using var client = Client(async (request, _) =>
        {
            Assert.Equal("/api/auth/me", request.RequestUri!.AbsolutePath);
            Assert.Equal("old-token", request.Headers.Authorization?.Parameter);
            Interlocked.Increment(ref calls);
            await Task.Yield();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Session()) };
        });
        var validator = new SessionValidator(client, auth);

        await Task.WhenAll(validator.CheckAsync(), validator.CheckAsync());

        Assert.Equal(1, calls);
        Assert.False(auth.SessionCheckRequired);
        Assert.NotNull(auth.Session);
        Assert.False(auth.TakeExpiryNotice());
    }

    [Fact]
    public async Task ExpiredForegroundSessionNotifiesOnceAndDoesNotSendOutbound()
    {
        var auth = SignedIn();
        auth.RequireSessionCheck();
        using var validationClient = Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        var outboundCalls = 0;
        using var client = new HttpClient(new SessionExpiryHandler(auth, new SessionValidator(validationClient, auth))
        {
            InnerHandler = new CallbackHandler((_, _) =>
            {
                outboundCalls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            })
        }) { BaseAddress = new Uri("https://example.test") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        using var response = await client.PostAsync("/api/fg/outbound", null);
        auth.ExpireSession("old-token");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, outboundCalls);
        Assert.Null(auth.Session);
        Assert.True(auth.TakeExpiryNotice());
        Assert.False(auth.TakeExpiryNotice());
    }

    [Theory]
    [InlineData(403)]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ConnectionFailureIsNotSessionExpiry(int status)
    {
        var auth = SignedIn();
        auth.RequireSessionCheck();
        using var client = Client((_, _) => status switch
        {
            0 => throw new HttpRequestException("Offline"),
            -1 => throw new TaskCanceledException("Timeout"),
            _ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status))
        });

        await new SessionValidator(client, auth).CheckAsync();

        Assert.NotNull(auth.Session);
        Assert.True(auth.SessionCheckRequired);
        Assert.NotNull(auth.SessionCheckError);
        Assert.False(auth.TakeExpiryNotice());
    }

    [Fact]
    public async Task StaleUnauthorizedResponseDoesNotSignOutNewLogin()
    {
        var auth = SignedIn();
        auth.RequireSessionCheck();
        using var client = Client((_, _) =>
        {
            auth.SignIn("new-token", Session());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        });

        await new SessionValidator(client, auth).CheckAsync();

        Assert.Equal("new-token", auth.Token);
        Assert.False(auth.TakeExpiryNotice());
    }

    [Fact]
    public async Task OldCheckCannotUnblockAnotherBackgroundCycle()
    {
        var auth = SignedIn();
        auth.RequireSessionCheck();
        using var client = Client((_, _) =>
        {
            auth.RequireSessionCheck();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Session()) });
        });

        await new SessionValidator(client, auth).CheckAsync();

        Assert.True(auth.SessionCheckRequired);
    }

    [Fact]
    public async Task FailedValidationBlocksOutboundUntilRetrySucceeds()
    {
        var auth = SignedIn();
        auth.RequireSessionCheck();
        var connected = false;
        using var validationClient = Client((_, _) => Task.FromResult(connected
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Session()) }
            : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var validator = new SessionValidator(validationClient, auth);
        var outboundCalls = 0;
        using var client = new HttpClient(new SessionExpiryHandler(auth, validator)
        {
            InnerHandler = new CallbackHandler((_, _) =>
            {
                outboundCalls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            })
        }) { BaseAddress = new Uri("https://example.test") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        using var blocked = await client.PostAsync("/api/fg/outbound", null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, blocked.StatusCode);
        Assert.Equal(0, outboundCalls);

        connected = true;
        await validator.CheckAsync();
        using var allowed = await client.PostAsync("/api/fg/outbound", null);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(1, outboundCalls);
        Assert.False(auth.SessionCheckRequired);
    }

    [Fact]
    public void ConcurrentExpiryResponsesOnlyNotifyOnce()
    {
        var auth = SignedIn();
        var notices = 0;
        auth.OnChange += () =>
        {
            if (auth.TakeExpiryNotice()) Interlocked.Increment(ref notices);
        };
        Parallel.For(0, 20, _ => auth.ExpireSession("old-token"));
        Assert.Equal(1, notices);
    }

    [Fact]
    public void ExplicitLogoutHasNoExpiryAlert()
    {
        var auth = SignedIn();
        auth.SignOut();
        Assert.False(auth.TakeExpiryNotice());
    }

    private static HttpClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback)
        => new(new CallbackHandler(callback)) { BaseAddress = new Uri("https://example.test") };

    private sealed class CallbackHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => callback(request, cancellationToken);
    }
}
