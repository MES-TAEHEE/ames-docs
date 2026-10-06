using AMES.Data.Aps;
using AMES.Data.Aps.Contracts;
using AMES.Data.Aps.Domain;
using System.Text.Json;
using Xunit;

namespace AMES.Data.Tests.Aps;

/// 스펙 §2.3·§10.2 — 플래그 3개는 각각 기대 결과를 내고, 전부 false 면 골든 경로와 같다.
public class ApsOptionsTests
{
    static readonly Settings S = Fixtures.LoadSettings();
    static readonly Dictionary<string, LineInfo> L = Fixtures.LoadLines();
    static PlanBundle Lq10() => JsonSerializer.Deserialize<PlanBundle>(Fixtures.Json("plan_LQ10.json")["bundle"]!.ToJsonString(), ApsJson.Options)!;

    /// 합성 마스터: 완제품 라인 L1(assembly) · 사출 라인 M1(injection).
    static readonly Dictionary<string, LineInfo> SynthLines = new(StringComparer.OrdinalIgnoreCase)
    {
        ["L1"] = new LineInfo { LineCd = "L1", Type = "assembly" },
        ["M1"] = new LineInfo { LineCd = "M1", Type = "injection" },
    };

    static PlanBundle Synth(string[] dates, params AssemblyRow[] rows) => new()
    {
        Line = SynthLines["L1"], BaseDate = dates[0], PrevDate = ApsCalendar.PrevDate(dates[0]), Dates = dates.ToList(),
        Assembly = rows.ToList(),
    };

    static AssemblyRow Asm(string partNo, string[] dates, double[] demand, double opening, double safety) => new()
    {
        PartNo = partNo, LineCd = "L1", OpeningStock = opening, SafetyStock = safety,
        Days = dates.Select((d, i) => new AssemblyDay { Date = d, Demand = demand[i] }).ToList(),
    };

    [Fact]
    public void All_false_options_reproduce_golden_path()
    {
        var a = Lq10(); var b = Lq10();
        var ra = PlanCalc.Compute(a, new ShiftRules(S, L), new StageRules(S, L));
        var rb = PlanCalc.Compute(b, new ShiftRules(S, L), new StageRules(S, L), ApsOptions.Default);
        Assert.Equal(JsonSerializer.Serialize(ra.result, ApsJson.Options), JsonSerializer.Serialize(rb.result, ApsJson.Options));
        Assert.Equal(JsonSerializer.Serialize(ra.loads, ApsJson.Options), JsonSerializer.Serialize(rb.loads, ApsJson.Options));

        var ta = Autofill.RunWithTraces(a, new ShiftRules(S, L), new StageRules(S, L));
        var tb = Autofill.RunWithTraces(b, new ShiftRules(S, L), new StageRules(S, L), ApsOptions.Default);
        Assert.Equal(ta.warnings, tb.warnings);
        Assert.Equal(JsonSerializer.Serialize(ta.traces, ApsJson.Options), JsonSerializer.Serialize(tb.traces, ApsJson.Options));
        Assert.Equal(JsonSerializer.Serialize(a.Assembly, ApsJson.Options), JsonSerializer.Serialize(b.Assembly, ApsJson.Options));
    }

