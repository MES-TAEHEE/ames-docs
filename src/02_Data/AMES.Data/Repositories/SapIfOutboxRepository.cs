using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// SAP_IfOutbox(dist/migrate_sap_if_log.sql) — MES → B1 송신 큐. 현장 코드는 <see cref="Enqueue"/> 한 줄만 부르고 커밋하고,
/// SAPOUT Worker 가 EOS_IF 로 옮긴다. 같은 (IfID, MesKey, Action) 은 한 번만 들어간다(UNIQUE — 멱등).
/// 상태: N 대기 → P 전송중 → Y EOS_IF 기록 완료(이후 웅진 결과를 B1Status 로 회수) / E 전송 실패(재시도 초과).
/// </summary>
public sealed class SapIfOutboxRepository(AmesConnectionFactory f)
{
    public sealed record Row(long OutboxID, string IfID, string MesKey, string Action, string PayloadJson,
        DateTime EventAt, string Status, int RetryCount, string? LastError, long? EosIfSeq);

    private const string Cols = "OutboxID, IfID, MesKey, Action, PayloadJson, EventAt, Status, RetryCount, LastError, EosIfSeq";
    private const string OutCols = "inserted.OutboxID, inserted.IfID, inserted.MesKey, inserted.Action, inserted.PayloadJson, inserted.EventAt, " +
                                   "inserted.Status, inserted.RetryCount, inserted.LastError, inserted.EosIfSeq";

    /// <summary>
    /// 아웃박스에 한 건 넣는다. 이미 있으면 null. 호출자의 트랜잭션 안에서 부를 수 있다(현장 확정 트랜잭션 — 같은 AMES DB).
    /// 실패해도 확정을 막지 않도록 호출자는 세이브포인트로 감싸 쓴다.
    /// </summary>
    public static long? Enqueue(SqlConnection conn, SqlTransaction? tx, string ifId, string mesKey, string action,
        string payloadJson, DateTime? eventAt = null, string actor = "SAP-IF")
    {
        using var cmd = new SqlCommand("""
            IF NOT EXISTS (SELECT 1 FROM dbo.SAP_IfOutbox WHERE IfID = @If AND MesKey = @Key AND Action = @Act)
                INSERT INTO dbo.SAP_IfOutbox (IfID, MesKey, Action, PayloadJson, EventAt, CreatedBy)
                OUTPUT inserted.OutboxID
                VALUES (@If, @Key, @Act, @Json, ISNULL(@At, SYSDATETIME()), @By)
            """, conn, tx) { CommandTimeout = 30 };
        ColumnParams.Add(cmd, "@If", "SAP_IfOutbox.IfID", ifId);
        ColumnParams.Add(cmd, "@Key", "SAP_IfOutbox.MesKey", mesKey);
        ColumnParams.Add(cmd, "@Act", "SAP_IfOutbox.Action", action);
        cmd.Parameters.Add("@Json", System.Data.SqlDbType.NVarChar, -1).Value = payloadJson;
        cmd.Parameters.Add("@At", System.Data.SqlDbType.DateTime2).Value = (object?)eventAt ?? DBNull.Value;
        ColumnParams.Add(cmd, "@By", "SAP_IfOutbox.CreatedBy", actor);
        var o = cmd.ExecuteScalar();
        return o is null or DBNull ? null : Convert.ToInt64(o);
    }

    /// <summary>
    /// 처리 순서 게이트(정의서 2.4: 생성(C) → 투입 → 실적 → 종료(L)). 앞 단계가 B1 에서 성공(B1Status='Y')해야 다음 단계를 보낸다 —
    /// 앞 단계가 E 면 뒤 단계가 연쇄로 E 가 나지 않도록 기다린다. 같은 WO 는 payload 의 hdr.MES_WO_NO 로 잇는다.
    /// </summary>
    internal static string GateSql(string ifId) => ifId switch
    {
        "IF-T02" => """
            AND EXISTS (SELECT 1 FROM dbo.SAP_IfOutbox g WHERE g.IfID = 'IF-T01' AND g.Action = 'C' AND g.Status = 'Y' AND g.B1Status = 'Y'
                AND JSON_VALUE(g.PayloadJson, '$.hdr.MES_WO_NO') = JSON_VALUE(x.PayloadJson, '$.hdr.MES_WO_NO'))
            """,
        "IF-T03" => """
            AND EXISTS (SELECT 1 FROM dbo.SAP_IfOutbox g WHERE g.IfID = 'IF-T02' AND g.Status = 'Y' AND g.B1Status = 'Y'
                AND JSON_VALUE(g.PayloadJson, '$.hdr.MES_WO_NO') = JSON_VALUE(x.PayloadJson, '$.hdr.MES_WO_NO'))
            """,
        // 종료(L)·취소(X)·수정은 생성(C)이 B1 에 반영된 뒤에만, 종료(L)는 그 WO 의 투입·실적이 모두 B1 에 반영된 뒤에만
        "IF-T01" => """
            AND (x.Action = 'C' OR EXISTS (SELECT 1 FROM dbo.SAP_IfOutbox g WHERE g.IfID = 'IF-T01' AND g.Action = 'C' AND g.Status = 'Y' AND g.B1Status = 'Y'
                AND g.MesKey = x.MesKey))
            AND (x.Action <> 'L' OR NOT EXISTS (SELECT 1 FROM dbo.SAP_IfOutbox g WHERE g.IfID IN ('IF-T02', 'IF-T03')
                AND JSON_VALUE(g.PayloadJson, '$.hdr.MES_WO_NO') = JSON_VALUE(x.PayloadJson, '$.hdr.MES_WO_NO')
                AND (g.Status <> 'Y' OR ISNULL(g.B1Status, '') <> 'Y')))
            """,
        _ => "",
    };

