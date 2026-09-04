namespace AMES.Contracts.Dto;

/// <summary>
/// IMG-MAIN 좌측 패널 한 행 — 스테이션 BOP 품번의 당일 현황.
/// INJ 판(<see cref="InjItemDailyDto"/>)과 달리 원천 LOT 이 없으므로 INPUT/미확정 열이 없다.
/// 양품은 PR_ProductionResult.EntryAt, 불량은 PR_DefectDetail.DetectedAt 기준 당일.
/// </summary>
public sealed class ItemDailyDto
{
    public required string  ItemNo    { get; init; }
    public required string  ItemName  { get; init; }
    /// <summary>오늘 PP_LineSchedule 에 배치된 WO 의 PlannedQty 합.</summary>
    public required decimal PlanQty   { get; init; }
    /// <summary>오늘 이 라인에 기록된 GoodQty 합.</summary>
    public required int     GoodQty   { get; init; }
    /// <summary>오늘 이 공정으로 등록된 불량 수량 합 (라인에 단계가 있는 WO 만).</summary>
    public required int     NgQty     { get; init; }
    /// <summary>false = BOP 에 없는데 오늘 실적/일정이 있어 붙인 행.</summary>
    public required bool    InBop     { get; init; }
    /// <summary>이 라인에 Released/In Progress 단계가 있는 WO 가 하나라도 있는가.</summary>
    public required bool    HasOpenWo { get; init; }
}
