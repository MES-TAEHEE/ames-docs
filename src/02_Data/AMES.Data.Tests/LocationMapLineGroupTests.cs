using Xunit;

namespace AMES.Data.Tests;

public sealed class LocationMapLineGroupTests
{
    [Fact]
    public void Line_group_membership_uses_location_line_ID_and_the_selected_warehouse_not_display_names()
    {
        var load=Read("src/06_Web/AMES.Web/Components/Shared/LocationMapDatabase.cs");
        Assert.Contains("IsLineArea = locations.Any(location => !string.IsNullOrWhiteSpace(location.LineId)",load);
        Assert.Contains("string.Equals(location.WhCode, area.WarehouseCode, StringComparison.OrdinalIgnoreCase)",load);
        Assert.Contains("string.Equals(location.AreaCode, area.AreaCode, StringComparison.OrdinalIgnoreCase)",load);
        Assert.DoesNotContain("StartsWith(\"Line",load);
        Assert.DoesNotContain("INSERT ",load);
        Assert.DoesNotContain("UPDATE ",load);
        Assert.Contains("bool IsLineArea = false",Read("src/06_Web/AMES.Web/Components/Shared/LocationMapData.cs"));
    }

    [Fact]
    public void Virtual_line_card_is_navigation_only_but_real_line_area_editing_and_stock_guards_remain()
    {
        var page=Read("src/06_Web/AMES.Web/Components/Shared/LocationMapExplorer.razor");
        Assert.Contains("lines.Sum(x => x.LocationCount), lines.Sum(x => x.Qty), IsLineGroup: true",page);
        Assert.Contains("if (row.IsLineGroup) { ShowLineAreas(); return; }",page);
        Assert.Contains("CanAddHierarchy && !row.IsLineGroup",page);
        Assert.Contains("if (_stage == MapStage.Area && key == LineGroupKey) return;",page);
        Assert.Contains("var area = _stage is MapStage.Area or MapStage.Line ? key : _area",page);
        Assert.Contains("row.IsLineGroup || _stage is not (MapStage.Warehouse or MapStage.Area or MapStage.Line)",page);
        Assert.Contains("if (impact.StockRows > 0)",page);
        Assert.Contains("Warehouse.RenameLocationMapArea(_warehouse!, _code, _name)",page);
        Assert.Contains("if (kind == \"Area\" && _stage == MapStage.Line) return;",page);
    }

    [Fact]
    public void Line_maps_return_to_the_line_list_and_breadcrumbs_keep_the_extra_level()
    {
        var page=Read("src/06_Web/AMES.Web/Components/Shared/LocationMapExplorer.razor");
        Assert.Contains("MapStage { Warehouse, Area, Line, Map }",page);
        Assert.Contains("else if (_stage == MapStage.Map && SelectedArea?.IsLineArea == true) ShowLineAreas();",page);
        Assert.Contains("else if (_stage is MapStage.Map or MapStage.Line) ShowAreas();",page);
        Assert.Contains("Click=\"ShowLineAreas\"",page);
        Assert.Contains("var visible = cards.Where(x => !IsLineArea(x.Key)).ToList();",page);
        Assert.Contains("if (_stage == MapStage.Line) return cards.Where(x => IsLineArea(x.Key)).ToList();",page);
        Assert.Contains("else if (_stage == MapStage.Line && !AreaCards.Any(x => IsLineArea(x.Key))) ShowAreas();",page);
        foreach(var language in new[]{"",".en",".ko",".es"})
            Assert.Contains("name=\"WH.LocMap.LineArea\"",Read($"src/06_Web/AMES.Web/Resources/SharedResources{language}.resx"));
    }

    private static string Read(string path,[System.Runtime.CompilerServices.CallerFilePath]string source="")
        =>File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!,"../../..",path)));
}
