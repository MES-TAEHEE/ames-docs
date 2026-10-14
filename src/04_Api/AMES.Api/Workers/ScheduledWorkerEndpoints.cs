using AMES.Api.Auth;
using AMES.Data.Services.PoSync;

namespace AMES.Api.Workers;

/// <summary>
/// 수동 실행 HTTP 규칙 한 곳 — 각 API 엔드포인트는 자기 경로·쿼리 이름(Swagger 파라미터)을 유지한 채 이 함수를 부른다.
/// 401 세션·서비스 키 없음 · 404 미등록 키 · 409 진행 중/쿨다운 · 200 결과 목록.
/// 서비스 키는 POP 세션이 없는 AMES.Web 이 부르는 길이다 — 엔드포인트가 키 공급자를 넘길 때만 열리고,
/// 공급자가 null·빈 값을 주거나 예외를 내면 닫힌 채로 남는다.
/// </summary>
public static class ScheduledWorkerEndpoints
{
    public const string ServiceKeyHeader = PoSyncConfig.ServiceKeyHeader;

    public static async Task<IResult> RunAsync<TTarget, TResult>(
        HttpContext ctx, ScheduledWorker<TTarget, TResult> worker, string? key, CancellationToken requestCancellation,
        Func<string?>? serviceKey = null)
    {
        var session = ctx.GetSession();
        if (session is null && !HasServiceKey(ctx, serviceKey)) return Results.Unauthorized();
        if (session is { IsAdmin: false }) return Results.StatusCode(StatusCodes.Status403Forbidden);
        try
        {
            // Once accepted, a browser/PDA disconnect must not cancel an import already in progress.
            var lifetime = ctx.RequestServices is { } services ? services.GetService<IHostApplicationLifetime>() : null;
            var results=await worker.RunNowAsync(key, lifetime?.ApplicationStopping ?? CancellationToken.None);
            try
            {
                var factory=ctx.RequestServices?.GetService<AMES.Data.Connection.AmesConnectionFactory>();
                if (factory is not null)
                    new AMES.Data.Repositories.SysRepository(factory).InsertAuditLog("PP",null,"RUN","ScheduledWorker",
                        key is { Length:<=40 } ? key : "ALL",null,null,session?.EmployeeNo ?? "SERVICE-KEY",
                        ctx.Connection.RemoteIpAddress?.ToString(),note:$"Manual import requested: {ctx.Request.Path}");
            }
            catch (Exception ex)
            { ctx.RequestServices?.GetService<ILoggerFactory>()?.CreateLogger("ManualImport").LogWarning(ex,"Manual import audit failed"); }
            return Results.Ok(results);
        }
        catch (KeyNotFoundException ex)      { return Results.NotFound(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (Microsoft.Data.SqlClient.SqlException)
        { return Results.Problem("Database is unavailable. Please retry.", statusCode: StatusCodes.Status503ServiceUnavailable); }
    }

    private static bool HasServiceKey(HttpContext ctx, Func<string?>? serviceKey)
    {
        if (serviceKey is null) return false;
        var presented = ctx.Request.Headers[ServiceKeyHeader].ToString();
        if (presented.Length == 0) return false;
        try   { return PoSyncConfig.ServiceKeyMatches(serviceKey(), presented); }
        catch { return false; }
    }
}
