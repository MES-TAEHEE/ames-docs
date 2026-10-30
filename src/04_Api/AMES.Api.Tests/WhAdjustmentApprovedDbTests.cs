using System.Data;
using System.Reflection;
using AMES.Api.Endpoints;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Api.Tests;

public sealed class WhAdjustmentApprovedDbTests
{
    [ApprovedAdjustmentTheory]
    [InlineData(false, "", 30, true)]
    [InlineData(true, "same", 30, true)]
    [InlineData(false, "different", 30, false)]
    [InlineData(true, "", 30, false)]
    [InlineData(false, "", 29, false)]
    [InlineData(true, "same", 29, false)]
    public void Api_adjusts_unassigned_stock_but_rejects_stale_quantity_or_location(
        bool located, string expectedLocation, int expectedQty, bool succeeds)
    {
        var factory = new AmesConnectionFactory(Environment.GetEnvironmentVariable("AMES_PDA_APPROVED_FLOW_DB")!);
        var key = "VAJ" + Guid.NewGuid().ToString("N")[..10];
        var lot = key + "-BOX";
        var location = key + "-LOC";
        using var conn = factory.OpenConnection();
        try
        {
            using (var seed = new SqlCommand("""
                INSERT dbo.MD_Location(LocationID,LocationName,WhCode,AreaCode,LocationLevel,ActiveFlag,CreatedBy)
                VALUES(@Location,'Isolated API adjustment verification','EOS','MAT_AREA',2,1,@Key);
                INSERT dbo.WH_Inventory(LotNo,UnitType,PartNo,PartName,Qty,LocationNo,ReceivedAt)
                SELECT @Lot,'PART',ItemNo,ItemName,30,CASE WHEN @Located=1 THEN @Location END,SYSDATETIME()
                FROM dbo.MD_Item WHERE ItemNo='12490-04101';
                IF @@ROWCOUNT<>1 THROW 51999,'Verification master part is missing.',1;
                """, conn))
            {
                seed.Parameters.Add("@Key", SqlDbType.VarChar, 20).Value = key;
                seed.Parameters.Add("@Lot", SqlDbType.VarChar, 50).Value = lot;
                seed.Parameters.Add("@Location", SqlDbType.VarChar, 50).Value = location;
                seed.Parameters.Add("@Located", SqlDbType.Bit).Value = located;
                seed.ExecuteNonQuery();
            }
            var expected = expectedLocation switch { "same" => location, "different" => key + "-OTHER", _ => expectedLocation };
            var request = new WhEndpoints.AdjustSaveReq("WH", lot, -5, "COUNT_DIFF", "Approved API verification",
                ExpectedQty: expectedQty, ExpectedLocation: expected);
            var save = typeof(WhEndpoints).GetMethod("ExecuteAdjustSave", BindingFlags.NonPublic | BindingFlags.Static)!;
            var result = (WhEndpoints.InboundReceiveResult)save.Invoke(null, [factory, request, key, false])!;
            Assert.True(succeeds == result.Success, result.Message);
            if (!succeeds) Assert.Contains("Stock quantity or location changed", result.Message);
            using var check = new SqlCommand("""
                SELECT Qty,LocationNo FROM dbo.WH_Inventory WHERE LotNo=@Lot;
                SELECT COUNT(*) FROM dbo.WH_InventoryTransaction WHERE LotNo=@Lot;
                """, conn);
            check.Parameters.Add("@Lot", SqlDbType.VarChar, 50).Value = lot;
            using var reader = check.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(succeeds ? 25m : 30m, reader.GetDecimal(0));
            Assert.Equal(located ? location : null, reader[1] as string);
            Assert.True(reader.NextResult()); Assert.True(reader.Read());
            Assert.Equal(succeeds ? 1 : 0, reader.GetInt32(0));
        }
        finally
        {
            using var cleanup = new SqlCommand("""
                DELETE dbo.WH_InventoryTransaction WHERE LotNo=@Lot AND CreatedBy=@Key;
                DELETE dbo.WH_Inventory WHERE LotNo=@Lot;
                DELETE dbo.MD_Location WHERE LocationID=@Location AND CreatedBy=@Key;
                """, conn);
            cleanup.Parameters.Add("@Lot", SqlDbType.VarChar, 50).Value = lot;
            cleanup.Parameters.Add("@Key", SqlDbType.VarChar, 20).Value = key;
            cleanup.Parameters.Add("@Location", SqlDbType.VarChar, 50).Value = location;
            cleanup.ExecuteNonQuery();
        }
    }

    sealed class ApprovedAdjustmentTheoryAttribute : TheoryAttribute
    {
        public ApprovedAdjustmentTheoryAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AMES_PDA_APPROVED_FLOW_DB")))
                Skip = "Requires approval and AMES_PDA_APPROVED_FLOW_DB; only unique test stock is created and removed.";
        }
    }
}
