using AMES.Data.Connection;
using AMES.Data.Services;
using AMES.Data.Services.DemandPlan;

namespace AMES.Api.Workers.DemandPlanSync;

/// <summary>
/// 고객사 SRM MM30011(일별 구매계획, JIT) 수집 Worker. 대상 = 공통코드 SW_DPSYNC_SOURCE 한 행(고객사).
/// 전역 SW_DPSYNC.INTERVAL=0 이면 스케줄러가 멈춘다(수동 실행은 가능).
/// 틱 간격·시작 지연·HTTP 타임아웃은 SW_DPSYNC.TICK_SEC / STARTUP_DELAY_SEC / TIMEOUT_SEC, 없으면 appsettings ScheduledWorker.
/// </summary>
public sealed class DemandPlanSyncWorker(AmesConnectionFactory f, IDemandPlanSource source, IConfiguration cfg, ILogger<DemandPlanSyncWorker> log)
    : ScheduledWorker<DemandPlanSource, DemandPlanRunResult>(f, cfg, log)
{
    private readonly DemandPlanRunner _runner = new(f, source);

    protected override string Code  => DemandPlanConfig.MonitorCode;
    protected override string Name  => "Demand Plan Sync";
    protected override string Actor => DemandPlanRunner.Actor;

    protected override ScheduledPlan<DemandPlanSource> LoadPlan()
    {
        using var conn = Factory.OpenConnection();
        var config = DemandPlanConfig.Load(conn);
        return new ScheduledPlan<DemandPlanSource>(
            config.Sources,
            config.Errors.Select(e => new TargetConfigError(e.Key, e.Name, e.Message)).ToList(),
            Enabled: config.GlobalIntervalMin != 0,
            config.TickSec, config.StartupDelaySec);
    }

    protected override string KeyOf(DemandPlanSource s) => s.Key;
    protected override int IntervalMinOf(DemandPlanSource s) => s.IntervalMin;

    protected override Task<DemandPlanRunResult> RunTargetAsync(DemandPlanSource s, CancellationToken ct)
        => _runner.RunAsync(s, ct);

    protected override DemandPlanRunResult RecordConfigError(TargetConfigError e)
        => _runner.RecordConfigError(new WorkerConfigError(e.Key, e.Name, e.Message));

    protected override DemandPlanRunResult Skipped(DemandPlanSource s, string reason)
        => new(s.Key, false, 0, 0, 0, 0, 0, 0, 0, null, null, reason);

    protected override bool IsOk(DemandPlanRunResult r) => r.Ok;

    protected override void LogResult(DemandPlanSource s, DemandPlanRunResult r)
    {
        if (r.Ok)
        {
            Log.LogInformation("DP sync {Source}: fetched {F} mapped {M} skipped {S} items {I} cells {C}",
                r.Source, r.Fetched, r.Mapped, r.Skipped, r.Items, r.Cells);
            // 매퍼 경고는 모니터 error 컬럼에 넣지 않는다(OK 인 실행) — 여기 로그로만 남긴다.
            if (r.Warnings is { Count: > 0 })
                foreach (var w in r.Warnings) Log.LogWarning("DP sync {Source}: {Warning}", r.Source, w);
        }
        else Log.LogWarning("DP sync {Source} failed: {Err}", r.Source, r.Error);
    }
}
