namespace AMES.Data.Services;

/// <summary>
/// 고장 대응 이력 — MNT-002 상세 타임라인과 MNT-009 고장 알림의 대응 단계.
/// 원천은 고장 등록(발생 시각·신고자) + MNT_FailureAction(POP 안돈 보전 도착 ARRIVED·ACK, 웹 수리 완료 REPAIRED).
/// 안돈 종료는 조치 이력 행 없이 등록 행을 RESOLVED·ResolvedAt 으로만 닫으므로, REPAIRED 가 없는 해결은 등록 행의 해결 시각으로 보인다.
/// </summary>
public static class FailureTimeline
{
    public enum Kind { Reported, Arrived, Ack, Repaired, Resolved, Other }

    public enum Stage { Waiting, Arrived, Acked, Resolved }

    public sealed record Action(string? Type, DateTime? At, string? Who, string? Note);

    /// <summary><paramref name="Minutes"/> = 발생부터 그 시점까지 분(발생 시각을 모르면 null). <paramref name="RawType"/> = 모르는 조치 유형의 원래 코드.</summary>
    public sealed record Step(Kind Kind, DateTime At, string? Who, string? Note, int? Minutes, string? RawType = null);

    public static bool IsResolved(string? status) => status is "RESOLVED" or "CLOSED";

    public static List<Step> Build(DateTime? reportedAt, string? reportedBy, string? status, DateTime? resolvedAt,
        IEnumerable<Action> actions)
    {
        var steps = new List<Step>();
        if (reportedAt is { } rep) steps.Add(new(Kind.Reported, rep, reportedBy, null, 0));

        var acts = actions.Where(a => a.At is not null)
            .Select(a => (Kind: KindOf(a.Type), A: a))
            .OrderBy(x => x.A.At).ThenBy(x => x.Kind)
            .Select(x => new Step(x.Kind, x.A.At!.Value, x.A.Who,
                x.Kind is Kind.Repaired or Kind.Other ? x.A.Note : null,
                Minutes(reportedAt, x.A.At!.Value),
                x.Kind == Kind.Other ? x.A.Type : null))
            .ToList();
        steps.AddRange(acts);

        if (IsResolved(status) && resolvedAt is { } done && acts.All(s => s.Kind != Kind.Repaired))
            steps.Add(new(Kind.Resolved, done, null, null, Minutes(reportedAt, done)));
        return steps;
    }

    /// <summary>해결 여부는 등록 행 상태가 정본이다 — 조치 이력은 해결 전까지의 진행 단계만 정한다.</summary>
    public static Stage CurrentStage(string? status, IEnumerable<string?> actionTypes)
    {
        if (IsResolved(status)) return Stage.Resolved;
        var kinds = actionTypes.Select(KindOf).ToHashSet();
        return kinds.Contains(Kind.Ack) ? Stage.Acked : kinds.Contains(Kind.Arrived) ? Stage.Arrived : Stage.Waiting;
    }

    /// <summary>해결까지 걸린 분 — 해결 전이면 발생부터 <paramref name="now"/> 까지(경과).</summary>
    public static int? ElapsedMinutes(DateTime? reportedAt, string? status, DateTime? resolvedAt, DateTime now)
    {
        if (reportedAt is not { } rep) return null;
        var end = IsResolved(status) ? resolvedAt : now;
        return end is { } e ? Minutes(rep, e) : null;
    }

    static Kind KindOf(string? type) => type?.Trim().ToUpperInvariant() switch
    {
        "ARRIVED"  => Kind.Arrived,
        "ACK"      => Kind.Ack,
        "REPAIRED" => Kind.Repaired,
        _          => Kind.Other,
    };

    static int? Minutes(DateTime? from, DateTime to) =>
        from is { } f ? Math.Max(0, (int)Math.Floor((to - f).TotalMinutes)) : null;
}
