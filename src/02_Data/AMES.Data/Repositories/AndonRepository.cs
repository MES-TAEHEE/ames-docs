using System.Data;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// PR_AndonCall / PR_AndonDeptCall. 상태 전이는 모두 WHERE 로 현재 상태를 확인해
/// 두 터미널이 같은 안돈을 동시에 밀어도 한 번만 적용된다.
/// </summary>
public sealed class AndonRepository
{
    private readonly AmesConnectionFactory _factory;
    public AndonRepository(AmesConnectionFactory f) => _factory = f;

    /// <summary>
    /// 안돈 발동 + 라인 다운타임 시작(PP_LineDowntimeLog, EndTS 는 Resolve 가 닫는다).
    /// severity 는 슈퍼바이저가 나중에 고르므로 보통 null 로 둔다.
    /// </summary>
    public int Raise(string lineId, string? equipId, string triggerSource,
                     string ruleId, string? severity, string employeeNo)
    {
        const string sql = """
            SET XACT_ABORT ON;
            BEGIN TRAN;
            DECLARE @now DATETIME2 = SYSDATETIME();
            DECLARE @ids TABLE (AndonID INT);
            INSERT INTO dbo.PR_AndonCall
                (LineID, EquipID, TriggerSource, RuleID, Severity, TriggeredAt,
                 Status, CreatedBy, CreatedTS)
            OUTPUT INSERTED.AndonID INTO @ids
            VALUES (@L, @E, @T, @R, @S, @now, 'OPEN', @By, @now);
            DECLARE @id INT = (SELECT TOP 1 AndonID FROM @ids);
            INSERT INTO dbo.PP_LineDowntimeLog
                (LineID, StartTS, ReasonCode, Comment, LoggedBy, AndonID, CreatedBy, CreatedTS)
            VALUES (@L, @now, 'ANDON', N'Andon #' + CAST(@id AS NVARCHAR(10)), @By, @id, @By, @now);
            COMMIT;
            SELECT @id;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@L",  SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@E",  SqlDbType.VarChar, 20).Value = (object?)equipId ?? DBNull.Value;
        cmd.Parameters.Add("@T",  SqlDbType.VarChar, 20).Value = triggerSource;
        cmd.Parameters.Add("@R",  SqlDbType.VarChar, 20).Value = ruleId;
        cmd.Parameters.Add("@S",  SqlDbType.VarChar, 10).Value = (object?)severity ?? DBNull.Value;
        cmd.Parameters.Add("@By", SqlDbType.VarChar, 50).Value = employeeNo;
        var id = (int)cmd.ExecuteScalar()!;

        // Push stubs — recipients hard-coded for now; replaced when SYS_NotificationRule wires up.
        RecordPush(id, "SUPERVISOR", "PDA",   employeeNo);
        RecordPush(id, "LINE-LEAD",  "EMAIL", employeeNo);
        return id;
    }

    public void RecordPush(int andonId, string recipient, string channel, string employeeNo)
    {
        const string sql = """
            INSERT INTO dbo.PR_AndonPush
                (AndonID, Recipient, Channel, CreatedBy, CreatedTS)
            VALUES (@A, @R, @C, @By, SYSDATETIME());
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@A",  SqlDbType.Int        ).Value = andonId;
        cmd.Parameters.Add("@R",  SqlDbType.VarChar,100).Value = recipient;
        cmd.Parameters.Add("@C",  SqlDbType.VarChar, 20).Value = channel;
        cmd.Parameters.Add("@By", SqlDbType.VarChar, 50).Value = employeeNo;
        cmd.ExecuteNonQuery();
    }

