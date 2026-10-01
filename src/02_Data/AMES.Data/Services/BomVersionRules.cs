namespace AMES.Data.Services;

/// <summary>
/// MD-004 BOM 버전 승인 규칙(정본). 한 품번의 승인 버전은 유효기간이 겹치지 않는다.
/// 새 버전을 승인하면 기간이 겹치는 기존 승인 버전의 종료일을 "새 시작일 − 1일" 로 닫는다(구 승인 버전은 화면에서 고칠 수 없으므로).
/// 닫을 수 없으면(새 버전 시작일 없음 · 기존 버전 시작일이 새 시작일과 같거나 늦음) 승인을 거부한다. 기간 NULL 은 무한대.
/// </summary>
public static class BomVersionRules
{
    public sealed record Range(string VersionId, DateOnly? From, DateOnly? To);

    /// <param name="Conflict">승인을 막는 기존 승인 버전 ID (없으면 null)</param>
    /// <param name="Close">종료일을 닫을 기존 승인 버전과 새 종료일</param>
    public sealed record ApprovalPlan(string? Conflict, IReadOnlyList<(string VersionId, DateOnly NewTo)> Close);

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
