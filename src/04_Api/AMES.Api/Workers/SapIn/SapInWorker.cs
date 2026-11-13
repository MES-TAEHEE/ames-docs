using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Data.Services;
using AMES.Data.Services.SapIf;

namespace AMES.Api.Workers.SapIn;

/// <summary>
/// SAP B1 마스터 수신 Worker(B1 → 웅진 → EOS_IF.IF_MD_* → AMES MD_*). 대상 = 구현된 마스터 IF(단위·고객·공급사·로케이션).
/// 설정은 공통코드 SW_SAPIN(INTERVAL 분 — 0 이면 전체 중지, BATCH, DRYRUN). 접속은 환경변수 ConnectionStrings__EOS_IF.
/// <b>DRYRUN 기본 1</b>: MD_* 반영을 롤백하고 EOS_IF 에 되쓰지 않는다 — 운영 EOS_IF 라서 웅진과 시점을 맞춘 뒤 0 으로 바꾼다.
/// EOS_IF 접속문자열이 없으면 이 Worker 는 조용히 멈춘다(개발 PC 가 공유 DB 의 모니터 행을 오류로 더럽히지 않게).
/// </summary>
public sealed class SapInWorker(AmesConnectionFactory f, EosIfConnectionFactory eos, SapIfLogRepository sapLog,
        IConfiguration cfg, ILogger<SapInWorker> log)
    : ScheduledWorker<SapInTarget, SapInRunResult>(f, cfg, log)
{
    public const string MonitorCode = "SAPIN";
    private const int DefaultIntervalMin = 5;
    private const int DefaultBatch = 100;
    private readonly SapInRunner _runner = new(f, eos, sapLog);
    private bool _loggedUnconfigured;

    protected override string Code  => MonitorCode;
    protected override string Name  => "SAP 수신";
    protected override string Actor => SapIfRules.Actor;

    protected override ScheduledPlan<SapInTarget> LoadPlan()
    {
        if (!eos.IsConfigured)
        {
            if (!_loggedUnconfigured) { Log.LogInformation("{Code} 비활성 — {Why}", Code, eos.Describe()); _loggedUnconfigured = true; }
            return new ScheduledPlan<SapInTarget>([], [], Enabled: false);
        }

        using var conn = Factory.OpenConnection();
        var rows = WorkerSourceConfig.Load(conn, MonitorCode);
        var cfgResult = WorkerSourceConfig.Resolve(rows, MonitorCode, DefaultIntervalMin);
        int batch = int.TryParse(cfgResult.Globals.GetValueOrDefault("BATCH")?.Trim(), out var b) && b > 0 ? b : DefaultBatch;
        // 행이 없으면 DryRun 으로 본다(안전한 쪽). 끄려면 SW_SAPIN.DRYRUN 의 Attribute1 을 0 으로.
        bool dry = !(cfgResult.Globals.TryGetValue("DRYRUN", out var d) && d?.Trim() == "0");

        var targets = SapInMasters.All.Select(s => new SapInTarget(s, cfgResult.GlobalIntervalMin, batch, dry)).ToList();
        return new ScheduledPlan<SapInTarget>(targets, [], Enabled: cfgResult.GlobalIntervalMin != 0,
            cfgResult.TickSec, cfgResult.StartupDelaySec);
    }

    protected override string KeyOf(SapInTarget t) => t.Spec.IfId;
    protected override int IntervalMinOf(SapInTarget t) => t.IntervalMin;

    protected override async Task<SapInRunResult> RunTargetAsync(SapInTarget t, CancellationToken ct)
    {
        var result = await Task.Run(() => _runner.Run(t, ct), ct);
        RecordMonitor(t, result);
        return result;
    }

    private void RecordMonitor(SapInTarget t, SapInRunResult r)
    {
        try
        {
            // 정의서 2.3: N 행 30분 체류를 이상으로 본다 → 최대 간격 30분.
            new SysRepository(Factory).UpsertInterfaceMonitor(
                WorkerSourceConfig.InterfaceCodeFor(Code, t.Spec.IfId),
                $"SAP 수신 {t.Spec.IfId} {t.Spec.Name}",
                $"EOS_IF.dbo.{t.Spec.EosTable}", 30,
                ok: r.Ok, recordCount: r.Applied,
                error: r.Ok ? (r.DryRun ? "DRYRUN(반영 안 함) " + r.Note : r.Note) : r.Error,
                actor: SapIfRules.Actor, direction: "INBOUND");
        }
        catch (Exception ex) { Log.LogWarning(ex, "{Code} monitor upsert failed", Code); }
    }

    protected override SapInRunResult RecordConfigError(TargetConfigError e)
        => new(e.Key, false, 0, 0, 0, true, e.Message);

    protected override SapInRunResult Skipped(SapInTarget t, string reason)
        => new(t.Spec.IfId, false, 0, 0, 0, t.DryRun, reason);

    protected override bool IsOk(SapInRunResult r) => r.Ok;

    protected override void LogResult(SapInTarget t, SapInRunResult r)
    {
        if (r.Ok) Log.LogInformation("SAP in {If}: read {R} applied {A}{Dry}", r.IfId, r.Read, r.Applied, r.DryRun ? " (dry-run)" : "");
        else      Log.LogWarning("SAP in {If} failed: read {R} applied {A} failed {F} {Err}", r.IfId, r.Read, r.Applied, r.Failed, r.Error);
    }
}
