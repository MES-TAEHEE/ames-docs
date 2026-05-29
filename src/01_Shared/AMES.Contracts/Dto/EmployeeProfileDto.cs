namespace AMES.Contracts.Dto;

/// <summary>
/// Combined view of AspNetUsers + SYS_UserProfile.
/// Loaded by AuthRepository.FindByEmployeeNo / FindByBadge.
/// </summary>
public sealed class EmployeeProfileDto
{
    /// <summary>AspNetUsers.Id (NVARCHAR(450)) — used as OperatorID FK everywhere.</summary>
    public required string UserId { get; init; }

    /// <summary>AspNetUsers.UserName — login handle (often equals EmployeeNo).</summary>
    public required string UserName { get; init; }

    /// <summary>SYS_UserProfile.EmployeeNo — the value printed on the badge.</summary>
    public required string EmployeeNo { get; init; }

    /// <summary>Display name shown on POP top bar after login.</summary>
    public required string EmployeeName { get; init; }

    public string? Department { get; init; }
    public string? DefaultShift { get; init; }

    /// <summary>
    /// JSON array of LineIDs the operator may log into.
    /// Empty / null => not restricted (treated as 'all lines' for now).
    /// </summary>
    public string? AssignedLinesJson { get; init; }

    /// <summary>Active / Locked / Disabled.</summary>
    public string? AccountStatus { get; init; }

    /// <summary>SYS_UserProfile.FailedLoginCount (domain counter, separate from AspNet's AccessFailedCount).</summary>
    public int FailedLoginCount { get; init; }

    /// <summary>AspNetUsers.PasswordHash — checked by PinHasher.Verify(pin, hash).</summary>
    public required string PasswordHash { get; init; }
}
