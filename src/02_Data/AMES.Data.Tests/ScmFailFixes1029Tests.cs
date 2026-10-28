using AMES.Data.Repositories;
using Xunit;

namespace AMES.Data.Tests;

public sealed class ScmFailFixes1029Tests
{
    static string Read(string path,[System.Runtime.CompilerServices.CallerFilePath]string file="")
        =>File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../..",path)));

    [Theory]
    [InlineData("Registered:0", "Registered")]
    [InlineData("Shipped:0", "Shipped")]
    [InlineData("Shipped:40", "PartiallyReceived")]
    [InlineData("Shipped:0|Received:25", "PartiallyReceived")]
    [InlineData("Received:60|Received:40", "Received")]
    [InlineData("Received:60|Cancelled:0", "Received")]
    [InlineData("Cancelled:0|Cancelled:0", "Cancelled")]
    [InlineData("Shipped:0|Cancelled:5", "Shipped")]
    public void Delivery_note_status_reads_partial_receipts_from_quantities(string lines, string expected)
    {
        var deliveries = lines.Split('|').Select(x => x.Split(':')).Select(x => (x[0], decimal.Parse(x[1])));
        Assert.Equal(expected, ScmRepository.DeliveryNoteStatus(deliveries));
    }

    [Fact]
    public void Portal_note_filter_and_column_use_the_same_status()
    {
        var page = Read("src/06_Web/AMES.Web/Components/Shared/ScmPreview.razor");
        Assert.Contains(".Where(g=>string.IsNullOrEmpty(_status)||NoteStatus(g)==_status)", page);
        Assert.Contains("ScmRepository.DeliveryNoteStatus(Store.Deliveries", page);
        Assert.DoesNotContain("g.Deliveries.Any(d=>d.Status==_status)", page);
    }

    [Fact]
    public void Missing_vendor_stops_with_the_select_vendor_message()
    {
        var page = Read("src/06_Web/AMES.Web/Components/Shared/ScmPreview.razor");
        var guard = page.IndexOf("if (string.IsNullOrWhiteSpace(order.Vendor)) { _error = L[\"Scm.Text.SelectAVendorFirst\"]; return false; }", StringComparison.Ordinal);
        Assert.True(guard > 0);
        Assert.True(guard < page.IndexOf("if (!LoadMaterials(order.Vendor)) return false;", StringComparison.Ordinal));
    }

    [Fact]
    public void Shipping_unit_order_table_shows_confirmed_like_the_order_list()
    {
        var page = Read("src/06_Web/AMES.Web/Components/Pages/Portal/DeliveryCases.razor");
        Assert.Contains("<span class=\"case-confirmed\">@L[\"Scm.Text.Confirmed\"]</span>", page);
        Assert.DoesNotContain("Scm.Text.Confirmed.538025", page);
    }
}
