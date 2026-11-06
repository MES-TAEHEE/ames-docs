using System.Xml.Linq;
using Xunit;

namespace AMES.Data.Tests;

public sealed class ShipmentOrderCreateUiTests
{
    [Fact]
    public void Due_date_is_between_so_and_customer_and_only_past_dates_are_red()
    {
        var source = Read("src/06_Web/AMES.Web/Components/Shared/ShipmentOrderCreate.razor");
        var so = source.IndexOf("Property=\"SoNumber\"", StringComparison.Ordinal);
        var due = source.IndexOf("Property=\"RequestedDeliveryDate\"", StringComparison.Ordinal);
        var customer = source.IndexOf("Property=\"CustomerId\"", StringComparison.Ordinal);
        Assert.True(so >= 0 && so < due && due < customer);
        Assert.Equal(1, source.Split("Property=\"RequestedDeliveryDate\"").Length - 1);
        Assert.DoesNotContain("Col.Requested", source);
        Assert.Contains("due.Date < DbClock.Today", source);
        Assert.Contains("r.RequestedDeliveryDate is {} due", source);
        Assert.Contains("r.RequestedDeliveryDate?.ToString(DateFmt.Date) ?? \"—\"", source);
        Assert.Contains("\"order-due-overdue\" : null", source);
        var css = Read("src/06_Web/AMES.Web/Components/Shared/ShipmentOrderCreate.razor.css");
        Assert.Contains(".order-due-overdue {color:var(--ames-tx-red-400);", css);
    }

    [Theory]
    [InlineData("", "납기일")]
    [InlineData(".ko", "납기일")]
    [InlineData(".en", "Due Date")]
    [InlineData(".es", "Fecha de vencimiento")]
    public void Due_date_label_is_localized_without_changing_other_screens(string culture, string expected)
    {
        var resource = XDocument.Parse(Read($"src/06_Web/AMES.Web/Resources/SharedResources{culture}.resx"));
        var label = Assert.Single(resource.Root!.Elements("data"),
            x => (string?)x.Attribute("name") == "FG.OrderCreate.Col.DueDate");
        Assert.Equal(expected, (string?)label.Element("value"));
    }

    private static string Read(string path, [System.Runtime.CompilerServices.CallerFilePath] string current = "")
        => File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(current)!, "../../..", path)));
}
