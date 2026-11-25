using AMES.Api.Auth;
using AMES.Api.Workers;
using AMES.Api.Workers.DemandPlanSync;
using AMES.Api.Workers.PoSync;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Data.Services.PoSync;

namespace AMES.Api.Endpoints;

/// <summary>
/// Production Planning (PP) read-only API surface — wraps PpRepository
/// so PDA / external systems can pull the same data the Office Web shows.
/// All routes require bearer auth. Query params nullable so callers can omit them.
/// </summary>
public static class PpEndpoints
{
    public static void MapPp(this WebApplication app, AmesConnectionFactory factory)
    {
        var repo = new PpRepository(factory);
        var g = app.MapGroup("/api/pp").WithTags("Production Planning");

        g.MapGet("/forecast", (HttpContext ctx, int? monthsBack, int? monthsAhead) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListForecast(monthsBack ?? 6, monthsAhead ?? 6)));

        g.MapGet("/supply-plan-import", (HttpContext ctx, int? daysBack) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListSupplyPlanImports(daysBack ?? 30)));

        g.MapGet("/supply-plans", (HttpContext ctx, int? topN) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListSupplyPlans(topN ?? 30)));

        g.MapGet("/mrp-runs", (HttpContext ctx, int? topN) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListMrpRuns(topN ?? 20)));

        g.MapGet("/purchase-requests", (HttpContext ctx, int? topN) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListPurchaseRequests(topN ?? 50)));

        g.MapGet("/releasable", (HttpContext ctx, int? topN) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListReleasable(topN ?? 50)));

        g.MapGet("/schedule", (HttpContext ctx, int? daysAhead) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListSchedule(daysAhead ?? 7)));

        g.MapGet("/oee", (HttpContext ctx, int? daysBack) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListOee(daysBack ?? 14)));

        g.MapGet("/downtime", (HttpContext ctx, int? daysBack) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListDowntime(daysBack ?? 7)));

        g.MapGet("/line-states", (HttpContext ctx, int? hoursBack) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListLineStates(hoursBack ?? 4)));

        g.MapGet("/otd", (HttpContext ctx, int? daysBack) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListOtd(daysBack ?? 30)));

        g.MapPost("/po-sync/run", (HttpContext ctx, string? source, DateOnly? from, DateOnly? to, PoSyncWorker worker) =>
        {
            // 기간은 둘 다 주거나 둘 다 비운다 — 비우면 설정 창(+EOS 자동 확장). 7일씩 나눠 부르므로 31일 제한은 없고 안전 상한만 둔다.
            if (from.HasValue != to.HasValue)
                return Task.FromResult<IResult>(Results.BadRequest(new { error = "from 과 to 는 함께 지정해야 합니다." }));
            if (from is { } f && to is { } t
                && (t < f || t.DayNumber - f.DayNumber + 1 > PoSyncConfig.MaxManualRangeDays))
                return Task.FromResult<IResult>(Results.BadRequest(new { error = $"기간이 잘못됐습니다(시작 ≤ 끝, 최대 {PoSyncConfig.MaxManualRangeDays}일)." }));

            Func<PoSyncSource, PoSyncSource>? adjust = from is null ? null : s => s with { FixedFrom = from, FixedTo = to };
            return ScheduledWorkerEndpoints.RunAsync(ctx, worker, source, CancellationToken.None, () =>
            {
                using var conn = factory.OpenConnection();
                return PoSyncConfig.LoadServiceKey(conn);
            }, adjust);
        })
         .WithDescription("고객사 SRM PO 수집을 즉시 실행. source 생략 시 활성 소스 전부. from·to(yyyy-MM-dd)를 주면 그 기간만 받는다 " +
                          "(EOS = 납기일, SRM = 발주일, 최대 366일 — EOS 는 7일씩 나눠 부른다). " +
                          "Bearer 세션 또는 X-AMES-Service-Key(공통코드 SW_POSYNC_AUTH / AMES_SERVICE_KEY) 필요.");

        g.MapPost("/demand-plan-sync/run", (HttpContext ctx, string? source, DemandPlanSyncWorker worker) =>
            ScheduledWorkerEndpoints.RunAsync(ctx, worker, source, CancellationToken.None, () =>
            {
                using var conn = factory.OpenConnection();
                return PoSyncConfig.LoadServiceKey(conn);   // 서비스 키는 PO Sync 것을 같이 쓴다(스펙 §6)
            }))
         .WithDescription("고객사 SRM 일별 구매계획(MM30011) 수집을 즉시 실행. source 생략 시 활성 소스 전부. Bearer 세션 또는 X-AMES-Service-Key 필요.");
    }
}
