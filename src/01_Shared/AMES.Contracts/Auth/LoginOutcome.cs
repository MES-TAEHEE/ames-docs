using AMES.Contracts.Dto;
using AMES.Contracts.Enums;

namespace AMES.Contracts.Auth;

/// <summary>
/// Result of PopAuthService.Login().
/// On Result == Ok the Session is non-null; otherwise Session is null and
/// FailReason carries a short tag (also stored on PR_PopAuthLog.FailReason).
/// </summary>
public sealed class LoginOutcome
{
    public required AuthResult Result { get; init; }
    public PopSessionDto? Session { get; init; }
    public string? FailReason { get; init; }

    public bool IsSuccess => Result == AuthResult.Ok && Session is not null;

    public static LoginOutcome Success(PopSessionDto session) =>
        new() { Result = AuthResult.Ok, Session = session };

    public static LoginOutcome Failure(AuthResult result, string reason) =>
        new() { Result = result, FailReason = reason };
}
