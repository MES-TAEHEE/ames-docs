namespace AMES.Contracts.Dto;

/// <summary>
/// INJ-MAIN · IMG-MAIN 좌측 패널 한 행 — 스테이션 BOP 품번의 당일 현황.
/// 기준일은 LOT 생성일. InputQty = FinalQty + NgQty + PendingQty 가 항상 성립한다 (전부 LOT 상태로 센다).
/// </summary>
public sealed class InjItemDailyDto
{
    public required string  ItemNo     { get; init; }
    public required string  ItemName   { get; init; }
    /// <summary>오늘 PP_LineSchedule 에 배치된 WO 의 PlannedQty 합.</summary>
    public required decimal PlanQty    { get; init; }
    /// <summary>오늘 생성된 원천 LOT 전량 (확정 여부 무관).</summary>
    public required int     InputQty   { get; init; }
    /// <summary>오늘 생성 LOT 중 DEFECT + SCRAPPED (+ INJ 는 NG_BLOCKED).</summary>
    public required int     NgQty      { get; init; }
    /// <summary>오늘 생성 LOT 중 CONFIRMED (수리 양품 포함).</summary>
    public required int     FinalQty   { get; init; }
    /// <summary>오늘 생성 LOT 중 아직 RAW.</summary>
    public required int     PendingQty { get; init; }
    /// <summary>false = BOP 에 없는데 오늘 실적/일정이 있어 붙인 행.</summary>
    public required bool    InBop      { get; init; }
    /// <summary>이 라인에 Released/In Progress 단계가 있는 WO 가 하나라도 있는가.</summary>
    public required bool    HasOpenWo  { get; init; }
}
