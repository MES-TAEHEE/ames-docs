using AMES.Data.Repositories;
using AMES.Web.Services;
using Xunit;

namespace AMES.Data.Tests;

public sealed class InventoryQuantityChangesTests
{
    [Fact]
    public void Daily_changes_use_opening_stock_and_include_adjustments_and_cancellations()
    {
        var start=new DateTime(2026,1,2);
        RptRepository.InventoryDailyBalance[] balances=[
            new(start.AddDays(-1),"WH","RESIN","G",100),new(start,"WH","RESIN","G",115),
            new(start.AddDays(1),"WH","RESIN","G",95),new(start.AddDays(2),"WH","RESIN","G",95)];
        RptRepository.InventoryEvent Move(DateTime day,string type,decimal qty,string reason="")=>
            new(1,day,"WH","RESIN","G","M1","Resin","L1","A1",type,reason,"",qty,"itest");
        var result=InventoryQuantityChanges.Build(balances,
            [Move(start,"IN",30),Move(start,"OUT",-10),Move(start.AddDays(1),"OUT",-20,"INBOUND_CANCEL")],start,start.AddDays(2));
        Assert.Collection(result,
            day=>{Assert.Equal(100m,day.Opening);Assert.Equal(115m,day.Closing);Assert.Equal(15m,day.Change);Assert.Equal(30m,day.Received);Assert.Equal(10m,day.Released);},
            day=>{Assert.Equal(-20m,day.Change);Assert.Equal(0m,day.Received);Assert.Equal(0m,day.Released);},
            day=>{Assert.Equal(0m,day.Change);Assert.Equal(95m,day.Closing);});
    }

    [Fact]
    public void Depleted_stock_remains_visible_and_units_and_inventories_stay_separate()
    {
        var day=new DateTime(2026,1,2);
        RptRepository.InventoryDailyBalance[] balances=[new(day.AddDays(-1),"WH","RESIN","G",10),
            new(day,"WH","RESIN","KG",5),new(day,"FG","RESIN","G",3)];
        var result=InventoryQuantityChanges.Build(balances,[],day,day);
        Assert.Equal(3,result.Count);
        Assert.Equal(-10m,Assert.Single(result,x=>x.Source=="WH"&&x.Unit=="G").Change);
        Assert.Equal(5m,Assert.Single(result,x=>x.Unit=="KG").Closing);
        Assert.Equal(3m,Assert.Single(result,x=>x.Source=="FG").Closing);
    }
}
