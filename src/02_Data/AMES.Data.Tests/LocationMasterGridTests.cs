using System.Data;
using System.Reflection;
using System.Text.Json;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

public sealed class LocationMasterGridTests
{
    [Fact]
    public void System_prefixes_preserve_unused_suggestions_and_skip_reserved_codes()
    {
        var used=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"I1","AA","AB"};
        Assert.Equal("AC",WarehouseRepository.AvailableLocationPrefix("I1",used));
        Assert.Equal("FG",WarehouseRepository.AvailableLocationPrefix("FG",used));
        used.Add("AC");
        Assert.Equal("AD",WarehouseRepository.AvailableLocationPrefix("",used));
    }
    [Theory]
    [InlineData(0,1,1)] [InlineData(1,0,1)] [InlineData(1,1,0)]
    [InlineData(100,1,1)] [InlineData(1,261,1)] [InlineData(1,1,100)] [InlineData(20,20,3)]
    public void Invalid_dimensions_are_rejected(int x,int y,int f)
        => Assert.Throws<ArgumentException>(()=>WarehouseRepository.PlanLocationGrid("GG",x,y,f));

    [Fact]
    public void Plan_preserves_coordinate_naming_and_can_add_exactly_one_child()
    {
        var grid=WarehouseRepository.PlanLocationGrid("I1",2,2,2);
        Assert.Equal(8,grid.Count);
        Assert.Equal("I101A1F1",grid[0].Id);
        Assert.Equal("I102B1F2",grid[^1].Id);
        Assert.Equal("I103A2F4",Assert.Single(WarehouseRepository.PlanLocationGrid("I1",3,27,4,true)).Id);
    }

    [SkippableFact]
    public void All_floors_save_atomically_and_children_keep_parent_scope_and_type()
    {
        using var c=Fixture();
        Apply(c,"GG",2,2,2,create:true);
        Apply(c,"HH",1,1,1,create:true);
        Assert.Equal(8,Scalar(c,"SELECT COUNT(*) FROM #Locations WHERE ParentLocationID='GGAREA' AND ActiveFlag=1"));
        Assert.Equal(8,Scalar(c,"SELECT COUNT(*) FROM #Locations WHERE ParentLocationID='GGAREA' AND WhCode='EOS' AND AreaCode='MAT_AREA' AND LocationType='RACK' AND LocationLevel=2"));
        Exec(c,"INSERT #Stock VALUES('GG02B1F2',5)");
        Assert.Equal(52085,Assert.Throws<SqlException>(()=>Apply(c,"GG",1,1,1)).Number);
        Assert.Equal(8,Scalar(c,"SELECT COUNT(*) FROM #Locations WHERE ParentLocationID='GGAREA' AND ActiveFlag=1"));
        Assert.Equal(1,Scalar(c,"SELECT COUNT(*) FROM #Locations WHERE ParentLocationID='HHAREA' AND ActiveFlag=1"));
        Exec(c,"UPDATE #Stock SET Qty=0; INSERT #Transactions VALUES('GG02A1F1')");
        Apply(c,"GG",1,1,1);
        Assert.Equal(1,Scalar(c,"SELECT COUNT(*) FROM #Locations WHERE ParentLocationID='GGAREA' AND ActiveFlag=1"));
        Assert.Equal(2,Scalar(c,"SELECT COUNT(*) FROM #Locations WHERE ParentLocationID='GGAREA' AND ActiveFlag=0"));
        Apply(c,"GG",2,2,2);
        Assert.Equal(8,Scalar(c,"SELECT COUNT(*) FROM #Locations WHERE ParentLocationID='GGAREA' AND ActiveFlag=1"));
        Apply(c,"GG",3,3,3,single:true);
        Assert.Equal(9,Scalar(c,"SELECT COUNT(*) FROM #Locations WHERE ParentLocationID='GGAREA' AND ActiveFlag=1"));
        Assert.Equal(1,Scalar(c,"SELECT COUNT(*) FROM #Locations WHERE LocationID='GG03C1F3' AND ParentLocationID='GGAREA'"));
    }

    [SkippableTheory]
    [InlineData("INSERT #Picking VALUES('GG02A1F1',NULL,'Open')",52086)]
    [InlineData("INSERT #Parts VALUES('GG02A1F1')",52087)]
    [InlineData("INSERT #Spares VALUES('GG02A1F1')",52085)]
    [InlineData("UPDATE #Locations SET PartNo='PART' WHERE LocationID='GG02A1F1'",52087)]
    public void Pending_work_and_assignment_settings_block_shrinking(string arrange,int error)
    {
        using var c=Fixture();
        Apply(c,"GG",2,1,1,create:true);
        Exec(c,arrange);
        Assert.Equal(error,Assert.Throws<SqlException>(()=>Apply(c,"GG",1,1,1)).Number);
        Assert.Equal(2,Scalar(c,"SELECT COUNT(*) FROM #Locations WHERE ParentLocationID='GGAREA' AND ActiveFlag=1"));
    }

    [SkippableFact]
    public void Foreign_barcode_conflicts_roll_back_the_whole_expansion()
    {
        using var c=Fixture();
        Apply(c,"GG",1,1,1,create:true);
        Exec(c,"INSERT #Locations(LocationID,WhCode,AreaCode,LocationLevel,ParentLocationID,Aisle,Bay,Slot) VALUES('GG02A1F1','EOS','MAT_AREA',2,'OTHER','A1','02','F1')");
        Assert.Equal(52083,Assert.Throws<SqlException>(()=>Apply(c,"GG",3,1,1)).Number);
        Assert.Equal(0,Scalar(c,"SELECT COUNT(*) FROM #Locations WHERE LocationID='GG03A1F1'"));
        Assert.Equal(1,Scalar(c,"SELECT COUNT(*) FROM #Locations WHERE LocationID='GG02A1F1' AND ParentLocationID='OTHER'"));
    }

    static SqlConnection Fixture()
    {
        Skip.If(string.IsNullOrWhiteSpace(AmesDevDb.Conn),"Set AMES_TEST_CONN; only temporary tables are modified.");
        var c=new SqlConnection(AmesDevDb.Conn); c.Open();
        Exec(c,"""
            CREATE TABLE #Locations(LocationID varchar(20) COLLATE DATABASE_DEFAULT PRIMARY KEY,LocationName nvarchar(60) COLLATE DATABASE_DEFAULT,WhCode varchar(20) COLLATE DATABASE_DEFAULT,AreaCode varchar(20) COLLATE DATABASE_DEFAULT,
                LocationLevel tinyint,ActiveFlag bit DEFAULT 1,PlantCode varchar(20) COLLATE DATABASE_DEFAULT,LineID varchar(20) COLLATE DATABASE_DEFAULT,LocationType varchar(20) COLLATE DATABASE_DEFAULT,
                ParentLocationID varchar(20) COLLATE DATABASE_DEFAULT,Aisle varchar(5) COLLATE DATABASE_DEFAULT,Bay varchar(5) COLLATE DATABASE_DEFAULT,Slot varchar(5) COLLATE DATABASE_DEFAULT,PartNo varchar(20) COLLATE DATABASE_DEFAULT,MinimumQty decimal(18,3),
                CreatedBy varchar(20) COLLATE DATABASE_DEFAULT,ModifiedBy varchar(20) COLLATE DATABASE_DEFAULT,ModifiedTS datetime2);
            CREATE TABLE #Codes(CodeID varchar(50) COLLATE DATABASE_DEFAULT,GroupCode varchar(20) COLLATE DATABASE_DEFAULT,ParentCodeID varchar(50) COLLATE DATABASE_DEFAULT,CodeValue varchar(20) COLLATE DATABASE_DEFAULT,UseFlag bit,Attribute2 varchar(20) COLLATE DATABASE_DEFAULT);
            INSERT #Codes VALUES('W','WH_CODE',NULL,'EOS',1,NULL),('A','WH_AREA','W','MAT_AREA',1,'GRID');
            CREATE TABLE #Lines(LineID varchar(20) COLLATE DATABASE_DEFAULT,LotPrefix varchar(2) COLLATE DATABASE_DEFAULT);
            CREATE TABLE #Stock(LocationNo varchar(20) COLLATE DATABASE_DEFAULT,Qty decimal(18,3));
            CREATE TABLE #Transactions(LocationNo varchar(20) COLLATE DATABASE_DEFAULT);
            CREATE TABLE #Spares(LocationID varchar(20) COLLATE DATABASE_DEFAULT);
            CREATE TABLE #Picking(ReqLocation varchar(20) COLLATE DATABASE_DEFAULT,CloseDate datetime2,Status varchar(20) COLLATE DATABASE_DEFAULT);
            CREATE TABLE #Parts(LocationID varchar(20) COLLATE DATABASE_DEFAULT);
            """);
        return c;
    }
    static void Apply(SqlConnection c,string prefix,int x,int y,int f,bool create=false,bool single=false)
    {
        var sql=(string)typeof(WarehouseRepository).GetField("LocationGridSaveSql",BindingFlags.NonPublic|BindingFlags.Static)!.GetRawConstantValue()!;
        foreach(var pair in new[]{("WH_InventoryTransaction","Transactions"),("WH_Inventory","Stock"),("MD_LocationPart","Parts"),
            ("MD_Location","Locations"),("MD_CodeItem","Codes"),("MD_Line","Lines"),("MNT_SparePartItem","Spares"),("WH_PickSlip","Picking")})
            sql=sql.Replace("dbo."+pair.Item1,"#"+pair.Item2);
        sql=sql.Replace("OBJECT_ID(N'#Parts',N'U')","OBJECT_ID(N'tempdb..#Parts',N'U')");
        using var tx=c.BeginTransaction(IsolationLevel.Serializable);
        using var cmd=new SqlCommand(sql,c,tx);
        foreach(var pair in new[]{("@Parent",prefix+"AREA"),("@Name","Grid"),("@Wh","EOS"),("@Area","MAT_AREA"),("@Actor","itest"),("@Prefix",prefix),("@Type","RACK")})
            cmd.Parameters.Add(pair.Item1,SqlDbType.VarChar,60).Value=pair.Item2;
        cmd.Parameters.Add("@WantedJson",SqlDbType.NVarChar,-1).Value=JsonSerializer.Serialize(WarehouseRepository.PlanLocationGrid(prefix,x,y,f,single));
        cmd.Parameters.Add("@Columns",SqlDbType.Int).Value=x;
        cmd.Parameters.Add("@Rows",SqlDbType.Int).Value=y;
        cmd.Parameters.Add("@Floors",SqlDbType.Int).Value=f;
        cmd.Parameters.Add("@Create",SqlDbType.Bit).Value=create;
        cmd.Parameters.Add("@Single",SqlDbType.Bit).Value=single;
        cmd.ExecuteNonQuery(); tx.Commit();
    }
    static void Exec(SqlConnection c,string sql) { using var cmd=new SqlCommand(sql,c); cmd.ExecuteNonQuery(); }
    static int Scalar(SqlConnection c,string sql) { using var cmd=new SqlCommand(sql,c); return Convert.ToInt32(cmd.ExecuteScalar()); }
}
