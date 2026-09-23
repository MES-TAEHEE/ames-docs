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
        const string sql = """
            SELECT TOP 1
                u.Id, u.UserName, u.PasswordHash, p.PinHash,
                p.EmployeeNo, p.EmployeeName, p.Department, p.DefaultShift,
                p.AssignedLines, p.AccountStatus, p.FailedLoginCount
            FROM   dbo.SYS_UserProfile p
            JOIN   dbo.AspNetUsers     u ON u.Id = p.UserID
            WHERE  p.EmployeeNo = @EmployeeNo;
            """;

        using var conn = _connFactory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
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
            AssignedLinesJson = rdr["AssignedLines"] as string,
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
        const string sql = """
            SELECT u.Id, u.UserName, u.PasswordHash, p.PinHash,
                   p.EmployeeNo, p.EmployeeName, p.Department, p.DefaultShift,
                   p.AssignedLines, p.AccountStatus, ISNULL(p.FailedLoginCount,0) AS FailedLoginCount
            FROM   dbo.SYS_UserProfile p
            JOIN   dbo.AspNetUsers     u ON u.Id = p.UserID
            WHERE  ISNULL(p.AccountStatus, 'Active') = 'Active'
            ORDER  BY p.EmployeeNo;
            """;

        using var conn = _connFactory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
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
                AssignedLinesJson = rdr["AssignedLines"] as string,
                AccountStatus     = rdr["AccountStatus"] as string,
                FailedLoginCount  = Convert.ToInt32(rdr["FailedLoginCount"]),
            });
        }
        return list;
    }

    /// <summary>
    /// Bumps SYS_UserProfile.FailedLoginCount by 1.
    /// When the count reaches 5 the AccountStatus is set to 'LOCKED' automatically.
    /// Returns true if the account is now LOCKED (either just locked or was already LOCKED).
    /// </summary>
    public bool IncrementFailedCount(string userId)
    {
        const string sql = """
            UPDATE dbo.SYS_UserProfile
            SET    FailedLoginCount = ISNULL(FailedLoginCount, 0) + 1,
                   AccountStatus    = CASE
                                          WHEN ISNULL(FailedLoginCount, 0) + 1 >= 5
                                          THEN 'LOCKED'
                                          ELSE ISNULL(AccountStatus, 'Active')
                                      END,
                   -- 행위자 컬럼은 varchar(20) — GUID 대신 본인 사번
                   ModifiedBy       = ISNULL(NULLIF(LTRIM(RTRIM(EmployeeNo)), ''), LEFT(@UserID, 20)),
                   ModifiedTS       = SYSDATETIME()
            WHERE  UserID = @UserID;
            SELECT ISNULL(AccountStatus, 'Active') FROM dbo.SYS_UserProfile WHERE UserID = @UserID;
            """;

        using var conn = _connFactory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@UserID", SqlDbType.NVarChar, 450).Value = userId;
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
            SELECT ISNULL(AccountStatus, 'Active'), ISNULL(FailedLoginCount, 0)
            FROM   dbo.SYS_UserProfile
            WHERE  UserID = @UserID;
            """;

        using var conn = _connFactory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@UserID", SqlDbType.NVarChar, 450).Value = userId;
        using var rdr = cmd.ExecuteReader();
        if (!rdr.Read()) return ("Active", 0);
        return ((string)rdr[0], (int)rdr[1]);
    }

    /// <summary>
    /// Resets AccountStatus to 'Active' and clears FailedLoginCount.
    /// Called by SYS-001 admin unlock action.
    /// </summary>
    public void UnlockAccount(string userId, string modifiedBy)
    {
        const string sql = """
            UPDATE dbo.SYS_UserProfile
            SET    AccountStatus    = 'Active',
                   FailedLoginCount = 0,
                   ModifiedBy       = @ModifiedBy,
                   ModifiedTS       = SYSDATETIME()
            WHERE  UserID = @UserID;
            """;

        using var conn = _connFactory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@UserID",     SqlDbType.NVarChar,  450).Value = userId;
        cmd.Parameters.Add("@ModifiedBy", SqlDbType.VarChar,    50).Value = modifiedBy;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Clears the failure counter and stamps LastLoginTS after a successful login.
    /// </summary>
    public void RecordSuccessfulLogin(string userId)
    {
        const string sql = """
            UPDATE dbo.SYS_UserProfile
            SET    FailedLoginCount = 0,
                   LastLoginTS      = SYSDATETIME(),
                   -- 행위자 컬럼은 varchar(20)(09-23 migrate_audit_actor_varchar20) — GUID 대신 본인 사번, 없으면 앞 20자
                   ModifiedBy       = ISNULL(NULLIF(LTRIM(RTRIM(EmployeeNo)), ''), LEFT(@UserID, 20)),
                   ModifiedTS       = SYSDATETIME()
            WHERE  UserID = @UserID;
            """;

        using var conn = _connFactory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@UserID", SqlDbType.NVarChar, 450).Value = userId;
        cmd.ExecuteNonQuery();
    }
}
