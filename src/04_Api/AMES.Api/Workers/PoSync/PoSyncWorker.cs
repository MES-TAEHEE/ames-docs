using AMES.Data.Connection;
using AMES.Data.Services.PoSync;

namespace AMES.Api.Workers.PoSync;

/// <summary>
/// 고객사 SRM 구매오더(MM31006 INQUERY) 수집 Worker. 대상 = 공통코드 SW_POSYNC_SOURCE 한 행(고객사).
/// 전역 SW_POSYNC.INTERVAL=0 이면 스케줄러가 멈춘다(수동 실행은 가능).
/// 틱 간격·시작 지연·HTTP 타임아웃은 SW_POSYNC.TICK_SEC / STARTUP_DELAY_SEC / TIMEOUT_SEC, 없으면 appsettings ScheduledWorker.
/// </summary>
public sealed class PoSyncWorker(AmesConnectionFactory f, IPoSource source, IConfiguration cfg, ILogger<PoSyncWorker> log)
    : ScheduledWorker<PoSyncSource, PoSyncRunResult>(f, cfg, log)
{
    private readonly PoSyncRunner _runner = new(f, source);

    protected override string Code  => PoSyncConfig.MonitorCode;
    protected override string Name  => "PO Sync";
    protected override string Actor => PoSyncRunner.Actor;

    protected override ScheduledPlan<PoSyncSource> LoadPlan()
    {
        using var conn = Factory.OpenConnection();
        var config = PoSyncConfig.Load(conn);
        return new ScheduledPlan<PoSyncSource>(
            config.Sources,
            config.Errors.Select(e => new TargetConfigError(e.Key, e.Name, e.Message)).ToList(),
            Enabled: config.GlobalIntervalMin != 0,
            config.TickSec, config.StartupDelaySec);
    }

    protected override string KeyOf(PoSyncSource s) => s.Key;
    protected override int IntervalMinOf(PoSyncSource s) => s.IntervalMin;

    protected override Task<PoSyncRunResult> RunTargetAsync(PoSyncSource s, CancellationToken ct)
        => _runner.RunAsync(s, ct);

    protected override PoSyncRunResult RecordConfigError(TargetConfigError e)
        => _runner.RecordConfigError(new PoSyncConfigError(e.Key, e.Name, e.Message));

    protected override PoSyncRunResult Skipped(PoSyncSource s, string reason)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return new PoSyncRunResult(s.Key, false, 0, 0, 0, 0, 0, reason,
            today.AddDays(s.WindowFrom), today.AddDays(s.WindowTo));
    }

    protected override bool IsOk(PoSyncRunResult r) => r.Ok;

    protected override void LogResult(PoSyncSource s, PoSyncRunResult r)
    {
        if (r.Ok) Log.LogInformation("PO sync {Source}: fetched {F} mapped {M} skipped {S} ins {I} upd {U}",
                      r.Source, r.Fetched, r.Mapped, r.Skipped, r.Inserted, r.Updated);
        else      Log.LogWarning("PO sync {Source} failed: {Err}", r.Source, r.Error);
    }
}
