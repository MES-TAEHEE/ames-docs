using System.Text.RegularExpressions;
using AMES.Data.Repositories;
using Xunit;

namespace AMES.Data.Tests;

public sealed class CustomerReturnContractTests
{
    static string Root([System.Runtime.CompilerServices.CallerFilePath] string sourceFile="")
    {
        foreach(var start in new[]{Path.GetDirectoryName(sourceFile)!,AppContext.BaseDirectory,Environment.CurrentDirectory})
            for(var d=new DirectoryInfo(start);d is not null;d=d.Parent)
                if(File.Exists(Path.Combine(d.FullName,"dist","AMES_Schema.sql")))return d.FullName;
        throw new DirectoryNotFoundException("Repository schema directory not found.");
    }
    static string Read(string path)=>File.ReadAllText(Path.Combine(Root(),path));
    static string Procedure(string source,string name)
        =>Regex.Match(source,@"CREATE\s+(?:OR\s+ALTER\s+)?PROCEDURE\s+dbo\."+name+@"\b[\s\S]*?(?=^GO\s*$)",RegexOptions.Multiline|RegexOptions.IgnoreCase).Value;

    [Fact]
    public void Return_model_uses_explicit_customer_and_part_fields()
    {
        var names=typeof(FinishedGoodsRepository.ReturnRow).GetProperties().Select(p=>p.Name).ToArray();
        foreach(var name in new[]{"ReturnId","CustomerId","ShipmentId","OutboundBarcode","PartNo","PartName","LotNo"})Assert.Contains(name,names);
        foreach(var name in new[]{"ReturnNumber","RmaNo","CustomerCode","ItemNo","ItemsJson","CapaTriggered"})Assert.DoesNotContain(name,names);
    }

    [Fact]
    public void Fresh_schema_does_not_recreate_retired_return_columns()
    {
        var table=Regex.Match(Read("dist/AMES_Schema.sql"),@"CREATE TABLE \[dbo\]\.\[FG_CustomerReturn\][\s\S]*?(?=^GO\s*$)",RegexOptions.Multiline).Value;
        Assert.NotEmpty(table);
        foreach(var name in new[]{"ReturnID","CustomerID","ShipmentID","PartNo","PartName"})Assert.Contains("["+name+"]",table);
        foreach(var name in new[]{"ReturnNumber","RMANo","CustomerClaimID","OriginalShipmentOrderID","OriginalDeliveryNoteID","ItemNo","ItemsJSON","NcrID","CapaTriggered"})Assert.DoesNotContain("["+name+"]",table);
    }

    [Theory]
    [InlineData("dist/AMES_Schema.sql")]
    [InlineData("dist/migrate_fg_shipment_actual.sql")]
    public void Return_procedures_use_one_contract_across_deployment_paths(string path)
    {
        var source=Read(path);
        foreach(var name in new[]{"FG_PDA_RETURN_SCAN","FG_PDA_RETURN_RECEIVE"})
        {
            var proc=Procedure(source,name);Assert.NotEmpty(proc);
            foreach(var retired in new[]{"ReturnNumber","RMANo","OriginalShipmentOrderID","CustomerClaimID","CapaTriggered","R.ItemsJSON","R.ItemNo"})Assert.DoesNotContain(retired,proc);
        }
        var scan=Procedure(source,"FG_PDA_RETURN_SCAN");
        Assert.Contains("dbo.MD_Customer",scan);
        Assert.Contains("C.CustomerID",scan);
        Assert.Contains("T.SourceType='FG_OUTBOUND'",scan);
        Assert.Contains("S.ShipmentID=T.SourceID",scan);
        var receive=Procedure(source,"FG_PDA_RETURN_RECEIVE");
        Assert.Contains("PartNo,PartName",receive);
        Assert.Contains("WITH(UPDLOCK,HOLDLOCK)",receive);
        Assert.Contains("ROLLBACK TRANSACTION",receive);
        Assert.Contains("'FG_RETURN',@ReturnID",receive);
    }

    [Fact]
    public void Migration_rejects_lossy_conversion_and_never_deletes_return_rows()
    {
        var sql=Read("dist/migrate_fg_customer_return_columns.sql");
        Assert.Contains("Split multi-part legacy returns",sql);
        Assert.Contains("no unique Customer Master match",sql);
        Assert.DoesNotMatch(@"(?i)DELETE\s+(?:FROM\s+)?dbo\.FG_CustomerReturn",sql);
        Assert.DoesNotContain("DROP TABLE",sql);
    }

    [SkippableFact]
    public void New_return_and_history_queries_work_without_database_writes()
    {
        var connection=Environment.GetEnvironmentVariable("AMES_TEST_CONN");
        Skip.If(string.IsNullOrWhiteSpace(connection),"Set AMES_TEST_CONN for read-only migrated-schema verification.");
        var repo=new FinishedGoodsRepository(new AMES.Data.Connection.AmesConnectionFactory(connection!));
        repo.ListReturns();
        repo.ListHistory(eventType:"RETURN");
    }
}
