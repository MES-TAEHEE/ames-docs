using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

public class WarehouseColumnReviewTests
{
    [Theory]
    [InlineData("SCTEST1")]
    [InlineData("SCTEST2")]
    [InlineData("PTEST")]
    public void Production_test_accounts_are_rejected_before_database_access(string employee)
    {
        var factory=new AMES.Data.Connection.AmesConnectionFactory("Server=unused");
        var service=new AMES.Data.Services.PopAuthService(new AMES.Data.Repositories.AuthRepository(factory),
            new AMES.Data.Repositories.WorkerRepository(factory),new AMES.Data.Repositories.PopSessionRepository(factory),false);
        var result=service.Login(new AMES.Contracts.Auth.LoginRequest
        {
            AttemptedId=employee,Pin="0000",Method=AMES.Contracts.Enums.AuthMethod.Pin,TerminalId="PDA",LineId="01",ShiftCode="DAY"
        });
        Assert.False(result.IsSuccess);
        Assert.Equal(AMES.Contracts.Enums.AuthResult.InactiveAccount,result.Result);
    }
    static string Root([System.Runtime.CompilerServices.CallerFilePath] string file="")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../.."));
    static string Read(string path)=>File.ReadAllText(Path.Combine(Root(),path));

    [Fact]
    public void Warehouse_picking_excludes_finished_goods_lines_and_containers()
    {
        var rule=AMES.Data.Repositories.WarehouseRepository.WarehouseBoxPredicate;
        Assert.Contains("W.UnitType='BOX'",rule);
        Assert.Contains("NOT LIKE 'FG%'",rule);
        Assert.Contains("'FG','FG_AREA'",rule);
        Assert.Contains("ML.LotPrefix",rule);
        Assert.Contains("C.ParentLotNo=W.LotNo",rule);
    }

    [Theory]
    [InlineData("WH_Inventory","PartNo",20)]
    [InlineData("WH_Inventory","LocationNo",20)]
    [InlineData("WH_Inventory","CaseNo",50)]
    [InlineData("WH_Inventory","DeliveryNoteNo",30)]
    [InlineData("WH_InventoryTransaction","PartNo",20)]
    [InlineData("WH_InventoryTransaction","LocationNo",20)]
    [InlineData("WH_InventoryTransaction","OperatorID",20)]
    [InlineData("WH_InventoryTransaction","ApproverID",20)]
    [InlineData("WH_PickSlip","ReqLocation",20)]
    [InlineData("WH_PickSlip","ReqUserId",20)]
    [InlineData("WH_PickSlip","CloseUserId",20)]
    [InlineData("WH_ReleaseSchedule","ReqLocation",20)]
    [InlineData("WH_ReleaseSchedule","ReqUserId",20)]
    [InlineData("WH_ReleaseSchedule","CloseUserId",20)]
    [InlineData("WH_ReleaseSchedule","CreatedBy",20)]
    [InlineData("WH_ReleaseSchedule","ModifiedBy",20)]
    [InlineData("FG_PutAway","OperatorID",20)]
    [InlineData("FG_ShipmentOrder","ConfirmedBy",20)]
    [InlineData("FG_ShipmentOrder","ShipmentOperatorID",20)]
    [InlineData("FG_CustomerReturn","ReceivedBy",20)]
    [InlineData("FG_CustomerReturn","ClosedBy",20)]
    [InlineData("SCM_DeliveryCase","CreatedBy",20)]
    [InlineData("SCM_DeliveryCase","PoNumber",20)]
    [InlineData("SCM_DeliveryBox","UnitCode",10)]
    public void Fresh_schema_matches_storage_contract(string table,string column,int width)
    {
        var batch=Regex.Match(Read("dist/AMES_Schema.sql"),@"CREATE TABLE \[dbo\]\.\["+table+@"\][\s\S]*?(?=^GO\s*$)",RegexOptions.Multiline).Value;
        Assert.Matches(@"\["+column+@"\] \[varchar\]\("+width+@"\)",batch);
    }

