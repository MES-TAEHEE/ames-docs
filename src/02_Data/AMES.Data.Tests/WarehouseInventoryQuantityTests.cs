using System.Xml.Linq;
using AMES.Data.Repositories;
using AMES.Data.Services;
using AMES.Web.Components.Shared;
using AMES.Web.Services;
using Xunit;

namespace AMES.Data.Tests;

public sealed class WarehouseInventoryQuantityTests
{
    static string Read(string path, [System.Runtime.CompilerServices.CallerFilePath] string file = "")
        => File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../..", path)));

    [Fact]
    public void Current_stock_includes_inspection_but_not_historical_defects_and_unassigned_is_a_subset()
    {
        LocationMapStock[] rows = [
            new("A1", "M1", "Material", "L1", 30.5m, "G", DefectQty: 7),
            new("", "M1", "Material", "L2", 10.25m, "G"),
            new("  ", "M1", "Material", "L3", 2.5m, "G", InspectionQty: 2.5m),
            new("", "M1", "Material", "L4", 0m, "G", DefectQty: 4)
        ];
        var current = rows.Sum(row => row.Qty);
        var unassigned = InventorySearchRows.UnassignedQty(rows);
        Assert.Equal(43.25m, current);
        Assert.Equal(12.75m, unassigned);
        Assert.Equal(30.5m, current - unassigned);
        Assert.Equal(11m, rows.Sum(row => row.DefectQty));
    }

    [Fact]
    public void Master_items_without_stock_remain_in_the_list_and_unknown_parts_are_excluded()
    {
        var item = new MasterDataRepository.ItemRow("M1", "Material", "MATERIAL", null, false, null, "G",
            null, null, null, null, null, null, null, null, null, null, false, true, null, null, null, null);
        var rows = InventorySearchRows.FromMaster([item], [new("A1", "UNKNOWN", "Other", "L1", 5, "EA")]);
        var row = Assert.Single(rows);
        Assert.Equal("M1", row.PartNo);
        Assert.Equal(0, row.Qty);
        Assert.Equal(0, InventorySearchRows.UnassignedQty(rows));
    }

    [Fact]
    public void Requirements_reuse_current_remaining_work_orders_and_approved_bom_without_writes()
    {
        var sql = WarehouseRepository.RemainingProductionDemandSql;
        Assert.Contains("ISNULL(w.OrderQty,0)-ISNULL(w.CompletedQty,0)", sql);
        Assert.Contains($"NOT IN ({WoStatusRules.FinishedSqlList})", sql);
        Assert.Contains("ISNULL(w.OrderQty,0)>ISNULL(w.CompletedQty,0)", sql);
        var source = Read("src/02_Data/AMES.Data/Repositories/WarehouseRepository.MaterialRequirements.cs");
        Assert.Contains("PpRepository.EffectiveBomSql", source);
        Assert.Contains("MrpCalculator.Explode", source);
        Assert.DoesNotContain(".RunMrp(", source);
        Assert.DoesNotMatch(@"(?i)\b(INSERT|UPDATE|DELETE|MERGE|EXEC|CREATE|DROP)\s+", sql);
    }

    [Fact]
    public void Remaining_materials_use_decimal_bom_quantities_and_report_missing_boms()
    {
        var result = MrpCalculator.Explode(
            [new(1, "ASSY1", 8, null), new(2, "ASSY2", 3, null), new(3, "NO-BOM", 2, null), new(4, "ASSY1", 0, null)],
            [new("ASSY1", "SUB1", 2, 0), new("SUB1", "RESIN", 1.25m, 0), new("ASSY2", "RESIN", 2, 10)],
            new Dictionary<string, MrpCalculator.Supply>(), new DateTime(2026, 11, 3));
        Assert.Equal(26.6m, Assert.Single(result.Materials).Required);
        Assert.Equal(2, result.WosConsidered);
        Assert.Equal(3, Assert.Single(result.SkippedNoBom));
    }

    [Fact]
    public void Web_columns_and_export_use_current_stored_unassigned_and_live_requirements()
    {
        var lookup = Read("src/06_Web/AMES.Web/Components/Shared/InventoryLookup.razor");
        foreach (var column in new[] { "Qty", "StoredQty", "UnassignedQty", "RequiredQty", "DefectQty" })
            Assert.Contains($"Property=\"{column}\"", lookup);
        foreach (var removed in new[] { "InspectionQty", "LocationCount", "LotCount" })
            Assert.DoesNotContain($"Property=\"{removed}\"", lookup);
        Assert.DoesNotContain("WH.Inventory.AvailableQty", lookup);
        Assert.DoesNotContain("WH.Inventory.Col.InspectionQty", lookup);
        Assert.Contains("row.Qty > row.InspectionQty", lookup);
        Assert.Contains("lot.Qty<=lot.InspectionQty", lookup);
        Assert.Contains("ProductionRequirements is null ? null", lookup);
        Assert.Contains("row.StoredQty, row.UnassignedQty", lookup);
        Assert.Contains(".Append(row.DefectQty)", lookup);
        Assert.Contains("new object?[] { row.RequiredQty }", lookup);
        var page = Read("src/06_Web/AMES.Web/Components/Pages/Wh/Inventory.razor");
        Assert.DoesNotContain("x.Qty-x.InspectionQty", page);
        Assert.Contains("GetRemainingMaterialRequirements()", page);
        Assert.Contains("ShowProductionRequirements=\"true\"", page);
        Assert.Contains("result.SkippedNoBom.Count", page);
        Assert.Contains("WH.Inventory.RequirementsFailed", page);
    }

    [Theory]
    [InlineData("SharedResources.resx")]
    [InlineData("SharedResources.en.resx")]
    [InlineData("SharedResources.ko.resx")]
    [InlineData("SharedResources.es.resx")]
    public void Quantity_labels_exist_in_every_supported_language(string resource)
    {
        var document = XDocument.Parse(Read("src/06_Web/AMES.Web/Resources/" + resource));
        var keys = document.Root!.Elements("data").Select(e => e.Attribute("name")!.Value).ToList();
        foreach (var suffix in new[] { "CurrentQty", "StoredQty", "UnassignedQty", "RequiredQty", "RequiredQtyHint", "RequirementsFailed", "RequirementsMissingBom" })
            Assert.Single(keys, key => key == "WH.Inventory." + suffix);
    }
}
