using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Data.Services;
using AMES.Data.Services.SapIf;

namespace AMES.Api.Workers.SapOut;

/// <summary>
/// SAP B1 송신 Worker(AMES 아웃박스 → EOS_IF.IF_TX_* → 웅진 → B1 문서). 대상 = IF 종류별(생산오더·투입·실적·입고·이동·출하·조정).
/// 설정은 공통코드 SW_SAPOUT(INTERVAL 분 — 0 이면 전체 중지, BATCH, MAX_RETRY, <b>DRYRUN 기본 1</b>).
/// DryRun 이면 EOS_IF 에 쓰지 않고 보낼 행·변환 결과만 아웃박스 메모와 SAP_IfLog 에 남긴다 — 운영 B1 에 실제 문서가 생기므로
/// SAP 팀과 시점을 맞춘 뒤 0 으로 바꾼다. EOS_IF 접속문자열이 없으면 조용히 멈춘다(SAPIN 과 같다).
/// </summary>
public sealed class SapOutWorker(AmesConnectionFactory f, EosIfConnectionFactory eos, SapIfOutboxRepository outbox,
        SapIfLogRepository sapLog, IConfiguration cfg, ILogger<SapOutWorker> log)
    : ScheduledWorker<SapOutTarget, SapOutRunResult>(f, cfg, log)
{
    public const string MonitorCode = "SAPOUT";
    private const int DefaultIntervalMin = 1;
    private const int DefaultBatch = 50;
    private const int DefaultMaxRetry = 8;
    private readonly SapOutRunner _runner = new(outbox, new SapOutSender(eos), new EosIfConnectionFactoryAccessor(eos), sapLog);
    private readonly SapOutCollector _collector = new(f, sapLog);
    private bool _loggedUnconfigured;

    protected override string Code => MonitorCode;
    protected override string Name => "SAP 송신";
    protected override string Actor => SapIfRules.Actor;

    protected override ScheduledPlan<SapOutTarget> LoadPlan()
    {
        if (!eos.IsConfigured)
        {
            if (!_loggedUnconfigured) { Log.LogInformation("{Code} 비활성 — {Why}", Code, eos.Describe()); _loggedUnconfigured = true; }
            return new ScheduledPlan<SapOutTarget>([], [], Enabled: false);
        }

        using var conn = Factory.OpenConnection();
        var rows = WorkerSourceConfig.Load(conn, MonitorCode);
        var c = WorkerSourceConfig.Resolve(rows, MonitorCode, DefaultIntervalMin);
        int batch = int.TryParse(c.Globals.GetValueOrDefault("BATCH")?.Trim(), out var b) && b > 0 ? b : DefaultBatch;
        int maxRetry = int.TryParse(c.Globals.GetValueOrDefault("MAX_RETRY")?.Trim(), out var m) && m > 0 ? m : DefaultMaxRetry;
        // 행이 없으면 DryRun 으로 본다(안전한 쪽). 실제 송신은 SW_SAPOUT.DRYRUN 의 Attribute1 을 0 으로 바꿀 때만.
        bool dry = !(c.Globals.TryGetValue("DRYRUN", out var d) && d?.Trim() == "0");

        var targets = SapOutSpecs.All.Select(s => new SapOutTarget(s, c.GlobalIntervalMin, batch, maxRetry, dry)).ToList();
        return new ScheduledPlan<SapOutTarget>(targets, [], Enabled: c.GlobalIntervalMin != 0, c.TickSec, c.StartupDelaySec);
    }

    protected override string KeyOf(SapOutTarget t) => t.Spec.IfId;
    protected override int IntervalMinOf(SapOutTarget t) => t.IntervalMin;

    protected override async Task<SapOutRunResult> RunTargetAsync(SapOutTarget t, CancellationToken ct)
    {
        // 한 주기에 한 번(첫 IF) — 현장에서 확정된 투입·실적·종료·취소를 아웃박스에 적재한다. 실패해도 송신은 계속한다.
        if (t.Spec.IfId == SapOutSpecs.All[0].IfId)
        {
            var c = await Task.Run(() => _collector.Collect(ct), ct);
            if (c.Total > 0) Log.LogInformation("SAP out collect: 투입 {I} 실적 {R} 불량 {D} 종료 {C} 취소 {X} 출하 {S}", c.Issues, c.Receipts, c.Rejects, c.Closes, c.Cancels, c.Deliveries);
            if (c.Error is not null) Log.LogWarning("SAP out collect failed: {Err}", c.Error);
        }
        var result = await Task.Run(() => _runner.Run(t, ct), ct);
        RecordMonitor(t, result);
        return result;
    }

    private void RecordMonitor(SapOutTarget t, SapOutRunResult r)
    {
        try
        {
            new SysRepository(Factory).UpsertInterfaceMonitor(
                WorkerSourceConfig.InterfaceCodeFor(Code, t.Spec.IfId),
                $"SAP 송신 {t.Spec.IfId} {t.Spec.Name}",
                $"EOS_IF.dbo.{t.Spec.HdrTable}", 30,
                ok: r.Ok, recordCount: r.Sent,
                error: r.Ok ? (r.DryRun ? "DRYRUN(EOS_IF 에 쓰지 않음) " + r.Note : r.Note) : r.Error,
                actor: SapIfRules.Actor, direction: "OUTBOUND");
        }
        catch (Exception ex) { Log.LogWarning(ex, "{Code} monitor upsert failed", Code); }
    }

    protected override SapOutRunResult RecordConfigError(TargetConfigError e) => new(e.Key, false, 0, 0, 0, 0, true, e.Message);
    protected override SapOutRunResult Skipped(SapOutTarget t, string reason) => new(t.Spec.IfId, false, 0, 0, 0, 0, t.DryRun, reason);
    protected override bool IsOk(SapOutRunResult r) => r.Ok;

    protected override void LogResult(SapOutTarget t, SapOutRunResult r)
    {
        if (r.Ok) Log.LogInformation("SAP out {If}: due {D} sent {S} refreshed {R}{Dry}", r.IfId, r.Due, r.Sent, r.Refreshed, r.DryRun ? " (dry-run)" : "");
        else Log.LogWarning("SAP out {If} failed: due {D} sent {S} failed {F} {Err}", r.IfId, r.Due, r.Sent, r.Failed, r.Error);
    }
}
