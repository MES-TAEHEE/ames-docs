using AMES.Data.Connection;
using AMES.Data.Services.PoSync;

namespace AMES.Web.Services;

/// <summary>
/// PP-002 "API 가져오기" — AMES.Api 의 PO 수집 Worker 를 소스 하나만 즉시 실행시킨다(POST /api/pp/po-sync/run?source=).
/// HTTP·서비스 키·상태 코드 분기는 <see cref="WorkerRunClient"/> 공용(DemandPlanSyncClient 와 공유) — 여기서는
/// 결과 형(Outcome/Failure)만 PP-002 다이얼로그가 이미 쓰는 모양 그대로 유지한다.
/// </summary>
public sealed class PoSyncClient(IHttpClientFactory http, IConfiguration cfg, AmesConnectionFactory factory)
{
    // EOS 는 7일씩 여러 번 부른다(자동 확장·긴 기간이면 20회 안팎) — 원격 호출 합계 + 업서트보다 길어야 결과를 받는다
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(300);

    public enum Failure { None, NoApiUrl, NoServiceKey, Unauthorized, NotFound, Busy, Unreachable, Timeout, Unexpected }

    /// <summary>Failure=None 이어도 Result.Ok=false 일 수 있다(원격 호출 실패·소스 설정 오류) — 그 사유는 Result.Error.</summary>
    public sealed record Outcome(Failure Failure, PoSyncRunResult? Result = null, string? Detail = null);

    /// <summary>from·to 를 주면 그 기간만(Api 가 7일씩 나눠 부른다), 비우면 설정 창 + 자동 확장.</summary>
    public async Task<Outcome> RunAsync(string sourceKey, DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default)
    {
        var q = from is { } f && to is { } t ? $"from={f:yyyy-MM-dd}&to={t:yyyy-MM-dd}" : null;
        var o = await WorkerRunClient.RunAsync<PoSyncRunResult>(http, cfg, factory, "/api/pp/po-sync/run", sourceKey, Timeout, ct, q);
        return new Outcome((Failure)o.Failure, o.Result, o.Detail);
    }
}
