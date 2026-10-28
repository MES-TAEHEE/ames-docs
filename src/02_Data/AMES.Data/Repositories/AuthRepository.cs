using System.Data;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// Read/update access to AspNetUsers + SYS_UserProfile.
/// Identity is stored exactly as ASP.NET Identity expects so the same user
/// row works for the future Blazor portal — only the auxiliary
/// SYS_UserProfile counters / status are touched here.
/// </summary>
public sealed class AuthRepository
{
    private readonly AmesConnectionFactory _connFactory;

    public AuthRepository(AmesConnectionFactory connFactory)
    {
        _connFactory = connFactory ?? throw new ArgumentNullException(nameof(connFactory));
    }

    /// <summary>
    /// Lookup by SYS_UserProfile.EmployeeNo (badge number / login id typed by operator).
    /// Returns null when no profile or no matching AspNetUsers row.
    /// </summary>
    public EmployeeProfileDto? FindByEmployeeNo(string employeeNo)
    {
        const string sql = "dbo.SP_POP_AUTH_FindByEmployeeNo";

        using var conn = _connFactory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@EmployeeNo", SqlDbType.VarChar, 20).Value = employeeNo;

        using var rdr = cmd.ExecuteReader();
        if (!rdr.Read()) return null;

        return new EmployeeProfileDto
        {
            UserId            = (string)rdr["Id"],
            UserName          = (string)rdr["UserName"],
            PasswordHash      = rdr["PasswordHash"] as string ?? string.Empty,
            PinHash           = rdr["PinHash"] as string,
            EmployeeNo        = (string)rdr["EmployeeNo"],
            EmployeeName      = (string)rdr["EmployeeName"],
            Department        = rdr["Department"]   as string,
            DefaultShift      = rdr["DefaultShift"] as string,
            AccountStatus     = rdr["AccountStatus"] as string,
            FailedLoginCount  = rdr["FailedLoginCount"] as int? ?? 0,
        };
    }

