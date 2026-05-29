namespace AMES.Contracts.Dto;

/// <summary>
/// Latest known state of a piece of equipment.
/// </summary>
public sealed class EquipmentDto
{
    public required string  EquipId     { get; init; }
    public required string  EquipName   { get; init; }
    public string?          LineId      { get; init; }
    public string?          Status      { get; init; }   // RUN / IDLE / STOP / ALARM / OFFLINE
    public DateTime?        StatusSince { get; init; }   // last status change

    public TimeSpan? Uptime =>
        StatusSince is null ? null : DateTime.Now - StatusSince.Value;
}
