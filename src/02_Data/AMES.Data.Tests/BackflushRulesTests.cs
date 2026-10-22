using AMES.Data.Services;
using Xunit;

namespace AMES.Data.Tests;

public class BackflushRulesTests
{
    static BackflushBomRow Row(string comp, string type = "MATERIAL", decimal qtyPer = 1m, decimal scrap = 0m, string uom = "G", bool active = true)
        => new(comp, type, qtyPer, scrap, uom, active);

    [Fact]
    public void Requirements_keep_only_active_material_children_with_scrap()
    {
        var rows = new[]
        {
            Row("RESIN", qtyPer: 977.49m),
            Row("CHEM",  qtyPer: 10m, scrap: 5m),
            Row("CORE",  type: "SUB", qtyPer: 1m, uom: "EA"),
            Row("OLD",   qtyPer: 3m, active: false),
            Row("ZERO",  qtyPer: 0m),
        };
        var req = BackflushRules.Requirements(rows);
        Assert.Collection(req,   // 품번순
            r => { Assert.Equal("CHEM",  r.PartNo); Assert.Equal(10.500m,  r.Qty); },
            r => { Assert.Equal("RESIN", r.PartNo); Assert.Equal(977.490m, r.Qty); Assert.Equal("G", r.Uom); });
    }

    [Fact]
    public void Requirements_sums_duplicate_components_and_rounds_to_3_places()
    {
        var req = BackflushRules.Requirements(new[] { Row("A", qtyPer: 0.0004m), Row("A", qtyPer: 0.0002m), Row("A", qtyPer: 1m) });
        var a = Assert.Single(req);
        Assert.Equal(1.001m, a.Qty);   // 1.0006 → 1.001 (AwayFromZero)
    }

    [Fact]
    public void Requirements_orders_by_part_number_so_every_confirm_locks_materials_in_the_same_order()
    {
        var req = BackflushRules.Requirements(new[] { Row("ZZ-RESIN"), Row("AA-CLIP", uom: "EA"), Row("MM-GLUE") });
        Assert.Equal(new[] { "AA-CLIP", "MM-GLUE", "ZZ-RESIN" }, req.Select(r => r.PartNo));
    }

    [Fact]
    public void Allocate_takes_fifo_then_falls_to_placeholder()
    {
        var fifo = new[] { new BackflushLot("L1", 5m), new BackflushLot("L2", 3m) };
        var a = BackflushRules.Allocate(10m, fifo, placeholderQty: 0m);
        Assert.Empty(a.Nets);
        Assert.Collection(a.Takes,
            t => { Assert.Equal("L1", t.LotNo); Assert.Equal(5m, t.Qty); },
            t => { Assert.Equal("L2", t.LotNo); Assert.Equal(3m, t.Qty); });
        Assert.Equal(2m, a.PlaceholderTake);
        Assert.Equal(2m, a.Shortage);
    }

    [Fact]
    public void Allocate_fills_negative_placeholder_from_fifo_before_taking()
    {
        var fifo = new[] { new BackflushLot("L1", 4m), new BackflushLot("L2", 10m) };
        var a = BackflushRules.Allocate(3m, fifo, placeholderQty: -6m);
        Assert.Collection(a.Nets,
            n => { Assert.Equal("L1", n.LotNo); Assert.Equal(4m, n.Qty); },
            n => { Assert.Equal("L2", n.LotNo); Assert.Equal(2m, n.Qty); });
        var t = Assert.Single(a.Takes);
        Assert.Equal("L2", t.LotNo); Assert.Equal(3m, t.Qty);
        Assert.Equal(0m, a.PlaceholderTake);
        Assert.Equal(0m, a.Shortage);
    }

    [Fact]
    public void Allocate_with_no_lots_goes_entirely_to_placeholder()
    {
        var a = BackflushRules.Allocate(7m, Array.Empty<BackflushLot>(), placeholderQty: -2m);
        Assert.Empty(a.Nets); Assert.Empty(a.Takes);
        Assert.Equal(7m, a.PlaceholderTake);
        Assert.Equal(7m, a.Shortage);
    }

    [Fact]
    public void Allocate_uses_positive_placeholder_before_going_negative()
    {
        var a = BackflushRules.Allocate(5m, Array.Empty<BackflushLot>(), placeholderQty: 2m);
        Assert.Equal(5m, a.PlaceholderTake);
        Assert.Equal(3m, a.Shortage);
    }

    [Fact]
    public void Allocate_zero_requirement_does_nothing_even_with_negative_placeholder()
    {
        var a = BackflushRules.Allocate(0m, new[] { new BackflushLot("L1", 4m) }, placeholderQty: -6m);
        Assert.Empty(a.Nets); Assert.Empty(a.Takes);
        Assert.Equal(0m, a.PlaceholderTake); Assert.Equal(0m, a.Shortage);
    }

    [Fact]
    public void PlaceholderLotNo_is_prefixed_by_line_and_part()
        => Assert.Equal("BF-I1-21343A0-PIDHA", BackflushRules.PlaceholderLotNo("I1", "21343A0-PIDHA"));

    [Fact]
    public void Requirements_include_injection_children_only_when_asked()
    {
        var rows = new[]
        {
            Row("RESIN", qtyPer: 100m),
            new BackflushBomRow("CORE-LH", "SUB", 1m, 0m, "EA", true, InjFlag: true),
            new BackflushBomRow("S-ASSY",  "SUB", 1m, 0m, "EA", true, InjFlag: false),
        };
        Assert.Equal(new[] { "RESIN" }, BackflushRules.Requirements(rows).Select(r => r.PartNo));
        Assert.Equal(new[] { "CORE-LH", "RESIN" }, BackflushRules.Requirements(rows, includeInjection: true).Select(r => r.PartNo));
    }

    [Fact]
    public void IncludesInjection_is_true_only_for_pnt()
    {
        Assert.True(BackflushRules.IncludesInjection("PNT"));
        Assert.True(BackflushRules.IncludesInjection("pnt"));
        Assert.False(BackflushRules.IncludesInjection("IMG"));
        Assert.False(BackflushRules.IncludesInjection("INJ"));
        Assert.False(BackflushRules.IncludesInjection(null));
    }
}
