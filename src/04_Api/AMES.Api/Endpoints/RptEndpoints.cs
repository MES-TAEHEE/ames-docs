using AMES.Api.Auth;
using AMES.Data.Connection;
using AMES.Data.Repositories;

namespace AMES.Api.Endpoints;

/// <summary>Reports (RPT) read-only API surface — wraps RptRepository aggregates.</summary>
public static class RptEndpoints
{
    public static void MapRpt(this WebApplication app, AmesConnectionFactory factory)
    {
        var repo = new RptRepository(factory);
        var g = app.MapGroup("/api/rpt").WithTags("Reports");

        g.MapGet("/daily-production", (HttpContext ctx, int? daysBack) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListDailyProduction(daysBack ?? 14)));

        g.MapGet("/defect-pareto", (HttpContext ctx, int? daysBack, int? topN) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListDefectPareto(daysBack ?? 30, topN ?? 20)));

        g.MapGet("/daily-shipment", (HttpContext ctx, int? daysBack) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListDailyShipment(daysBack ?? 14)));

        g.MapGet("/otd", (HttpContext ctx, int? daysBack) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListOtd(daysBack ?? 30)));

        g.MapGet("/inventory", (HttpContext ctx, int? topN) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListInventory(topN ?? 100)));

        g.MapGet("/equipment-oee", (HttpContext ctx, int? daysBack) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListEquipmentOee(daysBack ?? 30)));

        g.MapGet("/monthly-kpi", (HttpContext ctx, int? monthsBack) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListMonthlyKpi(monthsBack ?? 6)));

        g.MapGet("/schedule-adherence", (HttpContext ctx, int? daysBack) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListScheduleAdherence(daysBack ?? 14)));

        g.MapGet("/catalog", (HttpContext ctx) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListReportCatalog()));
    }
}
