using System.Data;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// MD-033 라인 슈퍼바이저 관리 — MD_LineSupervisor (LineID, EmployeeNo) CRUD.
/// 사번은 Supervisor 역할(AspNetUserRoles)을 가진 웹 사용자(SYS_UserProfile.EmployeeNo)만 등록할 수 있고,
/// POP 안돈은 이 표의 활성 행으로만 슈퍼바이저 배지 스캔을 받는다(AndonRepository.IsLineSupervisor).
/// 키(라인·사번)는 바꾸지 않는다 — 바꾸려면 삭제 후 재등록.
/// </summary>
public sealed class LineSupervisorRepository
{
    /// <summary>이 역할(AspNetRoles.Name)을 가진 웹 사용자만 슈퍼바이저 후보가 된다. 현장 작업자(MD_Worker)는 후보가 아니다.</summary>
    public const string SupervisorRole = "Supervisor";

    public sealed record Row(string LineId, string? LineName, string? LineNameEn, string EmployeeNo, string? EmployeeName,
        bool ActiveFlag, string CreatedBy, DateTime? CreatedTs, string? ModifiedBy, DateTime? ModifiedTs);

    /// <summary>슈퍼바이저 후보 — Supervisor 역할 웹 사용자. Active 는 계정이 명시적으로 막히지 않았다는 뜻(웹 로그인 전 UNVERIFIED 도 배지 안돈은 가능).</summary>
    public sealed record PersonRow(string EmployeeNo, string? Name, bool Active);

    private readonly AmesConnectionFactory _f;
    public LineSupervisorRepository(AmesConnectionFactory f) => _f = f ?? throw new ArgumentNullException(nameof(f));

    // 사번 → 이름 해석: 웹 사용자(SYS_UserProfile)만
    private const string PersonJoin = """
        OUTER APPLY (SELECT TOP 1 u.EmployeeName FROM dbo.SYS_UserProfile u WHERE u.EmployeeNo = s.EmployeeNo ORDER BY u.UserProfileID) up
        """;

    public List<Row> ListAll()
    {
        const string sql = $"""
            SELECT  s.LineID, l.LineName, l.LineNameEn, s.EmployeeNo, up.EmployeeName,
                    s.ActiveFlag, s.CreatedBy, s.CreatedTS, s.ModifiedBy, s.ModifiedTS
            FROM    dbo.MD_LineSupervisor s
            LEFT JOIN dbo.MD_Line l ON l.LineID = s.LineID
            {PersonJoin}
            ORDER BY s.LineID, s.EmployeeNo;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        using var r    = cmd.ExecuteReader();
        var list = new List<Row>();
        while (r.Read())
            list.Add(new Row((string)r["LineID"], r["LineName"] as string, r["LineNameEn"] as string, (string)r["EmployeeNo"],
                r["EmployeeName"] as string, (bool)r["ActiveFlag"],
                (string)r["CreatedBy"], r["CreatedTS"] as DateTime?, r["ModifiedBy"] as string, r["ModifiedTS"] as DateTime?));
        return list;
    }

    public List<PersonRow> ListPeople()
    {
        const string sql = """
            SELECT DISTINCT u.EmployeeNo, u.EmployeeName AS Name,
                   -- 웹 로그인 전(UNVERIFIED) 계정도 배지로 안돈을 받을 수 있으므로 명시적으로 막힌 상태만 제외
                   CASE WHEN UPPER(ISNULL(u.AccountStatus, '')) IN ('DISABLED', 'LOCKED', 'SUSPENDED', 'INACTIVE') THEN 0 ELSE 1 END AS Active
            FROM   dbo.SYS_UserProfile u
            JOIN   dbo.AspNetUserRoles ur ON ur.UserId = u.UserID
            JOIN   dbo.AspNetRoles     r  ON r.Id = ur.RoleId AND r.Name = @Role
            WHERE  u.EmployeeNo IS NOT NULL AND u.EmployeeNo <> ''
            ORDER BY u.EmployeeNo;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Role", SqlDbType.NVarChar, 256).Value = SupervisorRole;
        using var r    = cmd.ExecuteReader();
        var list = new List<PersonRow>();
        while (r.Read())
            list.Add(new PersonRow((string)r["EmployeeNo"], r["Name"] as string, Convert.ToInt32(r["Active"]) == 1));
        return list;
    }

    public bool Exists(string lineId, string employeeNo)
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand("SELECT 1 FROM dbo.MD_LineSupervisor WHERE LineID = @L AND EmployeeNo = @E", conn);
        cmd.Parameters.Add("@L", SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@E", SqlDbType.VarChar, 20).Value = employeeNo;
        return cmd.ExecuteScalar() is not null;
    }

    public void Insert(string lineId, string employeeNo, bool activeFlag, string actor)
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand("""
            INSERT INTO dbo.MD_LineSupervisor (LineID, EmployeeNo, ActiveFlag, CreatedBy, CreatedTS)
            VALUES (@L, @E, @A, @By, SYSDATETIME());
            """, conn);
        cmd.Parameters.Add("@L",  SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@E",  SqlDbType.VarChar, 20).Value = employeeNo;
        cmd.Parameters.Add("@A",  SqlDbType.Bit).Value         = activeFlag;
        cmd.Parameters.Add("@By", SqlDbType.VarChar, 50).Value = actor.Length > 50 ? actor[..50] : actor;
        cmd.ExecuteNonQuery();
    }

    /// <summary>키(라인·사번)는 고정 — 활성 여부만 바꾼다.</summary>
    public void SetActive(string lineId, string employeeNo, bool activeFlag, string actor)
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand("""
            UPDATE dbo.MD_LineSupervisor
            SET    ActiveFlag = @A, ModifiedBy = @By, ModifiedTS = SYSDATETIME()
            WHERE  LineID = @L AND EmployeeNo = @E;
            """, conn);
        cmd.Parameters.Add("@L",  SqlDbType.VarChar,  20).Value = lineId;
        cmd.Parameters.Add("@E",  SqlDbType.VarChar,  20).Value = employeeNo;
        cmd.Parameters.Add("@A",  SqlDbType.Bit).Value          = activeFlag;
        cmd.Parameters.Add("@By", SqlDbType.NVarChar, 450).Value = actor;
        cmd.ExecuteNonQuery();
    }

    public void Delete(string lineId, string employeeNo)
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand("DELETE FROM dbo.MD_LineSupervisor WHERE LineID = @L AND EmployeeNo = @E", conn);
        cmd.Parameters.Add("@L", SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@E", SqlDbType.VarChar, 20).Value = employeeNo;
        cmd.ExecuteNonQuery();
    }
}
