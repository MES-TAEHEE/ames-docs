using AMES.Contracts.Enums;

namespace AMES.Contracts.Auth;

/// <summary>
/// Inputs to PopAuthService.Login(). One operator attempt from one terminal.
/// </summary>
public sealed class LoginRequest
{
    /// <summary>Employee number entered (PIN path) or scanned (Badge path).</summary>
    public required string AttemptedId { get; init; }

    /// <summary>PIN typed on the keypad. Ignored for AuthMethod.Badge.</summary>
    public string? Pin { get; init; }

    public required AuthMethod Method { get; init; }

    /// <summary>From AppConfig.StationId — the physical terminal making the call.</summary>
    public required string TerminalId { get; init; }

    /// <summary>From AppConfig.LineId — the line the terminal is bound to.</summary>
    public required string LineId { get; init; }

    /// <summary>From AppConfig.DefaultShift until shift-roster lookup is wired up.</summary>
    public required string ShiftCode { get; init; }
}
