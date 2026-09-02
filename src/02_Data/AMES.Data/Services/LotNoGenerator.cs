using System.Data;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Services;

/// <summary>
/// INJ 원천 Lot 9자리 채번: [년1][월1][일1][라인코드2][순번4] (예 A91I10001).
/// 년은 (연도-2026) mod 26 → A~Z 26년 순환 — Lot 수명이 26년을 넘지 않아 실무 모호성 없음.
/// 순번은 SYS_LotSeq 원자 증가 — MAX+1 스캔의 동시 중복과 테이블 스캔 비용을 피한다.
/// </summary>
public static class LotNoGenerator
{
    public static char EncodeYear(int year)
        => (char)('A' + (((year - 2026) % 26) + 26) % 26);

    public static char EncodeMonth(int month) => month switch
    {
        >= 1 and <= 9   => (char)('0' + month),
        >= 10 and <= 12 => (char)('A' + month - 10),
        _ => throw new ArgumentOutOfRangeException(nameof(month)),
    };

    public static char EncodeDay(int day) => day switch
    {
        >= 1 and <= 9   => (char)('0' + day),
        >= 10 and <= 31 => (char)('A' + day - 10),
        _ => throw new ArgumentOutOfRangeException(nameof(day)),
    };

    public static string BuildHeader(DateTime date, string linePrefix)
    {
        ArgumentNullException.ThrowIfNull(linePrefix);
        if (linePrefix.Length != 2)
            throw new ArgumentException($"LotPrefix must be 2 chars: '{linePrefix}'", nameof(linePrefix));
        return $"{EncodeYear(date.Year)}{EncodeMonth(date.Month)}{EncodeDay(date.Day)}{linePrefix}";
    }

    /// <summary>
    /// 호출자의 트랜잭션 안에서 다음 LotNo 를 원자적으로 채번한다.
    /// 커밋 전까지 같은 헤더의 채번이 직렬화되고, 롤백 시 카운터도 롤백된다(결번 없음).
    /// </summary>
    public static string NextLotNo(SqlConnection conn, SqlTransaction tx, string lineId, DateTime date)
    {
        string? prefix;
        using (var cmd = new SqlCommand(
            "SELECT LotPrefix FROM dbo.MD_Line WHERE LineID = @L;", conn, tx))
        {
            cmd.Parameters.Add("@L", SqlDbType.VarChar, 20).Value = lineId;
            prefix = (cmd.ExecuteScalar() as string)?.Trim();
        }
        if (string.IsNullOrEmpty(prefix))
            throw new InvalidOperationException(
                $"MD_Line.LotPrefix 미등록: {lineId} — 라인 마스터에 2자 코드를 등록해야 채번할 수 있다.");

        var header = BuildHeader(date, prefix);
        var seq = NextSeq(conn, tx, header);
        if (seq > 9999)
            throw new InvalidOperationException($"LotNo 순번 초과(9999): header={header}");
        return header + seq.ToString("D4");
    }

    static int NextSeq(SqlConnection conn, SqlTransaction tx, string header)
    {
        const string updateSql = """
            UPDATE dbo.SYS_LotSeq SET LastSeq += 1, ModifiedTS = SYSDATETIME()
            OUTPUT inserted.LastSeq WHERE Header = @H;
            """;
        int? Exec(string sql)
        {
            using var cmd = new SqlCommand(sql, conn, tx);
            cmd.Parameters.Add("@H", SqlDbType.Char, 5).Value = header;
            return cmd.ExecuteScalar() as int?;
        }

        var seq = Exec(updateSql);
        if (seq is not null) return seq.Value;

        // 그날 그 라인의 첫 채번 — INSERT 경쟁에서 지면 PK 충돌 → UPDATE 재시도 1회.
        try
        {
            using var ins = new SqlCommand(
                "INSERT INTO dbo.SYS_LotSeq (Header, LastSeq) VALUES (@H, 1);", conn, tx);
            ins.Parameters.Add("@H", SqlDbType.Char, 5).Value = header;
            ins.ExecuteNonQuery();
            return 1;
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
            return Exec(updateSql)
                ?? throw new InvalidOperationException($"SYS_LotSeq 채번 실패: {header}");
        }
    }
}
