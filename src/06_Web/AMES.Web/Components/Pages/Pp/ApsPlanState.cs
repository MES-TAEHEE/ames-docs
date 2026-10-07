using System.Text.Json;
using AMES.Data.Aps;
using AMES.Data.Aps.Contracts;
using AMES.Data.Aps.Domain;
using AMES.Data.Repositories;

namespace AMES.Web.Components.Pages.Pp;

/// <summary>
/// PP-APS 한 번의 조회~저장 사이의 편집 상태. 페이지 필드로만 산다(회로 메모리) — 페이지를 떠나면 사라진다.
/// Bundle 이 편집 정본(Supply / PlanShifts(교대별, 2026-10-07 — PlanDay/PlanNight 는 파생) / Locked 를 직접 고친다)이고 Result·Loads·Traces 는 그 파생값이다.
/// </summary>
public sealed class ApsPlanState
{
    public required ApsQuery Query { get; init; }
    public required ApsBuild Build { get; init; }
    public PlanBundle Bundle => Build.Bundle;

    public PlanResult? Result { get; private set; }
    public List<LoadRow> Loads { get; private set; } = new();
    public List<Trace> Traces { get; private set; } = new();               // Explain 또는 RescheduleWithTraces
    public List<AutofillTrace> AutofillTraces { get; private set; } = new();
    public List<string> ScheduleWarnings { get; private set; } = new();

    public int? SavedRunId { get; set; }
    public bool IsReleased { get; set; }
    /// <summary>실행 이력에서 재현한 상태 — 읽기 전용(편집·저장 불가, 「현재 데이터로 재계산」만).</summary>
    public bool Restored { get; set; }
    public bool Dirty { get; set; }
    public bool ReadOnly => Restored || IsReleased;

    public IEnumerable<string> AllWarnings => Build.Warnings.Concat(ScheduleWarnings);

    /// <summary>등록 계획(기존 WO 슬롯)으로 잠긴 셀 — BuildBundle 직후 Locked==true 인 셀의 스냅샷. 사용자가 풀 수 없다(스펙 §4.5).</summary>
    public HashSet<(string Kind, string PartNo, string Date)> PlanLocked { get; } = new();
    public bool IsPlanLocked(string kind, string partNo, string date) => PlanLocked.Contains((kind, partNo, date));

    Dictionary<(string Kind, string PartNo, string Date), List<ApsRegisteredWo>>? _wos;
    /// <summary>
    /// 셀의 Work Order 줄 — Qty = 배치된(슬롯 있는) WO 수량, Unplaced = 슬롯 없는 APS WO 수량(PP-LSB 미배치), 툴팁(WO 번호 × 수량, 부모 품번 경유·미배치 표시).
    /// 복원 실행도 현재 WO 를 보인다(Build 가 fresh).
    /// </summary>
    public (double Qty, double Unplaced, string Title, int Count) WoIssued(string kind, string partNo, string date, string unplacedLabel)
    {
        _wos ??= Build.RegisteredWos.GroupBy(w => (w.Kind.ToUpperInvariant(), w.ItemNo.ToUpperInvariant(), w.Date))
                                    .ToDictionary(g => g.Key, g => g.OrderBy(w => w.Unplaced).ThenBy(w => w.WoNumber, StringComparer.Ordinal).ToList());
        if (!_wos.TryGetValue((kind.ToUpperInvariant(), partNo.ToUpperInvariant(), date), out var list)) return (0, 0, "", 0);
        var title = string.Join("\n", list.Select(w => $"{w.WoNumber} × {w.Qty:0.###}"
                                                     + (string.Equals(w.SlotItemNo, partNo, StringComparison.OrdinalIgnoreCase) ? "" : $" ({w.SlotItemNo})")
                                                     + (w.Unplaced ? $" — {unplacedLabel}" : "")));
        return (list.Where(w => !w.Unplaced).Sum(w => w.Qty), list.Where(w => w.Unplaced).Sum(w => w.Qty), title, list.Count);
    }

    /// <summary>근거 drawer 가 보는 셀 키 (Kind = ApsRepository.KindAsm / KindInj).</summary>
    public (string Kind, string ItemNo, string Date)? SelectedCell { get; set; }

    public static ApsPlanState Create(ApsQuery q, ApsBuild build)
    {
        var s = new ApsPlanState { Query = q, Build = build };
        s.SnapshotPlanLocks();
        s.Compute();
        return s;
    }

    void SnapshotPlanLocks()
    {
        PlanLocked.Clear();
        foreach (var r in Bundle.Assembly)
            foreach (var d in r.Days.Where(d => d.Locked))
                PlanLocked.Add((ApsRepository.KindAsm, r.PartNo, d.Date));
        foreach (var r in Bundle.Injection)
            foreach (var d in r.Days.Where(d => d.Locked))
                PlanLocked.Add((ApsRepository.KindInj, r.PartNo, d.Date));
    }

    void ComputeCore()
    {
        var (result, loads) = PlanCalc.Compute(Bundle, Build.Rules, Build.Stages, Build.Options);
        Result = result;
        Loads  = loads;
    }