    [Fact]
    public void WarnStatus_marks_cells_between_zero_and_safety_stock()
    {
        var dates = new[] { "2026-09-28" };
        var bundle = Synth(dates,
            Asm("A", dates, new double[] { 0 }, opening: 10, safety: 20),    // 0 ≤ 10 < 20 → warn
            Asm("C", dates, new double[] { 0 }, opening: -1, safety: 20));   // 음수 → short (플래그 무관)
        bundle.Injection.Add(new InjectionRow { PartNo = "B", LineCd = "M1", Uph = 60, PackSize = 5, OpeningStock = 5, SafetyStock = 10,
            Days = { new InjectionDay { Date = dates[0] } } });           // 소요 0 → remain 5 < 10 → warn
        var s = Settings.Default();
        var rules = new ShiftRules(s, SynthLines); var stages = new StageRules(s, SynthLines);

        var plain = PlanCalc.Compute(bundle, rules, stages).result;
        Assert.Equal("ok", plain.Assembly[0].Cells[0].Status);
        Assert.Equal("short", plain.Assembly[1].Cells[0].Status);
        Assert.Equal("ok", plain.Injection[0].Cells[0].Status);

        var warn = PlanCalc.Compute(bundle, rules, stages, new ApsOptions(WarnStatus: true)).result;
        Assert.Equal("warn", warn.Assembly[0].Cells[0].Status);
        Assert.Equal("short", warn.Assembly[1].Cells[0].Status);
        Assert.Equal("warn", warn.Injection[0].Cells[0].Status);
        Assert.Equal(1, warn.Summary[0].ShortageCount);   // warn 은 부족 건수에 세지 않는다
    }

    [Fact]
    public void WarnStatus_on_golden_input_only_turns_ok_cells_under_safety_stock_into_warn()
    {
        var bundle = Lq10();
        var plain = PlanCalc.Compute(bundle, new ShiftRules(S, L), new StageRules(S, L));
        var warn = PlanCalc.Compute(bundle, new ShiftRules(S, L), new StageRules(S, L), new ApsOptions(WarnStatus: true));
        Assert.Equal(JsonSerializer.Serialize(plain.loads, ApsJson.Options), JsonSerializer.Serialize(warn.loads, ApsJson.Options));
        Assert.Equal(JsonSerializer.Serialize(plain.result.Summary, ApsJson.Options), JsonSerializer.Serialize(warn.result.Summary, ApsJson.Options));
        for (var k = 0; k < bundle.Assembly.Count; k++)
            foreach (var (p, w) in plain.result.Assembly[k].Cells.Zip(warn.result.Assembly[k].Cells))
            {
                if (p.Status == "short") { Assert.Equal("short", w.Status); continue; }
                if (w.Status == "warn") Assert.True(bundle.Assembly[k].SafetyStock > 0 && w.Stock >= 0 && w.Stock < bundle.Assembly[k].SafetyStock, $"{p.Date} {w.Stock}");
                else Assert.Equal("ok", w.Status);
            }
        for (var k = 0; k < bundle.Injection.Count; k++)
            foreach (var (p, w) in plain.result.Injection[k].Cells.Zip(warn.result.Injection[k].Cells))
            {
                if (p.Status == "short") { Assert.Equal("short", w.Status); continue; }
                if (w.Status == "warn") Assert.True(bundle.Injection[k].SafetyStock > 0 && w.Remain >= 0 && w.Remain < bundle.Injection[k].SafetyStock, $"{p.Date} {w.Remain}");
                else Assert.Equal("ok", w.Status);
            }
    }

    /// ORIG SupplyPlanner.cs:134 — target = max(cover × base, SafetyStock); 마지막 날 trim 은 그대로 0.
    /// Settings.Default(): cover 구간 100/0.4 · 20/0.7 · 0/1.5, roundTo 5, 라인 능력 없음(cap ∞). 일평균 수요 10 → cover 1.5.
    [Fact]
    public void SafetyStockTerm_raises_target_to_safety_stock_except_trimmed_last_day()
    {
        var dates = new[] { "2026-09-28", "2026-09-29", "2026-09-30" };
        var s = Settings.Default();
        var rules = new ShiftRules(s, SynthLines); var stages = new StageRules(s, SynthLines);

        var plain = Synth(dates, Asm("X-1", dates, new double[] { 10, 10, 10 }, opening: 0, safety: 50));
        Autofill.Run(plain, rules, stages);
        Assert.Equal(new double[] { 25, 10, 0 }, plain.Assembly[0].Days.Select(d => d.Supply).ToArray());   // d0: 10 + 1.5×10 = 25

        var term = Synth(dates, Asm("X-1", dates, new double[] { 10, 10, 10 }, opening: 0, safety: 50));
        var (_, traces) = Autofill.RunWithTraces(term, rules, stages, new ApsOptions(SafetyStockTerm: true));
        Assert.Equal(new double[] { 60, 10, 0 }, term.Assembly[0].Days.Select(d => d.Supply).ToArray());    // d0: 10 + max(15, 50) = 60 · d2: trim → 0
        Assert.Equal(new double[] { 50, 50, 0 }, traces.Select(t => t.TargetStock).ToArray());
    }

