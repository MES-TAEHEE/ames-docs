using Xunit;

namespace AMES.Data.Tests;

public sealed class PdaTrailerLoadingListTests
{
    [Fact]
    public void Open_DCR_list_requires_a_session_and_does_not_write_shipping_data()
    {
        var source = Read("src/04_Api/AMES.Api/Endpoints/ShippingEndpoints.cs");
        var list = source[source.IndexOf("group.MapGet(\"/deliveries\"", StringComparison.Ordinal)..source.IndexOf("group.MapGet(\"/delivery\"", StringComparison.Ordinal)];
        Assert.Contains("if(ctx.GetSession() is null)return Results.Unauthorized()", list);
        Assert.Contains("ListOpenShippingDeliveries()", list);
        Assert.DoesNotContain("LoadShippingDelivery(", list);
        Assert.DoesNotContain("CompleteShippingDelivery(", list);
        Assert.Contains("statusCode:503", list);
        var repo = Read("src/02_Data/AMES.Data/Repositories/FinishedGoodsRepository.ShippingOrders.cs");
        var query=repo[repo.IndexOf("public List<ShippingDeliveryListDto> ListOpenShippingDeliveries()",StringComparison.Ordinal)..repo.IndexOf("public List<string> CreateShippingOrders(",StringComparison.Ordinal)];
        Assert.Contains("FROM dbo.FG_Delivery D", query);
        Assert.Contains("WHERE D.Status IN('Open','Loading')", query);
        Assert.Contains("COUNT(DISTINCT L.PartNo)", query);
        Assert.Contains("ORDER BY D.ShipDate,D.DeliveryID", query);
    }

    [Fact]
    public void Selecting_a_DCR_opens_its_detail_without_loading_a_pallet()
    {
        var page = Read("src/05_Pda/AMES.Pda/Components/Pages/Fg/Fg05TrailerLoading.razor");
        Assert.Contains("@if(_delivery is null)", page);
        Assert.Contains("protected override Task OnInitializedAsync()=>ReloadDeliveries()", page);
        Assert.Contains("_deliveries=await Api.OpenShippingDeliveriesAsync()", page);
        Assert.Contains("delivery.Status is \"Open\" or \"Loading\"", page);
        Assert.Contains("@key=\"dcr.DeliveryId\"", page);
        Assert.Contains("Disabled=\"@(_busy||!CanOpenDelivery(dcr))\"", page);
        var select = page[page.IndexOf("async Task SelectDelivery(", StringComparison.Ordinal)..page.IndexOf("protected override async Task OnAfterRenderAsync", StringComparison.Ordinal)];
        Assert.Contains("_delivery is not null||!CanOpenDelivery(delivery)", select);
        Assert.Contains("_barcode=delivery.DeliveryNo;await Scan()", select);
        Assert.DoesNotContain("LoadShippingDeliveryAsync", select);
        Assert.Contains("await ReloadDeliveries();await Focus()", page);
        Assert.Contains("No open DCRs.", page);
        Assert.DoesNotContain("order.OrderNo", page[..page.IndexOf("@if(_delivery is {} delivery)",StringComparison.Ordinal)]);
        Assert.Contains("foreach(var order in delivery.OrderNos)", page);
        var api = Read("src/05_Pda/AMES.Pda/Services/FinishedGoodsApi.cs");
        Assert.Contains("GetRequiredAsync<List<AMES.Contracts.Dto.ShippingDeliveryListDto>>(\"/api/fg/shipping/deliveries\"", api);
    }

    [Fact]
    public void Loaded_pallet_detail_list_is_hidden_but_scanned_removal_is_preserved()
    {
        var page=Read("src/05_Pda/AMES.Pda/Components/Pages/Fg/Fg05TrailerLoading.razor");
        Assert.DoesNotContain("<details>",page);
        Assert.DoesNotContain("delivery.Loads.GroupBy",page);
        Assert.Contains("LOADED PALLETS:",page);
        Assert.Contains("REMOVE PALLET BARCODE",page);
        Assert.Contains("Api.UnloadShippingDeliveryAsync(_delivery!.DeliveryNo,value)",page);
        Assert.Contains("DCR Required",page);
        Assert.Contains("Scan or select a DCR first",page);
        Assert.Contains("if(_removing&&!loaded)",page);
        Assert.Contains("if(!_removing&&loaded)",page);
        Assert.Contains("No quantity was added",page);
    }

    [Fact]
    public void Shipment_success_resets_the_DCR_and_refreshes_after_releasing_the_busy_flag()
    {
        var page=Read("src/05_Pda/AMES.Pda/Components/Pages/Fg/Fg05TrailerLoading.razor");
        var complete=page[page.IndexOf("async Task Complete()",StringComparison.Ordinal)..page.IndexOf("void Show(",StringComparison.Ordinal)];
        Assert.Contains("if(result.Success)",complete);
        Assert.Contains("ResetDelivery();",complete);
        Assert.Contains("_deliveries.RemoveAll(d=>d.DeliveryNo==deliveryNo)",complete);
        Assert.DoesNotContain("_delivery=result.Delivery",complete);
        Assert.True(complete.IndexOf("finally{_busy=false;}",StringComparison.Ordinal)<complete.IndexOf("if(completed)await ReloadDeliveries()",StringComparison.Ordinal));
        Assert.Contains("else Show(\"Shipment Failed\",result.Message)",complete);
        Assert.Contains("void ResetDelivery(){_delivery=null;_barcode=\"\";_removing=false;_scanMessage=\"\";_confirm=false;}",page);
    }

    [Fact]
    public void Duplicate_pallet_or_child_LOT_is_blocked_across_deliveries_under_the_shipping_lock()
    {
        var repo=Read("src/02_Data/AMES.Data/Repositories/FinishedGoodsRepository.ShippingOrders.cs");
        Assert.Contains("IsolationLevel.Serializable);Gate(conn,tx)",repo);
        Assert.Contains("FROM dbo.FG_DeliveryLoad WITH(UPDLOCK,HOLDLOCK) WHERE LotNo=@Lot AND ShipmentID IS NULL",repo);
        Assert.Contains("already loaded on a delivery",repo);
        Assert.Contains("@Resource='FG_SHIPPING_ORDERS'",repo);
    }

    private static string Read(string path, [System.Runtime.CompilerServices.CallerFilePath] string current = "")
        => File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(current)!, "../../..", path)));
}