    /// <summary>
    /// 웹 로그인 계정(AspNetUsers.UserName)의 PIN 해시. 프로필이 없거나 PIN 미설정이면 null.
    /// 웹 화면이 민감한 변경을 본인 PIN 으로 다시 확인할 때 쓴다(PP-CAL 납기 변경).
    /// </summary>
    public string? GetPinHashByUserName(string userName)
    {
        const string sql = """
            SELECT TOP 1 p.PinHash
            FROM   dbo.AspNetUsers     u
            JOIN   dbo.SYS_UserProfile p ON p.UserID = u.Id
            WHERE  u.UserName = @UserName;
            """;
        using var conn = _connFactory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@UserName", SqlDbType.NVarChar, 256).Value = userName;
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>
    /// All active profiles ordered by EmployeeNo. Used by the dev User Picker
    /// popup on the login screen so testers can switch identities without
    /// scanning a barcode. The PasswordHash / PinHash are loaded too because the picker
    /// returns the same DTO shape as FindByEmployeeNo for consistency, but
    /// callers should treat the hashes as opaque — verify still flows through
    /// PopAuthService.
    /// </summary>
    public List<EmployeeProfileDto> ListAllProfiles()
    {
        const string sql = "dbo.SP_POP_AUTH_ListAllProfiles";

        using var conn = _connFactory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        using var rdr  = cmd.ExecuteReader();
        var list = new List<EmployeeProfileDto>();
        while (rdr.Read())
        {
            list.Add(new EmployeeProfileDto
            {
                UserId            = (string)rdr["Id"],
                UserName          = (string)rdr["UserName"],
                PasswordHash      = rdr["PasswordHash"] as string ?? string.Empty,
                PinHash           = rdr["PinHash"] as string,
                EmployeeNo        = (string)rdr["EmployeeNo"],
                EmployeeName      = (string)rdr["EmployeeName"],
                Department        = rdr["Department"]   as string,
                DefaultShift      = rdr["DefaultShift"] as string,
                AccountStatus     = rdr["AccountStatus"] as string,
                FailedLoginCount  = Convert.ToInt32(rdr["FailedLoginCount"]),
            });
        }
        return list;
    }

    /// <summary>POP와 같은 웹 계정 우선순위로 로그인 선택용 사번·이름만 조회한다.</summary>
    public List<LoginEmployeeDto> ListLoginEmployees()
    {
        const string sql = """
            SELECT p.EmployeeNo COLLATE DATABASE_DEFAULT AS EmployeeNo,
                   p.EmployeeName COLLATE DATABASE_DEFAULT AS EmployeeName
            FROM dbo.SYS_UserProfile p JOIN dbo.AspNetUsers u ON u.Id=p.UserID
            WHERE COALESCE(p.AccountStatus,'Active') IN ('Active','')
              AND NULLIF(LTRIM(RTRIM(p.EmployeeNo)),'') IS NOT NULL
            UNION
            SELECT w.EmployeeNo COLLATE DATABASE_DEFAULT, w.EmployeeName COLLATE DATABASE_DEFAULT
            FROM dbo.MD_Worker w
            WHERE w.ActiveFlag=1 AND NULLIF(LTRIM(RTRIM(w.EmployeeNo)),'') IS NOT NULL
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.SYS_UserProfile p JOIN dbo.AspNetUsers u ON u.Id=p.UserID
                  WHERE p.EmployeeNo COLLATE DATABASE_DEFAULT=w.EmployeeNo COLLATE DATABASE_DEFAULT)
            ORDER BY EmployeeNo;
            """;
        using var conn = _connFactory.OpenConnection();
        using var cmd = new SqlCommand(sql, conn);
        using var reader = cmd.ExecuteReader();
        var list = new List<LoginEmployeeDto>();
        while (reader.Read()) list.Add(new(reader.GetString(0), reader.GetString(1)));
        return list;
    }

    /// <summary>
    /// 시스템 관리자 역할(ROLE-SYSADMIN)을 가진 사용자 ID — POP 오프라인 로그인 캐시가 세션의 IsAdmin 을 미리 정해 두는 데 쓴다.
    /// </summary>
    public HashSet<string> ListAdminUserIds()
    {
        const string sql = "SELECT UserId FROM dbo.AspNetUserRoles WHERE RoleId = @RoleId;";
        using var conn = _connFactory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@RoleId", SqlDbType.NVarChar, 450).Value = AMES.Data.Services.SystemRoles.AdminId;
        using var rdr = cmd.ExecuteReader();
        var set = new HashSet<string>(StringComparer.Ordinal);
        while (rdr.Read()) set.Add(rdr.GetString(0));
        return set;
    }

    /// <summary>
    /// Bumps SYS_UserProfile.FailedLoginCount by 1.
    /// When the count reaches 5 the AccountStatus is set to 'LOCKED' automatically.
    /// Returns true if the account is now LOCKED (either just locked or was already LOCKED).
    /// </summary>
    /// <param name="modifiedBy">POP 은 시도한 사번을 넘긴다. 생략하면 프로필 사번, 없으면 userId 앞 20자.</param>
    public bool IncrementFailedCount(string userId, string? modifiedBy = null)
    {
        const string sql = "dbo.SP_POP_AUTH_IncrementFailedCount";

        using var conn = _connFactory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@UserID", SqlDbType.NVarChar, 450).Value = userId;
        cmd.Parameters.Add("@ModifiedBy", SqlDbType.VarChar,   20).Value = (object?)modifiedBy ?? DBNull.Value;
        var result = cmd.ExecuteScalar();
        return result is string s && s == "LOCKED";
    }

    /// <summary>
    /// Returns the current AccountStatus and FailedLoginCount for a user.
    /// Returns ("Active", 0) when no SYS_UserProfile row exists.
    /// </summary>
    /// <summary>
    /// 행위자 코드 → 표시 이름 사전. 키는 사번(SYS_UserProfile·MD_Worker), 사용자 ID(GUID)·사용자명(구 데이터), 그리고
    /// migrate_audit_actor_varchar20 이 만든 별칭(SYS_AuditActorMap.ActorCode → 원문). 값은 사원 이름(별칭은 원문의 @ 앞부분).
    /// </summary>
    public Dictionary<string, string> ListActorNames()
    {
        const string sql = """
            SELECT Code, Name FROM (
                SELECT p.EmployeeNo AS Code, p.EmployeeName AS Name FROM dbo.SYS_UserProfile p WHERE NULLIF(p.EmployeeNo,'') IS NOT NULL
                UNION ALL SELECT p.UserID, p.EmployeeName FROM dbo.SYS_UserProfile p
                UNION ALL SELECT u.UserName, p.EmployeeName FROM dbo.SYS_UserProfile p JOIN dbo.AspNetUsers u ON u.Id = p.UserID
                -- 사번 클레임이 없던 시절·로그인 전 화면이 남긴 "이메일 @ 앞부분" 코드도 이름으로
                UNION ALL SELECT LEFT(u.UserName, CHARINDEX('@', u.UserName) - 1), p.EmployeeName
                          FROM dbo.SYS_UserProfile p JOIN dbo.AspNetUsers u ON u.Id = p.UserID WHERE CHARINDEX('@', u.UserName) > 1
                UNION ALL SELECT w.EmployeeNo, w.EmployeeName FROM dbo.MD_Worker w WHERE NULLIF(w.EmployeeNo,'') IS NOT NULL
                UNION ALL SELECT m.ActorCode, COALESCE(p.EmployeeName, LEFT(m.OriginalValue, CASE WHEN CHARINDEX('@', m.OriginalValue) > 1 THEN CHARINDEX('@', m.OriginalValue) - 1 ELSE 60 END))
                          FROM dbo.SYS_AuditActorMap m
                          LEFT JOIN dbo.AspNetUsers u ON u.Id = m.OriginalValue OR u.NormalizedUserName = UPPER(m.OriginalValue)
                          LEFT JOIN dbo.SYS_UserProfile p ON p.UserID = u.Id
            ) x WHERE Code IS NOT NULL AND Name IS NOT NULL
            """;
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var conn = _connFactory.OpenConnection();
        using var cmd = new SqlCommand(sql, conn);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var code = r.GetString(0).Trim(); var name = r.GetString(1).Trim();
            if (code.Length > 0 && name.Length > 0 && !map.ContainsKey(code)) map[code] = name;
        }
        return map;
    }

