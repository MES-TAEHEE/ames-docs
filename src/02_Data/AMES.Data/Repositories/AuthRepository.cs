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
                   ModifiedBy       = @UserID,
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
                   ModifiedBy       = @UserID,
                   ModifiedTS       = SYSDATETIME()
            WHERE  UserID = @UserID;
            """;

        using var conn = _connFactory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@UserID", SqlDbType.NVarChar, 450).Value = userId;
        cmd.ExecuteNonQuery();
    }
}
