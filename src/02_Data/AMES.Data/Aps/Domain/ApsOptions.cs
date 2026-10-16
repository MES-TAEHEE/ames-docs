namespace AMES.Data.Aps.Domain;

/// ORIG(SegPlanner) 규칙 보충 플래그 — 전부 false 가 REBUILD 골든 경로. AMES 화면 기본값은 공통코드 APS_SETTING 의 PULL_FORWARD·SAFETY_TERM·WARN_STATUS (Phase C).
/// PullForwardSupply: Autofill 이 라인 능력 초과분을 깎는 대신 앞 근무일로 당긴다 (ORIG SupplyPlanner.cs:163-185).
/// SafetyStockTerm : 목표재고 = max(cover × base, SafetyStock) (ORIG SupplyPlanner.cs:134).
/// WarnStatus      : 셀 상태에 warn(0 ≤ 재고 < 안전재고) 추가 (ORIG PlanCalculator.cs:19-24).
/// SequentialShiftFill: 교대 목록 모드의 사출 블록을 가동 시간 비율(ShiftSplit.Proportional)이 아니라 앞 교대부터 차례로 채운다(ShiftSplit.Sequential — APS_SETTING.SHIFT_FILL_SEQ, 2026-10-08 사용자 결정).
///                   골든 경로(교대 목록 없음)는 원래 주간 먼저 규칙이라 이 플래그와 무관하다.
public sealed record ApsOptions(bool PullForwardSupply = false, bool SafetyStockTerm = false, bool WarnStatus = false, bool SequentialShiftFill = false)
{
    public static readonly ApsOptions Default = new();
}
