using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

public sealed class AssyReadyQueryTests
{
    [Fact]
    public void Pallet_grouping_keeps_each_actual_LOT_including_unpalletized_stock()
    {
        var groups=AssyReadyRepository.GroupRows([
            ("P-1","M-ASSY","Assembly",1m,"EA",(DateTime?)null,"LOT-1"),
            ("P-1","M-ASSY","Assembly",1m,"EA",null,"LOT-2"),
            (null,"M-ASSY","Assembly",1m,"EA",null,"LOT-3"),
            (null,"M-ASSY","Assembly",1m,"EA",null,"LOT-4")]);
        Assert.Equal(2,groups.Count);
        Assert.All(groups,group=>
        {
            Assert.Equal(2,group.LotCount);
            Assert.Equal(2m,Assert.Single(group.Parts).Qty);
            Assert.Equal(2,group.Lots!.Count);
        });
        Assert.Equal(new[]{"LOT-1","LOT-2"},Assert.Single(groups,g=>g.PalletNo=="P-1").Lots!.Select(lot=>lot.LotNo));
        Assert.Equal(new[]{"LOT-3","LOT-4"},Assert.Single(groups,g=>g.PalletNo is null).Lots!.Select(lot=>lot.LotNo));
    }

    [SkippableFact]
    public void Only_registered_unlocated_ASSY_LOTs_are_returned_whether_palletized_or_not()
    {
        var connection=Environment.GetEnvironmentVariable("AMES_SHIPPING_READ_TEST_DB");
        Skip.If(string.IsNullOrWhiteSpace(connection),"SELECT-only CTE fixtures; no application rows created or changed.");
        const string fixtures="""
            WITH FixtureStock AS (
                SELECT LotNo,UnitType,ParentLotNo,PartNo,LocationNo,CAST(Qty AS decimal(18,3)) Qty,
                    CAST('2026-10-28' AS datetime2) ReceivedAt
                FROM (VALUES
                    ('P-READY','PALLET',NULL,NULL,'FG-LOC',0),
                    ('NESTED-BOX','BOX','P-READY',NULL,NULL,0),
                    ('ASSY-READY','PART','NESTED-BOX','M-ASSY',NULL,1),
                    ('ASSY-BLANK','PART','P-READY','M-ASSY','   ',1),
                    ('ASSY-LOCATED','PART','P-READY','M-ASSY','FG-LOC',1),
                    ('ASSY-SHIPPED','PART','P-READY','M-ASSY',NULL,0),
                    ('ASSY-DEFECT','PART','P-READY','M-ASSY',NULL,1),
                    ('ASSY-INSPECT','PART','P-READY','M-ASSY',NULL,1),
                    ('RAW-CHILD','PART','P-READY','M-RAW',NULL,1),
                    ('UNPALLETIZED','PART',NULL,'M-ASSY',NULL,2),
                    ('REWORKED','PART',NULL,'M-ASSY',NULL,1)
                ) V(LotNo,UnitType,ParentLotNo,PartNo,LocationNo,Qty)
            ), FixtureItem AS (
                SELECT * FROM (VALUES ('M-ASSY','Assembly','EA','ASSY',1),('M-RAW','Material','G','RAW',1))
                    V(ItemNo,ItemName,DefaultUOM,ItemType,ActiveFlag)
            ), FixtureLot AS (
                SELECT 'PRODUCTION' LotCode,'M-ASSY' ItemNo,1 WoID,1 LotID,'ASSY' ProcessCode,'CONFIRMED' Status,
                    CAST('2026-10-28' AS datetime2) ProducedAt,CAST('2026-10-28' AS datetime2) CreatedTS
            ), FixtureOrder AS (
                SELECT 1 WoID,'M-ASSY' ItemNo
            ), FixtureProduction AS (
                SELECT 1 LotID,CAST(1 AS decimal(18,3)) GoodQty,CAST('2026-10-28' AS datetime2) EntryAt,0 DefectFlag,'ASSY' ProcessCode
            ), FixtureReturn AS (
                SELECT 'ASSY-INSPECT' LotNo,'Inspecting' Status
            ), FixtureTransaction AS (
                SELECT 'ASSY-DEFECT' LotNo,'INV_DEFECT' SourceType,CAST(-1 AS decimal(18,3)) QtyChange,
                    CAST('2026-10-28' AS datetime2) TransactionTime,1 TransactionID
                UNION ALL SELECT 'REWORKED','POP_DEFECT',-1,CAST('2026-10-28' AS datetime2),2
                UNION ALL SELECT 'REWORKED','PROD_REWORK',1,CAST('2026-10-29' AS datetime2),3
            )
            """;
        var sql=AssyReadyRepository.Sql
            .Replace("dbo.WH_InventoryTransaction","FixtureTransaction")
            .Replace("dbo.WH_Inventory","FixtureStock")
            .Replace("dbo.MD_Item","FixtureItem")
            .Replace("dbo.tbl_Lot","FixtureLot")
            .Replace("dbo.PP_WorkOrder","FixtureOrder")
            .Replace("dbo.PR_ProductionResult","FixtureProduction")
            .Replace("dbo.FG_CustomerReturn","FixtureReturn")
            .Replace("WITH PalletContents AS (",fixtures+", PalletContents AS (");
        using var conn=new AmesConnectionFactory(connection!).OpenConnection();
        using var cmd=new SqlCommand(sql,conn);
        using var reader=cmd.ExecuteReader();
        var rows=new List<(string? Pallet,decimal Qty,string LotNo)>();
        while(reader.Read())rows.Add((reader.IsDBNull(0)?null:reader.GetString(0),reader.GetDecimal(3),reader.GetString(6)));
        Assert.Equal(4,rows.Count);
        Assert.Equal(2m,rows.Where(r=>r.Pallet=="P-READY").Sum(r=>r.Qty));
        Assert.Null(Assert.Single(rows,r=>r.LotNo=="UNPALLETIZED").Pallet);
        Assert.Equal(1m,Assert.Single(rows,r=>r.LotNo=="REWORKED").Qty);
        Assert.DoesNotContain(rows,r=>r.LotNo=="PRODUCTION");
        Assert.Equal(new[]{"ASSY-BLANK","ASSY-READY","REWORKED","UNPALLETIZED"},rows.Select(r=>r.LotNo).OrderBy(x=>x).ToArray());
    }
}
