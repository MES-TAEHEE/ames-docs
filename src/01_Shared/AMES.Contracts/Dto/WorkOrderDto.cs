namespace AMES.Contracts.Dto;

/// <summary>
/// Snapshot of a PP_WorkOrder row enriched with item name + mold metadata
/// for display on INJ-03 WO Confirm and INJ-02/04 progress cards.
/// </summary>
public sealed class WorkOrderDto
{
    public required int     WoId         { get; init; }
    public required string  WoNumber     { get; init; }
    public required string  ItemNo       { get; init; }
    public required string  ItemName     { get; init; }
    public string?          ItemNameEn   { get; init; }
    public required decimal OrderQty     { get; init; }
    public required decimal OpenQty      { get; init; }
    public required decimal CompletedQty { get; init; }
    public required string  LineId       { get; init; }
    public string?          MoldId       { get; init; }
    public string?          RecipeId     { get; init; }
    public DateTime?        DueDate      { get; init; }
    public required string  Status       { get; init; }
    public string?          TerminalLock { get; init; }
    public int              Priority     { get; init; }

    /// <summary>0..100 percentage of CompletedQty / OrderQty.</summary>
    public double ProgressPct =>
        OrderQty == 0 ? 0 : (double)(CompletedQty * 100 / OrderQty);

    /// <summary>Days until DueDate (negative = overdue). Null when no due date.</summary>
    public int? DaysToDue =>
        DueDate is null ? null
            : (int)Math.Ceiling((DueDate.Value.Date - DateTime.Today).TotalDays);
}
