namespace AMES.Data.Services;

/// <summary>
/// 고장 「라인 정지」 → 라인 비가동(PP_LineDowntimeLog) 연결 규칙(10-16 사용자 결정). 정본 테스트 FailureDowntimeRulesTests.
///   · 심각도 CRITICAL 은 항상 라인 정지, 그 밖은 등록·수정 창에서 사용자가 정한다.
///   · 연결은 고장 → 비가동(MNT_FailureRegister.DowntimeID). 작업지시는 고장을 거쳐 따라간다.
///   · 같은 라인에 열린 비가동이 이미 있으면(안돈·PP-DTL·다른 고장) 새로 만들지 않고 그 행에 연결한다 — OEE 이중 계상 방지.
///   · 열린 행은 없지만 발생 시각 이후로 끝난 비가동과 겹치면 만들지 않는다(PP-DTL 겹침 금지와 같은 규칙).
///   · 고장이 만든 행(PP_LineDowntimeLog.FailureID)만 그 고장이 해결·종료될 때 닫고 삭제될 때 지운다.
///     그 행에 연결된 다른 열린 고장이 있으면 닫지 않고 소유를 그 고장으로 넘긴다.
/// </summary>
public static class FailureDowntimeRules
{
    public const string ReasonCode = "MACHINE";

    public enum Issue { None, NoLine, StartInFuture, Overlap }

    public enum Plan { Create, Link, Skip }

    public sealed record Existing(int DowntimeId, DateTime Start, DateTime? End);

    public sealed record Decision(Plan Plan, int? DowntimeId, Issue Issue);

    public static bool Forced(string? severity) =>
        string.Equals(severity?.Trim(), "CRITICAL", StringComparison.OrdinalIgnoreCase);

    public static bool Wanted(bool requested, string? severity) => requested || Forced(severity);

    /// <param name="sameLine">같은 라인에서 발생 시각 이후에 끝나는(또는 열린) 비가동</param>
    public static Decision Decide(DateTime start, DateTime now, IEnumerable<Existing> sameLine)
    {
        if (start > now) return new(Plan.Skip, null, Issue.StartInFuture);
        var rows = sameLine.Where(r => (r.End ?? DateTime.MaxValue) > start).ToList();
        if (rows.Where(r => r.End is null).OrderBy(r => r.Start).ThenBy(r => r.DowntimeId).FirstOrDefault() is { } open)
            return new(Plan.Link, open.DowntimeId, Issue.None);
        return rows.Count > 0 ? new(Plan.Skip, null, Issue.Overlap) : new(Plan.Create, null, Issue.None);
    }

    /// <summary>비가동을 닫는 시각 — 시작 전으로 거슬러 가지 않고 지금보다 뒤로 가지 않는다(PP-DTL 미래 시각 금지와 같은 기준).</summary>
    public static DateTime EndAt(DateTime start, DateTime at, DateTime now)
    {
        var end = at > now ? now : at;
        return end < start ? start : end;
    }
}
