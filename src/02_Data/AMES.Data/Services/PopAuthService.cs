using System.Text.Json;
using AMES.Contracts.Auth;
using AMES.Contracts.Dto;
using AMES.Contracts.Enums;
using AMES.Data.Repositories;
using AMES.Data.Security;

namespace AMES.Data.Services;

/// <summary>
/// Orchestrates a single POP login attempt: profile lookup → account-status check
/// → PIN verify (PBKDF2) → line-authorization check → session creation + audit log.
///
/// Every path also writes a row to PR_PopAuthLog so a supervisor can audit who
/// tried to log in where and why a terminal denied them.
/// </summary>
public sealed class PopAuthService
{
    private readonly AuthRepository       _auth;
    private readonly PopSessionRepository _sessions;

    public PopAuthService(AuthRepository auth, PopSessionRepository sessions)
    {
        _auth     = auth     ?? throw new ArgumentNullException(nameof(auth));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    }

    public LoginOutcome Login(LoginRequest req)
    {
        ArgumentNullException.ThrowIfNull(req);

        // 1) Find the operator. Both Badge and Pin paths key off EmployeeNo
        //    because the badge barcode encodes the EmployeeNo as well.
        var profile = _auth.FindByEmployeeNo(req.AttemptedId);
        if (profile is null)
        {
            _sessions.WriteAuthLog(req.TerminalId, req.AttemptedId, req.Method,
                AuthResult.BadCredentials, "unknown employee");
            return LoginOutcome.Failure(AuthResult.BadCredentials, "unknown employee");
        }

        // 2) AccountStatus gate.
        if (!IsActive(profile.AccountStatus))
        {
            _sessions.WriteAuthLog(req.TerminalId, req.AttemptedId, req.Method,
                AuthResult.InactiveAccount, $"status={profile.AccountStatus}");
            return LoginOutcome.Failure(AuthResult.InactiveAccount,
                $"account {profile.AccountStatus ?? "Unknown"}");
        }

        // 3) PIN verify (only on the PIN path; Badge currently trusts the scanner).
        if (req.Method == AuthMethod.Pin)
        {
            if (string.IsNullOrEmpty(req.Pin) || !PinHasher.Verify(req.Pin, profile.PasswordHash))
            {
                _auth.IncrementFailedCount(profile.UserId);
                _sessions.WriteAuthLog(req.TerminalId, req.AttemptedId, req.Method,
                    AuthResult.BadCredentials, "bad pin");
                return LoginOutcome.Failure(AuthResult.BadCredentials, "bad pin");
            }
        }

        // 4) Line authorization. AssignedLines is a JSON array of LineIDs;
        //    null/empty means "all lines" (dev convenience).
        if (!IsLineAuthorized(profile.AssignedLinesJson, req.LineId))
        {
            _sessions.WriteAuthLog(req.TerminalId, req.AttemptedId, req.Method,
                AuthResult.LineNotAuthorized, $"line {req.LineId}");
            return LoginOutcome.Failure(AuthResult.LineNotAuthorized,
                $"not authorized for {req.LineId}");
        }

        // 5) All checks passed — create session + log + reset failure counter.
        var session = _sessions.CreateSession(profile, req.TerminalId, req.LineId,
                                              req.ShiftCode, req.Method);
        _sessions.WriteAuthLog(req.TerminalId, req.AttemptedId, req.Method,
                               AuthResult.Ok, null);
        _auth.RecordSuccessfulLogin(profile.UserId);

        return LoginOutcome.Success(session);
    }

    private static bool IsActive(string? status) =>
        string.IsNullOrEmpty(status) ||
        string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase);

    private static bool IsLineAuthorized(string? assignedLinesJson, string lineId)
    {
        if (string.IsNullOrWhiteSpace(assignedLinesJson)) return true;
        try
        {
            var lines = JsonSerializer.Deserialize<string[]>(assignedLinesJson);
            if (lines is null || lines.Length == 0) return true;
            return lines.Any(l => string.Equals(l, lineId, StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException)
        {
            // Malformed JSON — fail open in dev rather than locking everyone out.
            return true;
        }
    }
}
