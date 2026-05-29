using System.Data;
using AMES.Contracts.Dto;
using AMES.Contracts.Enums;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// Writes the two POP audit tables: PR_PopSession (open session header)
/// and PR_PopAuthLog (every attempt, success or failure).
/// </summary>
public sealed class PopSessionRepository
{
    /// <summary>How long a POP session is valid before forced re-login.</summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(12);

    private readonly AmesConnectionFactory _connFactory;

    public PopSessionRepository(AmesConnectionFactory connFactory)
    {
        _connFactory = connFactory ?? throw new ArgumentNullException(nameof(connFactory));
    }

    /// <summary>
    /// Inserts an open PR_PopSession row and returns it (with SessionID + ExpiresAt populated).
    /// </summary>
    public PopSessionDto CreateSession(
        EmployeeProfileDto profile,
        string terminalId,
        string lineId,
        string shiftCode,
        AuthMethod method)
    {
        var startedAt  = DateTime.Now;
        var expiresAt  = startedAt + DefaultLifetime;

        const string sql = """
            INSERT INTO dbo.PR_PopSession
                (OperatorID, TerminalID, LineID, ShiftCode, AuthMethod,
                 StartedAt, ExpiresAt, CreatedBy, CreatedTS)
            OUTPUT INSERTED.SessionID
            VALUES
                (@OperatorID, @TerminalID, @LineID, @ShiftCode, @AuthMethod,
                 @StartedAt, @ExpiresAt, @CreatedBy, SYSDATETIME());
            """;

        using var conn = _connFactory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@OperatorID", SqlDbType.NVarChar, 450).Value = profile.UserId;
        cmd.Parameters.Add("@TerminalID", SqlDbType.VarChar,   20 ).Value = terminalId;
        cmd.Parameters.Add("@LineID",     SqlDbType.VarChar,   20 ).Value = lineId;
        cmd.Parameters.Add("@ShiftCode",  SqlDbType.VarChar,   10 ).Value = shiftCode;
        cmd.Parameters.Add("@AuthMethod", SqlDbType.VarChar,   20 ).Value = method.ToString();
        cmd.Parameters.Add("@StartedAt",  SqlDbType.DateTime2     ).Value = startedAt;
        cmd.Parameters.Add("@ExpiresAt",  SqlDbType.DateTime2     ).Value = expiresAt;
        cmd.Parameters.Add("@CreatedBy",  SqlDbType.VarChar,   50 ).Value = profile.EmployeeNo;

        var sessionId = (int)cmd.ExecuteScalar()!;

        return new PopSessionDto
        {
            SessionId    = sessionId,
            OperatorId   = profile.UserId,
            EmployeeNo   = profile.EmployeeNo,
            EmployeeName = profile.EmployeeName,
            TerminalId   = terminalId,
            LineId       = lineId,
            ShiftCode    = shiftCode,
            AuthMethod   = method,
            StartedAt    = startedAt,
            ExpiresAt    = expiresAt,
        };
    }

    /// <summary>
    /// Marks the session as logged out. Called from Dashboard's Logout button
    /// and from idle / shift-end watchers (future).
    /// </summary>
    public void CloseSession(int sessionId, string reason)
    {
        const string sql = """
            UPDATE dbo.PR_PopSession
            SET    LoggedOutAt   = SYSDATETIME(),
                   LogoutReason  = @Reason,
                   ModifiedTS    = SYSDATETIME()
            WHERE  SessionID     = @SessionID
              AND  LoggedOutAt IS NULL;
            """;

        using var conn = _connFactory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@SessionID", SqlDbType.Int           ).Value = sessionId;
        cmd.Parameters.Add("@Reason",    SqlDbType.VarChar, 20   ).Value = reason;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Append-only audit row for every login attempt — including failures and lockouts.
    /// </summary>
    public void WriteAuthLog(
        string  terminalId,
        string  attemptedId,
        AuthMethod method,
        AuthResult  result,
        string? failReason)
    {
        const string sql = """
            INSERT INTO dbo.PR_PopAuthLog
                (TerminalID, AttemptedID, AuthMethod, Result, FailReason,
                 AttemptedAt, CreatedBy, CreatedTS)
            VALUES
                (@TerminalID, @AttemptedID, @AuthMethod, @Result, @FailReason,
                 SYSDATETIME(), @CreatedBy, SYSDATETIME());
            """;

        using var conn = _connFactory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@TerminalID",  SqlDbType.VarChar, 20).Value = terminalId;
        cmd.Parameters.Add("@AttemptedID", SqlDbType.VarChar, 50).Value = attemptedId;
        cmd.Parameters.Add("@AuthMethod",  SqlDbType.VarChar, 20).Value = method.ToString();
        cmd.Parameters.Add("@Result",      SqlDbType.VarChar, 10).Value = ShortResult(result);
        cmd.Parameters.Add("@FailReason",  SqlDbType.VarChar, 40).Value = (object?)failReason ?? DBNull.Value;
        cmd.Parameters.Add("@CreatedBy",   SqlDbType.VarChar, 50).Value = terminalId;
        cmd.ExecuteNonQuery();
    }

    private static string ShortResult(AuthResult r) => r switch
    {
        AuthResult.Ok                => "OK",
        AuthResult.BadCredentials    => "FAIL",
        AuthResult.InactiveAccount   => "FAIL",
        AuthResult.LineNotAuthorized => "FAIL",
        AuthResult.Locked            => "LOCK",
        _                            => "FAIL",
    };
}
