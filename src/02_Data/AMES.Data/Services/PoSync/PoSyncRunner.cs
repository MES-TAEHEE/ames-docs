using AMES.Data.Connection;
using AMES.Data.Repositories;

namespace AMES.Data.Services.PoSync;

public sealed record PoSyncRunResult(string Source, bool Ok, int Fetched, int Mapped, int Skipped,
    int Inserted, int Updated, string? Error, DateOnly From, DateOnly To);

/// <summary>
/// 소스 1개 실행: fetch → map → PP_CustomerOrder 업서트 → SYS_InterfaceMonitor 기록.
/// 호출자의 토큰이 취소된 OperationCanceledException 만 밖으로 낸다 — 그 외의 예외는 무인 루프에서
/// 한 소스의 실패가 다른 소스를 막으면 안 되므로 모니터 행(SYS-Interfaces 화면)에만 남긴다.
/// 매핑 후 0행은 정상이다.
/// </summary>
public sealed class PoSyncRunner(AmesConnectionFactory f, IPoSource source)
{
    public const string Actor = "PO-SYNC";

    private readonly PpRepository  _pp  = new(f);
    private readonly SysRepository _sys = new(f);

    public async Task<PoSyncRunResult> RunAsync(PoSyncSource s, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var from  = today.AddDays(s.WindowFrom);
        var to    = today.AddDays(s.WindowTo);
        try
        {
            var rows   = await source.FetchAsync(s, from, to, ct);
            var mapped = SrmPoMapper.Map(rows);
            var (ins, upd) = mapped.Rows.Count == 0
                ? (0, 0)
                : _pp.UpsertCustomerOrders(s.CustomerId, mapped.Rows, Actor);

            _sys.UpsertInterfaceMonitor(s.InterfaceCode, s.Name, s.Url, s.IntervalMin, ok: true,
                recordCount: mapped.Rows.Count, error: null, actor: Actor);
            return new PoSyncRunResult(s.Key, true, rows.Count, mapped.Rows.Count, mapped.Skipped, ins, upd, null, from, to);
        }
        // HttpClient 타임아웃은 TaskCanceledException(OperationCanceledException 파생)이라
        // 호출자 취소(ct)와 구분해야 한다 — ct 가 취소되지 않았으면 타임아웃/기타 실패로 보고 ERROR 기록.
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            var msg = ex.GetType().Name + ": " + ex.Message;
            TryRecord(s.InterfaceCode, s.Name, s.Url, s.IntervalMin, msg);
            return new PoSyncRunResult(s.Key, false, 0, 0, 0, 0, 0, msg, from, to);
        }
    }

    /// <summary>설정이 깨진 소스도 모니터에 ERROR 로 보이게 한다 — 그래야 등록 실수를 화면에서 알아챈다.</summary>
    public PoSyncRunResult RecordConfigError(PoSyncConfigError e)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        TryRecord(PoSyncConfig.InterfaceCodeFor(e.Key), e.Name, "", 0, "설정 오류: " + e.Message);
        return new PoSyncRunResult(e.Key, false, 0, 0, 0, 0, 0, "설정 오류: " + e.Message, today, today);
    }

    // 모니터 기록 자체가 실패(DB 다운)하면 삼킨다 — 호출자는 이미 실패 결과를 들고 있다.
    private void TryRecord(string code, string name, string url, int gap, string error)
    {
        try { _sys.UpsertInterfaceMonitor(code, name, url, gap, ok: false, recordCount: null, error: error, actor: Actor); }
        catch { /* 의도적 무시 */ }
    }
}
