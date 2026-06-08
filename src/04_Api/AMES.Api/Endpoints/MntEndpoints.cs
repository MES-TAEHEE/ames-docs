using AMES.Api.Auth;
using AMES.Data.Connection;
using AMES.Data.Repositories;

namespace AMES.Api.Endpoints;

/// <summary>Maintenance (MNT) read-only API surface — wraps MntRepository.</summary>
public static class MntEndpoints
{
    public static void MapMnt(this WebApplication app, AmesConnectionFactory factory)
    {
        var repo = new MntRepository(factory);
        var g = app.MapGroup("/api/mnt").WithTags("Maintenance");

        g.MapGet("/equipment", (HttpContext ctx, string? lineId) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListEquipment(string.IsNullOrWhiteSpace(lineId) ? null : lineId)));

        g.MapGet("/failures", (HttpContext ctx, int? topN, string? status) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListFailures(topN ?? 100,
                                                string.IsNullOrWhiteSpace(status) ? null : status)));

        g.MapGet("/oee", (HttpContext ctx, int? daysBack, string? equipId) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListOee(daysBack ?? 14,
                                           string.IsNullOrWhiteSpace(equipId) ? null : equipId)));

        g.MapGet("/molds", (HttpContext ctx) =>
            ctx.GetSession() is null ? Results.Unauthorized() : Results.Ok(repo.ListMolds()));

        g.MapGet("/pm-schedule", (HttpContext ctx, int? daysAhead, int? daysBack) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListPmSchedule(daysAhead ?? 30, daysBack ?? 7)));

        g.MapGet("/downtime", (HttpContext ctx, int? daysBack) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListDowntime(daysBack ?? 7)));

        g.MapGet("/work-orders", (HttpContext ctx, int? topN, string? status) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListWorkOrders(topN ?? 100,
                                                  string.IsNullOrWhiteSpace(status) ? null : status)));

        g.MapGet("/spare-parts", (HttpContext ctx) =>
            ctx.GetSession() is null ? Results.Unauthorized() : Results.Ok(repo.ListSpareParts()));

        g.MapGet("/spare-parts-txn", (HttpContext ctx, int? topN, string? partNo) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListSparePartsTxn(topN ?? 50,
                                                     string.IsNullOrWhiteSpace(partNo) ? null : partNo)));

        g.MapGet("/dashboard", (HttpContext ctx) =>
            ctx.GetSession() is null ? Results.Unauthorized() : Results.Ok(repo.GetDashboardKpis()));
    }
}
