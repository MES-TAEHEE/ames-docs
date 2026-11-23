using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Services.SapIf;

public sealed record SapOutParam(string Name, string Table, string Column, object? Value);

public sealed record SapOutResult(string Status, int? DocEntry, int? DocNum, string? Msg);

/// <summary>EOS_IF 송신 INSERT·조회 SQL 을 만든다(순수 — 단위 테스트). 실행은 <see cref="SapOutSender"/>.</summary>
public static class SapOutSql
{
    public static (string Sql, IReadOnlyList<SapOutParam> Params) HeaderInsert(
        SapOutSpec spec, string action, DateTime eventAt, IReadOnlyDictionary<string, object?> hdr)
    {
        var ps = new List<SapOutParam>
        {
            new("@act", spec.HdrTable, "IF_ACTION", action),
            new("@ts", spec.HdrTable, "MES_TS", eventAt),
        };
        var cols = new List<string>(); var vals = new List<string>();
        int i = 0;
        foreach (var c in spec.HdrCols)
        {
            if (!hdr.TryGetValue(c, out var v)) continue;
            var p = "@h" + i++;
            ps.Add(new(p, spec.HdrTable, c, v)); cols.Add(c); vals.Add(p);
        }
        var sql = $"INSERT dbo.{spec.HdrTable} (IF_ACTION, IF_STATUS, MES_TS, {string.Join(", ", cols)}) " +
                  $"OUTPUT inserted.IF_SEQ VALUES (@act, 'N', @ts, {string.Join(", ", vals)})";
        return (sql, ps);
    }

    public static (string Sql, IReadOnlyList<SapOutParam> Params) LineInsert(
        SapOutSpec spec, long hdrSeq, IReadOnlyDictionary<string, object?> line)
    {
        var ps = new List<SapOutParam>();
        var cols = new List<string> { "HDR_SEQ" }; var vals = new List<string> { "@hdr" };
        int i = 0;
        foreach (var c in spec.LineCols ?? [])
        {
            if (!line.TryGetValue(c, out var v)) continue;
            var p = "@l" + i++;
            ps.Add(new(p, spec.LineTable!, c, v)); cols.Add(c); vals.Add(p);
        }
        return ($"INSERT dbo.{spec.LineTable} ({string.Join(", ", cols)}) VALUES ({string.Join(", ", vals)})", ps);
    }

    /// <summary>이미 보낸 행인가 — 키(생산오더는 키+액션)로 찾는다. 재전송 때 중복 INSERT 를 막고 기존 IF_SEQ 를 돌려준다(멱등).</summary>
    public static string ExistsSql(SapOutSpec spec)
        => $"SELECT TOP (1) IF_SEQ FROM dbo.{spec.HdrTable} WHERE {spec.KeyCol} = @key"
           + (spec.KeyWithAction ? " AND IF_ACTION = @act" : "") + " ORDER BY IF_SEQ DESC";

    public static string ResultSql(SapOutSpec spec)
        => $"SELECT IF_STATUS, B1_DOCENTRY, B1_DOCNUM, IF_MSG FROM dbo.{spec.HdrTable} WHERE IF_SEQ = @seq";
}

/// <summary>
/// 아웃박스 한 건을 EOS_IF 에 쓴다. 헤더 + 라인을 EOS_IF 트랜잭션 하나로 넣고(AMES 트랜잭션과 분리), 넣은 헤더 IF_SEQ 를 돌려준다.
/// 같은 키가 이미 있으면 다시 넣지 않고 기존 IF_SEQ 를 돌려준다(AMES 쪽 처리 표시가 실패해도 재시도가 안전하다).
/// 값 길이·형식은 EOS_IF 컬럼 기준으로 <c>ColumnParams</c> 가 저장 전에 검사한다.
/// </summary>
public sealed class SapOutSender(EosIfConnectionFactory eos)
{
    public long Send(SapOutSpec spec, string action, DateTime eventAt, SapOutPayload payload)
    {
        using var conn = eos.OpenConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            var key = payload.Hdr[spec.KeyCol]!.ToString();
            using (var ex = new SqlCommand(SapOutSql.ExistsSql(spec), conn, tx) { CommandTimeout = 30 })
            {
                ColumnParams.Add(ex, "@key", $"{spec.HdrTable}.{spec.KeyCol}", key);
                if (spec.KeyWithAction) ColumnParams.Add(ex, "@act", $"{spec.HdrTable}.IF_ACTION", action);
                var found = ex.ExecuteScalar();
                if (found is not null and not DBNull) { tx.Rollback(); return Convert.ToInt64(found); }
            }

            var (hsql, hps) = SapOutSql.HeaderInsert(spec, action, eventAt, payload.Hdr);
            long seq;
            using (var h = new SqlCommand(hsql, conn, tx) { CommandTimeout = 30 })
            {
                foreach (var p in hps) ColumnParams.Add(h, p.Name, $"{p.Table}.{p.Column}", p.Value);
                seq = Convert.ToInt64(h.ExecuteScalar()!);
            }

            foreach (var line in payload.Lines)
            {
                var (lsql, lps) = SapOutSql.LineInsert(spec, seq, line);
                using var l = new SqlCommand(lsql, conn, tx) { CommandTimeout = 30 };
                l.Parameters.Add("@hdr", System.Data.SqlDbType.BigInt).Value = seq;
                foreach (var p in lps) ColumnParams.Add(l, p.Name, $"{p.Table}.{p.Column}", p.Value);
                l.ExecuteNonQuery();
            }

            tx.Commit();
            return seq;
        }
        catch { try { tx.Rollback(); } catch { } throw; }
    }

    /// <summary>웅진이 되쓴 처리 결과(IF_STATUS N/P/Y/E, B1 문서번호, 오류 사유). 행이 없으면 null.</summary>
    public SapOutResult? ReadResult(SqlConnection eosConn, SapOutSpec spec, long seq)
    {
        using var cmd = new SqlCommand(SapOutSql.ResultSql(spec), eosConn) { CommandTimeout = 30 };
        cmd.Parameters.Add("@seq", System.Data.SqlDbType.BigInt).Value = seq;
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new SapOutResult(r.GetString(0).Trim(),
            r.IsDBNull(1) ? null : r.GetInt32(1), r.IsDBNull(2) ? null : r.GetInt32(2), r.IsDBNull(3) ? null : r.GetString(3));
    }
}
