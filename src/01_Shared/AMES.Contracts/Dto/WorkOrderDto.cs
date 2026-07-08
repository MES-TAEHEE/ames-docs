namespace AMES.Contracts.Dto;

public sealed class WorkOrderDto
{
    public required int      WoId          { get; init; }
    public required string   WoNumber      { get; init; }
    public required string   ItemNo        { get; init; }
    public required string   ItemName      { get; init; }
    public string?           ItemNameEn    { get; init; }
    public required decimal  OrderQty      { get; init; }
    public required decimal  OpenQty       { get; init; }
    public required decimal  CompletedQty  { get; init; }
    public required string   LineId        { get; init; }
    public string?           MoldId        { get; init; }
    public string?           RecipeId      { get; init; }
    public DateTime?         DueDate       { get; init; }
    public required string   Status        { get; init; }
    public string?           TerminalLock  { get; init; }
    public int               Priority      { get; init; }

    // PP-004 additions
    /// <summary>"A" = Wrapping (IMG line), "B" = Painting (PNT line), null = INJ.</summary>
    public string?           RoutingType   { get; init; }
    /// <summary>Item has both BOM and BOP entries registered (Phase-0 gate BR-PP-001).</summary>
    public bool              Phase0Complete { get; init; }
    /// <summary>SAP B1 supply order reference — null until SAP integration live.</summary>
    public string?           SapRef        { get; init; }
    /// <summary>Source sales order number (via PP_CustomerOrder.SoID), when WO was created from a plan.</summary>
    public string?           SoNumber      { get; init; }

    /// <summary>0–100 % of CompletedQty / OrderQty.</summary>
    public double ProgressPct =>
        OrderQty == 0 ? 0 : Math.Min(100, (double)(CompletedQty * 100 / OrderQty));

    /// <summary>Days until DueDate (negative = overdue). Null when no due date.</summary>
    public int? DaysToDue =>
        DueDate is null ? null
            : (int)Math.Ceiling((DueDate.Value.Date - DateTime.Today).TotalDays);
}