    [Fact]
    public void All_scoped_created_modified_actors_are_varchar20()
    {
        var source=Read("dist/AMES_Schema.sql");
        var tables=Regex.Matches(source,@"CREATE TABLE \[dbo\]\.\[(?:WH|FG|SCM)_[^\]]+\][\s\S]*?(?=^GO\s*$)",RegexOptions.Multiline);
        Assert.NotEmpty(tables);
        foreach(Match table in tables)
            foreach(Match actor in Regex.Matches(table.Value,@"\[(?:CreatedBy|ModifiedBy)\] [^\r\n]+"))
                Assert.Contains("[varchar](20)",actor.Value);
    }

    [Fact]
    public void Session_schema_has_persistent_bearer_columns_and_unique_hash_index()
    {
        var source=Read("dist/AMES_Schema.sql");
        Assert.Contains("[BearerHash] [char](64)",source);
        Assert.Contains("[SessionJson] [nvarchar](max)",source);
        Assert.Contains("UX_PR_PopSession_BearerHash",source);
    }

    [Fact]
    public void Length_migration_preserves_data_and_indexes_and_has_no_database_rebuild()
    {
        var sql=Read("dist/migrate_wh_fg_scm_column_lengths.sql");
        Assert.Contains("Nothing changed.",sql);
        Assert.Contains("#Indexes",sql);
        Assert.Contains("ROLLBACK TRANSACTION",sql);
        Assert.DoesNotContain("DROP DATABASE",sql,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE TABLE",sql,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(@"(?i)DELETE\s+(FROM\s+)?dbo\.",sql);
    }

    [SkippableTheory]
    [InlineData(40,20,20)]
    [InlineData(60,20,40)]
    public void Duplicate_part_requests_are_allocated_without_losing_demand(int picked,int first,int second)
    {
        var cs=Environment.GetEnvironmentVariable("AMES_TEST_CONN");
        Skip.If(string.IsNullOrWhiteSpace(cs),"Set AMES_TEST_CONN for read-only SQL allocation checks.");
        var code=Read("src/04_Api/AMES.Api/Endpoints/WhEndpoints.cs");
        var start=code.IndexOf(";WITH PickCounts AS",StringComparison.Ordinal);
        var end=code.IndexOf("\"\"\"",start,StringComparison.Ordinal);
        var sql=code[start..end];
        var a=sql.IndexOf("UPDATE RS",StringComparison.Ordinal);
        var b=sql.IndexOf("FROM dbo.WH_PickSlip RS",a,StringComparison.Ordinal);
        sql=sql[..a]+"SELECT RS.PickSlipID,Q.PickedQty "+sql[b..];
        sql=sql.Replace("dbo.WH_PickSlip","@Requests").Replace("dbo.WH_InventoryTransaction","@Transactions");
        using var connection=new SqlConnection(cs);connection.Open();
        using var command=new SqlCommand(@"
            DECLARE @Requests TABLE(PickSlipID int,ItemNo varchar(20),PickSlipNo nvarchar(40),Status varchar(20),DemandQty decimal(18,3),CreatedBy varchar(20));
            DECLARE @Transactions TABLE(SourceID int,LotNo nvarchar(50),QtyChange decimal(18,3),TransactionType varchar(10),SourceType varchar(30));
            INSERT @Requests VALUES(1,'PART-A','PK01','Open',20,'WH-AUTO'),(2,'PART-A','PK01','Open',30,'WH-AUTO'),(3,'PART-A','PK01','Cancelled',100,'WH-AUTO');
            INSERT @Transactions VALUES(1,'BOX-1',-@Picked,'OUT','PICK_SLIP');
            "+sql,connection);
        command.Parameters.Add("@Picked",SqlDbType.Int).Value=picked;
        command.Parameters.Add("@Slip",SqlDbType.NVarChar,40).Value="PK01";
        using var reader=command.ExecuteReader();
        var results=new Dictionary<int,decimal>();while(reader.Read())results.Add(reader.GetInt32(0),reader.GetDecimal(1));
        Assert.Equal(2,results.Count);Assert.Equal(first,results[1]);Assert.Equal(second,results[2]);
    }
}
