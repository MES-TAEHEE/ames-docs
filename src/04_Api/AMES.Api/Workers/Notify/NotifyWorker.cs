using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Data.Services.Notify;

namespace AMES.Api.Workers.Notify;

public sealed record NotifyTarget(string Key, NotifyConfigResult Config);

public sealed record NotifyRunResult(bool Ok, int Events, int Created, int Quiet, int Sent, int Failed, string? Error);

/// <summary>
/// PUSH 알림 발송기 — 매 주기 ① 이벤트 감지·수신자 기기별 발송 이력(PENDING) 생성 ② 보낼 행을 잡아 FCM 으로 발송(SENT / FAILED 재시도).
/// 설정은 공통코드 SW_NOTIFY(INTERVAL·LOOKBACK_MIN·MAX_RETRY·BATCH, 없으면 기본값), FCM 키는 appsettings <c>Notify:FcmServiceAccountPath</c>.
/// 키가 없으면 ①만 하고 이력은 PENDING 으로 쌓인다(앱 목록에는 보임). 결과는 SYS_InterfaceMonitor 의 NOTIFY-PUSH 행.
/// 대상은 하나(PUSH)뿐이다 — 여러 Api 인스턴스가 같은 DB 를 써도 감지는 applock, 발송은 행 단위 잡기(SENDING)로 겹치지 않는다.
/// </summary>
public sealed class NotifyWorker(AmesConnectionFactory f, IPushSender sender, IConfiguration cfg, ILogger<NotifyWorker> log)
    : ScheduledWorker<NotifyTarget, NotifyRunResult>(f, cfg, log)
{
    public const string TargetKey = NotifyRules.Channel;

    protected override string Code  => NotifyRules.MonitorCode;
    protected override string Name  => "PUSH Notify";
    protected override string Actor => NotifyRules.Actor;

    protected override ScheduledPlan<NotifyTarget> LoadPlan()
    {
        using var conn = Factory.OpenConnection();
        var c = NotifyConfig.Load(conn);
        return new ScheduledPlan<NotifyTarget>([new NotifyTarget(TargetKey, c)], [], Enabled: c.IntervalMin != 0, c.TickSec, c.StartupDelaySec);
    }

    protected override string KeyOf(NotifyTarget t) => t.Key;
    protected override int IntervalMinOf(NotifyTarget t) => t.Config.IntervalMin;

    protected override async Task<NotifyRunResult> RunTargetAsync(NotifyTarget t, CancellationToken ct)
    {
        var repo = new NotificationRepository(Factory);
        var monitorCode = Code + "-" + t.Key;
        try
        {
            var enq = repo.EnqueuePush(t.Config.LookbackMin);
            int sent = 0, failed = 0;
            string? lastError = sender.ConfigError;
            if (lastError is null)
            {
                foreach (var item in repo.ClaimDue(t.Config.MaxRetry, t.Config.BatchSize))
                {
                    var r = await sender.SendAsync(item, ct);
                    if (r.Ok) { repo.MarkSent(item.Id); sent++; continue; }
                    failed++;
                    lastError = r.Error;
                    repo.MarkFailed(item.Id, r.Error ?? "unknown", r.Permanent, t.Config.MaxRetry, r.InvalidToken ? item.Token : null);
                }
            }
            var ok = lastError is null || (failed > 0 && sent > 0);   // 일부 기기 실패는 정상 운영으로 본다 — 키 없음·전부 실패만 오류
            var error = lastError is null ? null
                      : sender.ConfigError is not null ? $"{lastError} (대기 중 알림 {enq.Created}건 추가)"
                      : $"{failed}건 실패: {lastError}";
            new SysRepository(Factory).UpsertInterfaceMonitor(monitorCode, "PUSH Notify (FCM)", "fcm.googleapis.com", 10,
                ok: ok, recordCount: sent, error: ok ? null : error, actor: Actor, direction: "OUTBOUND");
            return new NotifyRunResult(ok, enq.Events, enq.Created, enq.Quiet, sent, failed, ok ? null : error);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            var msg = ex.GetType().Name + ": " + ex.Message;
            try
            {
                new SysRepository(Factory).UpsertInterfaceMonitor(monitorCode, "PUSH Notify (FCM)", "fcm.googleapis.com", 10,
                    ok: false, recordCount: null, error: msg, actor: Actor, direction: "OUTBOUND");
            }
            catch { /* 기록 실패는 삼킨다 — 다음 주기가 다시 시도 */ }
            return new NotifyRunResult(false, 0, 0, 0, 0, 0, msg);
        }
    }

    protected override NotifyRunResult RecordConfigError(TargetConfigError e) => new(false, 0, 0, 0, 0, 0, e.Message);

    protected override NotifyRunResult Skipped(NotifyTarget t, string reason) => new(false, 0, 0, 0, 0, 0, reason);

    protected override bool IsOk(NotifyRunResult r) => r.Ok;

    protected override void LogResult(NotifyTarget t, NotifyRunResult r)
    {
        if (r.Created == 0 && r.Sent == 0 && r.Failed == 0 && r.Ok) return;   // 1분마다 도는 작업 — 할 일이 없던 회차는 남기지 않는다
        if (r.Ok) Log.LogInformation("PUSH notify: events {E} created {C} quiet {Q} sent {S} failed {F}", r.Events, r.Created, r.Quiet, r.Sent, r.Failed);
        else      Log.LogWarning("PUSH notify: events {E} created {C} sent {S} failed {F} — {Err}", r.Events, r.Created, r.Sent, r.Failed, r.Error);
    }
}