    private const string DueCondition = """
        ((x.Status = 'N' AND (x.NextTryAt IS NULL OR x.NextTryAt <= SYSDATETIME()))
         OR (x.Status = 'P' AND x.ModifiedTS < DATEADD(MINUTE, -@Re, SYSDATETIME())))
        """;

    /// <summary>보낼 차례인 행(읽기 전용 — DryRun 용). 순서·게이트는 <see cref="ClaimDue"/> 와 같다.</summary>
    public List<Row> PeekDue(string ifId, int batch, int reclaimMinutes = 10)
    {
        using var conn = f.OpenConnection();
        using var cmd = new SqlCommand($"""
            SELECT TOP (@N) {Cols.Replace("OutboxID", "x.OutboxID")}
            FROM dbo.SAP_IfOutbox AS x
            WHERE x.IfID = @If AND {DueCondition} {GateSql(ifId)}
            ORDER BY x.OutboxID
            """, conn) { CommandTimeout = 30 };
        AddDueParams(cmd, ifId, batch, reclaimMinutes);
        return Read(cmd);
    }

    /// <summary>보낼 차례인 행을 잡아(P) 돌려준다. 여러 Api 가 같은 DB 를 써도 한 행은 한 번만 잡힌다(READPAST).</summary>
    public List<Row> ClaimDue(string ifId, int batch, int reclaimMinutes = 10)
    {
        using var conn = f.OpenConnection();
        using var cmd = new SqlCommand($"""
            ;WITH q AS (
                SELECT TOP (@N) x.*
                FROM dbo.SAP_IfOutbox AS x WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE x.IfID = @If AND {DueCondition} {GateSql(ifId)}
                ORDER BY x.OutboxID)
            UPDATE q SET Status = 'P', ModifiedBy = 'SAPOUT', ModifiedTS = SYSDATETIME()
            OUTPUT {OutCols}
            """, conn) { CommandTimeout = 30 };
        AddDueParams(cmd, ifId, batch, reclaimMinutes);
        return Read(cmd);
    }

    private static void AddDueParams(SqlCommand cmd, string ifId, int batch, int reclaimMinutes)
    {
        cmd.Parameters.Add("@N", System.Data.SqlDbType.Int).Value = Math.Max(1, batch);
        cmd.Parameters.Add("@Re", System.Data.SqlDbType.Int).Value = Math.Max(1, reclaimMinutes);
        ColumnParams.Add(cmd, "@If", "SAP_IfOutbox.IfID", ifId);
    }

