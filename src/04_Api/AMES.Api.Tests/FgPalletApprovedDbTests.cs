using System.Data;
using System.Reflection;
using System.Runtime.ExceptionServices;
using AMES.Api.Endpoints;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Api.Tests;

// Real API save helpers in an outer rollback transaction; no existing stock is changed.
public sealed class FgPalletApprovedDbTests
{
    [ApprovedPalletTheory]
    [InlineData("VALID")]
    [InlineData("CAPACITY")]
    [InlineData("MIXED")]
    public void Pallet_contents_move_together_or_not_at_all(string scenario)
    {
        var connection = Environment.GetEnvironmentVariable("AMES_PDA_APPROVED_FLOW_DB")!;
        using var conn = new SqlConnection(connection);
        conn.Open();
        using var tx = conn.BeginTransaction();
        var key = "VFY" + Guid.NewGuid().ToString("N")[..10];
        var pallet = key + "-P";
        var location = key + "-L";
        var lot1 = key + "-A";
        var lot2 = key + "-B";
        try
        {
            using (var seed = new SqlCommand("""
                INSERT dbo.MD_Location(LocationID,LocationName,WhCode,AreaCode,LocationType,LocationLevel,Capacity,ActiveFlag,CreatedBy)
                VALUES(@Location,'Rollback pallet verification','EOS','FG_AREA','FG',2,@Capacity,1,@Key);
                INSERT dbo.MD_Pallet(PalletNo,PalletUse,ActiveFlag,ShippedFlag,CreatedBy)
                VALUES(@Pallet,'INTERNAL',1,0,@Key);
                INSERT dbo.WH_Inventory(LotNo,UnitType,PalletNo,Qty,ReceivedAt)
                VALUES(@Pallet,'PALLET',@Pallet,0,SYSDATETIME());
                INSERT dbo.tbl_Lot(LotCode,ItemNo,LineID,ProcessCode,BatchSize,RemainingQty,ProducedAt,Status,QualityFlag,InventoryStatus,CreatedBy,CreatedTS)
                VALUES(@Lot1,'M2320-PI010NNB','LINE-IMG-01','IMG',1,1,SYSDATETIME(),'CONFIRMED','OK','CREATED',@Key,SYSDATETIME());
                DECLARE @Id1 int=CONVERT(int,SCOPE_IDENTITY());
                INSERT dbo.tbl_Lot(LotCode,ItemNo,LineID,ProcessCode,BatchSize,RemainingQty,ProducedAt,Status,QualityFlag,InventoryStatus,CreatedBy,CreatedTS)
                VALUES(@Lot2,@Part2,'LINE-IMG-01','IMG',1,1,SYSDATETIME(),'CONFIRMED','OK','CREATED',@Key,SYSDATETIME());
                DECLARE @Id2 int=CONVERT(int,SCOPE_IDENTITY());
                INSERT dbo.PR_ProductionResult(EntryNo,LotID,LineID,ProcessCode,GoodQty,OperatorID,DefectFlag,EntryAt,ProdDate,CreatedBy,CreatedTS)
                VALUES(@Lot1,@Id1,'LINE-IMG-01','IMG',1,@Key,0,SYSDATETIME(),CONVERT(date,SYSDATETIME()),@Key,SYSDATETIME()),
                      (@Lot2,@Id2,'LINE-IMG-01','IMG',1,@Key,0,SYSDATETIME(),CONVERT(date,SYSDATETIME()),@Key,SYSDATETIME());
                INSERT dbo.WH_Inventory(LotNo,UnitType,ParentLotNo,PartNo,PartName,Qty,ReceivedAt)
                SELECT @Lot1,'PART',@Pallet,ItemNo,ItemName,1,SYSDATETIME() FROM dbo.MD_Item WHERE ItemNo='M2320-PI010NNB';
                INSERT dbo.WH_Inventory(LotNo,UnitType,ParentLotNo,PartNo,PartName,Qty,ReceivedAt)
                SELECT @Lot2,'PART',@Pallet,ItemNo,ItemName,1,SYSDATETIME() FROM dbo.MD_Item WHERE ItemNo=@Part2;
                """, conn, tx))
            {
                foreach (var (name, value) in new[] { ("@Key", key), ("@Pallet", pallet), ("@Location", location), ("@Lot1", lot1), ("@Lot2", lot2), ("@Part2", scenario == "MIXED" ? "12490-04101" : "M2320-PI010NNB") })
                    seed.Parameters.Add(name, SqlDbType.VarChar, 40).Value = value;
                seed.Parameters.Add("@Capacity", SqlDbType.Int).Value = scenario == "CAPACITY" ? 1 : 2;
                seed.ExecuteNonQuery();
            }

            FgEndpoints.PutAwayScanRow Scan() => (FgEndpoints.PutAwayScanRow)Invoke("FindPutAwayScanRow", conn, tx, pallet)!;
            if (scenario == "MIXED")
            {
                Assert.Contains("not registered as ASSY", Assert.Throws<InvalidOperationException>(() => Scan()).Message);
                using var unchanged = new SqlCommand("SELECT COUNT(*) FROM dbo.WH_Inventory WHERE ParentLotNo=@Pallet AND LocationNo IS NULL AND Qty=1", conn, tx);
                unchanged.Parameters.Add("@Pallet", SqlDbType.VarChar, 50).Value = pallet;
                Assert.Equal(2, Convert.ToInt32(unchanged.ExecuteScalar()));
                return;
            }

            var row = Scan();
            Assert.Equal("PALLET", row.BarcodeType);
            Assert.True(row.IsProductionCompleted);
            Assert.Equal(2m, row.Qty);
            var expected = new Dictionary<string, decimal> { [lot1] = 1, [lot2] = 1 };
            Assert.Throws<InvalidOperationException>(() => Invoke("SavePutAwayUnit", conn, tx, row, location, key,
                new Dictionary<string, decimal> { [lot1] = 1 }));
            if (scenario == "CAPACITY")
            {
                Assert.Equal(52518, Assert.Throws<SqlException>(() => Invoke("SavePutAwayUnit", conn, tx, row, location, key, expected)).Number);
                Assert.Null(tx.Connection); // Failure of child two rolled child one back as well.
                using var absent = new SqlCommand("SELECT COUNT(*) FROM dbo.WH_Inventory WHERE LotNo IN(@A,@B)", conn);
                absent.Parameters.Add("@A", SqlDbType.VarChar, 50).Value = lot1;
                absent.Parameters.Add("@B", SqlDbType.VarChar, 50).Value = lot2;
                Assert.Equal(0, Convert.ToInt32(absent.ExecuteScalar()));
                return;
            }

            Assert.Equal(pallet, Invoke("SavePutAwayUnit", conn, tx, row, location, key, expected));
            using var check = new SqlCommand("""
                SELECT COUNT(*) FROM dbo.WH_Inventory WHERE LocationNo=@Location AND (LotNo=@Pallet OR ParentLotNo=@Pallet);
                SELECT COUNT(*) FROM dbo.WH_InventoryTransaction WHERE LotNo IN(@A,@B) AND TransactionType='MOVE' AND QtyChange=0;
                """, conn, tx);
            check.Parameters.Add("@Location", SqlDbType.VarChar, 20).Value = location;
            check.Parameters.Add("@Pallet", SqlDbType.VarChar, 50).Value = pallet;
            check.Parameters.Add("@A", SqlDbType.VarChar, 50).Value = lot1;
            check.Parameters.Add("@B", SqlDbType.VarChar, 50).Value = lot2;
            using var reader = check.ExecuteReader();
            Assert.True(reader.Read()); Assert.Equal(3, reader.GetInt32(0));
            Assert.True(reader.NextResult()); Assert.True(reader.Read()); Assert.Equal(2, reader.GetInt32(0));
        }
        finally { if (tx.Connection is not null) tx.Rollback(); }
    }

    static object? Invoke(string name, params object[] args)
    {
        try { return typeof(FgEndpoints).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    sealed class ApprovedPalletTheoryAttribute : TheoryAttribute
    {
        public ApprovedPalletTheoryAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AMES_PDA_APPROVED_FLOW_DB")))
                Skip = "Requires approval and AMES_PDA_APPROVED_FLOW_DB; creates only unique rollback test data.";
        }
    }
}
