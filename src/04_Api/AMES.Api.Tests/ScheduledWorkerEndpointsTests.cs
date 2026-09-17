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
}
