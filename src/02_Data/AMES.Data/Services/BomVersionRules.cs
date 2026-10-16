namespace AMES.Data.Services;

/// <summary>
/// MD-004 BOM 버전 승인 규칙(정본). 한 품번의 승인 버전은 유효기간이 겹치지 않는다.
/// 새 버전을 승인하면 기간이 겹치는 기존 승인 버전의 종료일을 "새 시작일 − 1일" 로 닫는다(구 승인 버전은 화면에서 고칠 수 없으므로).
/// 닫을 수 없으면(새 버전 시작일 없음 · 기존 버전 시작일이 새 시작일과 같거나 늦음) 승인을 거부한다. 기간 NULL 은 무한대.
/// </summary>
public static class BomVersionRules
{
    public sealed record Range(string VersionId, DateOnly? From, DateOnly? To);

    public sealed record VersionInfo(string VersionId, string? RootItemNo, string? Status, DateOnly? From, DateOnly? To, DateTime? CreatedTs);

    /// <summary>
    /// 품번의 대표 BOM 버전(하위 전개·BOP 상하위 조회 공용): 오늘 유효한 승인 → 승인 중 최신 시작일 → 그 밖의 최신 등록. 없으면 null.
    /// </summary>
    public static VersionInfo? Representative(IEnumerable<VersionInfo> versions, string itemNo, DateOnly today)
    {
        var mine = versions.Where(v => string.Equals(v.RootItemNo, itemNo, StringComparison.OrdinalIgnoreCase)).ToList();
        var approved = mine.Where(v => v.Status == "APPROVED").OrderByDescending(v => v.From).ThenBy(v => v.VersionId, StringComparer.Ordinal).ToList();
        return approved.FirstOrDefault(v => (v.From is null || v.From <= today) && (v.To is null || v.To >= today))
            ?? approved.FirstOrDefault()
            ?? mine.OrderByDescending(v => v.CreatedTs).ThenBy(v => v.VersionId, StringComparer.Ordinal).FirstOrDefault();
    }

    /// <param name="Conflict">승인을 막는 기존 승인 버전 ID (없으면 null)</param>
    /// <param name="Close">종료일을 닫을 기존 승인 버전과 새 종료일</param>
    public sealed record ApprovalPlan(string? Conflict, IReadOnlyList<(string VersionId, DateOnly NewTo)> Close);

    /// <summary>
    /// 새 버전의 기본 시작일 — 같은 품번의 직전 버전(반려 제외, 시작일 최신)에 종료일이 있으면 그 다음 날로 이어 붙이고
    /// (기간은 양 끝 포함이라 같은 날이면 하루 겹친다), 종료일이 없거나 직전 버전이 없으면 오늘. 무기한 직전 버전은 승인 때 닫힌다(<see cref="PlanApproval"/>).
    /// </summary>
    public static DateOnly DefaultStart(IEnumerable<VersionInfo> versions, string itemNo, DateOnly today)
    {
        var prev = versions
            .Where(v => string.Equals(v.RootItemNo, itemNo, StringComparison.OrdinalIgnoreCase) && v.Status != "REJECTED")
            .OrderByDescending(v => v.From ?? DateOnly.MinValue).ThenByDescending(v => v.CreatedTs)
            .FirstOrDefault();
        return prev?.To is { } end ? end.AddDays(1) : today;
    }

    static readonly System.Text.RegularExpressions.Regex VersionNoPattern =
        new(@"^[Vv]?\s*(\d{1,3})(?:\.(\d{1,3}))?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// 버전 번호 형식 강제 — "V{주}.{부}" 로만 저장한다. V 는 생략 가능(자동으로 붙임), 부 번호가 없으면 0:
    /// "2" → V2.0, "2.1" → V2.1, "v02.10" → V2.10. 그 밖의 형식(빈 값·문자·점 두 개·음수 등)은 false.
    /// </summary>
    public static bool TryNormalizeVersionNo(string? input, out string normalized)
    {
        normalized = "";
        var m = VersionNoPattern.Match((input ?? "").Trim());
        if (!m.Success) return false;
        var major = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var minor = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
        normalized = $"V{major}.{minor}";
        return true;
    }

    public static bool Overlaps(DateOnly? aFrom, DateOnly? aTo, DateOnly? bFrom, DateOnly? bTo)
        => (aFrom ?? DateOnly.MinValue) <= (bTo ?? DateOnly.MaxValue)
        && (bFrom ?? DateOnly.MinValue) <= (aTo ?? DateOnly.MaxValue);

    public static ApprovalPlan PlanApproval(Range approving, IEnumerable<Range> approvedOthers)
    {
        var close = new List<(string, DateOnly)>();
        foreach (var o in approvedOthers.OrderBy(o => o.VersionId, StringComparer.Ordinal))
        {
            if (!Overlaps(approving.From, approving.To, o.From, o.To)) continue;
            if (approving.From is not { } start || (o.From is { } of && of >= start))
                return new ApprovalPlan(o.VersionId, []);
            close.Add((o.VersionId, start.AddDays(-1)));
        }
        return new ApprovalPlan(null, close);
    }
}
