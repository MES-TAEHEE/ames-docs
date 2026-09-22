using System.Net;
using System.Net.Http.Json;
using AMES.Data.Connection;
using AMES.Data.Services.PoSync;

namespace AMES.Web.Services;

/// <summary>
/// PP-002 "API 가져오기" — AMES.Api 의 PO 수집 Worker 를 소스 하나만 즉시 실행시킨다(POST /api/pp/po-sync/run?source=).
/// Web 에서 직접 수집하지 않는 이유는 Worker 의 대상별 잠금·60초 쿨다운·모니터 기록을 스케줄 틱과 공유해야 하기 때문이다.
/// 인증은 공통코드 SW_POSYNC_AUTH / AMES_SERVICE_KEY 를 헤더로 보낸다 — Web 에는 POP 세션(Bearer)이 없다.
/// </summary>
public sealed class PoSyncClient(IHttpClientFactory http, IConfiguration cfg, AmesConnectionFactory factory)
{
    // 원격 SRM 타임아웃(SW_POSYNC.TIMEOUT_SEC, 기본 60초) + 업서트 시간보다 길어야 결과를 받는다
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(150);

    public enum Failure { None, NoApiUrl, NoServiceKey, Unauthorized, NotFound, Busy, Unreachable, Timeout, Unexpected }

    /// <summary>Failure=None 이어도 Result.Ok=false 일 수 있다(원격 호출 실패·소스 설정 오류) — 그 사유는 Result.Error.</summary>
    public sealed record Outcome(Failure Failure, PoSyncRunResult? Result = null, string? Detail = null);

    public async Task<Outcome> RunAsync(string sourceKey, CancellationToken ct = default)
    {
        var apiBase = cfg["Services:ApiBaseUrl"];
        if (string.IsNullOrWhiteSpace(apiBase)) return new(Failure.NoApiUrl);

        string? key;
        using (var conn = factory.OpenConnection())
            key = PoSyncConfig.LoadServiceKey(conn);
        if (key is null) return new(Failure.NoServiceKey);

        using var client = http.CreateClient();
        client.Timeout = Timeout;
        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"{apiBase.TrimEnd('/')}/api/pp/po-sync/run?source={Uri.EscapeDataString(sourceKey)}");
        req.Headers.Add(PoSyncConfig.ServiceKeyHeader, key);

        try
        {
            using var res = await client.SendAsync(req, ct);
            switch (res.StatusCode)
            {
                case HttpStatusCode.Unauthorized: return new(Failure.Unauthorized);
                case HttpStatusCode.NotFound:     return new(Failure.NotFound, Detail: await ErrorOf(res, ct));
                case HttpStatusCode.Conflict:     return new(Failure.Busy,     Detail: await ErrorOf(res, ct));
            }
            if (!res.IsSuccessStatusCode)
                return new(Failure.Unexpected, Detail: $"HTTP {(int)res.StatusCode}");

            var rows = await res.Content.ReadFromJsonAsync<List<PoSyncRunResult>>(ct);
            return rows is { Count: > 0 }
                ? new(Failure.None, rows[0])
                : new(Failure.Unexpected, Detail: "empty result");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(Failure.Timeout); }
        catch (HttpRequestException ex) { return new(Failure.Unreachable, Detail: ex.Message); }
    }

    private sealed record ErrorBody(string? Error);

    private static async Task<string?> ErrorOf(HttpResponseMessage res, CancellationToken ct)
    {
        try   { return (await res.Content.ReadFromJsonAsync<ErrorBody>(ct))?.Error; }
        catch { return null; }
    }
}