    /// ORIG SupplyPlanner.cs:163-185 — 라인 능력(LineShift L1: UPH 10 × 줄 1 × 10h = 100) 초과분을 앞 근무일 여유로 옮긴다. cover 0 이라 앞날 선행 생산이 없다.
    static Settings CapSettings() => new()
    {
        DefaultDaysOfCover = 0, CoverTiers = new(), RoundTo = 5,
        LineShifts = { new LineShift { LineCd = "L1", Day = 10, Night = 0, Uph = 10, Stations = 1 } },
    };

    [Fact]
    public void PullForwardSupply_moves_overflow_to_earlier_days_and_clears_warning()
    {
        var dates = new[] { "2026-09-28", "2026-09-29", "2026-09-30", "2026-10-01" };
        var s = CapSettings();
        var rules = new ShiftRules(s, SynthLines); var stages = new StageRules(s, SynthLines);
        Assert.Equal(100, rules.AsmCapFor("L1", dates[0])!.Value.cap);

        var plain = Synth(dates, Asm("X-1", dates, new double[] { 0, 0, 250, 0 }, opening: 0, safety: 0));
        var pw = Autofill.Run(plain, rules, stages);
        Assert.Equal(new double[] { 0, 0, 250, 0 }, plain.Assembly[0].Days.Select(d => d.Supply).ToArray());
        Assert.Single(pw, w => w.Contains("라인 능력") && w.Contains("2026-09-30") && w.Contains("150개"));

        var pull = Synth(dates, Asm("X-1", dates, new double[] { 0, 0, 250, 0 }, opening: 0, safety: 0));
        var (warnings, traces) = Autofill.RunWithTraces(pull, rules, stages, new ApsOptions(PullForwardSupply: true));
        Assert.Empty(warnings);
        Assert.Equal(new double[] { 50, 100, 100, 0 }, pull.Assembly[0].Days.Select(d => d.Supply).ToArray());   // 09-29 에 100, 09-28 에 50 을 당겨 만든다
        var t = traces.Single(x => x.PartNo == "X-1" && x.Date == "2026-09-30");
        Assert.Equal(150, t.PrevClosing);   // 앞날에 만든 150 이 이 날 아침 재고
        Assert.Equal(100, t.Supply);
        Assert.Equal(0, t.Closing);
    }

    [Fact]
    public void PullForwardSupply_skips_locked_earlier_cells_and_keeps_warning_for_the_rest()
    {
        var dates = new[] { "2026-09-28", "2026-09-29", "2026-09-30", "2026-10-01" };
        var s = CapSettings();
        var rules = new ShiftRules(s, SynthLines); var stages = new StageRules(s, SynthLines);

        var bundle = Synth(dates, Asm("X-1", dates, new double[] { 0, 0, 250, 0 }, opening: 0, safety: 0));
        bundle.Assembly[0].Days[1].Locked = true;   // 09-29 는 사용자 잠금(공급 0)
        var warnings = Autofill.Run(bundle, rules, stages, new ApsOptions(PullForwardSupply: true));
        Assert.Equal(new double[] { 100, 0, 150, 0 }, bundle.Assembly[0].Days.Select(d => d.Supply).ToArray());   // 09-28 로만 100, 나머지 50 은 초과
        Assert.Single(warnings, w => w.Contains("라인 능력") && w.Contains("2026-09-30") && w.Contains("50개"));
    }
}
