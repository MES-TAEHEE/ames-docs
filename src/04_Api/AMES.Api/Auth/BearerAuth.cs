using AMES.Contracts.Dto;
using Microsoft.AspNetCore.Http;

namespace AMES.Api.Auth;

/// <summary>
/// Tiny bearer-token middleware. Inspects `Authorization: Bearer <token>`
/// and attaches the resolved PopSessionDto to HttpContext.Items["session"].
/// Endpoints that need auth call ctx.RequireSession() (extension below).
/// </summary>
public static class BearerAuth
{
    public const string SessionKey = "ames-session";

    public static IApplicationBuilder UseBearerAuth(this IApplicationBuilder app, TokenStore store)
    {
        return app.Use(async (ctx, next) =>
        {
            var auth = ctx.Request.Headers.Authorization.ToString();
            if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = auth["Bearer ".Length..].Trim();
                var session = store.Resolve(token);
                if (session is not null) ctx.Items[SessionKey] = session;
            }
            await next();
        });
    }

    public static PopSessionDto? GetSession(this HttpContext ctx)
        => ctx.Items.TryGetValue(SessionKey, out var s) ? s as PopSessionDto : null;

    public static IResult RequireSession(this HttpContext ctx, out PopSessionDto session)
    {
        var s = ctx.GetSession();
        if (s is null) { session = null!; return Results.Unauthorized(); }
        session = s;
        return null!;
    }
}
