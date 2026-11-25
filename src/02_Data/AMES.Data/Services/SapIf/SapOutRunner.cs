using System.Diagnostics;
using AMES.Data.Repositories;

namespace AMES.Data.Services.SapIf;

public sealed record SapOutTarget(SapOutSpec Spec, int IntervalMin, int Batch, int MaxRetry, bool DryRun);

public sealed record SapOutRunResult(
    string IfId, bool Ok, int Due, int Sent, int Failed, int Refreshed, bool DryRun, string? Error, string? Note = null);

/// <summary>
/// 아웃박스의 한 IF 를 EOS_IF 로 보낸다. ① 앞서 보낸 행의 웅진 결과(Y/E, B1 문서번호)를 회수 ② 보낼 차례인 행(순서 게이트 통과)을 전송.
/// DryRun 이면 EOS_IF 에 아무것도 쓰지 않고(결과 회수만 한다) 보낼 행과 변환 결과를 아웃박스 LastError 메모와 SAP_IfLog 에 남긴다.
/// 한 행의 실패는 다른 행을 막지 않는다 — 실패는 백오프 뒤 재시도, 재시도 초과는 E.
/// </summary>
public sealed class SapOutRunner(SapIfOutboxRepository outbox, SapOutSender sender, EosIfConnectionFactoryAccessor eosAccess, SapIfLogRepository log)
{
    public SapOutRunResult Run(SapOutTarget t, CancellationToken ct = default)
    {
        var spec = t.Spec;
        var runId = Guid.NewGuid();
        var sw = Stopwatch.StartNew();
        int due = 0, sent = 0, failed = 0, refreshed = 0;
        try
        {
            refreshed = RefreshResults(runId, spec, sw);

            if (t.DryRun)
            {
                var rows = outbox.PeekDue(spec.IfId, t.Batch);
                due = rows.Count;
                foreach (var r in rows)
                {
                    ct.ThrowIfCancellationRequested();
                    string note;
                    try
                    {
                        var p = SapOutPayload.Parse(spec, r.PayloadJson);
                        note = $"[DRYRUN] {spec.HdrTable} {p.Hdr[spec.KeyCol]} 액션 {r.Action} 헤더 {p.Hdr.Count}컬럼 라인 {p.Lines.Count}건 — EOS_IF 에 쓰지 않음";
                    }
                    catch (Exception ex) { note = "[DRYRUN] 변환 오류 — " + SapIfRules.ErrorMessage(ex); failed++; }
                    if (outbox.SetDryNote(r.OutboxID, note))
                        log.Write(runId, spec.IfId, "OUT", r.MesKey, null, r.Action, note.Contains("오류") ? "FAIL" : "OK", note);
                }
                return new SapOutRunResult(spec.IfId, failed == 0, due, 0, failed, refreshed, true, null,
                    $"보낼 차례 {due}건(DryRun) · 결과 회수 {refreshed}건");
            }

            var claimed = outbox.ClaimDue(spec.IfId, t.Batch);
            due = claimed.Count;
            foreach (var r in claimed)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var payload = SapOutPayload.Parse(spec, r.PayloadJson);
                    var seq = sender.Send(spec, r.Action, r.EventAt, payload);
                    outbox.MarkSent(r.OutboxID, seq);
                    sent++;
                    log.Write(runId, spec.IfId, "OUT", r.MesKey, seq, r.Action, "OK",
                        $"EOS_IF {spec.HdrTable} IF_SEQ {seq} 기록(헤더 {payload.Hdr.Count}컬럼 · 라인 {payload.Lines.Count}건)", null, (int)sw.ElapsedMilliseconds);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failed++;
                    int retry = r.RetryCount + 1;
                    bool final = retry >= t.MaxRetry;
                    var msg = SapIfRules.ErrorMessage(ex);
                    outbox.MarkFailed(r.OutboxID, retry, final ? null : DateTime.Now.AddMinutes(SapIfRules.BackoffMinutes(retry)), msg, final);
                    log.Write(runId, spec.IfId, "OUT", r.MesKey, null, r.Action, "FAIL",
                        $"전송 실패 {retry}/{t.MaxRetry}{(final ? " — 재시도 초과(E)" : "")}: {msg}", null, (int)sw.ElapsedMilliseconds);
                }
            }
            return new SapOutRunResult(spec.IfId, failed == 0, due, sent, failed, refreshed, false,
                failed == 0 ? null : $"{failed}건 전송 실패(재시도 대기 또는 E)", $"전송 {sent}건 · 결과 회수 {refreshed}건");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var msg = SapIfRules.ErrorMessage(ex);
            log.Write(runId, spec.IfId, "OUT", null, null, null, "FAIL", "송신 실행 실패: " + msg, null, (int)sw.ElapsedMilliseconds);
            return new SapOutRunResult(spec.IfId, false, due, sent, failed, refreshed, t.DryRun, msg);
        }
    }

    private static string Cut(string? s) => s is null ? "" : s.Length <= 1000 ? s : s[..1000];

    /// <summary>웅진이 되쓴 IF_STATUS(Y/E)·B1 문서번호를 아웃박스 B1Status 로 옮긴다(N/P 는 아직 처리 중이라 그대로).</summary>
    private int RefreshResults(Guid runId, SapOutSpec spec, Stopwatch sw)
    {
        var pending = outbox.PendingResults(spec.IfId);
        if (pending.Count == 0) return 0;
        int n = 0;
        using var eosConn = eosAccess.Open();
        foreach (var (id, seq, curStatus, curMsg) in pending)
        {
            var r = sender.ReadResult(eosConn, spec, seq);
            if (r is null || r.Status is not ("Y" or "E")) continue;
            // 이미 같은 오류(E + 같은 메시지)로 기록된 건은 다시 쓰지도 로그에 남기지도 않는다 — 1분마다 같은 실패가 쌓이지 않게
            if (r.Status == "E" && curStatus == "E" && string.Equals(Cut(r.Msg), curMsg ?? "", StringComparison.Ordinal)) continue;
            outbox.UpdateB1(id, r.Status, r.DocEntry, r.DocNum, r.Msg);
            n++;
            log.Write(runId, spec.IfId, "OUT", null, seq, null, r.Status == "Y" ? "OK" : "FAIL",
                r.Status == "Y" ? $"B1 반영 완료 DocEntry {r.DocEntry} DocNum {r.DocNum}" : "B1 반영 오류: " + r.Msg, null, (int)sw.ElapsedMilliseconds);
        }
        return n;
    }
}

/// <summary>Runner 가 EOS_IF 연결만 열 수 있게 하는 얇은 접근자(테스트에서 대체 가능).</summary>
public sealed class EosIfConnectionFactoryAccessor(Connection.EosIfConnectionFactory factory)
{
    public Microsoft.Data.SqlClient.SqlConnection Open() => factory.OpenConnection();
}