    private static List<Row> Read(SqlCommand cmd)
    {
        var list = new List<Row>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new Row(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetDateTime(5),
                r.GetString(6).Trim(), r.GetInt32(7), r.IsDBNull(8) ? null : r.GetString(8), r.IsDBNull(9) ? null : r.GetInt64(9)));
        return list;
    }

    /// <summary>EOS_IF 에 쓴 뒤 — Y, 넣은 IF_SEQ, 웅진 결과 대기(B1Status N).</summary>
    public void MarkSent(long id, long eosIfSeq)
        => Exec("UPDATE dbo.SAP_IfOutbox SET Status = 'Y', EosIfSeq = @S, SentAt = SYSDATETIME(), B1Status = 'N', LastError = NULL, " +
                "ModifiedBy = 'SAPOUT', ModifiedTS = SYSDATETIME() WHERE OutboxID = @Id",
            ("@S", System.Data.SqlDbType.BigInt, eosIfSeq), ("@Id", System.Data.SqlDbType.BigInt, id));

    /// <summary>전송 실패 — 다음 시도 시각을 정해 N 으로 되돌리거나(재시도 초과면 <paramref name="final"/> 로 E).</summary>
    public void MarkFailed(long id, int retryCount, DateTime? nextTryAt, string error, bool final)
    {
        using var conn = f.OpenConnection();
        using var cmd = new SqlCommand("""
            UPDATE dbo.SAP_IfOutbox
               SET Status = @St, RetryCount = @R, NextTryAt = @Next, LastError = @Err, ModifiedBy = 'SAPOUT', ModifiedTS = SYSDATETIME()
             WHERE OutboxID = @Id
            """, conn) { CommandTimeout = 30 };
        cmd.Parameters.Add("@St", System.Data.SqlDbType.Char, 1).Value = final ? "E" : "N";
        cmd.Parameters.Add("@R", System.Data.SqlDbType.Int).Value = retryCount;
        cmd.Parameters.Add("@Next", System.Data.SqlDbType.DateTime2).Value = (object?)nextTryAt ?? DBNull.Value;
        cmd.Parameters.Add("@Err", System.Data.SqlDbType.NVarChar, 1000).Value = Truncate(error, 1000);
        cmd.Parameters.Add("@Id", System.Data.SqlDbType.BigInt).Value = id;
        cmd.ExecuteNonQuery();
    }

    /// <summary>DryRun 메모 — 값이 바뀔 때만 쓴다(true = 바뀜). 상태는 N 그대로.</summary>
    public bool SetDryNote(long id, string note)
    {
        using var conn = f.OpenConnection();
        using var cmd = new SqlCommand(
            "UPDATE dbo.SAP_IfOutbox SET LastError = @N, ModifiedBy = 'SAPOUT', ModifiedTS = SYSDATETIME() " +
            "WHERE OutboxID = @Id AND ISNULL(LastError, N'') <> @N", conn) { CommandTimeout = 30 };
        cmd.Parameters.Add("@N", System.Data.SqlDbType.NVarChar, 1000).Value = Truncate(note, 1000);
        cmd.Parameters.Add("@Id", System.Data.SqlDbType.BigInt).Value = id;
        return cmd.ExecuteNonQuery() > 0;
    }

    /// <summary>EOS_IF 에 썼지만 웅진 결과가 확정(Y)되지 않은 행(EOS_IF IF_SEQ). 오류(E)도 포함 — 웅진이 B1 쪽을 고친 뒤 같은 행을 다시 처리해 Y 로 바꾸기 때문(11-23).</summary>
    public List<(long OutboxID, long EosIfSeq)> PendingResults(string ifId)
    {
        using var conn = f.OpenConnection();
        using var cmd = new SqlCommand("""
            SELECT OutboxID, EosIfSeq FROM dbo.SAP_IfOutbox
             WHERE IfID = @If AND Status = 'Y' AND EosIfSeq IS NOT NULL AND ISNULL(B1Status, 'N') IN ('N', 'P', 'E')
             ORDER BY OutboxID
            """, conn) { CommandTimeout = 30 };
        ColumnParams.Add(cmd, "@If", "SAP_IfOutbox.IfID", ifId);
        var list = new List<(long, long)>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add((r.GetInt64(0), r.GetInt64(1)));
        return list;
    }

    public void UpdateB1(long id, string b1Status, int? docEntry, int? docNum, string? msg)
    {
        using var conn = f.OpenConnection();
        using var cmd = new SqlCommand("""
            UPDATE dbo.SAP_IfOutbox
               SET B1Status = @S, B1DocEntry = @E, B1DocNum = @D, B1Msg = @M, ModifiedBy = 'SAPOUT', ModifiedTS = SYSDATETIME()
             WHERE OutboxID = @Id
            """, conn) { CommandTimeout = 30 };
        cmd.Parameters.Add("@S", System.Data.SqlDbType.Char, 1).Value = b1Status;
        cmd.Parameters.Add("@E", System.Data.SqlDbType.Int).Value = (object?)docEntry ?? DBNull.Value;
        cmd.Parameters.Add("@D", System.Data.SqlDbType.Int).Value = (object?)docNum ?? DBNull.Value;
        cmd.Parameters.Add("@M", System.Data.SqlDbType.NVarChar, 1000).Value = (object?)Truncate(msg, 1000) ?? DBNull.Value;
        cmd.Parameters.Add("@Id", System.Data.SqlDbType.BigInt).Value = id;
        cmd.ExecuteNonQuery();
    }

    private void Exec(string sql, params (string Name, System.Data.SqlDbType Type, object Value)[] ps)
    {
        using var conn = f.OpenConnection();
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        foreach (var (n, t, v) in ps) cmd.Parameters.Add(n, t).Value = v;
        cmd.ExecuteNonQuery();
    }

    private static string? Truncate(string? s, int max) => s is null || s.Length <= max ? s : s[..max];
}
