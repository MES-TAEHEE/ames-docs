using AMES.Data.Connection;
using AMES.Data.Repositories;
using Xunit;

namespace AMES.Data.Tests;

public sealed class ShipmentHistoryTests
{
    [Fact]
    public void Group_keys_do_not_collide_with_standalone_shipments()
    {
        var dcr=new FinishedGoodsRepository.ShipmentHistoryRow(1,1,"DCR-1",default,"C",null,null,1,2,2,null);
        Assert.NotEqual(dcr.Key,(dcr with {DeliveryId=null}).Key);
        Assert.Equal(dcr.Key,(dcr with {ShipmentId=2}).Key);
    }

    [Fact]
    public void Shipped_filter_preserves_all_orders_for_DCR_printing()
    {
        var source=File.ReadAllText(Path.Combine(Root(),"src/06_Web/AMES.Web/Components/Pages/Fg/ShippingOrders.razor"));
        Assert.Contains("Data=\"@VisibleOrders\"",source);
        Assert.Contains("!string.Equals(order.Status,\"Shipped\",StringComparison.OrdinalIgnoreCase)",source);
        Assert.Contains("_printOrders=_orders.Where",source);
        Assert.Contains("FG.ShipOrders.Btn.CreateOrder",source);
        Assert.Contains("FG.ShipOrders.IncludeShipped",source);
    }

    [Fact]
    public void Shipment_rows_use_inline_expandable_details_without_a_trailer_column()
    {
        var source=File.ReadAllText(Path.Combine(Root(),"src/06_Web/AMES.Web/Components/Pages/Fg/Shipments.razor"));
        Assert.DoesNotContain("Property=\"TrailerNo\"",source);
        Assert.Contains("ShowExpandColumn=\"false\"",source);
        Assert.Contains("class=\"ames-rz-btn shipment-toggle\"",source);
        Assert.Contains("IsExpanded(row) ? \"expand_more\" : \"chevron_right\"",source);
        Assert.Contains("Text=\"@row.ReferenceNo\" Click=\"@(()=>ToggleDetails(row))\"",source);
        Assert.Contains("await _grid.ExpandRow(row)",source);
        Assert.Contains("<Template Context=\"shipment\">",source);
        Assert.DoesNotContain("RowClick=",source);
        Assert.DoesNotContain("RowRender=",source);
        Assert.Contains("OpenLots(shipment,item)",source);
        Assert.Contains("ListShipmentHistoryLots(shipment.DeliveryId,shipment.ShipmentId,part.PalletNo,part.PartNo)",source);
        Assert.Contains("Property=\"LotNo\"",source);
        Assert.Contains("request==_lotRequest",source);
        Assert.Contains("ShipmentPalletPartRow",source);
        Assert.Contains("@onclick:stopPropagation=\"true\"",source);
        Assert.Contains("_children.ContainsKey(row.Key)",source);
    }

    [SkippableFact]
    public void Shipped_DCR_totals_and_details_are_not_multiplied_by_load_rows()
    {
        var connection=Environment.GetEnvironmentVariable("AMES_SHIPPING_READ_TEST_DB");
        Skip.If(string.IsNullOrWhiteSpace(connection),"Opt-in SELECT-only shipment history verification.");
        var repo=new FinishedGoodsRepository(new AmesConnectionFactory(connection!));
        var rows=repo.ListShipmentHistory("DCR-261019-000001");
        var dcr=Assert.Single(rows);
        Assert.Equal(1,dcr.DeliveryId);
        Assert.Equal(6,dcr.PalletCount);
        Assert.Equal(140,dcr.ItemCount);
        Assert.Equal(140m,dcr.Qty);
        var items=repo.ListShipmentHistoryParts(dcr.DeliveryId,dcr.ShipmentId);
        Assert.Equal(6,items.Count);
        Assert.Equal(items.Count,items.Select(x=>(x.PalletNo,x.PartNo,x.Unit)).Distinct().Count());
        Assert.Equal(dcr.Qty,items.Sum(x=>x.Qty));
        Assert.Equal(6,items.Select(x=>x.PalletNo).Distinct().Count());
        Assert.Equal(4m,Assert.Single(items,x=>x.PalletNo=="EOS-PLT-2610-0006").Qty);
        Assert.Equal("M2310-PI010NNB",Assert.Single(items,x=>x.PalletNo=="EOS-PLT-2610-0006").PartNo);
        foreach(var part in items)
        {
            var lots=repo.ListShipmentHistoryLots(dcr.DeliveryId,dcr.ShipmentId,part.PalletNo,part.PartNo);
            Assert.Equal(part.Qty,lots.Sum(x=>x.Qty));
            Assert.All(lots,x=>Assert.Equal(part.PartNo,x.PartNo));
            Assert.Equal(lots.Count,lots.Select(x=>x.TransactionId).Distinct().Count());
            if(part.PalletNo=="EOS-PLT-2610-0006") Assert.Equal(4,lots.Count);
        }
        Assert.Empty(repo.ListShipmentHistoryLots(dcr.DeliveryId,dcr.ShipmentId,"not-this-pallet","M2310-PI010NNB"));
        Assert.Equal(dcr,Assert.Single(repo.ListShipmentHistory("EOS-PLT-2610-0006")));
        foreach(var direct in repo.ListShipmentHistory().Where(x=>x.DeliveryId is null))
        {
            var original=Assert.Single(repo.ListShipments(direct.ReferenceNo),x=>x.ShipmentId==direct.ShipmentId);
            Assert.Equal(original.Qty,direct.Qty);
            Assert.Equal(original.ItemCount,direct.ItemCount);
            Assert.Equal(original.Qty,repo.ListShipmentHistoryParts(null,direct.ShipmentId).Sum(x=>x.Qty));
            foreach(var part in repo.ListShipmentHistoryParts(null,direct.ShipmentId))
                Assert.Equal(part.Qty,repo.ListShipmentHistoryLots(null,direct.ShipmentId,part.PalletNo,part.PartNo).Sum(x=>x.Qty));
        }
    }

    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string file="")
        =>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../.."));
}
