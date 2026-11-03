using AMES.Data.Repositories;
using Xunit;

namespace AMES.Data.Tests;

public sealed class LineReplenishmentRulesTests
{
    [Theory]
    [InlineData(0, 100, false, false, 100, "Pending")]
    [InlineData(30, 100, false, false, 70, "Pending")]
    [InlineData(100, 100, false, false, 0, "Sufficient")]
    [InlineData(120, 100, false, false, 0, "Sufficient")]
    [InlineData(0, 0, false, false, 0, "Sufficient")]
    [InlineData(30, 100, true, false, 70, "Requested")]
    [InlineData(0, 100, false, true, 100, "Different part at location")]
    public void Only_shortage_at_the_assigned_location_becomes_a_new_request(
        decimal qty, decimal minimum, bool open, bool wrongPart, decimal required, string state)
    {
        var row = new WarehouseRepository.LineStockRow("W101A1F1", "PART-A", "Part A", qty, minimum, open, wrongPart);
        Assert.Equal(minimum, row.MinimumQty);
        Assert.Equal(required, row.RequiredQty);
        Assert.Equal(state, row.State);
    }

    [Fact]
    public void Automatic_demand_uses_registered_detail_locations_not_legacy_prefix_or_item_safety_stock()
    {
        var source = Read("src/02_Data/AMES.Data/Repositories/WarehouseRepository.LineStock.cs");
        var demand = source[source.IndexOf("private const string LineStockSql", StringComparison.Ordinal)..source.IndexOf("public List<LineStockRow> ListLineStock", StringComparison.Ordinal)];
        Assert.Contains("JOIN dbo.MD_Line M ON M.LineID=L.LineID", demand);
        Assert.Contains("L.LocationLevel=2", demand);
        Assert.Contains("L.MinimumQty>0", demand);
        Assert.Contains("COALESCE(L.ActiveFlag,1)=1", demand);
        Assert.Contains("COALESCE(M.Status,'ACTIVE')='ACTIVE'", demand);
        Assert.Contains("W.LocationNo=L.LocationID AND W.PartNo=L.PartNo", demand);
        Assert.Contains("COALESCE(S.Qty,0) AS Qty", demand);
        Assert.Contains("NOT EXISTS(SELECT 1 FROM dbo.WH_Inventory C WHERE C.ParentLotNo=W.LotNo)", demand);
        Assert.Contains("O.CloseDate IS NULL", demand);
        Assert.Contains("W.PartNo<>S.PartNo", demand);
        foreach (var legacy in new[] { "LotPrefix", "I.SafetyStock", "MinStock", "UNION ALL" })
            Assert.DoesNotContain(legacy, demand);
        Assert.Contains("Property=\"MinimumQty\" Title=\"@L[\"WH.Picking.Col.MinimumQty\"].Value\"", Read("src/06_Web/AMES.Web/Components/Pages/Wh/PickingOrders.razor"));
    }

    [Fact]
    public void Overview_includes_unconfigured_and_inactive_locations_but_worker_keeps_its_original_filters()
    {
        var source = Read("src/02_Data/AMES.Data/Repositories/WarehouseRepository.LineStock.cs");
        Assert.Contains("@Overview=1 OR (COALESCE(L.ActiveFlag,1)=1", source);
        Assert.Contains("AND NULLIF(L.PartNo,'') IS NOT NULL AND L.MinimumQty>0))", source);
        Assert.Contains("LEFT JOIN dbo.MD_Item I", source);
        Assert.Contains("WHERE @Overview=1 OR (I.ItemNo IS NOT NULL AND COALESCE(I.ActiveFlag,1)=1)", source);
        Assert.Contains("ReadLineStock(conn, null, overview:true)", source);
        Assert.Contains("SqlTransaction? tx, bool overview=false", source);
        Assert.Contains(".Value = overview", source);
        Assert.Contains("var stock = ReadLineStock(conn, tx);", source);
        Assert.Contains(".OrderByDescending(row => row.DisplayRequiredQty)", source);
    }

    [Theory]
    [InlineData(0, 20, 20)]
    [InlineData(12, 20, 8)]
    [InlineData(20, 20, 0)]
    [InlineData(25, 20, 0)]
    [InlineData(0, 0, 0)]
    [InlineData(2.25, 3, 0.75)]
    public void Overview_required_quantity_is_the_exact_nonnegative_shortage(decimal qty, decimal minimum, decimal needed)
    {
        var row = new WarehouseRepository.LineStockRow("LI01A1F1", "PART-A", "A", qty, minimum, false, Unit:"G");
        Assert.Equal(qty, row.DisplayQty);
        Assert.Equal(needed, row.DisplayRequiredQty);
        Assert.Equal(decimal.Ceiling(needed), row.RequiredQty);
    }

    [Fact]
    public void Missing_assignment_is_unknown_not_zero_and_never_creates_a_replenishment_request()
    {
        var row = new WarehouseRepository.LineStockRow("LI01A1F1", "", "", 0, 50, false);
        Assert.Null(row.DisplayQty);
        Assert.Null(row.DisplayRequiredQty);
        Assert.Equal("Assign a part", row.State);
        Assert.Empty(WarehouseRepository.PendingLineOrders([row]));
        Assert.Equal("Inactive", (row with { PartNo="PART-A", IsActive=false }).State);
    }

    [Fact]
    public void Line_stock_grid_shows_line_and_location_and_sorts_needed_quantities_descending()
    {
        var page = Read("src/06_Web/AMES.Web/Components/Pages/Wh/PickingOrders.razor");
        foreach(var column in new[]{"LineName", "LineCode", "Unit", "MinimumQty", "DisplayQty", "DisplayRequiredQty"})
            Assert.Contains($"Property=\"{column}\"", page);
        Assert.Contains("SortOrder=\"SortOrder.Descending\"", page);
        Assert.Contains("@UomFmt.Qty(r.DisplayQty, r.Unit)", page);
        Assert.Contains("@UomFmt.Qty(r.DisplayRequiredQty, r.Unit)", page);
        Assert.DoesNotContain("WH.Picking.Col.RequiredEa", page);
    }

    private static string Read(string path, [System.Runtime.CompilerServices.CallerFilePath] string current = "")
        => File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(current)!, "../../..", path)));
}
