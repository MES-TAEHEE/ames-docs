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
                u.Id, u.UserName, u.PasswordHash,
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
    /// Bumps SYS_UserProfile.FailedLoginCount by 1.
    /// AspNetUsers.AccessFailedCount stays untouched (managed by Identity in the web app).
    /// </summary>
    public void IncrementFailedCount(string userId)
    {
        const string sql = """
            UPDATE dbo.SYS_UserProfile
            SET    FailedLoginCount = ISNULL(FailedLoginCount, 0) + 1,
                   ModifiedTS       = SYSDATETIME()
            WHERE  UserID = @UserID;
            """;

        using var conn = _connFactory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@UserID", SqlDbType.NVarChar, 450).Value = userId;
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
                   ModifiedTS       = SYSDATETIME()
            WHERE  UserID = @UserID;
            """;

        using var conn = _connFactory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@UserID", SqlDbType.NVarChar, 450).Value = userId;
        cmd.ExecuteNonQuery();
    }
}
