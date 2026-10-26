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
        var prefix = ResolvePrefix(conn, tx, lineId);
        var header = BuildHeader(date, prefix);
        var seq = NextSeq(conn, tx, header);
        if (seq > 9999)
            throw new InvalidOperationException($"LotNo 순번 초과(9999): header={header}");
        return header + seq.ToString("D4");
    }

    static string ResolvePrefix(SqlConnection conn, SqlTransaction tx, string lineId)
    {
        string? prefix;
        using (var cmd = new SqlCommand(
            "dbo.SP_POP_LOT_NextLotNo", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
        {
            cmd.Parameters.Add("@L", SqlDbType.VarChar, 20).Value = lineId;
            prefix = (cmd.ExecuteScalar() as string)?.Trim();
        }
        if (string.IsNullOrEmpty(prefix))
            throw new InvalidOperationException(
                $"MD_Line.LotPrefix 미등록: {lineId} — 라인 마스터에 2자 코드를 등록해야 채번할 수 있다.");
        return prefix;
    }

    /// <summary>
    /// 예약한 LotNo 구간 — <see cref="Header"/>(년월일+라인코드 5자) 뒤에 FirstSeq..FirstSeq+Count-1 을 4자리로 붙인다.
    /// 오프라인 터미널이 DB 없이 쓸 번호를 미리 확보한 것이라, 이 구간은 SYS_LotSeq 에서 이미 빠져 다른 누구에게도 발급되지 않는다.
    /// </summary>
    public sealed record LotNoBlock(string Header, int FirstSeq, int Count)
    {
        public IEnumerable<string> Codes
        {
            get { for (var i = 0; i < Count; i++) yield return Header + (FirstSeq + i).ToString("D4"); }
        }
    }

    /// <summary>
    /// 그 날·그 라인의 LotNo 순번 <paramref name="count"/> 개를 한 문장으로 확보한다(SYS_LotSeq.LastSeq += count).
    /// 9999 를 넘는 만큼은 확보하지 않으므로 Count 가 요청보다 작거나 0 일 수 있다. 안 쓴 번호는 결번으로 남는다.
    /// </summary>
    public static LotNoBlock Reserve(SqlConnection conn, SqlTransaction tx, string lineId, DateTime date, int count)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        var header = BuildHeader(date, ResolvePrefix(conn, tx, lineId));

        (int Before, int After)? Bump()
        {
            using var cmd = new SqlCommand("""
                UPDATE dbo.SYS_LotSeq
                SET    LastSeq = CASE WHEN LastSeq + @N > 9999 THEN 9999 ELSE LastSeq + @N END, ModifiedTS = SYSDATETIME()
                OUTPUT deleted.LastSeq, inserted.LastSeq
                WHERE  Header = @H;
                """, conn, tx);
            cmd.Parameters.Add("@H", SqlDbType.Char, 5).Value = header;
            cmd.Parameters.Add("@N", SqlDbType.Int).Value = count;
            using var r = cmd.ExecuteReader();
            return r.Read() ? (Convert.ToInt32(r[0]), Convert.ToInt32(r[1])) : null;
        }

        var hit = Bump();
        if (hit is null)
        {
            // 그날 그 라인의 첫 채번 — INSERT 경쟁에서 지면 PK 충돌 → UPDATE 재시도 1회(NextSeq 와 같은 규칙).
            try
            {
                var first = Math.Min(count, 9999);
                using var ins = new SqlCommand("INSERT INTO dbo.SYS_LotSeq (Header, LastSeq) VALUES (@H, @L);", conn, tx);
                ins.Parameters.Add("@H", SqlDbType.Char, 5).Value = header;
                ins.Parameters.Add("@L", SqlDbType.Int).Value = first;
                ins.ExecuteNonQuery();
                return new LotNoBlock(header, 1, first);
            }
            catch (SqlException ex) when (ex.Number is 2627 or 2601)
            {
                hit = Bump() ?? throw new InvalidOperationException($"SYS_LotSeq 예약 실패: {header}");
            }
        }
        return new LotNoBlock(header, hit.Value.Before + 1, hit.Value.After - hit.Value.Before);
    }

    /// <summary>
    /// 오프라인 사본용 — 그 날·그 라인의 LotNo 헤더(라인코드 포함)와 DB 에 기록된 마지막 순번(없으면 0).
    /// 읽기만 한다: 번호를 올리지 않는다.
    /// </summary>
    public static (string Prefix, string Header, int LastSeq) ReadCursor(SqlConnection conn, string lineId, DateTime date)
    {
        string? prefix;
        using (var cmd = new SqlCommand("SELECT LotPrefix FROM dbo.MD_Line WHERE LineID = @L;", conn))
        {
            cmd.Parameters.Add("@L", SqlDbType.VarChar, 20).Value = lineId;
            prefix = (cmd.ExecuteScalar() as string)?.Trim();
        }
        if (string.IsNullOrEmpty(prefix))
            throw new InvalidOperationException($"MD_Line.LotPrefix 미등록: {lineId}");
        var header = BuildHeader(date, prefix);
        using var last = new SqlCommand("SELECT LastSeq FROM dbo.SYS_LotSeq WHERE Header = @H;", conn);
        last.Parameters.Add("@H", SqlDbType.Char, 5).Value = header;
        var seq = last.ExecuteScalar() as int?;
        return (prefix, header, seq ?? 0);
    }

    /// <summary>
    /// 복구 뒤 오프라인 LOT 을 DB 에 넣을 때 호출한다 — 그 번호의 순번만큼 SYS_LotSeq 를 끌어올린다.
    /// 이렇게 해야 복구 뒤 DB 채번(INJ 등)이 이미 쓴 번호를 다시 내지 않는다. 같은 트랜잭션.
    /// </summary>
    public static void EnsureAtLeast(SqlConnection conn, SqlTransaction tx, string lotCode)
    {
        if (lotCode.Length != 9 || !int.TryParse(lotCode.AsSpan(5), out var seq))
            throw new ArgumentException($"LotNo 형식이 아니다: {lotCode}", nameof(lotCode));
        var header = lotCode[..5];
        using var cmd = new SqlCommand("""
            IF EXISTS (SELECT 1 FROM dbo.SYS_LotSeq WHERE Header = @H)
                UPDATE dbo.SYS_LotSeq SET LastSeq = CASE WHEN LastSeq < @S THEN @S ELSE LastSeq END, ModifiedTS = SYSDATETIME() WHERE Header = @H;
            ELSE
                INSERT INTO dbo.SYS_LotSeq (Header, LastSeq) VALUES (@H, @S);
            """, conn, tx);
        cmd.Parameters.Add("@H", SqlDbType.Char, 5).Value = header;
        cmd.Parameters.Add("@S", SqlDbType.Int).Value = seq;
        try { cmd.ExecuteNonQuery(); }
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
            // 같은 순간 다른 채번이 헤더를 만들었다 — 한 번 더 올림만 한다.
            using var upd = new SqlCommand(
                "UPDATE dbo.SYS_LotSeq SET LastSeq = CASE WHEN LastSeq < @S THEN @S ELSE LastSeq END, ModifiedTS = SYSDATETIME() WHERE Header = @H;",
                conn, tx);
            upd.Parameters.Add("@H", SqlDbType.Char, 5).Value = header;
            upd.Parameters.Add("@S", SqlDbType.Int).Value = seq;
            upd.ExecuteNonQuery();
        }
    }

    static int NextSeq(SqlConnection conn, SqlTransaction tx, string header)
    {
        const string updateSql = "dbo.SP_POP_LOT_NextSeq_UpdLotSeq";
        int? Exec(string sql)
        {
            using var cmd = new SqlCommand(sql, conn, tx) { CommandType = System.Data.CommandType.StoredProcedure };
            cmd.Parameters.Add("@H", SqlDbType.Char, 5).Value = header;
            return cmd.ExecuteScalar() as int?;
        }

        var seq = Exec(updateSql);
        if (seq is not null) return seq.Value;

        // 그날 그 라인의 첫 채번 — INSERT 경쟁에서 지면 PK 충돌 → UPDATE 재시도 1회.
        try
        {
            using var ins = new SqlCommand(
                "dbo.SP_POP_LOT_NextSeq_InsLotSeq", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure };
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
