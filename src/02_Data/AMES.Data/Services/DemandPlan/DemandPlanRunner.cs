using AMES.Data.Connection;
using AMES.Data.Repositories;

namespace AMES.Data.Services.DemandPlan;

public interface IDemandPlanSource
{
    Task<SrmMipResponse> FetchAsync(DemandPlanSource source, DateOnly planDate, CancellationToken ct);
}

public sealed record DemandPlanRunResult(string Source, bool Ok, int Fetched, int Mapped, int Skipped, int Items, int Cells, int Unmatched, int PackMismatch,
                                         DateOnly? From, DateOnly? To, string? Error, IReadOnlyList<string>? Warnings = null);

/// <summary>
/// 소스 1개 실행: fetch → map → PP_DemandPlan 창 교체 → SYS_InterfaceMonitor 기록. PoSyncRunner 와 같은 원칙 —
/// 호출자 취소만 밖으로 내고 그 외 실패는 모니터 행에만 남긴다(무인 루프에서 한 소스 실패가 다른 소스를 막지 않게).
/// 매핑 결과 0셸(<c>Cells.Count == 0</c>)이면 DB 를 건드리지 않는다 — 응답이 아예 비었든(Rows==0), 응답의 행이
/// 있어도 전부 걸러졌든(잘못된 PARTNO·전 날짜 0/음수 수량) 창을 비우면 안 된다. Rows==0 만 보면 후자를 놓친다.
/// </summary>
public sealed class DemandPlanRunner(AmesConnectionFactory f, IDemandPlanSource source)
{
    public const string Actor = "DP-SYNC";

    private readonly PpRepository  _pp  = new(f);
    private readonly SysRepository _sys = new(f);

    public async Task<DemandPlanRunResult> RunAsync(DemandPlanSource s, CancellationToken ct)
    {
        var planDate = DbClock.Today;
        try
        {
            var response = await source.FetchAsync(s, DateOnly.FromDateTime(planDate), ct);
            var mapped   = SrmMipMapper.Map(response);

            if (mapped.Cells.Count == 0)
            {
                _sys.UpsertInterfaceMonitor(s.InterfaceCode, s.Name, s.Url, s.IntervalMin, ok: true,
                    recordCount: 0, error: null, actor: Actor);
                return new DemandPlanRunResult(s.Key, true, response.Data?.Count ?? 0, mapped.Rows, mapped.Skipped, 0, 0, 0, 0, null, null, null, mapped.Warnings);
            }

            var batch  = "DS" + DbClock.Now.ToString("yyyyMMddHHmmssfff");
            var import = _pp.ReplaceDemandPlan(s.CustomerId, batch, PpRepository.DemandPlanSourceSrm, s.Key, null,
                mapped.From, mapped.To, mapped.Cells, Actor);

            // 매퍼 경고(dates 파싱·PARTNO 제외·음수 등)는 OK 인 실행이므로 모니터 error 에는 넣지 않는다 —
            // Worker.LogResult 가 Warning 레벨 로그로만 남긴다.
            _sys.UpsertInterfaceMonitor(s.InterfaceCode, s.Name, s.Url, s.IntervalMin, ok: true,
                recordCount: mapped.Cells.Count, error: null, actor: Actor);
            return new DemandPlanRunResult(s.Key, true, response.Data?.Count ?? 0, mapped.Rows, mapped.Skipped,
                import.ItemCount, mapped.Cells.Count, import.UnmatchedItems, import.PackMismatch, import.From, import.To, null, mapped.Warnings);
        }
        // HttpClient 타임아웃은 TaskCanceledException(OperationCanceledException 파생)이라
        // 호출자 취소(ct)와 구분해야 한다 — ct 가 취소되지 않았으면 타임아웃/기타 실패로 보고 ERROR 기록.
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            var msg = ex.GetType().Name + ": " + ex.Message;
            TryRecord(s.InterfaceCode, s.Name, s.Url, s.IntervalMin, msg);
            return new DemandPlanRunResult(s.Key, false, 0, 0, 0, 0, 0, 0, 0, null, null, msg);
        }
    }

    /// <summary>설정이 깨진 소스도 모니터에 ERROR 로 보이게 한다 — 그래야 등록 실수를 화면에서 알아챈다.</summary>
    public DemandPlanRunResult RecordConfigError(WorkerConfigError e)
    {
        TryRecord(DemandPlanConfig.InterfaceCodeFor(e.Key), e.Name, "", 0, "설정 오류: " + e.Message);
        return new DemandPlanRunResult(e.Key, false, 0, 0, 0, 0, 0, 0, 0, null, null, "설정 오류: " + e.Message);
    }

    // 모니터 기록 자체가 실패(DB 다운)하면 삼킨다 — 호출자는 이미 실패 결과를 들고 있다.
    private void TryRecord(string code, string name, string url, int gap, string error)
    {
        try { _sys.UpsertInterfaceMonitor(code, name, url, gap, ok: false, recordCount: null, error: error, actor: Actor); }
        catch { /* 의도적 무시 */ }
    }
}
