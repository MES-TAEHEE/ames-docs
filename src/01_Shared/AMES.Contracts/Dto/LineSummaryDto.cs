namespace AMES.Contracts.Dto;

/// <summary>
/// One row of the INJ-07 Production Status grid + the per-line Dashboard tiles.
/// Aggregates the active WO + today's good/defect totals + mold status.
/// </summary>
public sealed class LineSummaryDto
{
    public required string LineId       { get; init; }
    public required string LineName     { get; init; }
    public string?         EquipStatus  { get; init; }     // RUN / STOP / etc.
    public WorkOrderDto?   ActiveWo     { get; init; }
    public int             TodayGood    { get; init; }
    public int             TodayDefect  { get; init; }
    public MoldDto?        MountedMold  { get; init; }
    public string?         OperatorName { get; init; }

    public double DefectRatePct =>
        (TodayGood + TodayDefect) == 0
            ? 0
            : (double)TodayDefect * 100 / (TodayGood + TodayDefect);
}
