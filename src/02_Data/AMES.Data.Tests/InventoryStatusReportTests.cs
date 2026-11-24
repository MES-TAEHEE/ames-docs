using System.Data;
using System.Reflection;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

public sealed class InventoryStatusReportTests
{
    [SkippableFact]
    public void Report_reads_the_configured_database_without_writing_stock()
    {
        Skip.If(string.IsNullOrWhiteSpace(AmesDevDb.Conn),"Set AMES_TEST_CONN for report reads.");
        var repo=new RptRepository(new AmesConnectionFactory(AmesDevDb.Conn));
        Assert.All(repo.ListInventoryStatus(DateTime.Today),row=>Assert.True(row.Qty>0));
        repo.InventoryMovements(DateTime.Today.AddDays(-6),DateTime.Today);
        repo.ListInventoryEvents(DateTime.Today.AddDays(-6),DateTime.Today);
        var daily=repo.ListInventoryDailyBalances(DateTime.Today.AddDays(-6),DateTime.Today);
        var latest=daily.Where(d=>d.Day==DateTime.Today).OrderBy(d=>d.Source).ThenBy(d=>d.Category).ThenBy(d=>d.Unit)
            .Select(d=>(d.Source,d.Category,d.Unit,d.Qty)).ToArray();
        var stock=repo.ListInventoryStatus(DateTime.Today).GroupBy(d=>(d.Source,d.ItemCategory,d.Unit))
            .OrderBy(g=>g.Key.Source).ThenBy(g=>g.Key.ItemCategory).ThenBy(g=>g.Key.Unit)
            .Select(g=>(g.Key.Source,g.Key.ItemCategory,g.Key.Unit,g.Sum(d=>d.Qty))).ToArray();
        Assert.Equal(stock,latest);
        repo.ListInventoryTargets();
    }

    [Theory]
    [InlineData("WH",0)]
    [InlineData("FG",-1)]
    [InlineData("BAD",1)]
    [InlineData("ALL",1.0001)]
    public void Invalid_target_is_rejected_before_opening_the_database(string scope,decimal qty)
    {
        var repo=new RptRepository(new AmesConnectionFactory("Server=unused;Database=unused"));
        Assert.Throws<ArgumentException>(()=>repo.SaveInventoryTarget(new(scope,"RESIN","G",qty),"itest"));
    }

