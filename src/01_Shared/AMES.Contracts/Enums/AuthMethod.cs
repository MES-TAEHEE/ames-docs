namespace AMES.Contracts.Enums;

/// <summary>
/// How the operator authenticated at a POP terminal.
/// Persisted to PR_PopSession.AuthMethod and PR_PopAuthLog.AuthMethod.
/// </summary>
public enum AuthMethod
{
    /// <summary>USB HID badge barcode scan (Code-128). Primary path.</summary>
    Badge,
    /// <summary>4-digit PIN typed on the on-screen keypad. Backup path.</summary>
    Pin,
}
