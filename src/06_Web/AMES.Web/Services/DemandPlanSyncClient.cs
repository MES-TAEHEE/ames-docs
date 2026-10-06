using AMES.Data.Connection;
using AMES.Data.Services.DemandPlan;

namespace AMES.Web.Services;

/// <summary>
/// PP-001 일별 탭 "API 가져오기" — AMES.Api 의 일별 구매계획(SRM MM30011) 수집 Worker 를 소스 하나만 즉시
/// 실행시킨다(POST /api/pp/demand-plan-sync/run?source=). HTTP·서비스 키·상태 코드 분기는 PoSyncClient 와
/// 공유하는 <see cref="WorkerRunClient"/>.
/// </summary>
public sealed class DemandPlanSyncClient(IHttpClientFactory http, IConfiguration cfg, AmesConnectionFactory factory)
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(150);

    public Task<WorkerRunOutcome<DemandPlanRunResult>> RunAsync(string sourceKey, CancellationToken ct = default)
        => WorkerRunClient.RunAsync<DemandPlanRunResult>(http, cfg, factory, "/api/pp/demand-plan-sync/run", sourceKey, Timeout, ct);
}
