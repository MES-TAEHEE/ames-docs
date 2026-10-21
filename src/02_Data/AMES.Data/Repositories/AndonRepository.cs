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
        const string sql = "dbo.SP_POP_ANDON_Raise";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@L",  SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@E",  SqlDbType.VarChar, 20).Value = (object?)equipId ?? DBNull.Value;
        cmd.Parameters.Add("@T",  SqlDbType.VarChar, 20).Value = triggerSource;
        cmd.Parameters.Add("@R",  SqlDbType.VarChar, 20).Value = ruleId;
        cmd.Parameters.Add("@S",  SqlDbType.VarChar, 10).Value = (object?)severity ?? DBNull.Value;
        cmd.Parameters.Add("@By", SqlDbType.VarChar, 20).Value = employeeNo;
        var id = (int)cmd.ExecuteScalar()!;

        // Push stubs — recipients hard-coded for now; replaced when SYS_NotificationRule wires up.
        RecordPush(id, "SUPERVISOR", "PDA",   employeeNo);
        RecordPush(id, "LINE-LEAD",  "EMAIL", employeeNo);
        return id;
    }

    public void RecordPush(int andonId, string recipient, string channel, string employeeNo)
    {
        const string sql = "dbo.SP_POP_ANDON_RecordPush";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@A",  SqlDbType.Int        ).Value = andonId;
        cmd.Parameters.Add("@R",  SqlDbType.VarChar,100).Value = recipient;
        cmd.Parameters.Add("@C",  SqlDbType.VarChar, 20).Value = channel;
        cmd.Parameters.Add("@By", SqlDbType.VarChar, 20).Value = employeeNo;
        cmd.ExecuteNonQuery();
    }

    /// <summary>라인의 진행중(RESOLVED 아님) 안돈 최신 1건 + 부서 행. 없으면 null.</summary>
    public AndonCallDto? GetOpenForLine(string lineId)
    {
        // 구 팝업 상태값(ACKED/RESUMED)은 신 라이프사이클(OPEN/SUP_ACKED/DEPT_CALLED)이 아니므로
        // 활성 안돈으로 노출되면 안 된다 — <> 'RESOLVED' 로 걸면 레거시 행이 영구히 열린 것처럼 보인다.
        const string headSql = "dbo.SP_POP_ANDON_GetOpenForLine_GetAndonCall";
        const string deptSql = "dbo.SP_POP_ANDON_GetOpenForLine_GetAndonDeptCall";
        using var conn = _factory.OpenConnection();

        int andonId; string status, triggeredBy; string? equipId, supNo, supName, cause, severity;
        DateTime triggeredAt; DateTime? supAt, resolvedAt;
        using (var cmd = new SqlCommand(headSql, conn) { CommandType = System.Data.CommandType.StoredProcedure })
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
        using (var cmd = new SqlCommand(deptSql, conn) { CommandType = System.Data.CommandType.StoredProcedure })
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
        const string sql = "dbo.SP_POP_ANDON_IsLineSupervisor";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@L", SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@W", SqlDbType.VarChar, 20).Value = workerNo;
        return cmd.ExecuteScalar() is not null;
    }

    public List<AndonCauseDto> ListCauses()
    {
        const string sql = "dbo.SP_POP_ANDON_ListCauses";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
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
        const string sql = "dbo.SP_POP_ANDON_ListDepts";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        using var r    = cmd.ExecuteReader();
        var list = new List<AndonDeptDto>();
        while (r.Read())
            list.Add(new AndonDeptDto(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)));
        return list;
    }

    /// <summary>안돈 심각도는 불량 심각도 코드(DEFECT_SEVERITY)를 그대로 쓴다.</summary>
    public List<AndonSeverityDto> ListSeverities()
    {
        const string sql = "dbo.SP_POP_ANDON_ListSeverities";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        using var r    = cmd.ExecuteReader();
        var list = new List<AndonSeverityDto>();
        while (r.Read())
            list.Add(new AndonSeverityDto(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)));
        return list;
    }

    // operatorNo = 이 POP 에 로그인한 사번. 감사 컬럼(CreatedBy·ModifiedBy)은 이 값이고,
    // 배지를 찍은 사람은 전용 컬럼(AckedBy·CalledBy·ArrivedNo·TechnicianID·ReportedBy)에 남는다.
    public void AcknowledgeBySupervisor(int andonId, string workerNo, string? name, string operatorNo)
    {
        const string sql = "dbo.SP_POP_ANDON_AcknowledgeBySupervisor";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@ID", SqlDbType.Int          ).Value = andonId;
        cmd.Parameters.Add("@W",  SqlDbType.VarChar,   20).Value = workerNo;
        cmd.Parameters.Add("@N",  SqlDbType.NVarChar,  50).Value = (object?)name ?? DBNull.Value;
        cmd.Parameters.Add("@Op", SqlDbType.VarChar,   20).Value = operatorNo;
        cmd.ExecuteNonQuery();
    }

    /// <summary>원인 확정 + 부서 행 삽입(이미 있는 부서는 건너뜀) + DEPT_CALLED. 한 트랜잭션.</summary>
    /// <summary>
    /// 원인·심각도 확정 + 부서 행 삽입 + 다운타임 원인 기록. 보전(MAINT)이 처음 호출되면
    /// MNT_FailureRegister 도 같은 트랜잭션에서 만든다(안돈 1건당 1행, Source='ANDON').
    /// </summary>
    public void CallDepts(int andonId, string causeCode, string severity, IEnumerable<string> deptCodes, string calledBy, string operatorNo)
    {
        const string headSql = "dbo.SP_POP_ANDON_CallDepts_UpdAndonCall";
        const string downSql = "dbo.SP_POP_ANDON_CallDepts_UpdLineDowntimeLog";
        // 고장번호는 기존 데이터 형식 FAIL-yyMM-NNN 을 따른다. 월별 순번은 같은 트랜잭션 안에서
        // 범위 잠금으로 세므로 두 터미널이 동시에 만들어도 번호가 겹치지 않는다.
        const string failSql = "dbo.SP_POP_ANDON_CallDepts_InsMNT_FailureRegister";
        const string deptSql = "dbo.SP_POP_ANDON_CallDepts_InsAndonDeptCall";
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            using (var cmd = new SqlCommand(headSql, conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
            {
                cmd.Parameters.Add("@ID", SqlDbType.Int          ).Value = andonId;
                cmd.Parameters.Add("@C",  SqlDbType.VarChar,   30).Value = causeCode;
                cmd.Parameters.Add("@S",  SqlDbType.VarChar,   10).Value = severity;
                cmd.Parameters.Add("@Op", SqlDbType.VarChar,   20).Value = operatorNo;
                // 0행이면 다른 터미널이 이미 닫았거나(RESOLVED) 아직 SUP_ACKED 가 아닌 것 — 부서 행을 넣지 않고 롤백
                if (cmd.ExecuteNonQuery() == 0)
                {
                    tx.Rollback();
                    return;
                }
            }
            using (var cmd = new SqlCommand(downSql, conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
            {
                cmd.Parameters.Add("@ID", SqlDbType.Int          ).Value = andonId;
                cmd.Parameters.Add("@C",  SqlDbType.VarChar,   30).Value = causeCode;
                cmd.Parameters.Add("@Op", SqlDbType.VarChar,   20).Value = operatorNo;
                cmd.ExecuteNonQuery();
            }
            var codes = deptCodes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var d in codes)
            {
                using var cmd = new SqlCommand(deptSql, conn, tx) { CommandType = System.Data.CommandType.StoredProcedure };
                cmd.Parameters.Add("@ID", SqlDbType.Int        ).Value = andonId;
                cmd.Parameters.Add("@D",  SqlDbType.VarChar, 20).Value = d;
                cmd.Parameters.Add("@By", SqlDbType.VarChar, 20).Value = calledBy;
                cmd.Parameters.Add("@Op", SqlDbType.VarChar, 20).Value = operatorNo;
                cmd.ExecuteNonQuery();
            }
            if (codes.Contains(AndonDeptCodes.Maint, StringComparer.OrdinalIgnoreCase))
            {
                using var cmd = new SqlCommand(failSql, conn, tx) { CommandType = System.Data.CommandType.StoredProcedure };
                cmd.Parameters.Add("@ID",  SqlDbType.Int          ).Value = andonId;
                cmd.Parameters.Add("@Ref", SqlDbType.VarChar,   24).Value = andonId.ToString();
                cmd.Parameters.Add("@C",   SqlDbType.VarChar,   15).Value = causeCode;
                cmd.Parameters.Add("@S",   SqlDbType.VarChar,   10).Value = severity;
                cmd.Parameters.Add("@By",  SqlDbType.VarChar,   20).Value = calledBy;
                cmd.Parameters.Add("@Op",  SqlDbType.VarChar,   20).Value = operatorNo;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>도착 기록. 보전 행이면 고장 등록을 IN_PROGRESS 로 올리고 조치 이력(ARRIVED)을 남긴다.</summary>
    public void RecordArrival(int deptCallId, string workerNo, string? name, string operatorNo)
    {
        const string sql = "dbo.SP_POP_ANDON_RecordArrival";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@ID",    SqlDbType.Int         ).Value = deptCallId;
        cmd.Parameters.Add("@W",     SqlDbType.VarChar,  20).Value = workerNo;
        cmd.Parameters.Add("@N",     SqlDbType.NVarChar, 50).Value = (object?)name ?? DBNull.Value;
        cmd.Parameters.Add("@Op",    SqlDbType.VarChar,  20).Value = operatorNo;
        cmd.Parameters.Add("@Maint", SqlDbType.VarChar,  20).Value = AndonDeptCodes.Maint;
        cmd.ExecuteNonQuery();
    }

    /// <summary>ACK 기록. 보전 행이면 조치 이력(ACK)을 남긴다. 고장 종료는 안돈 Resolve 가 한다.</summary>
    public void AckDept(int deptCallId, string operatorNo)
    {
        const string sql = "dbo.SP_POP_ANDON_AckDept";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@ID",    SqlDbType.Int        ).Value = deptCallId;
        cmd.Parameters.Add("@Op",    SqlDbType.VarChar, 20).Value = operatorNo;
        cmd.Parameters.Add("@Maint", SqlDbType.VarChar, 20).Value = AndonDeptCodes.Maint;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 종료. 다운타임을 닫고 안돈이 만든 고장 등록을 RESOLVED 로 올린다.
    /// causeCode·severity 는 자체 해결(부서 호출 없음)일 때만 넘어온다.
    /// </summary>
    public void Resolve(int andonId, string? causeCode, string? severity, string operatorNo)
    {
        const string sql = "dbo.SP_POP_ANDON_Resolve";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@ID",  SqlDbType.Int        ).Value = andonId;
        cmd.Parameters.Add("@Ref", SqlDbType.VarChar, 24).Value = andonId.ToString();
        cmd.Parameters.Add("@C",   SqlDbType.VarChar, 20).Value = (object?)causeCode ?? DBNull.Value;
        cmd.Parameters.Add("@S",   SqlDbType.VarChar, 10).Value = (object?)severity ?? DBNull.Value;
        cmd.Parameters.Add("@Op",  SqlDbType.VarChar,   20).Value = operatorNo;
        cmd.ExecuteNonQuery();
    }
}
