namespace AMES.Contracts.Enums;

/// <summary>
/// Outcome of a POP login attempt.
/// Persisted as the Result column on PR_PopAuthLog.
/// </summary>
public enum AuthResult
{
    /// <summary>Login succeeded; PR_PopSession created.</summary>
    Ok,
    /// <summary>Employee number / badge unknown, or PIN mismatch.</summary>
    BadCredentials,
    /// <summary>SYS_UserProfile.AccountStatus is not 'Active'.</summary>
    InactiveAccount,
    /// <summary>Operator is not authorized for the line bound to this terminal.</summary>
    LineNotAuthorized,
    /// <summary>Account is currently locked (AspNetUsers.LockoutEnd in future).</summary>
    Locked,
}
