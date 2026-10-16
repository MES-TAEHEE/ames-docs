namespace AMES.Data.Services;

/// <summary>
/// 생산 WO(PP_WorkOrder.Status) 의 "끝난" 상태 정본 — 생산 완료·입고·마감·취소.
/// MRP 소요·APS 미배치 WO 는 이 상태를 열린 WO 에서 빼고, PP-CAL 납기 변경은 이 상태면 거부한다(10-14 사용자 결정).
/// SQL 은 <see cref="FinishedSqlList"/> 를 NOT IN 에 넣는다. 테스트: WoStatusRulesTests.
/// </summary>
public static class WoStatusRules
{
    public const string FinishedSqlList = "'Completed','Stocked','Closed','Cancelled'";

    public static readonly IReadOnlyList<string> Finished = ["Completed", "Stocked", "Closed", "Cancelled"];

    public static bool IsFinished(string? status)
        => status is not null && Finished.Contains(status.Trim(), StringComparer.OrdinalIgnoreCase);
}
