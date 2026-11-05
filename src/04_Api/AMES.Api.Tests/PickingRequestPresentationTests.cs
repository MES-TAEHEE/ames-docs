using Xunit;

namespace AMES.Api.Tests;

public sealed class PickingRequestPresentationTests
{
    [Fact]
    public void Scan_validation_service_and_permission_errors_use_the_shared_modal_and_block_followup_actions()
    {
        var page=Read("src/05_Pda/AMES.Pda/Components/Pages/Wh/WhPickingRequest.razor");
        Assert.Contains("role=\"alertdialog\"",page);
        Assert.Contains("wh-modal-card warn",page);
        Assert.Contains("if(_scanning || _error.Length>0)return;",page);
        Assert.Contains("await Api.WhInventoryScanAsync(value)",page);
        Assert.Contains("ShowAlert(\"Invalid Line Location\"",page);
        Assert.Contains("ShowAlert(\"Line Location Required\"",page);
        Assert.Contains("ShowAlert(\"Unable to Load\"",page);
        Assert.Contains("ShowAlert(\"Permission Required\"",page);
        Assert.Contains("ShowAlert(\"Request Failed\"",page);
        Assert.Contains("_locationBarcode=_destination;_partBarcode=_part;_focusScan=true;",page);
        Assert.DoesNotContain("<div class=\"pda-status bad\"",page);
    }

    [Fact]
    public void Web_keeps_only_orders_and_line_stock_tabs_and_hides_request_navigation_without_deleting_permissions()
    {
        var page=Read("src/06_Web/AMES.Web/Components/Pages/Wh/PickingOrders.razor");
        Assert.Contains("<AmesTab Title=\"@L[\"WH.Picking.Title\"].Value\">",page);
        Assert.Contains("<AmesTab Title=\"@L[\"WH.Picking.Tab.LineStock\"].Value\">",page);
        Assert.DoesNotContain("<PickingRequests",page);
        Assert.DoesNotContain("WH.Picking.Request.Title",page);
        var catalog=Read("src/06_Web/AMES.Web/Services/MenuCatalog.cs");
        Assert.Contains("path.Equals(\"wh/picking-requests\", StringComparison.OrdinalIgnoreCase)",catalog);
        Assert.DoesNotContain("new(\"wh\", \"wh/picking-requests\"",catalog);
        Assert.Contains("const string Href=\"wh/picking-requests\"",Read("src/06_Web/AMES.Web/Components/Pages/Wh/PickingRequests.razor"));
    }

    static string Read(string path,[System.Runtime.CompilerServices.CallerFilePath]string source="")
        =>File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!,"../../..",path)));
}
