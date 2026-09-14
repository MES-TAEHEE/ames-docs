namespace AMES.Data.Services;

/// <summary>
/// 계획 배치용 품번 → 금형 선택. 후보 = 품번의 활성 MD_MoldItem 금형 전부.
///   ① 직전 금형과 같은 후보(교체 없음) → ② 그 라인에 MD_MoldLine 배정된 후보 중 교체 시간 최소(동률 MoldId 순)
///   → ③ 전체 후보 중 MoldId 순 첫 번째. 후보 0개 = null 이며 INJ 단계에서는 계획 거부 사유다.
/// PP-003 서버(PpRepository)와 미리보기(PlanConfirmBatchDialog)가 같은 함수를 쓴다. 단위 테스트: MoldResolverTests.
/// </summary>
public static class MoldResolver
{
    public const string MoldProcessCode = "INJ";

    public static bool NeedsMold(string? processCode) =>
        string.Equals(processCode, MoldProcessCode, StringComparison.OrdinalIgnoreCase);

    /// <param name="ChangeMin">그 라인에서 이 금형으로 바꿀 때 걸리는 분 — COALESCE(MD_MoldLine.PrepTime, MD_Mold.MoldChangeMin, 0) 올림.</param>
    public sealed record MoldCandidate(string MoldId, bool AssignedToLine, int ChangeMin);

    public static MoldCandidate? Choose(IReadOnlyList<MoldCandidate> candidates, string? prevMoldId)
    {
        if (candidates.Count == 0) return null;
        if (prevMoldId is not null &&
            candidates.FirstOrDefault(c => string.Equals(c.MoldId, prevMoldId, StringComparison.OrdinalIgnoreCase)) is { } same)
            return same;
        var assigned = candidates.Where(c => c.AssignedToLine)
                                 .OrderBy(c => c.ChangeMin).ThenBy(c => c.MoldId, StringComparer.OrdinalIgnoreCase)
                                 .FirstOrDefault();
        return assigned ?? candidates.OrderBy(c => c.MoldId, StringComparer.OrdinalIgnoreCase).First();
    }
}
