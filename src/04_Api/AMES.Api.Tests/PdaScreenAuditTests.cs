using Xunit;

namespace AMES.Api.Tests;

// Source guards complement HTTP tests; they do not replace device interaction tests.
public sealed class PdaScreenAuditTests
{
    [Theory]
    [InlineData("Wh/Wh02PdaInbound.razor", "IsBusy", "_barcode = value;")]
    [InlineData("Wh/Wh07PdaRelease.razor", "_isBusy", "await ProcessBarcodeAsync(value);")]
    [InlineData("Fg/FgRtnReturn.razor", "_isBusy", "_barcode = NormalizeScan(scanText);")]
    public void Busy_or_open_modal_scans_cannot_replace_the_active_document(string file, string busy, string mutation)
    {
        var source = Read("Components/Pages/" + file);
        var start = source.IndexOf("public async Task OnHardwareScanAsync(", StringComparison.Ordinal);
        var end = source.IndexOf(mutation, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var beforeMutation = source[start..end];
        Assert.Contains(busy, beforeMutation);
        Assert.Contains("_modalOpen", beforeMutation);
    }

    [Fact]
    public void Typed_picking_scans_are_also_serialized()
    {
        var source = Read("Components/Pages/Wh/Wh07PdaRelease.razor");
        var start = source.IndexOf("private async Task ProcessBarcodeAsync(", StringComparison.Ordinal);
        var end = source.IndexOf("_barcode = \"\";", start, StringComparison.Ordinal);
        Assert.Contains("if (_isBusy || _modalOpen) return;", source[start..end]);
    }

    [Fact]
    public void Supplier_refresh_retries_missing_database_dates_and_clears_failed_results()
    {
        var source = Read("Components/Pages/Wh/Wh01DeliverySchedule.razor");
        var load = source[source.IndexOf("async Task Load()", StringComparison.Ordinal)..];
        Assert.Contains("await Api.DatabaseTimeAsync()", load);
        Assert.Contains("_from ??=", load);
        Assert.Contains("_to ??=", load);
        Assert.Contains("catch (Exception ex) { _rows = [];", load);
    }

    [Fact]
    public void Reprint_initialization_handles_service_failure_and_opens_only_fully_loaded_selection()
    {
        var source = Read("Components/Pages/Sp/SpMaster.razor");
        var start = source.IndexOf("_fromTransactions = true;", StringComparison.Ordinal);
        var end = source.IndexOf("protected override async Task OnAfterRenderAsync", start, StringComparison.Ordinal);
        Assert.Contains("catch (Exception ex)", source[start..end]);
        start = source.IndexOf("private async Task Select(", StringComparison.Ordinal);
        end = source.IndexOf("private void ToggleCreate()", start, StringComparison.Ordinal);
        var select = source[start..end];
        Assert.True(select.IndexOf("await Api.LabelsAsync", StringComparison.Ordinal)
            < select.IndexOf("_selected = selected;", StringComparison.Ordinal));
    }

    [Fact]
    public void Label_allocation_is_acknowledged_before_any_refresh_can_fail()
    {
        var source = Read("Components/Pages/Sp/SpMaster.razor");
        var start = source.IndexOf("var created = await Api.CreateLabelsAsync", StringComparison.Ordinal);
        var refresh = source.IndexOf("await Api.LabelsAsync", start, StringComparison.Ordinal);
        var selection = source.IndexOf("_selectedSerials.Clear();", start, StringComparison.Ordinal);
        Assert.True(selection < refresh, "Saved serials must replace pending selection before a failing refresh can cause duplicate allocation.");
    }

    private static string Read(string relative, [System.Runtime.CompilerServices.CallerFilePath] string file = "")
        => File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../05_Pda/AMES.Pda", relative)));
}
