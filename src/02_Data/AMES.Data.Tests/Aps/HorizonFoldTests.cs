using AMES.Data.Aps.Contracts;
using AMES.Data.Aps.Domain;
using Xunit;

namespace AMES.Data.Tests.Aps;

/// 기준일 이전 사출일에 떨어지는 완제품 공급(사출 선행일 때문에 i = j − 선행일 < 0)의 소요는 첫 사출일(기준일)에 몬다 — 수주 지연분을 첫날에 넣는 규칙과 같다.
/// 조용히 버리면 기준일 공급만큼 WO 가 빠진다(실행 #1355·#555). 사용자 결정 2026-10-06 (a).
public class HorizonFoldTests
{
    static readonly Dictionary<string, LineInfo> L = new(StringComparer.OrdinalIgnoreCase)
    {
        ["L1"] = new LineInfo { LineCd = "L1", Type = "assembly" },
        ["M1"] = new LineInfo { LineCd = "M1", Type = "injection" },
    };
    static readonly string[] Dates = { "2026-10-06", "2026-10-07", "2026-10-08" };

    static Settings WithOffset(int days) { var s = Settings.Default(); s.StageDefaults.Injection.OffsetDays = days; return s; }

    static PlanBundle Bundle(double[] supply)
    {
        var asm = new AssemblyRow
        {
            PartNo = "X", LineCd = "L1",
            Days = Dates.Select((d, i) => new AssemblyDay { Date = d, Demand = supply[i], Supply = supply[i], Locked = true }).ToList(),
        };
        var inj = new InjectionRow { PartNo = "X", LineCd = "M1", Uph = 60, PackSize = 1, Days = Dates.Select(d => new InjectionDay { Date = d }).ToList() };
        return new PlanBundle
        {
            Line = L["L1"], BaseDate = Dates[0], PrevDate = ApsCalendar.PrevDate(Dates[0]), Dates = Dates.ToList(),
            Assembly = { asm }, Injection = { inj }, Bom = { new BomEdge("X", "X", 1) },
        };
    }

    [Fact]
    public void Supply_before_the_first_injection_day_is_folded_into_day_zero_and_reported()
    {
        var s = WithOffset(1); var stages = new StageRules(s, L, foldPreHorizon: true);
        var b = Bundle(new double[] { 312, 96, 48 });

        var (req, folds) = PlanCalc.RequirementsWithFolds(b, stages);
        Assert.Equal(new double[] { 96 + 312, 48, 0 }, req[0]);          // 10/06 사출 = 10/07 공급 96 + 당긴 10/06 공급 312
        Assert.Equal(("X", 312d, 1), Assert.Single(folds));
        Assert.Equal(req[0], PlanCalc.Requirements(b, stages)[0]);           // 셀 소요(Compute)도 같은 값

        var w = InjectionScheduler.Reschedule(b, new ShiftRules(s, L), stages);
        Assert.Contains(w, x => x.Contains("X") && x.Contains("312") && x.Contains("2026-10-06") && x.Contains("당겼"));
    }

    [Fact]
    public void Offset_two_folds_two_leading_days()
    {
        var s = WithOffset(2); var stages = new StageRules(s, L, foldPreHorizon: true);
        var (req, folds) = PlanCalc.RequirementsWithFolds(Bundle(new double[] { 10, 20, 30 }), stages);
        Assert.Equal(new double[] { 30 + 10 + 20, 0, 0 }, req[0]);
        Assert.Equal(("X", 30d, 2), Assert.Single(folds));
    }

    [Fact]
    public void Offset_zero_folds_nothing_golden_path_unchanged()
    {
        var s = WithOffset(0); var stages = new StageRules(s, L, foldPreHorizon: true);
        var (req, folds) = PlanCalc.RequirementsWithFolds(Bundle(new double[] { 12, 8, 20 }), stages);
        Assert.Equal(new double[] { 12, 8, 20 }, req[0]);
        Assert.Empty(folds);
        Assert.DoesNotContain(InjectionScheduler.Reschedule(Bundle(new double[] { 12, 8, 20 }), new ShiftRules(s, L), stages), x => x.Contains("당겼"));
    }

    [Fact]
    public void Golden_path_without_the_flag_still_drops_pre_horizon_supply()
    {
        var s = WithOffset(1); var stages = new StageRules(s, L);
        var (req, folds) = PlanCalc.RequirementsWithFolds(Bundle(new double[] { 312, 96, 48 }), stages);
        Assert.Equal(new double[] { 96, 48, 0 }, req[0]);
        Assert.Empty(folds);
    }
}
