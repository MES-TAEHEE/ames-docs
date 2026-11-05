using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Data.Services;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

[Collection(BackflushDbCollection.Name)]
public sealed class LocationPartAssignmentTests
{
    [Fact]
    public void Multiple_parts_keep_independent_decimal_minima()
    {
        var rows=WarehouseRepository.ValidateLocationParts([new(" A ",10),new("B",0.125m)]);
        Assert.Equal("A",rows[0].PartNo);
        Assert.Equal(0.125m,rows[1].MinimumQty);
        Assert.Equal(2,rows.Count);
        Assert.Empty(WarehouseRepository.ValidateLocationParts([]));
    }

    [Theory]
    [InlineData(-0.001)]
    [InlineData(0.0001)]
    [InlineData(100000000000)]
    public void Invalid_minima_are_rejected(double value)
        => Assert.Throws<ArgumentException>(()=>WarehouseRepository.ValidateLocationParts([new("A",(decimal)value)]));

    [Fact]
    public void Duplicate_parts_cannot_be_saved_even_with_different_case()
        => Assert.Throws<ArgumentException>(()=>WarehouseRepository.ValidateLocationParts([new("A",1),new("a",2)]));

    [Fact]
    public void Two_materials_at_one_location_do_not_share_stock_or_minimum()
    {
        LinePickingPlanner.Location[] locations=[new("LOC","LINE","A",10,3),new("LOC","LINE","B",20,5,"G")];
        var plan=LinePickingPlanner.Plan([],[],locations,[]);
        Assert.Empty(plan.Issues);
        Assert.Equal(7,plan.Requests.Single(r=>r.PartNo=="A").Qty);
        Assert.Equal(15,plan.Requests.Single(r=>r.PartNo=="B").Qty);
        Assert.Equal("G",plan.Requests.Single(r=>r.PartNo=="B").Unit);
    }

    [Fact]
    public void Line_editor_shows_automatic_bop_parts_and_per_part_minima_with_atomic_save()
    {
        var page=Read("src/06_Web/AMES.Web/Components/Pages/Md/Fd/Location.razor");
        Assert.Contains("RowClick=\"@OnRowClick\"",page);
        Assert.Contains("Multiple=\"true\"",page);
        Assert.Contains("AllowSelectAll=\"false\"",page);
        Assert.Contains("OrderByDescending(p=>p.Recommended)",page);
        Assert.Contains("@bind-Value=\"p.MinimumQty\"",page);
        Assert.Contains("assignedParts:IsPrimary(_sel)?null:_f.Parts.Where",page);
        Assert.Contains("@if (string.IsNullOrEmpty(_sel?.LineId))",page);
        var repo=Read("src/02_Data/AMES.Data/Repositories/WarehouseRepository.LocationParts.cs");
        Assert.Contains("W.Qty>0",repo);
        Assert.Contains("P.CloseDate IS NULL",repo);
        Assert.Contains("WITH(UPDLOCK,HOLDLOCK)",repo);
        Assert.Contains("SET QUOTED_IDENTIFIER ON",repo);
        Assert.DoesNotContain("UPDATE dbo.WH_Inventory",repo);
        var lineStock=Read("src/02_Data/AMES.Data/Repositories/WarehouseRepository.LineStock.cs");
        Assert.Contains("LEFT JOIN Assignments A ON A.LocationID=L.LocationID",lineStock);
        Assert.Contains("W.PartNo=A.PartNo",lineStock);
        Assert.Contains("Assigned.PartNo=W.PartNo",lineStock);
        var api=Read("src/04_Api/AMES.Api/Endpoints/WhEndpoints.cs");
        Assert.Contains("Assigned.PartNo=RS.ItemNo",api);
        var migration=Read("dist/migrate_location_parts.sql");
        Assert.DoesNotContain("FOREIGN KEY",migration);
        Assert.Contains("NOT EXISTS(SELECT 1 FROM dbo.MD_LocationPart A",migration);
    }