    /// <summary>라인의 진행중(RESOLVED 아님) 안돈 최신 1건 + 부서 행. 없으면 null.</summary>
    public AndonCallDto? GetOpenForLine(string lineId)
    {
        // 구 팝업 상태값(ACKED/RESUMED)은 신 라이프사이클(OPEN/SUP_ACKED/DEPT_CALLED)이 아니므로
        // 활성 안돈으로 노출되면 안 된다 — <> 'RESOLVED' 로 걸면 레거시 행이 영구히 열린 것처럼 보인다.
        const string headSql = """
            SELECT TOP 1 AndonID, LineID, EquipID, Status, TriggeredAt, CreatedBy,
                   AckedBy, AckedAt, SupervisorName, ReasonCode, ResumedAt, Severity
            FROM   dbo.PR_AndonCall
            WHERE  LineID = @L AND Status IN ('OPEN', 'SUP_ACKED', 'DEPT_CALLED')
            ORDER  BY AndonID DESC;
            """;
        const string deptSql = """
            SELECT d.DeptCallID, d.DeptCode, d.CalledAt, d.ArrivedAt, d.ArrivedNo, d.ArrivedName, d.AckedAt,
                   COALESCE(c.CodeName, d.DeptCode) AS DeptName, c.CodeNameEn
            FROM   dbo.PR_AndonDeptCall d
            LEFT   JOIN dbo.MD_CodeItem c ON c.GroupCode = 'ANDON_DEPT' AND c.CodeValue = d.DeptCode
            WHERE  d.AndonID = @A
            ORDER  BY d.DeptCallID;
            """;
        using var conn = _factory.OpenConnection();

        int andonId; string status, triggeredBy; string? equipId, supNo, supName, cause, severity;
        DateTime triggeredAt; DateTime? supAt, resolvedAt;
        using (var cmd = new SqlCommand(headSql, conn))
        {
            cmd.Parameters.Add("@L", SqlDbType.VarChar, 20).Value = lineId;
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            andonId     = r.GetInt32(0);
            equipId     = r.IsDBNull(2) ? null : r.GetString(2);
            status      = r.IsDBNull(3) ? "OPEN" : r.GetString(3);
            triggeredAt = r.IsDBNull(4) ? DateTime.MinValue : r.GetDateTime(4);
            triggeredBy = r.GetString(5);
            supNo       = r.IsDBNull(6) ? null : r.GetString(6);
            supAt       = r.IsDBNull(7) ? null : r.GetDateTime(7);
            supName     = r.IsDBNull(8) ? null : r.GetString(8);
            cause       = r.IsDBNull(9) ? null : r.GetString(9);
            resolvedAt  = r.IsDBNull(10) ? null : r.GetDateTime(10);
            severity    = r.IsDBNull(11) ? null : r.GetString(11);
        }

        var depts = new List<AndonDeptCallDto>();
        using (var cmd = new SqlCommand(deptSql, conn))
        {
            cmd.Parameters.Add("@A", SqlDbType.Int).Value = andonId;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                depts.Add(new AndonDeptCallDto
                {
                    DeptCallId  = r.GetInt32(0),
                    DeptCode    = r.GetString(1),
                    CalledAt    = r.GetDateTime(2),
                    ArrivedAt   = r.IsDBNull(3) ? null : r.GetDateTime(3),
                    ArrivedNo   = r.IsDBNull(4) ? null : r.GetString(4),
                    ArrivedName = r.IsDBNull(5) ? null : r.GetString(5),
                    AckedAt     = r.IsDBNull(6) ? null : r.GetDateTime(6),
                    DeptName    = r.GetString(7),
                    DeptNameEn  = r.IsDBNull(8) ? null : r.GetString(8),
                });
            }
        }

