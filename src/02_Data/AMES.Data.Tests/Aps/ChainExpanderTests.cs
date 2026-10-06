using AMES.Data.Aps.Contracts;
using AMES.Data.Aps.Domain;
using Xunit;

namespace AMES.Data.Tests.Aps;

public class ChainExpanderTests
{
    static readonly Settings S = Fixtures.LoadSettings();
    static readonly Dictionary<string, LineInfo> L = Fixtures.LoadLines();
    static readonly Dictionary<string, OpeningInfo> NoStock = new();

    static ChainRequest Req(int levels, params (string date, double qty)[] plan) => new()
    {
        BaseDate = "2026-09-24", Dates = ApsCalendar.WorkDates(SundayOffCalendar.Instance, "2026-09-24", 3), Levels = levels,
        Roots = { new ChainRoot { PartNo = "A", LineCd = "LQ10", Plan = plan.ToDictionary(p => p.date, p => p.qty) } },
    };

    [Fact]
    public void Cyclic_bom_stops_at_levels()
    {
        var edges = new List<BomEdge> { new("A", "B", 1), new("B", "C", 1), new("C", "A", 1) };
        var parts = new Dictionary<string, PartInfo> { ["A"] = new("A", "LQ10", null, null, 0, "A"), ["B"] = new("B", "M2A100", null, null, 0, "B"), ["C"] = new("C", "M9A100", null, null, 0, "C") };
        var res = ChainExpander.Expand(Req(2, ("2026-09-24", 10), ("2026-09-25", 10), ("2026-09-26", 10)), edges, parts, L, NoStock, new ShiftRules(S, L), new StageRules(S, L));
        Assert.Equal(2, res.Levels.Count);
        Assert.Equal(new[] { "B", "C" }, res.Levels.SelectMany(l => l.Rows).Select(r => r.PartNo).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.All(res.Levels.SelectMany(l => l.Rows), r => Assert.NotEqual("A", r.PartNo));   // 루트는 다시 행이 되지 않는다
    }

    [Fact]
    public void Child_with_zero_requirement_is_counted_as_edge_but_not_listed()
    {
        var edges = new List<BomEdge> { new("A", "B", 1), new("B", "C", 1) };
        var parts = new Dictionary<string, PartInfo> { ["B"] = new("B", "M2A100", null, null, 0, "B"), ["C"] = new("C", "M9A100", null, null, 0, "C") };
        var stock = new Dictionary<string, OpeningInfo> { ["B"] = new(1000, true, "2026-09-24", 0) };   // B 는 재고가 넉넉해 계획 0
        var res = ChainExpander.Expand(Req(2, ("2026-09-24", 10), ("2026-09-25", 10), ("2026-09-26", 10)), edges, parts, L, stock, new ShiftRules(S, L), new StageRules(S, L));
        Assert.Equal(2, res.Edges);
        Assert.Single(res.Levels);
        Assert.Equal("B", res.Levels[0].Rows.Single().PartNo);
        Assert.All(res.Levels[0].Rows.Single().Days, d => Assert.Equal(0, d.Plan));
    }

    [Fact]
    public void Qty_per_scales_requirement_and_pegs()
    {
        var edges = new List<BomEdge> { new("A", "B", 4) };
        var parts = new Dictionary<string, PartInfo> { ["B"] = new("B", "LU10", null, null, 0, "B") };   // LU10: usesStock=false → plan = 소요
        var res = ChainExpander.Expand(Req(1, ("2026-09-24", 10), ("2026-09-25", 20), ("2026-09-26", 0)), edges, parts, L, NoStock, new ShiftRules(S, L), new StageRules(S, L));
        var b = res.Levels[0].Rows.Single();
        Assert.Equal(new double[] { 80, 0, 0 }, b.Days.Select(d => d.Requirement).ToArray());   // LU10 은 LQ10 보다 1일 앞 → 09-25 계획 20 × 4 가 09-24 소요
        Assert.Equal(40, b.Days[0].Overdue);                                                     // 09-24 계획 10 × 4 는 첫날 이전 몫
        Assert.Equal(new double[] { 80, 0, 0 }, b.Pegs.Single().Days.ToArray());
        Assert.Equal(new[] { "LQ10 −1일" }, b.Leads.ToArray());
    }
}
