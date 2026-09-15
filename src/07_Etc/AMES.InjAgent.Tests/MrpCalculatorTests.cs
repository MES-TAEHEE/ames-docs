using AMES.Data.Services;
using Xunit;
using static AMES.Data.Services.MrpCalculator;

namespace AMES.InjAgent.Tests;

/// <summary>PP-005 MRP 계산 규칙: BOM leaf 분해 → 소요 집계 → 부족 = 소요 − 재고 − 발주중 → 발주 기한 = 최단 납기 − L/T.</summary>
public class MrpCalculatorTests
{
    static readonly DateTime Today = new(2026, 9, 15);

    static Demand D(int wo, string item, decimal qty, DateTime? due = null) => new(wo, item, qty, due);
    static BomLine B(string parent, string comp, decimal qtyPer, decimal scrap = 0) => new(parent, comp, qtyPer, scrap);
    static Supply S(string item, decimal stock = 0, decimal onOrder = 0, int? lt = null) => new(item, stock, onOrder, lt);

    static Result Run(IEnumerable<Demand> demands, IEnumerable<BomLine> bom, params Supply[] supply)
        => Explode(demands, bom, supply.ToDictionary(s => s.ItemNo), Today);

    [Fact]
    public void Single_level_bom_multiplies_demand_by_qty_per()
    {
        var r = Run(new[] { D(1, "FG-A", 100) }, new[] { B("FG-A", "RM-1", 2.5m) });
        var m = Assert.Single(r.Materials);
        Assert.Equal("RM-1", m.ItemNo);
        Assert.Equal(250m, m.Required);
    }

    [Fact]
    public void Scrap_pct_inflates_requirement()
    {
        var r = Run(new[] { D(1, "FG-A", 100) }, new[] { B("FG-A", "RM-1", 1m, scrap: 10) });
        Assert.Equal(110m, Assert.Single(r.Materials).Required);
    }

    [Fact]
    public void Multi_level_bom_reports_only_leaf_materials()
    {
        var bom = new[] { B("FG-A", "SUB-1", 2m), B("SUB-1", "RM-1", 3m) };
        var r = Run(new[] { D(1, "FG-A", 10) }, bom);
        var m = Assert.Single(r.Materials);
        Assert.Equal("RM-1", m.ItemNo);
        Assert.Equal(60m, m.Required);
    }

    [Fact]
    public void Same_material_from_two_wos_accumulates_and_lists_both()
    {
        var bom = new[] { B("FG-A", "RM-1", 1m), B("FG-B", "RM-1", 2m) };
        var r = Run(new[] { D(1, "FG-A", 10, Today.AddDays(10)), D(2, "FG-B", 5, Today.AddDays(5)) }, bom);
        var m = Assert.Single(r.Materials);
        Assert.Equal(20m, m.Required);
        Assert.Equal(new[] { 2, 1 }, m.Wos.Select(w => w.WoId));   // 납기 빠른 WO 먼저
        Assert.Equal(2, r.WosConsidered);
    }

    [Fact]
    public void Shortage_is_required_minus_stock_minus_on_order()
    {
        var r = Run(new[] { D(1, "FG-A", 100) }, new[] { B("FG-A", "RM-1", 1m) }, S("RM-1", stock: 30, onOrder: 20));
        var m = Assert.Single(r.Materials);
        Assert.Equal(30m, m.Stock);
        Assert.Equal(20m, m.OnOrder);
        Assert.Equal(50m, m.Shortage);
    }

    [Fact]
    public void Surplus_is_negative_shortage()
    {
        var r = Run(new[] { D(1, "FG-A", 10) }, new[] { B("FG-A", "RM-1", 1m) }, S("RM-1", stock: 25));
        Assert.Equal(-15m, Assert.Single(r.Materials).Shortage);
    }

