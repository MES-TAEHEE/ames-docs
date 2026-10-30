using System.Data;
using AMES.Data.Repositories;
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
            var serial = CreateSerial(conn, tx, part, 0);
            Move(conn, tx, serial, "IN");
            Move(conn, tx, serial, "OUT");
            using var cmd = new SqlCommand("""
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
            var serial = CreateSerial(conn, tx, part, 3);
            var error = Assert.Throws<SqlException>(() => Move(conn, tx, serial, "OUT"));
            Assert.Equal(51817, error.Number);
            Assert.Contains("has not been received", error.Message);
        }
        finally { if (tx.Connection is not null) tx.Rollback(); }
        Assert.Equal(0, Convert.ToInt32(AmesDevDb.Scalar(factory,
            "SELECT COUNT(*) FROM dbo.MD_SparePart WHERE SparePartNo=@Part", ("@Part", part))));
    }

    [SkippableFact]
    public void Old_transaction_cannot_cancel_a_new_release_and_a_correct_cancel_still_works()
    {
        var factory = AmesDevDb.TryFactory();
        Skip.If(factory is null, "AMES_TEST_CONN unavailable");
        using var conn = factory!.OpenConnection();
        using var tx = conn.BeginTransaction();
        var part = "UTSP" + Guid.NewGuid().ToString("N")[..12];
        try
        {
            var serial = CreateSerial(conn, tx, part, 0);
            Move(conn, tx, serial, "IN"); Move(conn, tx, serial, "OUT");
            int Latest()
            {
                using var cmd = new SqlCommand("SELECT MAX(SparePartsTxnID) FROM dbo.MNT_SparePartsTxn WHERE SparePartNo=@Part AND MoveType='OUT'", conn, tx);
                cmd.Parameters.AddWithValue("@Part", part); return Convert.ToInt32(cmd.ExecuteScalar());
            }
            void Cancel(int id, SqlTransaction? transaction)
            {
                using var cmd = new SqlCommand(PdaSparePartsSql.Cancel, conn, transaction);
                cmd.Parameters.AddWithValue("@SerialNo", serial); cmd.Parameters.AddWithValue("@MoveType", "OUT");
                cmd.Parameters.AddWithValue("@UserId", "receipt-test"); cmd.Parameters.AddWithValue("@Note", "Isolated stale cancellation regression");
                cmd.Parameters.AddWithValue("@TransactionId", id); cmd.ExecuteNonQuery();
            }
            var first = Latest(); Cancel(first, tx);
            Move(conn, tx, serial, "OUT"); Assert.NotEqual(first, Latest());
            // The invalid request must roll back the entire isolated fixture transaction.
            Assert.Equal(51830, Assert.Throws<SqlException>(() => Cancel(first, tx)).Number);
            Assert.Null(tx.Connection);
            using var absent = new SqlCommand("SELECT COUNT(*) FROM dbo.MD_SparePart WHERE SparePartNo=@Part", conn);
            absent.Parameters.AddWithValue("@Part", part); Assert.Equal(0, Convert.ToInt32(absent.ExecuteScalar()));
        }
        finally { if (tx.Connection is not null) tx.Rollback(); }
    }

    private static string CreateSerial(SqlConnection conn, SqlTransaction tx, string part, int balance)
    {
        using (var seed = new SqlCommand("""
            INSERT dbo.MD_SparePart (SparePartNo,PartNo,PartName,ZoneCode,Slot,OnHandQty,ActiveFlag,CreatedBy)
            VALUES (@Part,@Part,'Rollback-only receipt test','SP_A1','01',@Balance,1,'receipt-test');
            """, conn, tx))
        {
            seed.Parameters.Add("@Part", SqlDbType.VarChar, 16).Value = part;
            seed.Parameters.Add("@Balance", SqlDbType.Int).Value = balance;
            seed.ExecuteNonQuery();
        }
        using var create = new SqlCommand(PdaSparePartsSql.SerialCreate, conn, tx);
        create.Parameters.Add("@SparePartNo", SqlDbType.VarChar, 16).Value = part;
        create.Parameters.Add("@Count", SqlDbType.Int).Value = 1;
        create.Parameters.Add("@UserId", SqlDbType.VarChar, 20).Value = "receipt-test";
        return (string)create.ExecuteScalar()!;
    }

    private static void Move(SqlConnection conn, SqlTransaction tx, string serial, string type)
    {
        using var move = new SqlCommand(PdaSparePartsSql.Move, conn, tx);
        move.Parameters.Add("@SerialNo", SqlDbType.VarChar, 24).Value = serial;
        move.Parameters.Add("@MoveType", SqlDbType.VarChar, 10).Value = type;
        move.Parameters.Add("@Qty", SqlDbType.Int).Value = 1;
        move.Parameters.Add("@LocationId", SqlDbType.VarChar, 20).Value = DBNull.Value;
        move.Parameters.Add("@WorkOrderNo", SqlDbType.VarChar, 28).Value = DBNull.Value;
        move.Parameters.Add("@UserId", SqlDbType.VarChar, 20).Value = "receipt-test";
        move.Parameters.Add("@Note", SqlDbType.NVarChar, 500).Value = "Rollback-only movement";
        move.ExecuteNonQuery();
    }
}
