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
        foreach (var key in new[] { "TitleFmt" })
        {
            var previewLabel = Assert.Single(resource.Root.Elements("data"),
                x => (string?)x.Attribute("name") == $"FG.OrderCreate.Preview.{key}");
            Assert.False(string.IsNullOrWhiteSpace((string?)previewLabel.Element("value")));
        }
    }

    [Fact]
    public void Live_preview_uses_positive_quantities_and_the_same_customer_grouping_as_creation()
    {
        var source = Read("src/06_Web/AMES.Web/Components/Shared/ShipmentOrderCreate.razor");
        Assert.Contains("Change=\"@(v=>_quantities[r.SoId]=v)\"", source);
        Assert.Contains("Immediate=\"true\"", source);
        Assert.Contains("_rows.Where(r=>_quantities.GetValueOrDefault(r.SoId)>0).OrderBy(r=>r.SoId).GroupBy(r=>r.CustomerId)", source);
        Assert.Contains("<ShipmentOrderLines Lines=\"lines\" CompactPallets=\"true\" />", source);
        Assert.Contains("@if(previewOrders.Count>0)", source);
        Assert.DoesNotContain("FG.OrderCreate.Preview.Hint", source);
        Assert.DoesNotContain("FG.OrderCreate.Preview.Empty", source);
        var lines = Read("src/06_Web/AMES.Web/Components/Shared/ShipmentOrderLines.razor");
        Assert.Contains("@if(CompactPallets)", lines);
        Assert.Contains("PLT({UomFmt.Qty(line.Qty,null)}/{UomFmt.Qty(line.PalletQty.Value,null)})", lines);
        Assert.Contains("[Parameter] public bool CompactPallets { get; set; }", lines);
        Assert.Contains("_shipDate?.ToString(DateFmt.Date)", source);
        Assert.Contains("item?.DefaultUOM,item?.PalletQty,item?.ToteFlag??false", source);
        var repo = Read("src/02_Data/AMES.Data/Repositories/FinishedGoodsRepository.ShippingOrders.cs");
        Assert.Contains("input.OrderBy(x=>x.SoId)", repo);
        Assert.Contains("selected.GroupBy(x=>x.Customer)", repo);
        var css = Read("src/06_Web/AMES.Web/Components/Shared/ShipmentOrderCreate.razor.css");
        Assert.Contains("max-height:320px;overflow:auto", css);
    }

    private static string Read(string path, [System.Runtime.CompilerServices.CallerFilePath] string current = "")
        => File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(current)!, "../../..", path)));

    [Fact]
    public void Order_and_dcr_actions_share_the_list_footer_without_delivery_risk_ui()
    {
        var source = Read("src/06_Web/AMES.Web/Components/Pages/Fg/ShippingOrders.razor");
        var gridEnd = source.IndexOf("</RadzenDataGrid>", StringComparison.Ordinal);
        var footerStart = source.IndexOf("<RadzenStack", gridEnd, StringComparison.Ordinal);
        var footerEnd = source.IndexOf("</RadzenStack>", footerStart, StringComparison.Ordinal);
        var footer = source[footerStart..footerEnd];
        Assert.Contains("Orientation=\"Orientation.Horizontal\"", footer);
        Assert.Contains("shipping-order-actions", footer);
        Assert.Contains("FG.ShipOrders.Btn.CreateOrder", footer);
        Assert.Contains("FG.ShipOrders.Btn.CreateDcr", footer);
        Assert.Contains("Disabled=\"@(_selected.Count==0)\"", footer);
        Assert.DoesNotContain("FG.ShipOrders.Btn.CreateOrder", source[..gridEnd]);
        Assert.DoesNotContain("DueRisk", source);
        Assert.DoesNotContain("DueRisk", Read("src/06_Web/AMES.Web/Components/Shared/ShipmentOrderCreate.razor"));
    }
}
