using Xunit;

namespace AMES.Data.Tests;

public sealed class PdaTrailerLoadingListTests
{
    [Fact]
    public void Open_order_list_requires_a_session_and_does_not_write_shipping_data()
    {
        var source = Read("src/04_Api/AMES.Api/Endpoints/ShippingEndpoints.cs");
        var list = source[source.IndexOf("group.MapGet(\"/orders\"", StringComparison.Ordinal)..source.IndexOf("group.MapGet(\"/delivery\"", StringComparison.Ordinal)];
        Assert.Contains("if(ctx.GetSession() is null)return Results.Unauthorized()", list);
        Assert.Contains("ListShippingOrders(onlyOpen:true)", list);
        Assert.DoesNotContain("LoadShippingDelivery(", list);
        Assert.DoesNotContain("CompleteShippingDelivery(", list);
        Assert.Contains("statusCode:503", list);
        var repo = Read("src/02_Data/AMES.Data/Repositories/FinishedGoodsRepository.ShippingOrders.cs");
        Assert.Contains("@OnlyOpen=0 OR O.Status IN('Open','Loading')", repo);
        Assert.Contains("ORDER BY O.ShipDate,O.OrderID,L.OrderLineID", repo);
    }

    [Fact]
    public void Selecting_an_assigned_order_uses_the_same_scan_flow_and_cannot_load_a_pallet()
    {
        var page = Read("src/05_Pda/AMES.Pda/Components/Pages/Fg/Fg05TrailerLoading.razor");
        Assert.Contains("@if(_delivery is null)", page);
        Assert.Contains("protected override Task OnInitializedAsync()=>ReloadOrders()", page);
        Assert.Contains("_orders=await Api.OpenShippingOrdersAsync()", page);
        Assert.Contains("order.DeliveryId is not null&&!string.IsNullOrWhiteSpace(order.DeliveryNo)", page);
        Assert.Contains("order.Status is \"Open\" or \"Loading\"", page);
        Assert.Contains("Disabled=\"@(_busy||!CanOpenOrder(order))\"", page);
        var select = page[page.IndexOf("async Task SelectOrder(", StringComparison.Ordinal)..page.IndexOf("protected override async Task OnAfterRenderAsync", StringComparison.Ordinal)];
        Assert.Contains("_delivery is not null||!CanOpenOrder(order)", select);
        Assert.Contains("_barcode=order.OrderNo;await Scan()", select);
        Assert.DoesNotContain("LoadShippingDeliveryAsync", select);
        Assert.Contains("await ReloadOrders();await Focus()", page);
        Assert.Contains("No open shipment orders.", page);
        Assert.Contains("DCR not assigned", page);
        var api = Read("src/05_Pda/AMES.Pda/Services/FinishedGoodsApi.cs");
        Assert.Contains("GetRequiredAsync<List<AMES.Contracts.Dto.ShippingOrderDto>>(\"/api/fg/shipping/orders\"", api);
    }

    private static string Read(string path, [System.Runtime.CompilerServices.CallerFilePath] string current = "")
        => File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(current)!, "../../..", path)));
}
