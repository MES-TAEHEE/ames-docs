namespace AMES.Contracts.Dto;

public sealed class WorkOrderDto
{
    public required int      WoId          { get; init; }
    public required string   WoNumber      { get; init; }
    public required string   ItemNo        { get; init; }
    public required string   ItemName      { get; init; }
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

    // ── 공정 단계 (PP_WorkOrderRouting) ──────────────────────────────
    // 필드 의미 규칙:
    //  · 라인 범위 조회(ListForLine, GetActiveForTerminal): LineId·Status·CompletedQty·TerminalLock 는 그 라인 단계 값이고
    //    RoutingLineId·StepSeq·ProcessCode 가 채워진다. Pop 은 이걸로 단계 진행률을 그대로 표시한다.
    //  · 헤더 조회(ListAll, GetById): LineId 는 레거시 헤더 PP_WorkOrder.LineID 값 그대로다 —
    //    신규 발행 WO 는 헤더에 쓰지 않으므로 대개 빈 문자열이지만, 마이그레이션 이전 WO 는 값이 남아 있다. 신뢰하지 말 것.
    //    Status·CompletedQty 는 헤더 값,
    //    RoutingLineId·StepSeq·ProcessCode 는 null, RouteLines 에 단계 라인 나열.
    /// <summary>PP_WorkOrderRouting.RoutingLineID — 라인 범위 조회에서만.</summary>
    public int?              RoutingLineId { get; init; }
    public int?              StepSeq       { get; init; }
    public string?           ProcessCode   { get; init; }
    /// <summary>"LINE-INJ-01 → LINE-IMG-01". 라인 없는 단계는 "QC(—)". 단계 행 없으면 null.</summary>
    public string?           RouteLines    { get; init; }

    /// <summary>0–100 % of CompletedQty / OrderQty.</summary>
    public double ProgressPct =>
        OrderQty == 0 ? 0 : Math.Min(100, (double)(CompletedQty * 100 / OrderQty));

    /// <summary>Days until DueDate (negative = overdue). Null when no due date.</summary>
    public int? DaysToDue =>
        DueDate is null ? null
            : (int)Math.Ceiling((DueDate.Value.Date - DateTime.Today).TotalDays);
}