    [Fact]
    public void Material_without_supply_row_counts_as_zero_stock()
    {
        var r = Run(new[] { D(1, "FG-A", 10) }, new[] { B("FG-A", "RM-1", 1m) });
        var m = Assert.Single(r.Materials);
        Assert.Equal(0m, m.Stock);
        Assert.Equal(10m, m.Shortage);
        Assert.Null(m.LeadTimeDays);
    }

    [Fact]
    public void Order_due_is_earliest_wo_due_minus_lead_time_when_short()
    {
        var bom = new[] { B("FG-A", "RM-1", 1m) };
        var r = Run(new[] { D(1, "FG-A", 10, Today.AddDays(30)), D(2, "FG-A", 10, Today.AddDays(20)) }, bom, S("RM-1", lt: 7));
        var m = Assert.Single(r.Materials);
        Assert.Equal(7, m.LeadTimeDays);
        Assert.Equal(Today.AddDays(13), m.OrderDue);
    }

    [Fact]
    public void Order_due_is_null_when_not_short()
    {
        var r = Run(new[] { D(1, "FG-A", 10, Today.AddDays(30)) }, new[] { B("FG-A", "RM-1", 1m) }, S("RM-1", stock: 10, lt: 7));
        Assert.Null(Assert.Single(r.Materials).OrderDue);
    }

    [Fact]
    public void Order_due_is_null_without_lead_time_or_wo_due()
    {
        var bom = new[] { B("FG-A", "RM-1", 1m) };
        Assert.Null(Assert.Single(Run(new[] { D(1, "FG-A", 10, Today.AddDays(30)) }, bom).Materials).OrderDue);
        Assert.Null(Assert.Single(Run(new[] { D(1, "FG-A", 10) }, bom, S("RM-1", lt: 7)).Materials).OrderDue);
    }

    [Fact]
    public void Wo_without_bom_is_skipped_and_reported()
    {
        var r = Run(new[] { D(1, "FG-A", 10), D(2, "FG-X", 10) }, new[] { B("FG-A", "RM-1", 1m) });
        Assert.Equal(1, r.WosConsidered);
        Assert.Equal(new[] { 2 }, r.SkippedNoBom);
    }

    [Fact]
    public void Zero_or_negative_demand_is_ignored()
    {
        var r = Run(new[] { D(1, "FG-A", 0), D(2, "FG-A", -5) }, new[] { B("FG-A", "RM-1", 1m) });
        Assert.Empty(r.Materials);
        Assert.Equal(0, r.WosConsidered);
    }

    [Fact]
    public void Circular_bom_throws()
    {
        var bom = new[] { B("FG-A", "SUB-1", 1m), B("SUB-1", "FG-A", 1m) };
        var ex = Assert.Throws<MrpCycleException>(() => Run(new[] { D(1, "FG-A", 10) }, bom));
        Assert.Contains("FG-A", ex.Message);
    }

    [Fact]
    public void Materials_are_ordered_shortage_first_then_item_no()
    {
        var bom = new[] { B("FG-A", "RM-Z", 1m), B("FG-A", "RM-B", 1m), B("FG-A", "RM-A", 1m) };
        var r = Run(new[] { D(1, "FG-A", 10) }, bom, S("RM-A", stock: 100), S("RM-B"), S("RM-Z"));
        Assert.Equal(new[] { "RM-B", "RM-Z", "RM-A" }, r.Materials.Select(m => m.ItemNo));
    }

    [Fact]
    public void Blocked_wo_ids_are_distinct_wos_of_shortage_materials()
    {
        var bom = new[] { B("FG-A", "RM-1", 1m), B("FG-A", "RM-2", 1m), B("FG-B", "RM-2", 1m), B("FG-C", "RM-3", 1m) };
        var r = Run(new[] { D(1, "FG-A", 10), D(2, "FG-B", 10), D(3, "FG-C", 10) }, bom, S("RM-3", stock: 99));
        Assert.Equal(new[] { 1, 2 }, BlockedWoIds(r.Materials).OrderBy(x => x));
    }
}
