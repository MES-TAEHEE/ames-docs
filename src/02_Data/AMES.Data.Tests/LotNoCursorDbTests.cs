using System.Data;
using AMES.Data.Services;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

/// <summary>
/// 복구 뒤 오프라인 LOT 이 DB 채번을 넘어서도록 SYS_LotSeq 를 끌어올리는지. 트랜잭션을 롤백하므로 데이터를 남기지 않는다.
/// AMES_TEST_CONN 이 닿을 때만 돈다.
/// </summary>
public class LotNoCursorDbTests
{
    // 실제 헤더와 겹치지 않는 가짜 헤더 — 롤백되므로 행은 남지 않는다.
    private const string Header = "ZZZ99";

    [SkippableFact]
    public void EnsureAtLeast_raises_the_sequence_and_never_lowers_it()
    {
        var f = AmesDevDb.TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        using var conn = f!.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            LotNoGenerator.EnsureAtLeast(conn, tx, Header + "0005");
            Assert.Equal(5, LastSeq(conn, tx));

            LotNoGenerator.EnsureAtLeast(conn, tx, Header + "0003");   // 낮은 번호는 내리지 않는다
            Assert.Equal(5, LastSeq(conn, tx));

            LotNoGenerator.EnsureAtLeast(conn, tx, Header + "0009");
            Assert.Equal(9, LastSeq(conn, tx));
        }
        finally { tx.Rollback(); }
    }

    private static int LastSeq(SqlConnection conn, SqlTransaction tx)
    {
        using var cmd = new SqlCommand("SELECT LastSeq FROM dbo.SYS_LotSeq WHERE Header = @H;", conn, tx);
        cmd.Parameters.Add("@H", SqlDbType.Char, 5).Value = Header;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}
