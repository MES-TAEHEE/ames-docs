using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

public class SparePartReceiptDbTests
{
    [SkippableFact]
    public void Receipt_then_release_records_history_without_leaving_test_stock()
    {
        var factory = AmesDevDb.TryFactory();
        Skip.If(factory is null, "AMES_DEV unavailable");
        using var conn = factory!.OpenConnection();
        using var tx = conn.BeginTransaction();
        var part = "UTSP" + Guid.NewGuid().ToString("N")[..12];
        try
        {
            using var cmd = new SqlCommand("""
                DECLARE @Location varchar(20) = (SELECT TOP 1 LocationID FROM dbo.MD_Location
                    WHERE AreaCode='SPARE_PARTS_AREA' AND COALESCE(ActiveFlag,1)=1 ORDER BY LocationID);
                IF @Location IS NULL THROW 51000, 'No SP test location.', 1;
                INSERT dbo.MD_SparePart (SparePartNo,PartNo,PartName,OnHandQty,ActiveFlag,CreatedBy)
                VALUES (@Part,@Part,'Rollback-only receipt test',0,1,'receipt-test');
                EXEC dbo.SP_PDA_STOCK_MOVE @Part,'IN',1,@Location,'receipt-test','Rollback-only receipt';
                EXEC dbo.SP_PDA_STOCK_MOVE @Part,'OUT',1,NULL,'receipt-test','Rollback-only release';
                SELECT P.OnHandQty, (SELECT COUNT(*) FROM dbo.MNT_SparePartsTxn T
                    WHERE T.SparePartNo=@Part AND T.MoveType='IN' AND T.RefType='PDA') AS Receipts
                FROM dbo.MD_SparePart P WHERE P.SparePartNo=@Part;
                """, conn, tx);
            cmd.Parameters.AddWithValue("@Part", part);
            using var reader = cmd.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(0, reader.GetInt32(0));
            Assert.Equal(1, reader.GetInt32(1));
        }
        finally { if (tx.Connection is not null) tx.Rollback(); }
        Assert.Equal(0, Convert.ToInt32(AmesDevDb.Scalar(factory,
            "SELECT COUNT(*) FROM dbo.MD_SparePart WHERE SparePartNo=@Part", ("@Part", part))));
    }

    [SkippableFact]
    public void Master_quantity_without_receipt_cannot_be_released()
    {
        var factory = AmesDevDb.TryFactory();
        Skip.If(factory is null, "AMES_DEV unavailable");
        using var conn = factory!.OpenConnection();
        using var tx = conn.BeginTransaction();
        var part = "UTSP" + Guid.NewGuid().ToString("N")[..12];
        try
        {
            using var cmd = new SqlCommand("""
                INSERT dbo.MD_SparePart (SparePartNo,PartNo,PartName,OnHandQty,ActiveFlag,CreatedBy)
                VALUES (@Part,@Part,'Rollback-only never-received test',3,1,'receipt-test');
                EXEC dbo.SP_PDA_STOCK_MOVE @Part,'OUT',1,NULL,'receipt-test','Must be blocked';
                """, conn, tx);
            cmd.Parameters.AddWithValue("@Part", part);
            var error = Assert.Throws<SqlException>(() => cmd.ExecuteNonQuery());
            Assert.Equal(51817, error.Number);
            Assert.Contains("has not been received", error.Message);
        }
        finally { if (tx.Connection is not null) tx.Rollback(); }
        Assert.Equal(0, Convert.ToInt32(AmesDevDb.Scalar(factory,
            "SELECT COUNT(*) FROM dbo.MD_SparePart WHERE SparePartNo=@Part", ("@Part", part))));
    }
}
