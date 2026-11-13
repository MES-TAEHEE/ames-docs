using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// SAP_IfLog(dist/migrate_sap_if_log.sql) 기록. 로그는 본 처리를 막으면 안 되므로 어떤 실패도 삼킨다
/// (마이그레이션 전이라 테이블이 없어도 Worker 는 그대로 돈다).
/// </summary>
public sealed class SapIfLogRepository(AmesConnectionFactory f)
{
    public void Write(Guid runId, string ifId, string direction, string? key, long? ifSeq, string? action,
        string result, string? message, int? rows = null, int? durationMs = null, string actor = "SAP-IF")
    {
        try
        {
            using var conn = f.OpenConnection();
            using var cmd = new SqlCommand("""
                INSERT INTO dbo.SAP_IfLog
                    (RunID, IfID, Direction, KeyValue, IfSeq, Action, Result, Message, RowsAffected, DurationMs, CreatedBy)
                VALUES (@Run, @If, @Dir, @Key, @Seq, @Act, @Res, @Msg, @Rows, @Ms, @By)
                """, conn);
            cmd.CommandTimeout = 10;
            cmd.Parameters.Add("@Run", System.Data.SqlDbType.UniqueIdentifier).Value = runId;
            ColumnParams.Add(cmd, "@If",  "SAP_IfLog.IfID",      ifId);
            ColumnParams.Add(cmd, "@Dir", "SAP_IfLog.Direction", direction);
            ColumnParams.Add(cmd, "@Key", "SAP_IfLog.KeyValue",  Trim(key, 60));
            cmd.Parameters.Add("@Seq", System.Data.SqlDbType.BigInt).Value = ifSeq is null ? DBNull.Value : ifSeq;
            ColumnParams.Add(cmd, "@Act", "SAP_IfLog.Action",    Trim(action, 2));
            ColumnParams.Add(cmd, "@Res", "SAP_IfLog.Result",    result);
            ColumnParams.Add(cmd, "@Msg", "SAP_IfLog.Message",   Trim(message, 1000));
            cmd.Parameters.Add("@Rows", System.Data.SqlDbType.Int).Value = rows is null ? DBNull.Value : rows;
            cmd.Parameters.Add("@Ms",   System.Data.SqlDbType.Int).Value = durationMs is null ? DBNull.Value : durationMs;
            ColumnParams.Add(cmd, "@By", "SAP_IfLog.CreatedBy",  actor);
            cmd.ExecuteNonQuery();
        }
        catch { /* 의도적 무시 — 로그가 본 처리를 막지 않는다 */ }
    }

    private static string? Trim(string? s, int max) => s is null || s.Length <= max ? s : s[..max];
}
