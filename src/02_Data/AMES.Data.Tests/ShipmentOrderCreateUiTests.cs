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
        Assert.Contains("InputAttributes=\"_shipQtyInputAttributes\"", source);
        Assert.Contains("[\"data-shipment-qty\"]", source);
        Assert.Contains("js/shipment-quantity-input.js?v=20261106", Read("src/06_Web/AMES.Web/Components/App.razor"));
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

    [Fact]
    public void Dcr_preview_checks_only_customer_and_shows_selected_order_contents()
    {
        var source = Read("src/06_Web/AMES.Web/Components/Pages/Fg/ShippingOrders.razor");
        Assert.Contains("Click=\"OpenDelivery\"", source);
        Assert.Contains("SelectedDeliveryOrders.Select(o=>o.CustomerId.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)", source);
        Assert.Contains("await Dialog.Alert(L[\"FG.ShipOrders.Err.MixedCustomers\"]", source);
        Assert.Contains("<ShippingDeliveryOrderSummary Orders=\"SelectedDeliveryOrders\" />", source);
        var summary = Read("src/06_Web/AMES.Web/Components/Shared/ShippingDeliveryOrderSummary.razor");
        Assert.Contains("@foreach(var order in Orders)", summary);
        Assert.Contains("<ShipmentOrderLines Lines=\"order.Lines\" CompactPallets=\"true\" />", summary);
        var repo = Read("src/02_Data/AMES.Data/Repositories/FinishedGoodsRepository.ShippingOrders.cs");
        Assert.Contains("var (customer,date)=ShippingDeliveryHeader(selected)", repo);
        Assert.Contains("CASE WHEN @ByDeliveryDate=1 THEN COALESCE(D.ShipDate,O.ShipDate) ELSE O.ShipDate END", repo);
        Assert.Contains("P.ShipDate>=@From", repo);
        Assert.Contains("onlyOpen:true,byDeliveryDate:true", Read("src/06_Web/AMES.Web/Components/Pages/Fg/ShipmentPlan.razor"));
        foreach (var culture in new[] { "", ".en", ".es", ".ko" })
        {
            var resource = XDocument.Parse(Read($"src/06_Web/AMES.Web/Resources/SharedResources{culture}.resx"));
            var hint = Assert.Single(resource.Root!.Elements("data"),
                x => (string?)x.Attribute("name") == "FG.ShipOrders.Hint.DcrSelectedFmt");
            Assert.DoesNotContain("·", (string?)hint.Element("value") ?? "");
        }
    }

    [Fact]
    public void Dcr_creation_stays_on_orders_and_plan_rows_open_the_same_summary_in_a_popup()
    {
        var orders = Read("src/06_Web/AMES.Web/Components/Pages/Fg/ShippingOrders.razor");
        Assert.DoesNotContain("Nav.NavigateTo($\"/fg/shipment-plan", orders);
        var plan = Read("src/06_Web/AMES.Web/Components/Pages/Fg/ShipmentPlan.razor");
        Assert.Contains("RowClick=\"OpenDeliveryDetails\"", plan);
        Assert.DoesNotContain("class=\"shipment-plan-details\"", plan);
        Assert.Contains("<ShippingDeliveryOrderSummary Orders=\"_detailDelivery.Orders\" ShipDate=\"_detailDelivery.ShipDate\" />", plan);
        Assert.Contains("@onclick:stopPropagation=\"true\"", plan);
    }

    [Fact]
    public void Print_preview_separates_DCR_and_each_SHU_without_changing_existing_order_barcodes()
    {
        var source = Read("src/06_Web/AMES.Web/Components/Shared/ShippingOrderPrintPreview.razor");
        Assert.Contains("Text=\"DCR\" Click=\"@(()=>_showShu=false)\"", source);
        Assert.Contains("Text=\"SHU\" Click=\"@(()=>_showShu=true)\"", source);
        Assert.Contains("Orders.Select(order=>(IReadOnlyList<ShippingOrderDto>)new[]{order})", source);
        Assert.Contains("var reference=_showShu?documentOrders[0].OrderNo:Delivery!.DeliveryNo;", source);
        Assert.Contains("<Code128Barcode Value=\"@reference\"", source);
        Assert.Contains("@foreach(var order in documentOrders)", source);
        Assert.Contains("@line.PartNo", source);
        Assert.Contains("@UomFmt.QtyUnit(line.Qty,line.Unit)", source);
        Assert.DoesNotContain(".Replace(\"ORD", source);
        var css = Read("src/06_Web/AMES.Web/Components/Shared/ShippingOrderPrintPreview.razor.css");
        Assert.Contains("break-before:page", css);
        Assert.Contains(".shipping-a4-toolbar { display:none !important; }", css);
    }
}