    /// <summary>사용자 ID(AspNetUsers.Id) → 사번. 담당자·신고자처럼 사용자 ID 를 저장하는 컬럼을 "이름 (사번)" 으로 보이려고 쓴다.</summary>
    public Dictionary<string, string> ListUserEmployeeNos()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var conn = _connFactory.OpenConnection();
        using var cmd = new SqlCommand("SELECT UserID, EmployeeNo FROM dbo.SYS_UserProfile WHERE NULLIF(EmployeeNo,'') IS NOT NULL", conn);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var id = r.GetString(0).Trim();
            if (id.Length > 0 && !map.ContainsKey(id)) map[id] = r.GetString(1).Trim();
        }
        return map;
    }

    /// <summary>행위자 코드용 사번(SYS_UserProfile.EmployeeNo). 프로필이 없으면 null.</summary>
    public string? GetEmployeeNo(string userId)
    {
        using var conn = _connFactory.OpenConnection();
        using var cmd = new SqlCommand("SELECT TOP (1) EmployeeNo FROM dbo.SYS_UserProfile WHERE UserID = @UserID", conn);
        cmd.Parameters.Add("@UserID", SqlDbType.NVarChar, 450).Value = userId;
        var v = cmd.ExecuteScalar();
        return v is string s && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;
    }

    public (string AccountStatus, int FailedLoginCount) GetProfileStatus(string userId)
    {
        const string sql = """
            SELECT ISNULL(AccountStatus, 'ACTIVE'), ISNULL(FailedLoginCount, 0)
            FROM   dbo.SYS_UserProfile
            WHERE  UserID = @UserID;
            """;

        using var conn = _connFactory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@UserID", SqlDbType.NVarChar, 450).Value = userId;
        using var rdr = cmd.ExecuteReader();
        // 프로필이 없으면 로그인할 수 없다 — 계정은 SYS-001 에서 프로필과 함께 만든다(10-07)
        if (!rdr.Read()) return ("INACTIVE", 0);
        return ((string)rdr[0], (int)rdr[1]);
    }

    /// <summary>
    /// Resets AccountStatus to 'ACTIVE' and clears FailedLoginCount.
    /// Called by SYS-001 admin unlock action.
    /// </summary>
    public void UnlockAccount(string userId, string modifiedBy)
    {
        const string sql = """
            UPDATE dbo.SYS_UserProfile
            SET    AccountStatus    = 'ACTIVE',
                   FailedLoginCount = 0,
                   ModifiedBy       = @ModifiedBy,
                   ModifiedTS       = SYSDATETIME()
            WHERE  UserID = @UserID;
            """;

        using var conn = _connFactory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@UserID",     SqlDbType.NVarChar,  450).Value = userId;
        cmd.Parameters.Add("@ModifiedBy", SqlDbType.VarChar,    20).Value = modifiedBy;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Stamps LastLoginTS after a successful login and, for web logins, clears the failure counter.
    /// </summary>
    /// <param name="modifiedBy">POP 은 로그인한 사번을 넘긴다. 생략하면 프로필 사번, 없으면 userId 앞 20자.</param>
    /// <param name="resetFailures">POP·PDA 는 false — 배지·PIN 성공이 웹 비밀번호 실패 횟수를 지우면 잠금이 무력화된다.</param>
    public void RecordSuccessfulLogin(string userId, string? modifiedBy = null, bool resetFailures = true)
    {
        const string sql = "dbo.SP_POP_AUTH_RecordSuccessfulLogin";

        using var conn = _connFactory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@UserID", SqlDbType.NVarChar, 450).Value = userId;
        cmd.Parameters.Add("@ModifiedBy", SqlDbType.VarChar,   20).Value = (object?)modifiedBy ?? DBNull.Value;
        cmd.Parameters.Add("@ResetFailures", SqlDbType.Bit).Value = resetFailures;
        cmd.ExecuteNonQuery();
    }
}
