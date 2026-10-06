using AMES.Data.Aps.Contracts;
using AMES.Data.Aps.Domain;
using Xunit;

namespace AMES.Data.Tests.Aps;

/// <summary>
/// 사출 라인 모드(스펙 §4.2, 선택 라인 Type = injection) — 선행일·재고 규칙은 완제품 모드와 같게 적용되고(StageRules 루트 = null),
/// 완제품 공급 능력은 선택 라인이 아니라 각 부모 완제품 행 자기 라인의 MD_Line.DailyCap 이다. 완제품 모드는 골든 테스트가 지킨다.
/// </summary>
public class InjectionModeTests
{
    static readonly string[] Dates = { "2026-10-05", "2026-10-06", "2026-10-07", "2026-10-08", "2026-10-09" };

    static readonly Dictionary<string, LineInfo> Lines = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A100"] = new LineInfo { LineCd = "A100", Type = "assembly" },
        ["A200"] = new LineInfo { LineCd = "A200", Type = "assembly" },
        ["I100"] = new LineInfo { LineCd = "I100", Type = "injection" },
    };

    static AssemblyRow Asm(string partNo, string line, string[] dates, double[] demand, double[]? supply = null, double opening = 0) => new()
    {
        PartNo = partNo, LineCd = line, OpeningStock = opening,
        Days = dates.Select((d, i) => new AssemblyDay { Date = d, Demand = demand[i], Supply = supply?[i] ?? 0 }).ToList(),
    };

    static PlanBundle Bundle(string selectedLine, string[] dates, IEnumerable<AssemblyRow> asm, IEnumerable<InjectionRow>? inj = null, IEnumerable<BomEdge>? bom = null) => new()
    {
        Line = Lines[selectedLine], BaseDate = dates[0], Dates = dates.ToList(),
        Assembly = asm.ToList(), Injection = (inj ?? Array.Empty<InjectionRow>()).ToList(), Bom = (bom ?? Array.Empty<BomEdge>()).ToList(),
    };

    [Fact]
    public void Requirements_in_injection_mode_keep_the_injection_offset()
    {
        var s = Settings.Default();
        s.StageDefaults.Injection.OffsetDays = 1;
        var stages = new StageRules(s, Lines);
        double[] supply = { 10, 20, 30, 40, 0 };

        PlanBundle Make(string selected) => Bundle(selected, Dates,
            new[] { Asm("P", "A100", Dates, new double[5], supply) },
            new[] { new InjectionRow { PartNo = "C", LineCd = "I100", Uph = 60, PackSize = 1, Days = Dates.Select(d => new InjectionDay { Date = d }).ToList() } },
            new[] { new BomEdge("P", "C", 1) });

        var asmMode = PlanCalc.Requirements(Make("A100"), stages);
        var injMode = PlanCalc.Requirements(Make("I100"), stages);

        Assert.Null(PlanCalc.RootLine(Make("I100")));
        Assert.Equal("A100", PlanCalc.RootLine(Make("A100")));
        Assert.Equal(asmMode[0], injMode[0]);
        Assert.Equal(new double[] { 20, 30, 40, 0, 0 }, injMode[0]);   // requirement[d] = supply[d + 1] — 선행 1일
    }

    [Fact]
    public void Requirements_with_a_negative_line_stage_offset_read_zero_before_the_horizon_instead_of_throwing()
    {
        // MD_ApsLineStage.OffsetDays 는 SQL 로 음수가 들어올 수 있다 — 범위 밖(d + offset < 0)은 0
        var s = Settings.Default();
        s.LineStages.Add(new LineStage { LineCd = "I100", OffsetDays = -1 });
        var b = Bundle("I100", Dates,
            new[] { Asm("P", "A100", Dates, new double[5], new double[] { 10, 20, 30, 40, 50 }) },
            new[] { new InjectionRow { PartNo = "C", LineCd = "I100", Uph = 60, PackSize = 1, Days = Dates.Select(d => new InjectionDay { Date = d }).ToList() } },
            new[] { new BomEdge("P", "C", 1) });

        Assert.Equal(new double[] { 0, 10, 20, 30, 40 }, PlanCalc.Requirements(b, new StageRules(s, Lines))[0]);
    }

    [Fact]
    public void Autofill_in_injection_mode_caps_each_parent_line_by_its_own_daily_cap()
    {
        // 수요 150/일, 기초 100, 커버 0.4 → 0일 need 110·min 50, 1일 need 160·min 100 → 자기 라인 능력 100 으로 깎인다.
        // 2일(마지막 · trim)은 min 150 > 100 → 150 그대로 + 라인마다 경고 1줄. 합산 능력 200 이면 0일에 첫 행만 20 깎여 90/110, 무제한이면 110/110
        var dates = Dates.Take(3).ToArray();
        var s = Settings.Default();
        s.LineShifts.Add(new LineShift { LineCd = "I100", Day = 10, Night = 10 });   // 어댑터가 사출 라인에 주/야를 싣는다(DailyCap 없음)
        s.LineShifts.Add(new LineShift { LineCd = "A100", DailyCap = 100 });
        s.LineShifts.Add(new LineShift { LineCd = "A200", DailyCap = 100 });
        var demand = new double[] { 150, 150, 150 };
        var b = Bundle("I100", dates, new[] { Asm("P1", "A100", dates, demand, opening: 100), Asm("P2", "A200", dates, demand, opening: 100) });

        var warnings = Autofill.Run(b, new ShiftRules(s, Lines), new StageRules(s, Lines), new ApsOptions(PullForwardSupply: false));

        Assert.Equal(new double[] { 100, 100, 150 }, b.Assembly[0].Days.Select(d => d.Supply).ToArray());
        Assert.Equal(new double[] { 100, 100, 150 }, b.Assembly[1].Days.Select(d => d.Supply).ToArray());
        Assert.Equal(2, warnings.Count);
        Assert.Single(warnings, w => w.StartsWith("A100 " + dates[2]) && w.Contains("100개") && w.Contains("50개"));
        Assert.Single(warnings, w => w.StartsWith("A200 " + dates[2]) && w.Contains("100개") && w.Contains("50개"));
    }

    [Fact]
    public void Autofill_pull_forward_in_injection_mode_uses_only_its_own_line_room()
    {
        // 커버 0 → need = min = 수요 − 전일 재고. P1(A100, 능력 100) 1일 150 → 50 초과를 0일로 당긴다 — 0일 A100 여유 100 − 0.
        // 0일 A200 의 P2 공급 500 은 A100 여유에 들어가지 않는다(전 행 합이면 100 − 500 < 0 이라 못 당기고 경고)
        var dates = Dates.Take(2).ToArray();
        var s = Settings.Default();
        s.CoverTiers = new();
        s.DefaultDaysOfCover = 0;
        s.LineShifts.Add(new LineShift { LineCd = "I100", Day = 10, Night = 10 });
        s.LineShifts.Add(new LineShift { LineCd = "A100", DailyCap = 100 });
        s.LineShifts.Add(new LineShift { LineCd = "A200", DailyCap = 1000 });
        var b = Bundle("I100", dates, new[] { Asm("P1", "A100", dates, new double[] { 0, 150 }), Asm("P2", "A200", dates, new double[] { 500, 0 }) });

        var warnings = Autofill.Run(b, new ShiftRules(s, Lines), new StageRules(s, Lines), new ApsOptions(PullForwardSupply: true));

        Assert.Empty(warnings);
        Assert.Equal(new double[] { 50, 100 }, b.Assembly[0].Days.Select(d => d.Supply).ToArray());
        Assert.Equal(new double[] { 500, 0 }, b.Assembly[1].Days.Select(d => d.Supply).ToArray());
    }
}
