using System.Data;
using AMES.Api.Endpoints;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Api.Tests;

public sealed class FgPutAwayBatchTests
{
    [Fact]
    public void Batch_rejects_empty_duplicate_and_mixed_destinations_before_database_access()
    {
        Assert.Throws<ArgumentException>(()=>FgEndpoints.SavePutAwayBatch(null!,null!,[],"TEST"));
        Assert.Throws<ArgumentException>(()=>FgEndpoints.SavePutAwayBatch(null!,null!,[null!],"TEST"));
        FgEndpoints.PutAwayConfirmReq a=new("LOT-1","LOC-1",new(){["LOT-1"]=1});
        Assert.Throws<ArgumentException>(()=>FgEndpoints.SavePutAwayBatch(null!,null!,[a,a],"TEST"));
        Assert.Throws<ArgumentException>(()=>FgEndpoints.SavePutAwayBatch(null!,null!,[a,a with {Barcode="LOT-2",LocationId="LOC-2"}],"TEST"));
    }

    [BatchDbTheory]
    [InlineData("VALID")]
    [InlineData("CAPACITY")]
    [InlineData("OVERLAP")]
    [InlineData("CHANGED")]
    [InlineData("WAREHOUSE")]
    public void Multiple_pallets_validate_and_save_under_one_rollback_transaction(string scenario)
    {
        using var connection=new SqlConnection(Environment.GetEnvironmentVariable("AMES_PDA_APPROVED_FLOW_DB"));
        connection.Open();using var transaction=connection.BeginTransaction();
        var key="VB"+Guid.NewGuid().ToString("N")[..10];
        var a=key+"A";var b=key+"B";var p=key+"P";var q=key+"Q";var location=key+"L";
        try
        {
            using(var seed=new SqlCommand("""
                INSERT dbo.MD_Location(LocationID,LocationName,WhCode,AreaCode,LocationType,LocationLevel,Capacity,ActiveFlag,CreatedBy)
                VALUES(@Location,'Rollback batch verification','EOS','FG_AREA',@Type,2,@Capacity,1,@Key);
                INSERT dbo.MD_Pallet(PalletNo,PalletUse,ActiveFlag,ShippedFlag,CreatedBy)
                VALUES(@P,'INTERNAL',1,0,@Key),(@Q,'INTERNAL',1,0,@Key);
                INSERT dbo.WH_Inventory(LotNo,UnitType,PalletNo,Qty,ReceivedAt)
                VALUES(@P,'PALLET',@P,0,SYSDATETIME()),(@Q,'PALLET',@Q,0,SYSDATETIME());
                INSERT dbo.tbl_Lot(LotCode,ItemNo,LineID,ProcessCode,BatchSize,RemainingQty,ProducedAt,Status,QualityFlag,InventoryStatus,CreatedBy,CreatedTS)
                VALUES(@A,'M2320-PI010NNB','LINE-IMG-01','IMG',1,1,SYSDATETIME(),'CONFIRMED','OK','CREATED',@Key,SYSDATETIME());
                DECLARE @IdA int=CONVERT(int,SCOPE_IDENTITY());
                INSERT dbo.tbl_Lot(LotCode,ItemNo,LineID,ProcessCode,BatchSize,RemainingQty,ProducedAt,Status,QualityFlag,InventoryStatus,CreatedBy,CreatedTS)
                VALUES(@B,'M2320-PI010NNB','LINE-IMG-01','IMG',1,1,SYSDATETIME(),'CONFIRMED','OK','CREATED',@Key,SYSDATETIME());
                DECLARE @IdB int=CONVERT(int,SCOPE_IDENTITY());
                INSERT dbo.PR_ProductionResult(EntryNo,LotID,LineID,ProcessCode,GoodQty,OperatorID,DefectFlag,EntryAt,ProdDate,CreatedBy,CreatedTS)
                VALUES(@A,@IdA,'LINE-IMG-01','IMG',1,@Key,0,SYSDATETIME(),CONVERT(date,SYSDATETIME()),@Key,SYSDATETIME()),
                    (@B,@IdB,'LINE-IMG-01','IMG',1,@Key,0,SYSDATETIME(),CONVERT(date,SYSDATETIME()),@Key,SYSDATETIME());
                INSERT dbo.WH_Inventory(LotNo,UnitType,ParentLotNo,PartNo,PartName,Qty,ReceivedAt)
                SELECT @A,'PART',@P,ItemNo,ItemName,1,SYSDATETIME() FROM dbo.MD_Item WHERE ItemNo='M2320-PI010NNB';
                INSERT dbo.WH_Inventory(LotNo,UnitType,ParentLotNo,PartNo,PartName,Qty,ReceivedAt)
                SELECT @B,'PART',@Q,ItemNo,ItemName,1,SYSDATETIME() FROM dbo.MD_Item WHERE ItemNo='M2320-PI010NNB';
                """,connection,transaction))
            {
                foreach(var pair in new[]{("@Key",key),("@Location",location),("@A",a),("@B",b),("@P",p),("@Q",q),("@Type",scenario=="WAREHOUSE" ? "WH" : "FG")})
                    seed.Parameters.Add(pair.Item1,SqlDbType.VarChar,40).Value=pair.Item2;
                seed.Parameters.Add("@Capacity",SqlDbType.Int).Value=scenario=="CAPACITY" ? 1 : 2;
                seed.ExecuteNonQuery();
            }
            var requests=new List<FgEndpoints.PutAwayConfirmReq>{new(p,location,new(){[a]=1}),new(q,location,new(){[b]=1})};
            if(scenario=="OVERLAP")requests[1]=new(a,location,new(){[a]=1});
            if(scenario=="CHANGED")requests[1]=new(q,location,new(){[b]=2});
            if(scenario=="VALID")Assert.True(FgEndpoints.SavePutAwayBatch(connection,transaction,requests,key).Success);
            else Assert.Throws<InvalidOperationException>(()=>FgEndpoints.SavePutAwayBatch(connection,transaction,requests,key));
            using var check=new SqlCommand("SELECT COUNT(*) FROM dbo.WH_Inventory WHERE LotNo IN(@A,@B,@P,@Q) AND LocationNo=@Location; SELECT COUNT(*) FROM dbo.WH_InventoryTransaction WHERE LotNo IN(@A,@B) AND TransactionType='MOVE'",connection,transaction);
            foreach(var pair in new[]{("@A",a),("@B",b),("@P",p),("@Q",q),("@Location",location)})check.Parameters.Add(pair.Item1,SqlDbType.VarChar,50).Value=pair.Item2;
            using var reader=check.ExecuteReader();reader.Read();Assert.Equal(scenario=="VALID" ? 4 : 0,reader.GetInt32(0));
            reader.NextResult();reader.Read();Assert.Equal(scenario=="VALID" ? 2 : 0,reader.GetInt32(0));
        }
        finally { if(transaction.Connection is not null)transaction.Rollback(); }
    }
    private sealed class BatchDbTheoryAttribute:TheoryAttribute
    {
        public BatchDbTheoryAttribute()
        { if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AMES_PDA_APPROVED_FLOW_DB")))Skip="Set the approved rollback-test database connection."; }
    }
}
