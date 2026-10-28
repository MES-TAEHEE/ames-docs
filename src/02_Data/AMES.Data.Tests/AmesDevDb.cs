using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

// 테스트 클래스를 차례로 돌린다(10-15) — DB 통합 테스트들이 같은 DB 의 같은 표(PP_LineSchedule·PP_CustomerOrder·WO·실적)와
// 전역 설정(SYS_Config PP_SO_AUTO_CONFIRM)을 동시에 써서, 병렬이면 교착 희생자·남의 데이터가 섞인 집계로 간헐 실패했다. 직렬화 비용은 전체 수십 초.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace AMES.Data.Tests;

/// <summary>AMES_DEV 통합 테스트 공용 헬퍼 — 접속·SQL 실행. 여러 *Tests 클래스가 공유한다.</summary>
internal static class AmesDevDb
{
    public static readonly string Conn =
        Environment.GetEnvironmentVariable("AMES_TEST_CONN")
        ?? "";   // 접속 정보는 소스에 두지 않는다 — 없으면 DB 테스트는 건너뛴다

    public static AmesConnectionFactory? TryFactory()
    {
        try
        {
            var f = new AmesConnectionFactory(Conn);
            using var c = f.OpenConnection();
            return f;
        }
        catch { return null; }
    }

    public static void Exec(AmesConnectionFactory f, string sql, params (string, object)[] ps)
    {
        using var conn = f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    public static object? Scalar(AmesConnectionFactory f, string sql, params (string, object)[] ps)
    {
        using var conn = f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        return cmd.ExecuteScalar();
    }
}
