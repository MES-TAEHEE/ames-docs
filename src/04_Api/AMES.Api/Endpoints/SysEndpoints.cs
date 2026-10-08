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
        var master = new MasterDataRepository(factory);
        var g = app.MapGroup("/api/sys").WithTags("System Admin");
        var admin = g.MapGroup("").AddEndpointFilter(async (context, next) =>
            RequireAdmin(context.HttpContext) is { } denied ? denied : await next(context));

        g.MapGet("/code-items/{groupCode}", (HttpContext ctx, string groupCode) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok((master.CodeGroupIsActive(groupCode) ? master.ListCodeItems(groupCode) : [])
                    .Where(x => x.UseFlag)
                    .Select(x => new { x.CodeValue, x.CodeName, x.CodeNameEn, x.Attribute1 })));

        admin.MapGet("/users", (HttpContext ctx, int? topN) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListUsers(topN ?? 200)));

        admin.MapGet("/role-permissions", (HttpContext ctx) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListRolePermissions()));

        admin.MapGet("/roles", (HttpContext ctx) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListRoles().Select(r => new { r.RoleId, r.RoleName, r.UserCount })));

        g.MapGet("/calendar", (HttpContext ctx, int? daysAhead, int? daysBack) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListCalendar(daysAhead ?? 30, daysBack ?? 7)));

        admin.MapGet("/interfaces", (HttpContext ctx) =>
            ctx.GetSession() is null ? Results.Unauthorized() : Results.Ok(repo.ListInterfaces()));

        admin.MapGet("/audit", (HttpContext ctx, int? topN) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListAudit(topN ?? 200)));

        admin.MapGet("/notification-rules", (HttpContext ctx) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListNotificationRules()));

        admin.MapGet("/notification-history", (HttpContext ctx, int? topN) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListNotificationHistory(topN ?? 100)));

        admin.MapGet("/config", (HttpContext ctx) =>
            ctx.GetSession() is null ? Results.Unauthorized()
                : Results.Ok(repo.ListConfig().Select(c => new
                {
                    c.ConfigId, c.ConfigKey, c.ConfigType, c.Category,
                    c.CodeName, c.Unit, c.IsActive, c.SortOrder
                })));

        admin.MapGet("/health", (HttpContext ctx) =>
            ctx.GetSession() is null ? Results.Unauthorized() : Results.Ok(repo.GetHealth()));
    }

    public static IResult? RequireAdmin(HttpContext ctx) => ctx.GetSession() switch
    {
        null => Results.Unauthorized(),
        { IsAdmin: false } => Results.StatusCode(StatusCodes.Status403Forbidden),
        _ => null
    };
}
