using Xunit;

namespace AMES.Api.Tests;

public sealed class PdaAdjustmentConfirmationTests
{
    [Fact]
    public void Save_first_opens_quantity_confirmation_and_only_yes_executes_the_captured_request()
    {
        var page=Read("Wh03InventoryStatus.razor");
        var start=page.IndexOf("private void PostInventoryAdjust()",StringComparison.Ordinal);
        var end=page.IndexOf("private async Task SaveInventoryAdjust",start,StringComparison.Ordinal);
        var confirm=page[start..end];
        Assert.Contains("ShowAlert(\"Confirm Quantity\"",confirm);
        Assert.Contains("_modalConfirmAction=()=>SaveInventoryAdjust(request);",confirm);
        Assert.DoesNotContain("WhSaveAdjustQtyAsync",confirm);
        Assert.Contains("Click=\"@ConfirmModalAction\" Text=\"YES\"",page);
        Assert.Contains("Click=\"@DismissModal\" Text=\"NO\"",page);
        Assert.Contains("_modalConfirmAction = null;",page);
        Assert.Contains("if(action is not null)await action();",page);
    }

    [Fact]
    public void Reprint_is_asked_only_after_success_and_its_button_shares_the_action_row_alignment()
    {
        var page=Read("Wh03InventoryStatus.razor");
        var save=page[page.IndexOf("private async Task SaveInventoryAdjust",StringComparison.Ordinal)..];
        Assert.True(save.IndexOf("if (!result.Success)",StringComparison.Ordinal)<save.IndexOf("ConfirmAdjustedLabelReprint();",StringComparison.Ordinal));
        Assert.Contains("ShowNavigationAlert(\"Reprint Label\", \"Adjustment saved. Would you like to reprint the label?\", target, \"YES\", \"NO\")",page);
        var reprint=page[page.IndexOf("private void ConfirmAdjustedLabelReprint()",StringComparison.Ordinal)..page.IndexOf("private void ShowAlert",StringComparison.Ordinal)];
        Assert.DoesNotContain("Confirm Quantity",reprint);
        Assert.Contains("wh03-action-row {(_adjustedLabelLotNo",page);
        Assert.Contains("label-action wh03-reprint-btn",page);
        var css=Read("Wh03InventoryStatus.razor.css");
        Assert.Contains("grid-template-columns:repeat(2,minmax(0,1fr))",css);
        Assert.Contains("grid-column:1 / -1; width:100% !important",css);
        Assert.Contains("align-items:center !important",css);
        Assert.Contains("line-height:1 !important",css);
        Assert.Contains("margin:0 !important",css);
    }

    [Fact]
    public void Both_reprint_previews_vertically_center_printer_names_labels_and_buttons()
    {
        var regular=Read("WhLabelPrint.razor.css");
        Assert.Contains("flex-direction:column; justify-content:center; align-items:flex-start",regular);
        Assert.Contains("display:inline-flex; align-items:center; justify-content:center; line-height:1",regular);
        var global=File.ReadAllText(Path.GetFullPath(Path.Combine(SourceDirectory(),"../../05_Pda/AMES.Pda/wwwroot/css/pda.css")));
        var start=global.IndexOf("body .pda-shell.wh-adjust-box-label :is(.label-action, .label-printer-choice)",StringComparison.Ordinal);
        var end=global.IndexOf("body .pda-shell.wh-adjust-box-label .label-printer-row .label-action",start,StringComparison.Ordinal);
        var styles=global[start..end];
        Assert.Contains("display: flex;",styles);Assert.Contains("justify-content: center;",styles);Assert.Contains("align-items: center;",styles);
        Assert.Contains("body .pda-shell.wh-adjust-box-label .label-printer-row > label",global);
    }

    [Fact]
    public void Adjust_reprint_shares_the_preview_print_button_style_instead_of_the_clear_button_style()
    {
        var global=File.ReadAllText(Path.GetFullPath(Path.Combine(SourceDirectory(),"../../05_Pda/AMES.Pda/wwwroot/css/pda.css")));
        Assert.Contains("body .pda-shell.wh-adjust-box-label :is(.label-action, .label-printer-choice),\nbody .pda-shell.wh.wh03-shell .wh03-reprint-btn.rz-button",global.Replace("\r\n","\n"));
        Assert.Contains("body .pda-shell.wh-adjust-box-label > .pda-bd > .label-action,\nbody .pda-shell.wh.wh03-shell .wh03-reprint-btn.rz-button",global.Replace("\r\n","\n"));
        var css=Read("Wh03InventoryStatus.razor.css");
        Assert.Contains("height:52px !important; padding:10px 14px !important; border-radius:6px !important;",css);
        Assert.DoesNotContain("background:#172737",css);
        Assert.DoesNotContain("wh03-date-clear wh03-reprint-btn",Read("Wh03InventoryStatus.razor"));
    }

    static string SourceDirectory([System.Runtime.CompilerServices.CallerFilePath]string source="")=>Path.GetDirectoryName(source)!;

    static string Read(string name,[System.Runtime.CompilerServices.CallerFilePath]string source="")
        =>File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!,"../../05_Pda/AMES.Pda/Components/Pages/Wh",name)));
}
