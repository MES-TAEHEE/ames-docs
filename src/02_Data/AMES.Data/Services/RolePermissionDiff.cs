namespace AMES.Data.Services;

/// <summary>
/// SYS-004 저장 대상 — 역할을 고를 때 읽은 부여(기준)와 지금 화면의 부여가 다른 화면만(10-14 사용자 결정).
/// 바뀌지 않은 화면은 저장에 보내지 않으므로, 조회가 실패했거나 그사이 다른 사람이 고친 화면을 덮어쓰지 않는다.
/// 부여 문자열은 R/E/A 글자만 비교한다(순서·'_'·'X'·빈 값과 없음은 같은 것). 테스트: RolePermissionDiffTests.
/// </summary>
public static class RolePermissionDiff
{
    public static IReadOnlyList<string> Changed(IReadOnlyDictionary<string, string> baseline, IReadOnlyDictionary<string, string> current)
    {
        var keys = new SortedSet<string>(baseline.Keys, StringComparer.OrdinalIgnoreCase);
        keys.UnionWith(current.Keys);
        return keys.Where(k => PermissionSlots.Granted(baseline.GetValueOrDefault(k)) != PermissionSlots.Granted(current.GetValueOrDefault(k)))
                   .ToList();
    }
}
