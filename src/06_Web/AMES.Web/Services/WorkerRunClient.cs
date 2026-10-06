using System.Net;
using System.Net.Http.Json;
using AMES.Data.Connection;
using AMES.Data.Services.PoSync;

namespace AMES.Web.Services;

public enum WorkerRunFailure { None, NoApiUrl, NoServiceKey, Unauthorized, NotFound, Busy, Unreachable, Timeout, Unexpected }

/// <summary>Failure=None 이어도 Result 자체가 실패를 담을 수 있다(원격 호출 실패·소스 설정 오류) — 그 사유는 각 Result.Error.</summary>
public sealed record WorkerRunOutcome<TResult>(WorkerRunFailure Failure, TResult? Result = default, string? Detail = null);

/// <summary>
/// PO Sync(PP-002)·DemandPlanSync(PP-001) 공용 — AMES.Api 의 ScheduledWorker 소스 하나만 즉시 실행시킨다
/// (POST {path}?source=). Web 에서 직접 수집하지 않는 이유는 Worker 의 대상별 잠금·60초 쿨다운·모니터 기록을
/// 스케줄 틱과 공유해야 하기 때문이다. 인증은 공통코드 SW_POSYNC_AUTH / AMES_SERVICE_KEY(모든 Worker 공유)를
/// 헤더로 보낸다 — Web 에는 POP 세션(Bearer)이 없다.
/// </summary>
public static class WorkerRunClient
{
    public static async Task<WorkerRunOutcome<TResult>> RunAsync<TResult>(
        IHttpClientFactory http, IConfiguration cfg, AmesConnectionFactory factory,
        string path, string sourceKey, TimeSpan timeout, CancellationToken ct)
    {
        var apiBase = cfg["Services:ApiBaseUrl"];
        if (string.IsNullOrWhiteSpace(apiBase)) return new(WorkerRunFailure.NoApiUrl);

        string? key;
        using (var conn = factory.OpenConnection())
            key = PoSyncConfig.LoadServiceKey(conn);
        if (key is null) return new(WorkerRunFailure.NoServiceKey);

        using var client = http.CreateClient();
        client.Timeout = timeout;
        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"{apiBase.TrimEnd('/')}{path}?source={Uri.EscapeDataString(sourceKey)}");
        req.Headers.Add(PoSyncConfig.ServiceKeyHeader, key);

        try
        {
            using var res = await client.SendAsync(req, ct);
            switch (res.StatusCode)
            {
                case HttpStatusCode.Unauthorized: return new(WorkerRunFailure.Unauthorized);
                case HttpStatusCode.NotFound:     return new(WorkerRunFailure.NotFound, Detail: await ErrorOf(res, ct));
                case HttpStatusCode.Conflict:     return new(WorkerRunFailure.Busy,     Detail: await ErrorOf(res, ct));
            }
            if (!res.IsSuccessStatusCode)
                return new(WorkerRunFailure.Unexpected, Detail: $"HTTP {(int)res.StatusCode}");

            var rows = await res.Content.ReadFromJsonAsync<List<TResult>>(ct);
            return rows is { Count: > 0 }
                ? new(WorkerRunFailure.None, rows[0])
                : new(WorkerRunFailure.Unexpected, Detail: "empty result");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(WorkerRunFailure.Timeout); }
        catch (HttpRequestException ex) { return new(WorkerRunFailure.Unreachable, Detail: ex.Message); }
    }

    private sealed record ErrorBody(string? Error);

    private static async Task<string?> ErrorOf(HttpResponseMessage res, CancellationToken ct)
    {
        try   { return (await res.Content.ReadFromJsonAsync<ErrorBody>(ct))?.Error; }
        catch { return null; }
    }
}
