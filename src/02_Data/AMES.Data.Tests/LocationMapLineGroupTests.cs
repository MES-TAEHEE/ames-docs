using Xunit;

namespace AMES.Data.Tests;

public sealed class LocationMapLineGroupTests
{
    [Fact]
    public void Initial_map_renders_loading_before_any_database_load_or_empty_cards()
    {
        foreach(var module in new[]{"Wh","Fg"})
        {
            var page=Read($"src/06_Web/AMES.Web/Components/Pages/{module}/LocationMap.razor");
            Assert.Contains("private bool _loading = true;",page);
            Assert.Contains("OnAfterRenderAsync(bool firstRender)",page);
            Assert.Contains("if (!firstRender) return;",page);
            Assert.Contains("finally { _loading = false; await InvokeAsync(StateHasChanged); }",page);
            Assert.Contains("<LocationMapLoading />",page);
            Assert.DoesNotContain("Msg.Loading",page);
            Assert.True(page.IndexOf("@if (_loading)",StringComparison.Ordinal)<page.IndexOf("<LocationMapExplorer",StringComparison.Ordinal));
            Assert.DoesNotContain("OnInitializedAsync",page);
        }
    }
    [Fact]
    public void Cards_wait_for_interactivity_show_loading_and_reject_duplicate_or_stale_clicks()
    {
        var page=Read("src/06_Web/AMES.Web/Components/Shared/LocationMapExplorer.razor");
        Assert.Contains("if (!_interactiveReady || _navigating)",page);
        Assert.Contains("Disabled=\"@(!_interactiveReady || _navigating || _saving)\"",page);
        Assert.Contains("<LocationMapLoading />",page);
        Assert.DoesNotContain("Msg.Loading",page);
        Assert.Contains("OpenStageRowAsync(row, cardScope)",page);
        Assert.Contains("scope != SelectionScope",page);
        Assert.Contains("if (firstRender || _navigationRendered)",page);
        Assert.Contains("finally { _navigationRendered = true; }",page);
        Assert.Contains("_dialog is not null || scope != SelectionScope",page);
        foreach(var module in new[]{"Wh","Fg"})
            Assert.Contains("await Task.Run(() => LocationMapDatabase.Load",Read($"src/06_Web/AMES.Web/Components/Pages/{module}/LocationMap.razor"));
    }
    [Fact]
    public void Map_loading_uses_a_small_spinner_and_an_existing_translated_label_not_a_full_width_bar()
    {
        var loader=Read("src/06_Web/AMES.Web/Components/Shared/LocationMapLoading.razor");
        Assert.Contains("<RadzenProgressBarCircular",loader);
        Assert.Contains("width:24px;height:24px;flex:none;",loader);
        Assert.Contains("ProgressBarMode.Indeterminate",loader);
        Assert.Contains("Lbl.Loading",loader);
        Assert.DoesNotContain("Msg.Loading",loader);
        Assert.DoesNotContain("<RadzenProgressBar ",loader);
        foreach(var language in new[]{"",".en",".ko",".es"})
        {
            var resources=System.Xml.Linq.XDocument.Parse(Read($"src/06_Web/AMES.Web/Resources/SharedResources{language}.resx"));
            var label=Assert.Single(resources.Root!.Elements("data"),entry=>(string?)entry.Attribute("name")=="Lbl.Loading");
            Assert.False(string.IsNullOrWhiteSpace(label.Element("value")?.Value));
        }
    }
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
