using AMES.Contracts.Enums;

namespace AMES.Contracts.Dto;

/// <summary>
/// Open POP session held in-memory by the WinForms shell after successful login.
/// Mirrors a single row of PR_PopSession with the operator's display info attached.
/// </summary>
public sealed class PopSessionDto
{
    public required int SessionId { get; init; }
    public required string OperatorId { get; init; }        // AspNetUsers.Id
    public required string EmployeeNo { get; init; }
    public required string EmployeeName { get; init; }
    public required string TerminalId { get; init; }
    public required string LineId { get; init; }
    public required string ShiftCode { get; init; }
    public required AuthMethod AuthMethod { get; init; }
    public required DateTime StartedAt { get; init; }
    public required DateTime ExpiresAt { get; init; }
}
