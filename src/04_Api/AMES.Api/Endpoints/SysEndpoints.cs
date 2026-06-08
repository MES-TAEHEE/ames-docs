using AMES.Api.Auth;
using AMES.Data.Connection;
using AMES.Data.Repositories;

namespace AMES.Api.Endpoints;

/// <summary>System / administration (SYS) read-only API surface — wraps SysRepository.</summary>
public static class SysEndpoints
{
    public static void MapSys(this WebApplication app, AmesConnectionFactory factory)
    {
        var repo = new SysRepository(factory);
        var g = app.MapGroup("/api/sys").WithTags("System Admin");

        g.MapGet("/users", (HttpContext ctx, int? topN) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListUsers(topN ?? 200)));

        g.MapGet("/role-permissions", (HttpContext ctx) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListRolePermissions()));

        g.MapGet("/roles", (HttpContext ctx) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListRoles().Select(r => new { r.RoleId, r.RoleName, r.UserCount })));

        g.MapGet("/calendar", (HttpContext ctx, int? daysAhead, int? daysBack) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListCalendar(daysAhead ?? 30, daysBack ?? 7)));

        g.MapGet("/interfaces", (HttpContext ctx) =>
            ctx.GetSession() is null ? Results.Unauthorized() : Results.Ok(repo.ListInterfaces()));

        g.MapGet("/audit", (HttpContext ctx, int? topN) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListAudit(topN ?? 200)));

        g.MapGet("/notification-rules", (HttpContext ctx) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListNotificationRules()));

        g.MapGet("/notification-history", (HttpContext ctx, int? topN) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListNotificationHistory(topN ?? 100)));

        g.MapGet("/config", (HttpContext ctx) =>
            ctx.GetSession() is null ? Results.Unauthorized() : Results.Ok(repo.ListConfig()));

        g.MapGet("/health", (HttpContext ctx) =>
            ctx.GetSession() is null ? Results.Unauthorized() : Results.Ok(repo.GetHealth()));
    }
}
