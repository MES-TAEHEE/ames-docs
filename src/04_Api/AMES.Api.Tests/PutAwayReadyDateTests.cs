using AMES.Pda.Components.Pages.Wh;
using Xunit;

namespace AMES.Api.Tests;

public sealed class PutAwayReadyDateTests
{
    [Theory]
    [InlineData(true, "10/29/2026")]
    [InlineData(false, "10-29 23:59")]
    public void Ready_date_includes_year_without_changing_WH_date_or_shifting_days(bool fg, string expected)
    {
        Assert.Equal(expected, WhPutAway.DateText(new DateTime(2026, 10, 29, 23, 59, 0, DateTimeKind.Utc), fg));
        Assert.Equal("-", WhPutAway.DateText(null, fg));
    }

    [Fact]
    public void Unified_putaway_labels_and_formats_FG_ready_dates()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src/05_Pda/AMES.Pda"))) root = root.Parent;
        Assert.NotNull(root);
        var page = File.ReadAllText(Path.Combine(root.FullName, "src/05_Pda/AMES.Pda/Components/Pages/Wh/WhPutAway.razor"));
        Assert.Contains("<span>READY DATE</span>", page);
        Assert.Contains("DateText(box.ReceivedAt, _fgScan is not null)", page);
    }
}
