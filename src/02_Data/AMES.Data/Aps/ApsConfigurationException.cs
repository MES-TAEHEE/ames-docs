namespace AMES.Data.Aps;

/// <summary>
/// APS 를 돌릴 수 없는 설정 상태(2026-10-06 사용자 결정: 사출 라인의 가동 시간 패턴이 미지정이거나 지정 패턴이 없어졌거나 ACTIVE 가 아니면 조회·WO 생성을 막는다).
/// Lines 는 라인마다 한 줄("LINE-INJ-01: 가동 시간 패턴이 지정되지 않았습니다" 등) — 화면은 이 목록을 그대로 보이고 설정 다이얼로그로 안내한다.
/// </summary>
public sealed class ApsConfigurationException : InvalidOperationException
{
    public IReadOnlyList<string> Lines { get; }

    public ApsConfigurationException(IReadOnlyList<string> lines)
        : base("APS 설정이 완료되지 않아 계획을 만들 수 없습니다 — " + string.Join(" / ", lines))
    {
        Lines = lines;
    }
}
