namespace AMES.Data.Aps.Domain;

/// ORIG(SegPlanner) 규칙 보충 플래그 — 전부 false 가 REBUILD 골든 경로. AMES 화면 기본값은 공통코드 APS_SETTING 의 PULL_FORWARD·SAFETY_TERM·WARN_STATUS (Phase C).
/// PullForwardSupply: Autofill 이 라인 능력 초과분을 깎는 대신 앞 근무일로 당긴다 (ORIG SupplyPlanner.cs:163-185).
/// SafetyStockTerm : 목표재고 = max(cover × base, SafetyStock) (ORIG SupplyPlanner.cs:134).
/// WarnStatus      : 셀 상태에 warn(0 ≤ 재고 < 안전재고) 추가 (ORIG PlanCalculator.cs:19-24).
public sealed record ApsOptions(bool PullForwardSupply = false, bool SafetyStockTerm = false, bool WarnStatus = false)
{
    public static readonly ApsOptions Default = new();
}
