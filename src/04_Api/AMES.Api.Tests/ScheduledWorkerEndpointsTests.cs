using AMES.Api.Auth;
using AMES.Api.Workers;
using AMES.Contracts.Dto;
using AMES.Contracts.Enums;
using Microsoft.AspNetCore.Http;
using Xunit;
using static AMES.Api.Tests.ScheduledWorkerTests;

namespace AMES.Api.Tests;

/// <summary>수동 실행 HTTP 규칙 — 각 API 엔드포인트가 한 줄로 이 함수를 부르므로 상태 코드 매핑의 정본.</summary>
public class ScheduledWorkerEndpointsTests
{
    static HttpContext Ctx(bool signedIn)
    {
        var ctx = new DefaultHttpContext();
        if (signedIn)
            ctx.Items[BearerAuth.SessionKey] = new PopSessionDto
            {
                SessionId = 1, OperatorId = "W001", EmployeeNo = "W001", EmployeeName = "Test",
                TerminalId = "T", LineId = "L", ShiftCode = "DAY", AuthMethod = AuthMethod.Pin,
                StartedAt = DateTime.Now, ExpiresAt = DateTime.Now.AddHours(1),
            };
        return ctx;
    }

    static int? Status(IResult r) => (r as IStatusCodeHttpResult)?.StatusCode;

    [Fact]
    public async Task No_session_is_401_and_nothing_runs()
    {
        var w = new FakeWorker { Plan = new ScheduledPlan<FakeTarget>([new("A", 30)], [], true) };
        Assert.Equal(401, Status(await ScheduledWorkerEndpoints.RunAsync(Ctx(false), w, "A", CancellationToken.None)));
        Assert.Empty(w.Runs);
    }

    [Fact]
    public async Task Known_key_is_200_unknown_is_404_cooldown_is_409()
    {
        var w = new FakeWorker { Plan = new ScheduledPlan<FakeTarget>([new("A", 30)], [], true) };

        Assert.Equal(200, Status(await ScheduledWorkerEndpoints.RunAsync(Ctx(true), w, "A", CancellationToken.None)));
        Assert.Equal(404, Status(await ScheduledWorkerEndpoints.RunAsync(Ctx(true), w, "NOPE", CancellationToken.None)));
        Assert.Equal(409, Status(await ScheduledWorkerEndpoints.RunAsync(Ctx(true), w, "A", CancellationToken.None)));
    }

    // ── 서비스 키 (Web → Api, 세션 없는 호출) ──────────────────────────

    const string Key = "0123456789abcdef0123456789abcdef";

    static HttpContext WithKey(string? presented)
    {
        var ctx = Ctx(false);
        if (presented is not null) ctx.Request.Headers[ScheduledWorkerEndpoints.ServiceKeyHeader] = presented;
        return ctx;
    }

    static FakeWorker OneTarget() => new() { Plan = new ScheduledPlan<FakeTarget>([new("A", 30)], [], true) };

    [Fact]
    public async Task Matching_service_key_runs_without_a_session()
    {
        var w = OneTarget();
        Assert.Equal(200, Status(await ScheduledWorkerEndpoints.RunAsync(WithKey(Key), w, "A", CancellationToken.None, () => Key)));
        Assert.Equal(["A"], w.Runs);
    }

    [Fact]
    public async Task Wrong_or_missing_service_key_is_401_and_nothing_runs()
    {
        var w = OneTarget();
        Assert.Equal(401, Status(await ScheduledWorkerEndpoints.RunAsync(WithKey(Key + "x"), w, "A", CancellationToken.None, () => Key)));
        Assert.Equal(401, Status(await ScheduledWorkerEndpoints.RunAsync(WithKey(null), w, "A", CancellationToken.None, () => Key)));
        Assert.Empty(w.Runs);
    }

    [Fact]
    public async Task Unconfigured_service_key_is_401_even_if_the_header_is_sent()
    {
        var w = OneTarget();
        Assert.Equal(401, Status(await ScheduledWorkerEndpoints.RunAsync(WithKey(Key), w, "A", CancellationToken.None, () => null)));
        Assert.Equal(401, Status(await ScheduledWorkerEndpoints.RunAsync(WithKey(""), w, "A", CancellationToken.None, () => "")));
        // 키 공급자를 안 준 엔드포인트는 종전대로 세션만 받는다
        Assert.Equal(401, Status(await ScheduledWorkerEndpoints.RunAsync(WithKey(Key), w, "A", CancellationToken.None)));
        Assert.Empty(w.Runs);
    }

    [Fact]
    public async Task Key_lookup_is_skipped_when_no_header_is_sent_or_a_session_exists()
    {
        var w = OneTarget();
        int lookups = 0;
        string? Provider() { lookups++; return Key; }

        await ScheduledWorkerEndpoints.RunAsync(WithKey(null), w, "A", CancellationToken.None, Provider);
        Assert.Equal(200, Status(await ScheduledWorkerEndpoints.RunAsync(Ctx(true), w, "A", CancellationToken.None, Provider)));
        Assert.Equal(0, lookups);
    }

    [Fact]
    public async Task Key_lookup_failure_is_401_not_500()
    {
        var w = OneTarget();
        Assert.Equal(401, Status(await ScheduledWorkerEndpoints.RunAsync(WithKey(Key), w, "A", CancellationToken.None,
            () => throw new InvalidOperationException("db down"))));
        Assert.Empty(w.Runs);
    }
}
