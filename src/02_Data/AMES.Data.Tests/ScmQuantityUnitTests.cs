using AMES.Data.Connection;
using AMES.Data.Repositories;
using Xunit;

namespace AMES.Data.Tests;

public sealed class ScmQuantityUnitTests
{
    [Theory]
    [InlineData("BOX")]
    [InlineData("plt")]
    [InlineData("PALLET")]
    [InlineData("SH")]
    [InlineData(" sheet ")]
    public void Packaging_units_cannot_be_used_for_new_purchase_orders(string unit)
    {
        Assert.False(MasterDataRepository.IsQuantityUnit(unit));
        var repository=new ScmRepository(new AmesConnectionFactory("Server=unused;Database=unused;Integrated Security=True"));
        var date=new DateTime(2026,10,13);
        var order=new ScmRepository.PurchaseOrder("","V1",date,date,"EOS","Draft","USD",
            [new(1,"PART-1","Part",unit,10,1,0)]);
        Assert.Throws<ArgumentException>(()=>repository.SavePurchaseOrder(order,new Dictionary<int,string>(),"tester"));
    }

    [Theory]
    [InlineData("BOX")]
    [InlineData("PLT")]
    [InlineData("PALLET")]
    [InlineData("SH")]
    [InlineData("SHEET")]
    public void Packaging_units_cannot_be_registered_or_used_as_base_units(string unit)
    {
        var master=new MasterDataRepository(new AmesConnectionFactory("Server=unused;Database=unused;Integrated Security=True"));
        Assert.Throws<ArgumentException>(()=>master.InsertUom(unit,"Packaging",null,false,null,null,0,null,true,"tester"));
        Assert.Throws<ArgumentException>(()=>master.UpdateUom(unit,"Packaging",null,false,null,null,0,null,true,"tester"));
        Assert.Throws<ArgumentException>(()=>master.InsertUom("EA","Each",null,false,unit,null,0,null,true,"tester"));
        Assert.Throws<ArgumentException>(()=>master.UpdateUom("EA","Each",null,false,unit,null,0,null,true,"tester"));
        Assert.Throws<ArgumentException>(()=>master.InsertItem("PART-1","Part","MATERIAL",null,false,null,unit,
            null,null,null,null,null,null,null,null,null,null,false,null,true,"tester"));
        Assert.Throws<ArgumentException>(()=>master.UpdateItem("PART-1","Part","MATERIAL",null,false,null,unit,
            null,null,null,null,null,null,null,null,null,null,false,null,true,"tester"));
    }

    [Fact]
    public void Item_and_order_models_no_longer_require_box_contents_columns()
    {
        foreach(var type in new[]{typeof(MasterDataRepository.ItemRow),typeof(ScmRepository.PurchaseLine),typeof(ScmRepository.CasePart)})
            Assert.DoesNotContain(type.GetProperties(),property=>property.Name.StartsWith("BoxContent",StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("EA")]
    [InlineData("KG")]
    [InlineData("G")]
    [InlineData("M")]
    public void Individual_quantity_units_remain_available(string unit)
        =>Assert.True(MasterDataRepository.IsQuantityUnit(unit));
}
