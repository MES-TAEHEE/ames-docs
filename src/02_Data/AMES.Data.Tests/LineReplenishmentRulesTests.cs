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
        Assert.Contains("Property=\"MinimumQty\" Title=\"MINIMUM (EA)\"", Read("src/06_Web/AMES.Web/Components/Pages/Wh/PickingOrders.razor"));
    }

    private static string Read(string path, [System.Runtime.CompilerServices.CallerFilePath] string current = "")
        => File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(current)!, "../../..", path)));
}
