using System.Data;
using AMES.Data.Repositories;
using AMES.Web.Components.Shared;
using AMES.Web.Services;
using Microsoft.Data.SqlClient;
using Xunit;
using static AMES.Data.Repositories.WarehouseRepository;

namespace AMES.Data.Tests;

public sealed class FactoryMapInventoryTests
{
    private static FactoryMapRegion Region(int id, string name, string? code = null)
        => new(id, name, new("EOS", "FG", "P1", 10, 10, 10, 10, code), "{}");

    [Fact]
    public void Existing_letters_are_retained_and_explicitly_cleared_letters_stay_clear()
    {
        Assert.Equal("A", FactoryMapView.Code(Region(1, "A")));
        Assert.Equal("", FactoryMapView.Code(Region(1, "A", "")));
        Assert.Equal("B", FactoryMapView.Code(Region(1, "Area A", "b")));
        Assert.Equal("C", FactoryMapView.NextCode([Region(1, "A"), Region(2, "B"), Region(3, "D")]));
        Assert.Equal("AA", FactoryMapView.NextCode(Enumerable.Range(0, 26).Select(i => Region(i, ((char)('A' + i)).ToString()))));
        Assert.Equal(7, Enumerable.Range(0, 7).Select(i => FactoryMapView.Color(Region(i, ((char)('A' + i)).ToString()))).Distinct().Count());
        Assert.Equal(FactoryMapView.Color(Region(39,"A")), FactoryMapView.Color(Region(39,"A","K")));
    }

    [Theory]
    [InlineData("A1")]
    [InlineData("ABCD")]
    [InlineData("A B")]
    public void Invalid_letters_are_rejected(string code)
        => Assert.Throws<ArgumentException>(() => NormalizeFactoryMapCode(code));

    [Fact]
    public void Inventory_is_scoped_to_the_selected_parent_warehouse_area_and_active_FG_locations()
    {
        MasterDataRepository.LocationRow Location(string id, string wh="EOS",string area="FG",string? parent="P1",bool active=true,string type="FG")
            => new(id,null,wh,area,null,null,null,null,null,type,null,active,null,null,null,null,ParentLocationID:parent,LocationLevel:2);
        var locations = new[] {Location("GOOD"),Location("OTHER-PARENT",parent:"P2"),Location("OTHER-WH",wh:"OTHER"),
            Location("OTHER-AREA",area:"OTHER"),Location("INACTIVE",active:false),Location("MATERIAL",type:"RACK")};
        var stock=locations.Select(x=>new LocationMapStock(x.LocationID,"P",null,x.LocationID,1,"EA"));
        var result=FactoryMapView.Stock(stock,locations,Region(1,"A"));
        Assert.Equal("GOOD",Assert.Single(result).LocationNo);
    }

    [Fact]
    public void Examples_are_deterministic_region_specific_and_visibly_synthetic()
    {
        LocationMapStock[] items=[new("L1","P1","Part one","REAL",5,"EA")];
        var day=new DateTime(2026,11,24);
        var a=FactoryMapView.Examples(Region(1,"A"),items,day);
        var b=FactoryMapView.Examples(Region(2,"B"),items,day);
        Assert.Equal(a,FactoryMapView.Examples(Region(1,"A"),items,day));
        Assert.Equal(4,a.Select(x=>x.LotNo).Distinct().Count());
        Assert.All(a,x=>Assert.StartsWith("DEMO-A-",x.LotNo));
        Assert.All(b,x=>Assert.StartsWith("DEMO-B-",x.LotNo));
        Assert.All(a,x=>Assert.True(x.ReceivedAt<=day));
        Assert.Equal("REAL",items[0].LotNo);
    }

    [SkippableTheory]
    [InlineData("A",0,true)]
    [InlineData("A",1,false)]
    [InlineData("B",0,true)]
    [InlineData("b",0,true)]
    [InlineData("C",0,false)]
    [InlineData("D",0,false)]
    [InlineData("E",0,false)]
    public void Duplicate_check_covers_legacy_codes_edits_cleared_codes_and_inactive_regions(string code,int id,bool duplicate)
    {
        Skip.If(string.IsNullOrWhiteSpace(AmesDevDb.Conn),"Set AMES_TEST_CONN for temporary-table checks.");
        using var conn=new SqlConnection(AmesDevDb.Conn);
        conn.Open();
        using (var setup=new SqlCommand("""
            CREATE TABLE #Config(ConfigID int,ConfigType varchar(15),Category varchar(30),CodeName nvarchar(80),ConfigValue nvarchar(500),IsActive bit);
            INSERT #Config VALUES(1,'FG_MAP','FG_LAYOUT','A','{}',1),
              (2,'FG_MAP','FG_LAYOUT','Description','{"Code":"B"}',1),
              (3,'FG_MAP','FG_LAYOUT','C','{"Code":""}',1),
              (4,'FG_MAP','FG_LAYOUT','D','{}',0),
              (5,'OTHER','SYSTEM','E','plain setting',1);
            """,conn))setup.ExecuteNonQuery();
        using var cmd=new SqlCommand(FactoryMapUniqueCodeSql.Replace("dbo.SYS_Config","#Config"),conn);
        cmd.Parameters.Add("@Id",SqlDbType.Int).Value=id;
        cmd.Parameters.Add("@Code",SqlDbType.NVarChar,3).Value=code;
        Assert.Equal(duplicate,cmd.ExecuteScalar() is not null);
    }
}
