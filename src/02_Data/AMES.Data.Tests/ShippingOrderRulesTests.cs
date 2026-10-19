using AMES.Contracts.Dto;
using AMES.Data.Repositories;
using Xunit;

namespace AMES.Data.Tests;

public sealed class ShippingOrderRulesTests
{
    [Theory]
    [InlineData(0,100,false,0)]
    [InlineData(100,100,false,1)]
    [InlineData(200,100,false,2)]
    [InlineData(201,100,false,3)]
    [InlineData(200,0,false,null)]
    [InlineData(200,null,false,null)]
    [InlineData(200,100,true,null)]
    public void Pallet_estimates_round_up_without_inventing_packing_quantities(int quantity,int? perPallet,bool tote,int? expected)
        =>Assert.Equal(expected is int count?(decimal?)count:null,FinishedGoodsRepository.EstimateShippingPallets(quantity,perPallet,tote));
    [Fact]
    public void Menu_numbering_preserves_screen_permissions_and_line_defaults_do_not_move_stock()
    {
        var root=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourcePath())!,"../../.."));
        var sql=File.ReadAllText(Path.Combine(root,"dist/migrate_shipping_ui_line_defaults.sql"));
        Assert.Contains("('fg/shipping-orders','FG-02',2)",sql);
        Assert.Contains("('fg/shipment-plan','FG-03',3)",sql);
        Assert.Contains("('fg/shipments','FG-04',4)",sql);
        Assert.DoesNotContain("UPDATE dbo.WH_Inventory",sql);
        Assert.DoesNotContain("DELETE",sql,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SET ScreenCode",sql);
        Assert.Contains("WHERE L.LineID=M.LineID",sql);
    }
    [Fact]
    public void Mixed_pallet_parts_must_all_be_required()
    {
        DeliveryRequirementDto[] required=[new("A","Part A",100,0)];
        DeliveryLoadDto[] pallet=[new("P1","L1","A",40),new("P1","L2","B",10)];
        Assert.Contains("not required",FinishedGoodsRepository.ValidateDeliveryLoad(required,pallet));
    }
    [Fact]
    public void All_lots_of_the_same_part_are_counted_together()
    {
        DeliveryRequirementDto[] required=[new("A","Part A",100,20)];
        Assert.Null(FinishedGoodsRepository.ValidateDeliveryLoad(required,[new("P1","L1","A",50),new("P1","L2","A",30)]));
        Assert.Contains("exceeds",FinishedGoodsRepository.ValidateDeliveryLoad(required,[new("P1","L1","A",50),new("P1","L2","A",31)]));
    }
    [Fact]
    public void Empty_pallet_is_not_loadable()=>Assert.NotNull(FinishedGoodsRepository.ValidateDeliveryLoad([],[]));
    [Theory]
    [InlineData("W1",1,1,1,"W101A1F1")]
    [InlineData("I7",2,27,3,"I702A2F3")]
    public void Line_locations_use_the_registered_line_prefix_and_X_Y_F(string prefix,int x,int y,int floor,string expected)
        =>Assert.Equal(expected,WarehouseRepository.LineLocationNo(prefix,x,y,floor));
    [Theory]
    [InlineData(0,1,1)] [InlineData(1,0,1)] [InlineData(1,1,0)] [InlineData(100,1,1)]
    public void Invalid_coordinates_are_rejected(int x,int y,int floor)
        =>Assert.Throws<ArgumentException>(()=>WarehouseRepository.LineLocationNo("W1",x,y,floor));
    [Fact]
    public void Wrong_part_does_not_generate_a_replenishment_request()
    {
        var row=new WarehouseRepository.LineStockRow("W101A1F1","A","Part A",0,100,false,true);
        Assert.Equal("Different part at location",row.State);
    }
    [Fact]
    public void Shipment_order_creation_is_enabled_for_the_existing_system_admin()
    {
        var root=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourcePath())!,"../../.."));
        var migration=File.ReadAllText(Path.Combine(root,"dist/migrate_line_locations_shipping_orders.sql"));
        Assert.Contains("UPDATE dbo.SYS_RolePermission SET PermissionLevel='REX'",migration);
        Assert.Contains("WHERE ScreenCode='FG-05' AND RoleID='ROLE-SYSADMIN'",migration);
    }
    private static string SourcePath([System.Runtime.CompilerServices.CallerFilePath]string path="")=>path;
}