    [SkippableTheory]
    [InlineData("2026-01-05",100,"A1",true,false)]
    [InlineData("2026-01-11",60,"B1",false,true)]
    public void Cutoff_restores_quantity_location_cancelled_receipts_and_excludes_future_receipts(
        string end,decimal expected,string location,bool cancelledPresent,bool futurePresent)
    {
        Skip.If(string.IsNullOrWhiteSpace(AmesDevDb.Conn),"Set AMES_TEST_CONN; this test writes temporary tables only.");
        using var conn=new SqlConnection(AmesDevDb.Conn);
        conn.Open();
        using var seed=new SqlCommand("""
            CREATE TABLE #Stock(LotNo nvarchar(50),PartNo varchar(20),Qty decimal(18,3),LocationNo varchar(20),
                ReceivedAt datetime2,CreatedAt datetime2,DeliveryNoteNo varchar(50),ParentLotNo nvarchar(50));
            CREATE TABLE #Events(TransactionID bigint,TransactionTime datetime2,LotNo nvarchar(50),PartNo varchar(20),
                QtyBefore decimal(18,3),QtyAfter decimal(18,3),QtyChange decimal(18,3),LocationNo varchar(20),TransactionType varchar(10));
            CREATE TABLE #Items(ItemNo varchar(20),ItemName nvarchar(50),ItemType varchar(20),DefaultUOM varchar(10),ItemCategory nvarchar(50));
            CREATE TABLE #Notes(NoteNumber varchar(50),VendorID varchar(20));
            INSERT #Items VALUES('M1',N'Resin','MATERIAL','G','RESIN'),('F1',N'Assembly','ASSY','EA','ASSY');
            INSERT #Stock VALUES
                ('L1','M1',60,'B1','20260101','20260101',NULL,NULL),
                ('FUTURE','M1',8,NULL,'20260108','20260108',NULL,NULL),
                ('LEGACY','M1',20,NULL,'20251201','20251201',NULL,NULL),
                ('ASSY','F1',2,'FG1','20260101','20260101',NULL,NULL),
                ('PALLET','F1',2,'FG1','20260101','20260101',NULL,NULL),
                ('CHILD','F1',2,'FG1','20260101','20260101',NULL,'PALLET');
            INSERT #Events VALUES
                (1,'20260101','L1','M1',0,100,100,NULL,'IN'),
                (2,'20260103','L1','M1',100,100,0,'A1','MOVE'),
                (3,'20260105','L1','M1',100,60,-40,'A1','OUT'),
                (4,'20260107','L1','M1',60,60,0,'B1','MOVE'),
                (5,'20260101','CANCELLED','M1',0,10,10,NULL,'IN'),
                (6,'20260106','CANCELLED','M1',10,0,-10,NULL,'OUT'),
                (7,'20260108','FUTURE','M1',0,8,8,NULL,'IN');
            """,conn);
        seed.ExecuteNonQuery();
        var sql=(string)typeof(RptRepository).GetField("InventoryStatusSql",BindingFlags.Static|BindingFlags.NonPublic)!.GetRawConstantValue()!;
        sql=sql.Replace("dbo.WH_InventoryTransaction","#Events").Replace("dbo.WH_Inventory","#Stock")
            .Replace("dbo.MD_Item","#Items").Replace("dbo.SCM_DeliveryNote","#Notes");
        using var cmd=new SqlCommand(sql,conn);
        cmd.Parameters.Add("@End",SqlDbType.DateTime2).Value=DateTime.Parse(end);
        using var reader=cmd.ExecuteReader();
        var rows=new Dictionary<string,(decimal Qty,string Location,string Source)>();
        while(reader.Read()) rows.Add((string)reader["LotNo"],(Convert.ToDecimal(reader["Qty"]),(string)reader["LocationNo"],(string)reader["Source"]));
        Assert.Equal((expected,location,"WH"),rows["L1"]);
        Assert.Equal(cancelledPresent,rows.ContainsKey("CANCELLED"));
        Assert.Equal(futurePresent,rows.ContainsKey("FUTURE"));
        Assert.Equal((20m,"0000","WH"),rows["LEGACY"]);
        Assert.Equal((2m,"FG1","FG"),rows["ASSY"]);
        Assert.False(rows.ContainsKey("PALLET"));
        reader.Close();
        var dailySql=(string)typeof(RptRepository).GetField("InventoryDailyBalancesSql",BindingFlags.Static|BindingFlags.NonPublic)!.GetRawConstantValue()!;
        dailySql=dailySql.Replace("dbo.WH_InventoryTransaction","#Events").Replace("dbo.WH_Inventory","#Stock").Replace("dbo.MD_Item","#Items");
        using var dailyCmd=new SqlCommand(dailySql,conn);
        dailyCmd.Parameters.Add("@From",SqlDbType.Date).Value=new DateTime(2026,1,3);
        dailyCmd.Parameters.Add("@To",SqlDbType.Date).Value=new DateTime(2026,1,8);
        using var dailyReader=dailyCmd.ExecuteReader();
        var balances=new Dictionary<(DateTime,string),decimal>();
        while(dailyReader.Read()) balances.Add(((DateTime)dailyReader["Day"],(string)dailyReader["Source"]),Convert.ToDecimal(dailyReader["Qty"]));
        Assert.Equal(130m,balances[(new(2026,1,3),"WH")]);
        Assert.Equal(90m,balances[(new(2026,1,5),"WH")]);
        Assert.Equal(80m,balances[(new(2026,1,6),"WH")]);
        Assert.Equal(88m,balances[(new(2026,1,8),"WH")]);
        Assert.Equal(4m,balances[(new(2026,1,8),"FG")]);
    }
}