    [SkippableFact]
    public void Local_database_saves_two_parts_preserves_minima_and_blocks_removing_a_requested_part()
    {
        var value=Environment.GetEnvironmentVariable("AMES_LOCATION_PARTS_TEST_CONNECTION");
        Skip.If(string.IsNullOrWhiteSpace(value),"Local clone test connection is not configured.");
        var settings=new SqlConnectionStringBuilder(value);
        Assert.StartsWith("AMES_TEST_",settings.InitialCatalog);
        Assert.Contains(settings.DataSource.ToLowerInvariant(),new[]{"localhost\\sqlexpress",".\\sqlexpress"});
        using var connection=new AmesConnectionFactory(value!).OpenConnection();
        using var transaction=connection.BeginTransaction();
        var id="VMP"+Guid.NewGuid().ToString("N")[..12];
        try
        {
            WarehouseRepository.LockLinePicking(connection,transaction);
            var parts=new List<string>();
            using(var query=new SqlCommand("SELECT TOP(2) ItemNo FROM dbo.MD_Item WHERE COALESCE(ActiveFlag,1)=1 ORDER BY ItemNo;",connection,transaction))
            using(var reader=query.ExecuteReader())while(reader.Read())parts.Add(reader.GetString(0));
            Assert.Equal(2,parts.Count);
            using(var create=new SqlCommand("""
                INSERT dbo.MD_Location(LocationID,LocationName,WhCode,AreaCode,LineID,LocationType,LocationLevel,ActiveFlag,CreatedBy)
                VALUES(@Id,'Multi-part assignment test','EOS','I1','LINE-INJ-01','LINE',2,1,'TEST');
                """,connection,transaction))
            {create.Parameters.AddWithValue("@Id",id);create.ExecuteNonQuery();}
            WarehouseRepository.SaveLocationParts(connection,transaction,id,[new(parts[0],10),new(parts[1],2.125m)],"TEST");
            using(var check=new SqlCommand("SELECT COUNT(*),SUM(MinimumQty) FROM dbo.MD_LocationPart WHERE LocationID=@Id",connection,transaction))
            {
                check.Parameters.AddWithValue("@Id",id);
                using var reader=check.ExecuteReader();Assert.True(reader.Read());Assert.Equal(2,reader.GetInt32(0));Assert.Equal(12.125m,reader.GetDecimal(1));
            }
            using(var stock=new SqlCommand("""
                INSERT dbo.WH_Inventory(LotNo,UnitType,PartNo,LocationNo,Qty,ReceivedAt,CreatedAt,UpdatedAt)
                VALUES(@Lot,'BOX',@Part,@Id,3,SYSDATETIME(),SYSDATETIME(),SYSDATETIME());
                """,connection,transaction))
            {stock.Parameters.AddWithValue("@Lot",id);stock.Parameters.AddWithValue("@Part",parts[0]);stock.Parameters.AddWithValue("@Id",id);stock.ExecuteNonQuery();}
            var actual=WarehouseRepository.ReadLineStock(connection,transaction,overview:true).Where(r=>r.LineCode==id&&parts.Contains(r.PartNo)).ToList();
            Assert.Equal(2,actual.Count);
            Assert.All(actual,r=>Assert.False(r.WrongPart));
            Assert.Equal(3,actual.Single(r=>r.PartNo==parts[0]).Qty);
            Assert.Equal(0,actual.Single(r=>r.PartNo==parts[1]).Qty);
            Assert.Equal(2.125m,actual.Single(r=>r.PartNo==parts[1]).MinimumQty);
            var stocked=Assert.Throws<SqlException>(()=>WarehouseRepository.SaveLocationParts(connection,transaction,id,[new(parts[1],1)],"TEST"));
            Assert.Contains("stock",stocked.Message);
            using(var pick=new SqlCommand("""
                INSERT dbo.WH_PickSlip(PickSlipNo,ReqLocation,ItemNo,DemandQty,PickedQty,Status,CreatedBy)
                VALUES(@Slip,@Id,@Part,1,0,'Open','WH-AUTO');
                """,connection,transaction))
            {
                pick.Parameters.AddWithValue("@Slip",id);pick.Parameters.AddWithValue("@Id",id);pick.Parameters.AddWithValue("@Part",parts[1]);pick.ExecuteNonQuery();
            }
            var error=Assert.Throws<SqlException>(()=>WarehouseRepository.SaveLocationParts(connection,transaction,id,[new(parts[0],1)],"TEST"));
            Assert.Equal(51050,error.Number);
            Assert.Contains("outstanding",error.Message);
        }
        finally {transaction.Rollback();}
    }

    private static string Read(string path,[System.Runtime.CompilerServices.CallerFilePath]string current="")
        => File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(current)!,"../../..",path)));
}