    /// <summary>편집 후 재계산 — PlanCalc.Compute + Explain(계획 불변, 근거만).</summary>
    public void Compute()
    {
        ComputeCore();
        Traces = InjectionScheduler.Explain(Bundle, Build.Rules, Build.Stages);
    }

    /// <summary>공급 자동 계산 → 사출 재배분 → 재계산. 잠긴 칸은 엔진이 건드리지 않는다.</summary>
    public void RunAutofill()
    {
        var (w1, autofill) = Autofill.RunWithTraces(Bundle, Build.Rules, Build.Stages, Build.Options);
        var (w2, traces)   = InjectionScheduler.RescheduleWithTraces(Bundle, Build.Rules, Build.Stages);
        AutofillTraces   = autofill;
        ScheduleWarnings = w1.Concat(w2).ToList();
        ComputeCore();
        Traces = traces;
        Dirty  = true;
    }

    /// <summary>사출 재배분만(완제품 공급은 그대로) → 재계산.</summary>
    public void Reschedule()
    {
        var (w, traces) = InjectionScheduler.RescheduleWithTraces(Bundle, Build.Rules, Build.Stages);
        ScheduleWarnings = w;
        ComputeCore();
        Traces = traces;
        Dirty  = true;
    }

    /// <summary>PP_ApsRun.ResultJson 의 모양 — PlanResult + loads + traces + autofillTraces + warnings (스펙 §3.4).</summary>
    public sealed class ResultSnapshot
    {
        public PlanResult Result { get; set; } = new();
        public List<LoadRow> Loads { get; set; } = new();
        public List<Trace> Traces { get; set; } = new();
        public List<AutofillTrace> AutofillTraces { get; set; } = new();
        public List<string> ScheduleWarnings { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    /// <summary>저장용 — ApsJson.Options 로 JSON 3개 + ApsPlanLines.From 정규화 사본.</summary>
    public ApsRepository.ApsRunSave ToSave()
    {
        if (Result is null) Compute();
        var snap = new ResultSnapshot
        {
            Result = Result!, Loads = Loads, Traces = Traces, AutofillTraces = AutofillTraces,
            ScheduleWarnings = ScheduleWarnings, Warnings = Build.Warnings,
        };
        return new ApsRepository.ApsRunSave(
            Query,
            JsonSerializer.Serialize(Build.Settings, ApsJson.Options),
            JsonSerializer.Serialize(Bundle, ApsJson.Options),
            JsonSerializer.Serialize(snap, ApsJson.Options),
            ApsPlanLines.From(Bundle, Result!),
            AllWarnings.Count());
    }

    /// <summary>실행 이력 재현 — BundleJson/ResultJson 역직렬화, 재계산 없음. fresh 는 Rules·Stages·Calendar·Actuals 를 빌려 오는 데만 쓴다.</summary>
    public static ApsPlanState Restore(ApsQuery q, ApsRepository.ApsRunDetail run, ApsBuild fresh)
    {
        var bundle   = JsonSerializer.Deserialize<PlanBundle>(run.BundleJson, ApsJson.Options) ?? new PlanBundle();
        var settings = JsonSerializer.Deserialize<Settings>(run.SettingsJson, ApsJson.Options) ?? Settings.Default();
        // 구 실행(PlanShifts 없음)을 교대 목록 모드로 — Day→첫 교대·Night→둘째 교대(ApsShiftCompat.Restore). 저장본에 교대 목록이 없으면 그대로(종전 주/야 표시)
        foreach (var r in bundle.Injection)
        {
            var shifts = settings.LineShifts.FirstOrDefault(l => string.Equals(l.LineCd, r.LineCd, StringComparison.OrdinalIgnoreCase))?.Shifts;
            if (shifts is not { Count: > 0 }) continue;
            foreach (var d in r.Days)
                if (d.PlanShifts is not { Count: > 0 }) d.PlanShifts = ApsShiftCompat.Restore(d.PlanDay, d.PlanNight, shifts);
        }
        var snap     = JsonSerializer.Deserialize<ResultSnapshot>(run.ResultJson, ApsJson.Options) ?? new ResultSnapshot();
        var build    = fresh with { Bundle = bundle, Settings = settings, Warnings = snap.Warnings, PlanDemand = null };
        var s = new ApsPlanState
        {
            Query = q, Build = build,
            SavedRunId = run.Row.RunId,
            IsReleased = string.Equals(run.Row.Status, ApsRepository.StatusReleased, StringComparison.OrdinalIgnoreCase),
            Restored   = true,
        };
        s.Result = snap.Result; s.Loads = snap.Loads; s.Traces = snap.Traces;
        s.AutofillTraces = snap.AutofillTraces; s.ScheduleWarnings = snap.ScheduleWarnings;
        s.SnapshotPlanLocks();   // 저장본의 Locked 은 등록계획+사용자 잠금이 섞여 있지만 재현은 읽기 전용이라 구분이 필요 없다
        return s;
    }
}
