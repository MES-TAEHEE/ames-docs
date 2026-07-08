using AMES.Api.Auth;
using AMES.Data.Connection;
using AMES.Data.Repositories;

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

        g.MapGet("/calendar", (HttpContext ctx, int? daysBack, int? daysAhead) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListCalendarOverrides(daysBack ?? 7, daysAhead ?? 30)));

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
    }
}
