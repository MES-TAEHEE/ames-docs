using System.Text.RegularExpressions;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using Xunit;

namespace AMES.Data.Tests;

public sealed class FinishedGoodsShipmentTests
{
    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string path="")
        =>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!,"../../.."));
    private static string Read(string path)=>File.ReadAllText(Path.Combine(Root(),path));

    [Fact]
    public void Shipment_header_has_only_actual_outbound_fields()
    {
        var schema=Read("dist/AMES_Schema.sql");
        var table=Regex.Match(schema,@"CREATE TABLE \[dbo\]\.\[FG_Shipment\][\s\S]*?(?=^GO\s*$)",RegexOptions.Multiline).Value;
        Assert.NotEmpty(table);
        foreach(var name in new[]{"ShipmentID","LotNo","UnitType","PalletNo","CustomerID","ShippedAt","ShippedBy"})
            Assert.Contains("["+name+"]",table);
        foreach(var name in new[]{"Note","ItemsJSON","ShipOrderNumber","Status","LicensePlate"})Assert.DoesNotContain("["+name+"]",table);
        Assert.DoesNotContain("CREATE TABLE [dbo].[FG_ShipmentOrder]",schema);
    }

    [Fact]
    public void Shipments_and_returns_use_the_same_source_link()
    {
        var sql=PdaFinishedGoodsSql.OutboundComplete+PdaFinishedGoodsSql.ReturnScan+PdaFinishedGoodsSql.ReturnReceive;
        Assert.Contains("INSERT dbo.FG_Shipment(LotNo,UnitType,PalletNo,CustomerID,ShippedAt,ShippedBy)",sql);
        Assert.Contains("'FG_OUTBOUND',@ShipmentID",sql);
        Assert.Contains("S.ShipmentID=T.SourceID",sql);
        Assert.Contains("ParentLotNo=NULL",sql);
        Assert.Contains("WHERE ShipmentID=@ShipmentID AND LotNo=@LotNo",sql);
        Assert.Contains("latest shipment changed",sql);
        Assert.Contains("ROLLBACK TRANSACTION",sql);
        Assert.DoesNotMatch(@"(?i)DELETE\s+(?:FROM\s+)?dbo\.WH_Inventory(?:Transaction)?\b",sql);
        Assert.DoesNotContain("DROP TABLE",sql);
    }

    [Fact]
    public void Customer_is_validated_before_shipment_and_stock_changes()
    {
        var body=PdaFinishedGoodsSql.OutboundComplete;
        Assert.True(body.IndexOf("Select the outbound customer",StringComparison.Ordinal)<body.IndexOf("INSERT dbo.FG_Shipment",StringComparison.Ordinal));
        Assert.Contains("The selected customer does not match",body);
        Assert.Contains("Different customers",body,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Legacy_seed_paths_cannot_create_actual_shipments()
    {
        var seed=Read("dist/pda/PDA_SEED.sql");
        Assert.DoesNotContain("FG_ShipmentOrder",seed);
        Assert.DoesNotMatch(@"(?i)INSERT\s+(?:INTO\s+)?dbo\.FG_Shipment\b",seed);
        Assert.Contains("FG shipment seeding is retired",Read("tools/seed_fg_demo/Program.cs"));
        foreach(var path in new[]{"dist/pda/PDA_SCHEMA.sql","dist/migrate_fg_inventory_consolidation.sql","dist/migrate_fg_customer_return_columns.sql"})
        {
            var legacy=Read(path);
            Assert.Contains("OBJECT_ID(N'dbo.FG_Shipment'",legacy);
            Assert.Contains("SET NOEXEC ON",legacy);
        }
    }

    [SkippableFact]
    public void Actual_shipment_queries_work_without_writes()
    {
        var connection=Environment.GetEnvironmentVariable("AMES_TEST_CONN");
        Skip.If(string.IsNullOrWhiteSpace(connection),"Set AMES_TEST_CONN for read-only verification.");
        var repo=new FinishedGoodsRepository(new AmesConnectionFactory(connection!));
        repo.ListShipments();repo.ListShipmentItems(0);repo.ListReturns();repo.ListHistory();
        var reports=new RptRepository(new AmesConnectionFactory(connection!));
        reports.ListShipmentDetail(DateTime.Today);reports.ListDailyShipment();reports.ShipmentDateExtent();reports.ListMonthlyMetrics(DateTime.Today,DateTime.Today);
    }
}
