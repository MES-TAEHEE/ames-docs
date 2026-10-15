namespace AMES.Data.Services;

/// <summary>
/// 보전 담당자(Technician) 후보 규칙(10-16 사용자 결정) — 부서(SYS_UserProfile.Department, 공통코드 DEPARTMENT)가 MAINTENANCE 인 사용자만.
/// MNT-007 배정·완료·수동 발행, MNT-005·010 PM 등록·수정·완료의 담당자 콤보가 같은 규칙을 쓴다. 테스트: MntTechniciansTests.
/// 이미 저장된 담당자는 부서가 바뀌었어도 수정 창에서 빈칸이 되지 않게 화면이 따로 남긴다.
/// </summary>
public static class MntTechnicians
{
    public const string Department = "MAINTENANCE";

    public static bool IsCandidate(string? department) =>
        string.Equals(department?.Trim(), Department, StringComparison.OrdinalIgnoreCase);
}
