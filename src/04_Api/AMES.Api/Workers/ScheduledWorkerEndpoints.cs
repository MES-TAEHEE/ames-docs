using AMES.Api.Auth;

namespace AMES.Api.Workers;

/// <summary>
/// 수동 실행 HTTP 규칙 한 곳 — 각 API 엔드포인트는 자기 경로·쿼리 이름(Swagger 파라미터)을 유지한 채 이 함수를 부른다.
/// 401 세션 없음 · 404 미등록 키 · 409 진행 중/쿨다운 · 200 결과 목록.
/// </summary>
public static class ScheduledWorkerEndpoints
{
    public static async Task<IResult> RunAsync<TTarget, TResult>(
        HttpContext ctx, ScheduledWorker<TTarget, TResult> worker, string? key, CancellationToken ct)
    {
        if (ctx.GetSession() is null) return Results.Unauthorized();
        try
        {
            return Results.Ok(await worker.RunNowAsync(key, ct));
        }
        catch (KeyNotFoundException ex)      { return Results.NotFound(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
    }
}
