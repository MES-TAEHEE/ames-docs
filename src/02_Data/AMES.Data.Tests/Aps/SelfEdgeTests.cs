using AMES.Data.Aps.Contracts;
using AMES.Data.Aps.Domain;
using Xunit;

namespace AMES.Data.Tests.Aps;

/// 자기 간선(X→X): Requirements 는 부모 공급 사전에서 자기 공급을 읽고, Autofill 은 완제품 사전, 스케줄러는 사출 행 인덱스로 각각 돌아간다.
public class SelfEdgeTests
{
    static readonly Settings S = Settings.Default();
    static readonly Dictionary<string, LineInfo> L = new(StringComparer.OrdinalIgnoreCase)
    {
        ["L1"] = new LineInfo { LineCd = "L1", Type = "assembly" },
        ["M1"] = new LineInfo { LineCd = "M1", Type = "injection" },
    };
    static readonly string[] Dates = { "2026-09-28", "2026-09-29", "2026-09-30" };

    static PlanBundle Bundle()
    {
        var supply = new double[] { 12, 8, 20 };
        var asm = new AssemblyRow
        {
            PartNo = "X", LineCd = "L1", OpeningStock = 0,
            Days = Dates.Select((d, i) => new AssemblyDay { Date = d, Demand = 10, Supply = supply[i], Locked = true }).ToList(),
        };
        var inj = new InjectionRow
        {
            PartNo = "X", LineCd = "M1", Uph = 60, PackSize = 5, OpeningStock = 0,
            Days = Dates.Select(d => new InjectionDay { Date = d }).ToList(),
        };
        return new PlanBundle
        {
            Line = L["L1"], BaseDate = Dates[0], PrevDate = ApsCalendar.PrevDate(Dates[0]), Dates = Dates.ToList(),
            Assembly = { asm }, Injection = { inj }, Bom = { new BomEdge("X", "X", 1) },
        };
    }

    [Fact]
    public void Self_edge_feeds_parent_supply_into_injection_requirement_and_schedules_normally()
    {
        var b = Bundle();
        var rules = new ShiftRules(S, L); var stages = new StageRules(S, L);

        var req = PlanCalc.Requirements(b, stages);
        Assert.Equal(new double[] { 12, 8, 20 }, req[0]);   // 소요 = 부모(자기) 공급, M1 선행일 0 (StageDefaults.Injection)

        Assert.Empty(Autofill.Run(b, rules, stages));         // 잠긴 칸이라 공급 불변 · 예외 없음
        Assert.Equal(new double[] { 12, 8, 20 }, b.Assembly[0].Days.Select(d => d.Supply).ToArray());

        Assert.Empty(InjectionScheduler.Reschedule(b, rules, stages));
        Assert.Equal(new double[] { 15, 5, 20 }, b.Injection[0].Days.Select(d => d.PlanDay + d.PlanNight).ToArray());   // 12→15(5개들이) · 잔여 3 → 8−3=5 · 20

        var (result, _) = PlanCalc.Compute(b, rules, stages);
        Assert.Equal(new double[] { 12, 8, 20 }, result.Injection[0].Cells.Select(c => c.Requirement).ToArray());
        Assert.Equal(new double[] { 3, 0, 0 }, result.Injection[0].Cells.Select(c => c.Remain).ToArray());
        Assert.All(result.Injection[0].Cells, c => Assert.Equal("ok", c.Status));
        Assert.All(result.Assembly[0].Cells, c => Assert.Equal("ok", c.Status));
    }
}
