using System.Data;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

public sealed class FinishedGoodsPutAwayTests
{
    private static string Read(string file, [System.Runtime.CompilerServices.CallerFilePath] string source="")
        => File.ReadAllText(Path.Combine(Path.GetDirectoryName(source)!,"../../..",file));

    [Fact]
    public void Put_away_has_one_storage_and_history_path()
    {
        var api=Read("src/04_Api/AMES.Api/Endpoints/FgEndpoints.cs");
        Assert.DoesNotContain("dbo.FG_PutAway",api);
        Assert.DoesNotContain("ResolvePalletSplit",api);
        Assert.Contains("record PutAwayConfirmReq(string Barcode, string LocationId)",api);
        Assert.Contains("dbo.FG_PDA_PUTAWAY_COMPLETE",api);
        Assert.DoesNotContain("dbo.FG_PutAway",Read("src/02_Data/AMES.Data/Repositories/FinishedGoodsRepository.cs"));
        Assert.DoesNotContain("CREATE TABLE [dbo].[FG_PutAway]",Read("dist/AMES_Schema.sql"));
        var migration=Read("dist/migrate_fg_putaway_transactions.sql");
        Assert.Contains("Not all old put-away events were preserved",migration);
        Assert.Contains("DROP TABLE dbo.FG_PutAway",migration);
        Assert.DoesNotContain("DELETE",migration,StringComparison.OrdinalIgnoreCase);
        var history=migration[migration.IndexOf("CREATE OR ALTER PROCEDURE dbo.FG_PDA_TRANSACTION_LIST",StringComparison.Ordinal)..];
        Assert.DoesNotContain("UNION ALL",history);
        Assert.DoesNotContain("dbo.FG_PutAway",history);
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void New_stock_is_received_once_and_unlocated_stock_only_moves(bool existingInventory)
    {
        var connection=Environment.GetEnvironmentVariable("AMES_TEST_CONN");
        Skip.If(string.IsNullOrWhiteSpace(connection),"Set AMES_TEST_CONN for rollback-only DB verification.");
        using var conn=new SqlConnection(connection);
        conn.Open();
        using var tx=conn.BeginTransaction();
        try
        {
            var lot=CreateProductionLot(conn,tx);
            if(existingInventory)
            {
                using var seed=new SqlCommand("""
                    INSERT dbo.WH_Inventory(LotNo,UnitType,PartNo,PartName,Qty,ReceivedAt,CreatedAt,UpdatedAt)
                    SELECT @Lot,'PART',ItemNo,ItemName,1,SYSDATETIME(),SYSDATETIME(),SYSDATETIME()
                    FROM dbo.MD_Item WHERE ItemNo='M2320-PI010NNB';
                    """,conn,tx);
                seed.Parameters.Add("@Lot",SqlDbType.NVarChar,50).Value=lot;
                seed.ExecuteNonQuery();
            }
            using(var putaway=new SqlCommand("dbo.FG_PDA_PUTAWAY_COMPLETE",conn,tx){CommandType=CommandType.StoredProcedure})
            {
                putaway.Parameters.Add("@Barcode",SqlDbType.NVarChar,50).Value=lot;
                putaway.Parameters.Add("@Location",SqlDbType.VarChar,20).Value="FA01C1F1";
                putaway.Parameters.Add("@OperatorID",SqlDbType.VarChar,20).Value="FG-PA-VERIFY";
                using var reader=putaway.ExecuteReader();
                Assert.True(reader.Read());
                Assert.Equal(1m,reader.GetDecimal(reader.GetOrdinal("Qty")));
                Assert.Equal(existingInventory,reader.GetBoolean(reader.GetOrdinal("ExistingInventory")));
            }
            using(var check=new SqlCommand("""
                SELECT I.Qty,I.LocationNo,T.TransactionType,T.QtyChange,T.QtyAfter,
                    (SELECT COUNT(*) FROM dbo.WH_InventoryTransaction X WHERE X.LotNo=@Lot) TxCount
                FROM dbo.WH_Inventory I JOIN dbo.WH_InventoryTransaction T ON T.LotNo=I.LotNo
                WHERE I.LotNo=@Lot;
                """,conn,tx))
            {
                check.Parameters.Add("@Lot",SqlDbType.NVarChar,50).Value=lot;
                using var reader=check.ExecuteReader();
                Assert.True(reader.Read());
                Assert.Equal(1m,reader.GetDecimal(0));
                Assert.Equal("FA01C1F1",reader.GetString(1));
                Assert.Equal(existingInventory?"MOVE":"IN",reader.GetString(2));
                Assert.Equal(existingInventory?0m:1m,reader.GetDecimal(3));
                Assert.Equal(1m,reader.GetDecimal(4));
                Assert.Equal(1,reader.GetInt32(5));
                Assert.False(reader.Read());
            }
            using var duplicate=new SqlCommand("dbo.FG_PDA_PUTAWAY_COMPLETE",conn,tx){CommandType=CommandType.StoredProcedure};
            duplicate.Parameters.Add("@Barcode",SqlDbType.NVarChar,50).Value=lot;
            duplicate.Parameters.Add("@Location",SqlDbType.VarChar,20).Value="FA01C1F1";
            duplicate.Parameters.Add("@OperatorID",SqlDbType.VarChar,20).Value="FG-PA-VERIFY";
            Assert.Equal(52515,Assert.Throws<SqlException>(()=>duplicate.ExecuteNonQuery()).Number);
        }
        finally { if(tx.Connection is not null) tx.Rollback(); }
    }

    private static string CreateProductionLot(SqlConnection conn,SqlTransaction tx)
    {
        var code="LOT-VERIFY-PA-"+Guid.NewGuid().ToString("N")[..10];
        using var cmd=new SqlCommand("""
            INSERT dbo.tbl_Lot(LotCode,ItemNo,LineID,ProcessCode,BatchSize,RemainingQty,ProducedAt,
                Status,QualityFlag,InventoryStatus,CreatedBy,CreatedTS)
            VALUES(@Lot,'M2320-PI010NNB','LINE-IMG-01','IMG',1,1,SYSDATETIME(),
                'CONFIRMED','OK','CREATED','FG-PA-VERIFY',SYSDATETIME());
            DECLARE @LotID int=CONVERT(int,SCOPE_IDENTITY());
            INSERT dbo.PR_ProductionResult(EntryNo,LotID,LineID,ProcessCode,GoodQty,OperatorID,
                DefectFlag,EntryAt,ProdDate,CreatedBy,CreatedTS)
            VALUES(@Entry,@LotID,'LINE-IMG-01','IMG',1,'FG-PA-VERIFY',0,SYSDATETIME(),
                CONVERT(date,SYSDATETIME()),'FG-PA-VERIFY',SYSDATETIME());
            """,conn,tx);
        cmd.Parameters.Add("@Lot",SqlDbType.VarChar,40).Value=code;
        cmd.Parameters.Add("@Entry",SqlDbType.VarChar,28).Value="PAVERIFY-"+Guid.NewGuid().ToString("N")[..12];
        cmd.ExecuteNonQuery();
        return code;
    }

    [SkippableTheory]
    [InlineData("FG_OUTBOUND", "This product has already been shipped. Put-away is not allowed.")]
    [InlineData("FG_ADJUST", "This LOT has no available quantity for put-away.")]
    public void Shipped_and_other_zero_quantity_stock_have_distinct_warnings(string source,string expected)
    {
        var connection=Environment.GetEnvironmentVariable("AMES_TEST_CONN");
        Skip.If(string.IsNullOrWhiteSpace(connection),"Set AMES_TEST_CONN for rollback-only DB verification.");
        using var conn=new SqlConnection(connection);
        conn.Open();
        using var tx=conn.BeginTransaction();
        try
        {
            var lot=CreateProductionLot(conn,tx);
            using(var seed=new SqlCommand("""
                INSERT dbo.WH_Inventory(LotNo,UnitType,PartNo,Qty,ReceivedAt,CreatedAt,UpdatedAt)
                VALUES(@Lot,'PART','M2320-PI010NNB',0,SYSDATETIME(),SYSDATETIME(),SYSDATETIME());
                INSERT dbo.WH_InventoryTransaction(TransactionTime,TransactionType,PartNo,LotNo,
                    QtyBefore,QtyChange,QtyAfter,SourceType,OperatorID,CreatedBy,CreatedTS)
                VALUES(SYSDATETIME(),CASE WHEN @Source='FG_OUTBOUND' THEN 'OUT' ELSE 'ADJ' END,
                    'M2320-PI010NNB',@Lot,1,-1,0,@Source,'FG-PA-VERIFY','FG-PA-VERIFY',SYSDATETIME());
                """,conn,tx))
            {
                seed.Parameters.Add("@Lot",SqlDbType.NVarChar,50).Value=lot;
                seed.Parameters.Add("@Source",SqlDbType.VarChar,30).Value=source;
                seed.ExecuteNonQuery();
            }
            using var cmd=new SqlCommand("dbo.FG_PDA_PUTAWAY_COMPLETE",conn,tx){CommandType=CommandType.StoredProcedure};
            cmd.Parameters.Add("@Barcode",SqlDbType.NVarChar,50).Value=lot;
            cmd.Parameters.Add("@Location",SqlDbType.VarChar,20).Value="FA01C1F1";
            cmd.Parameters.Add("@OperatorID",SqlDbType.VarChar,20).Value="FG-PA-VERIFY";
            var error=Assert.Throws<SqlException>(()=>cmd.ExecuteNonQuery());
            Assert.Equal(52514,error.Number);
            Assert.Contains(expected,error.Message);
        }
        finally { if(tx.Connection is not null) tx.Rollback(); }
    }
}