        return new AndonCallDto
        {
            AndonId = andonId, LineId = lineId, EquipId = equipId, Status = status,
            TriggeredAt = triggeredAt, TriggeredBy = triggeredBy,
            SupervisorNo = supNo, SupervisorName = supName, SupervisorAt = supAt,
            CauseCode = cause, Severity = severity, ResolvedAt = resolvedAt, Depts = depts,
        };
    }

    public bool IsLineSupervisor(string lineId, string workerNo)
    {
        const string sql = """
            SELECT 1 FROM dbo.MD_LineSupervisor
            WHERE  LineID = @L AND WorkerNo = @W AND ActiveFlag = 1;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@L", SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@W", SqlDbType.VarChar, 20).Value = workerNo;
        return cmd.ExecuteScalar() is not null;
    }

    public List<AndonCauseDto> ListCauses()
    {
        const string sql = """
            SELECT CodeValue, COALESCE(CodeName, CodeValue), CodeNameEn, NULLIF(LTRIM(RTRIM(Attribute1)), N'')
            FROM   dbo.MD_CodeItem
            WHERE  GroupCode = 'ANDON_CAUSE' AND ISNULL(UseFlag, 1) = 1
            ORDER  BY ISNULL(SortOrder, 9999), CodeValue;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        using var r    = cmd.ExecuteReader();
        var list = new List<AndonCauseDto>();
        while (r.Read())
            list.Add(new AndonCauseDto(r.GetString(0), r.GetString(1),
                                       r.IsDBNull(2) ? null : r.GetString(2),
                                       r.IsDBNull(3) ? null : r.GetString(3)));
        return list;
    }

    public List<AndonDeptDto> ListDepts()
    {
        const string sql = """
            SELECT CodeValue, COALESCE(CodeName, CodeValue), CodeNameEn
            FROM   dbo.MD_CodeItem
            WHERE  GroupCode = 'ANDON_DEPT' AND ISNULL(UseFlag, 1) = 1
            ORDER  BY ISNULL(SortOrder, 9999), CodeValue;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        using var r    = cmd.ExecuteReader();
        var list = new List<AndonDeptDto>();
        while (r.Read())
            list.Add(new AndonDeptDto(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)));
        return list;
    }

    /// <summary>안돈 심각도는 불량 심각도 코드(DEFECT_SEVERITY)를 그대로 쓴다.</summary>
    public List<AndonSeverityDto> ListSeverities()
    {
        const string sql = """
            SELECT CodeValue, COALESCE(CodeName, CodeValue), CodeNameEn
            FROM   dbo.MD_CodeItem
            WHERE  GroupCode = 'DEFECT_SEVERITY' AND ISNULL(UseFlag, 1) = 1
            ORDER  BY ISNULL(SortOrder, 9999), CodeValue;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        using var r    = cmd.ExecuteReader();
        var list = new List<AndonSeverityDto>();
        while (r.Read())
            list.Add(new AndonSeverityDto(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)));
        return list;
    }

    public void AcknowledgeBySupervisor(int andonId, string workerNo, string? name)
    {
        const string sql = """
            UPDATE dbo.PR_AndonCall
            SET    AckedBy = @W, AckedAt = SYSDATETIME(), SupervisorName = @N,
                   Status = 'SUP_ACKED', ModifiedBy = @W, ModifiedTS = SYSDATETIME()
            WHERE  AndonID = @ID AND Status = 'OPEN';
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@ID", SqlDbType.Int          ).Value = andonId;
        cmd.Parameters.Add("@W",  SqlDbType.NVarChar, 450).Value = workerNo;
        cmd.Parameters.Add("@N",  SqlDbType.NVarChar,  50).Value = (object?)name ?? DBNull.Value;
        cmd.ExecuteNonQuery();
    }

    /// <summary>원인 확정 + 부서 행 삽입(이미 있는 부서는 건너뜀) + DEPT_CALLED. 한 트랜잭션.</summary>
    /// <summary>
    /// 원인·심각도 확정 + 부서 행 삽입 + 다운타임 원인 기록. 보전(MAINT)이 처음 호출되면
    /// MNT_FailureRegister 도 같은 트랜잭션에서 만든다(안돈 1건당 1행, Source='ANDON').
    /// </summary>
    public void CallDepts(int andonId, string causeCode, string severity, IEnumerable<string> deptCodes, string calledBy)
    {
        const string headSql = """
            UPDATE dbo.PR_AndonCall
            SET    ReasonCode = @C, Severity = @S, Status = 'DEPT_CALLED', ModifiedBy = @By, ModifiedTS = SYSDATETIME()
            WHERE  AndonID = @ID AND Status IN ('SUP_ACKED', 'DEPT_CALLED');
            """;
        const string downSql = """
            UPDATE dbo.PP_LineDowntimeLog
            SET    CauseCode = @C, ModifiedBy = @By, ModifiedTS = SYSDATETIME()
            WHERE  AndonID = @ID AND EndTS IS NULL;
            """;
        // 고장번호는 기존 데이터 형식 FAIL-yyMM-NNN 을 따른다. 월별 순번은 같은 트랜잭션 안에서
        // 범위 잠금으로 세므로 두 터미널이 동시에 만들어도 번호가 겹치지 않는다.
        const string failSql = """
            IF NOT EXISTS (SELECT 1 FROM dbo.MNT_FailureRegister WHERE Source = 'ANDON' AND AndonRefID = @Ref)
            BEGIN
                DECLARE @ym  VARCHAR(4) = FORMAT(SYSDATETIME(), 'yyMM');
                DECLARE @seq INT = ISNULL((SELECT MAX(TRY_CAST(RIGHT(FailureNumber, 3) AS INT))
                                           FROM dbo.MNT_FailureRegister WITH (UPDLOCK, HOLDLOCK)
                                           WHERE FailureNumber LIKE 'FAIL-' + @ym + '-%'), 0) + 1;
                INSERT INTO dbo.MNT_FailureRegister
                    (FailureNumber, EquipID, FailureType, Symptom, Severity, Source, AndonRefID, DowntimeID,
                     Status, ReportedBy, ReportedAt, CreatedBy, CreatedTS)
                SELECT 'FAIL-' + @ym + '-' + RIGHT('000' + CAST(@seq AS VARCHAR(4)), 3),
                       a.EquipID, @C,
                       N'Andon #' + @Ref + N' · ' + COALESCE(ci.CodeName, @C),
                       @S, 'ANDON', @Ref, d.DowntimeID,
                       'OPEN', @By, SYSDATETIME(), @By, SYSDATETIME()
                FROM   dbo.PR_AndonCall a
                LEFT   JOIN dbo.MD_CodeItem ci ON ci.GroupCode = 'ANDON_CAUSE' AND ci.CodeValue = @C
                OUTER  APPLY (SELECT TOP 1 DowntimeID FROM dbo.PP_LineDowntimeLog WHERE AndonID = a.AndonID ORDER BY DowntimeID DESC) d
                WHERE  a.AndonID = @ID;
            END
            """;
        const string deptSql = """
            INSERT INTO dbo.PR_AndonDeptCall (AndonID, DeptCode, CalledAt, CalledBy, CreatedBy, CreatedTS)
            SELECT @ID, @D, SYSDATETIME(), @By, @By, SYSDATETIME()
            WHERE  NOT EXISTS (SELECT 1 FROM dbo.PR_AndonDeptCall WHERE AndonID = @ID AND DeptCode = @D);
            """;
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            using (var cmd = new SqlCommand(headSql, conn, tx))
            {
                cmd.Parameters.Add("@ID", SqlDbType.Int          ).Value = andonId;
                cmd.Parameters.Add("@C",  SqlDbType.VarChar,   30).Value = causeCode;
                cmd.Parameters.Add("@S",  SqlDbType.VarChar,   10).Value = severity;
                cmd.Parameters.Add("@By", SqlDbType.NVarChar, 450).Value = calledBy;
                // 0행이면 다른 터미널이 이미 닫았거나(RESOLVED) 아직 SUP_ACKED 가 아닌 것 — 부서 행을 넣지 않고 롤백
                if (cmd.ExecuteNonQuery() == 0)
                {
                    tx.Rollback();
                    return;
                }
            }
            using (var cmd = new SqlCommand(downSql, conn, tx))
            {
                cmd.Parameters.Add("@ID", SqlDbType.Int          ).Value = andonId;
                cmd.Parameters.Add("@C",  SqlDbType.VarChar,   30).Value = causeCode;
                cmd.Parameters.Add("@By", SqlDbType.NVarChar, 450).Value = calledBy;
                cmd.ExecuteNonQuery();
            }
            var codes = deptCodes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var d in codes)
            {
                using var cmd = new SqlCommand(deptSql, conn, tx);
                cmd.Parameters.Add("@ID", SqlDbType.Int        ).Value = andonId;
                cmd.Parameters.Add("@D",  SqlDbType.VarChar, 20).Value = d;
                cmd.Parameters.Add("@By", SqlDbType.VarChar, 50).Value = calledBy;
                cmd.ExecuteNonQuery();
            }
            if (codes.Contains(AndonDeptCodes.Maint, StringComparer.OrdinalIgnoreCase))
            {
                using var cmd = new SqlCommand(failSql, conn, tx);
                cmd.Parameters.Add("@ID",  SqlDbType.Int          ).Value = andonId;
                cmd.Parameters.Add("@Ref", SqlDbType.VarChar,   24).Value = andonId.ToString();
                cmd.Parameters.Add("@C",   SqlDbType.VarChar,   15).Value = causeCode;
                cmd.Parameters.Add("@S",   SqlDbType.VarChar,   10).Value = severity;
                cmd.Parameters.Add("@By",  SqlDbType.NVarChar, 450).Value = calledBy;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>도착 기록. 보전 행이면 고장 등록을 IN_PROGRESS 로 올리고 조치 이력(ARRIVED)을 남긴다.</summary>
    public void RecordArrival(int deptCallId, string workerNo, string? name)
    {
        const string sql = """
            SET XACT_ABORT ON;
            BEGIN TRAN;
            UPDATE dbo.PR_AndonDeptCall
            SET    ArrivedAt = SYSDATETIME(), ArrivedNo = @W, ArrivedName = @N,
                   ModifiedBy = @W, ModifiedTS = SYSDATETIME()
            WHERE  DeptCallID = @ID AND ArrivedAt IS NULL;
            IF @@ROWCOUNT > 0
            BEGIN
                UPDATE f SET Status = 'IN_PROGRESS', ModifiedBy = @W, ModifiedTS = SYSDATETIME()
                FROM   dbo.MNT_FailureRegister f
                JOIN   dbo.PR_AndonDeptCall d ON d.AndonID = TRY_CAST(f.AndonRefID AS INT)
                WHERE  d.DeptCallID = @ID AND d.DeptCode = @Maint AND f.Source = 'ANDON' AND f.Status = 'OPEN';
                INSERT INTO dbo.MNT_FailureAction (FailureID, ActionType, Description, TechnicianID, ActionAt, CreatedBy, CreatedTS)
                SELECT f.FailureID, 'ARRIVED', N'Andon responder arrived: ' + @W + COALESCE(N' ' + @N, N''), @W, SYSDATETIME(), @W, SYSDATETIME()
                FROM   dbo.MNT_FailureRegister f
                JOIN   dbo.PR_AndonDeptCall d ON d.AndonID = TRY_CAST(f.AndonRefID AS INT)
                WHERE  d.DeptCallID = @ID AND d.DeptCode = @Maint AND f.Source = 'ANDON';
            END
            COMMIT;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@ID",    SqlDbType.Int         ).Value = deptCallId;
        cmd.Parameters.Add("@W",     SqlDbType.VarChar,  20).Value = workerNo;
        cmd.Parameters.Add("@N",     SqlDbType.NVarChar, 50).Value = (object?)name ?? DBNull.Value;
        cmd.Parameters.Add("@Maint", SqlDbType.VarChar,  20).Value = AndonDeptCodes.Maint;
        cmd.ExecuteNonQuery();
    }

    /// <summary>ACK 기록. 보전 행이면 조치 이력(ACK)을 남긴다. 고장 종료는 안돈 Resolve 가 한다.</summary>
    public void AckDept(int deptCallId)
    {
        const string sql = """
            SET XACT_ABORT ON;
            BEGIN TRAN;
            UPDATE dbo.PR_AndonDeptCall
            SET    AckedAt = SYSDATETIME(), ModifiedBy = ArrivedNo, ModifiedTS = SYSDATETIME()
            WHERE  DeptCallID = @ID AND ArrivedAt IS NOT NULL AND AckedAt IS NULL;
            IF @@ROWCOUNT > 0
                INSERT INTO dbo.MNT_FailureAction (FailureID, ActionType, Description, TechnicianID, ActionAt, CreatedBy, CreatedTS)
                SELECT f.FailureID, 'ACK', N'Andon responder acknowledged', d.ArrivedNo, SYSDATETIME(), d.ArrivedNo, SYSDATETIME()
                FROM   dbo.MNT_FailureRegister f
                JOIN   dbo.PR_AndonDeptCall d ON d.AndonID = TRY_CAST(f.AndonRefID AS INT)
                WHERE  d.DeptCallID = @ID AND d.DeptCode = @Maint AND f.Source = 'ANDON';
            COMMIT;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@ID",    SqlDbType.Int        ).Value = deptCallId;
        cmd.Parameters.Add("@Maint", SqlDbType.VarChar, 20).Value = AndonDeptCodes.Maint;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 종료. 다운타임을 닫고 안돈이 만든 고장 등록을 RESOLVED 로 올린다.
    /// causeCode·severity 는 자체 해결(부서 호출 없음)일 때만 넘어온다.
    /// </summary>
    public void Resolve(int andonId, string? causeCode, string? severity)
    {
        const string sql = """
            SET XACT_ABORT ON;
            BEGIN TRAN;
            DECLARE @now DATETIME2 = SYSDATETIME();
            UPDATE dbo.PR_AndonCall
            SET    ResumedAt   = @now,
                   DowntimeSec = DATEDIFF(SECOND, TriggeredAt, @now),
                   ReasonCode  = COALESCE(@C, ReasonCode),
                   Severity    = COALESCE(@S, Severity),
                   Status      = 'RESOLVED',
                   ModifiedBy  = AckedBy,
                   ModifiedTS  = @now
            WHERE  AndonID = @ID AND Status <> 'RESOLVED';
            IF @@ROWCOUNT > 0
            BEGIN
                UPDATE dbo.PP_LineDowntimeLog
                SET    EndTS = @now, DurationMin = DATEDIFF(MINUTE, StartTS, @now),
                       CauseCode = COALESCE(CauseCode, @C), ModifiedTS = @now
                WHERE  AndonID = @ID AND EndTS IS NULL;
                UPDATE dbo.MNT_FailureRegister
                SET    Status = 'RESOLVED', ResolvedAt = @now, ModifiedTS = @now
                WHERE  Source = 'ANDON' AND AndonRefID = @Ref AND Status <> 'RESOLVED';
            END
            COMMIT;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@ID",  SqlDbType.Int        ).Value = andonId;
        cmd.Parameters.Add("@Ref", SqlDbType.VarChar, 24).Value = andonId.ToString();
        cmd.Parameters.Add("@C",   SqlDbType.VarChar, 30).Value = (object?)causeCode ?? DBNull.Value;
        cmd.Parameters.Add("@S",   SqlDbType.VarChar, 10).Value = (object?)severity ?? DBNull.Value;
        cmd.ExecuteNonQuery();
    }
}
