using Xunit;

namespace AMES.Data.Tests;

public sealed class PdaPickingRefreshTests
{
    [Fact]
    public void Refresh_has_pressed_loading_and_completion_feedback_and_prevents_duplicate_requests()
    {
        var page = Read("Wh07PdaRelease.razor");
        Assert.Contains("disabled=\"@(_isBusy || _loadingPickingOrders)\"", page);
        Assert.Contains("aria-busy=\"@(_loadingPickingOrders ? \"true\" : \"false\")\"", page);
        Assert.Contains("\"REFRESHING...\" : \"REFRESH\"", page);
        Assert.Contains("Loading picking orders...", page);
        Assert.Contains("Picking list updated.", page);
        Assert.Contains("if (_loadingPickingOrders) return;", page);
        Assert.Contains("await InvokeAsync(StateHasChanged);", page);
        Assert.Contains("finally { _loadingPickingOrders = false; }", page);
        Assert.Contains("if (_isBusy || _loadingPickingOrders || _modalOpen) return;", page);
        var css = Read("Wh07PdaRelease.razor.css");
        Assert.Contains("button.wh07-refresh:active", css);
        Assert.Contains("button.wh07-refresh.refreshing", css);
        Assert.Contains("animation: wh07-refresh-spin", css);
    }

    [Fact]
    public void Refresh_updates_both_views_together_and_clears_both_on_failure_without_generating_orders()
    {
        var page = Read("Wh07PdaRelease.razor");
        var start = page.IndexOf("private async Task LoadOpenPickingOrdersAsync()", StringComparison.Ordinal);
        var end = page.IndexOf("private async Task SelectPickingOrderAsync", start, StringComparison.Ordinal);
        var refresh = page[start..end];
        Assert.Contains("_pickingListUpdated = false;", refresh);
        Assert.Contains("await Task.WhenAll(orders, parts);", refresh);
        Assert.True(refresh.IndexOf("await Task.WhenAll", StringComparison.Ordinal) <
            refresh.IndexOf("_openPickingOrders =", StringComparison.Ordinal));
        Assert.Contains("_pickingListUpdated = true;", refresh);
        Assert.Contains("_openPickingOrders.Clear();", refresh);
        Assert.Contains("_pickingParts.Clear();", refresh);
        Assert.DoesNotContain("Generate", refresh);
    }

    private static string Read(string name, [System.Runtime.CompilerServices.CallerFilePath] string file = "")
        => File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,
            "../../05_Pda/AMES.Pda/Components/Pages/Wh", name)));
}
