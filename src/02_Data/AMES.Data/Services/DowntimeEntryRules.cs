namespace AMES.Data.Services;

/// <summary>
/// PP-DTL 비가동 이력 등록·수정 규칙. POP 이 남긴 행(안돈 연동, AndonID 있음)은 시각·사유·원인이
/// 안돈 기록과 묶여 있어 웹에서는 비고만 고친다.
/// </summary>
public static class DowntimeEntryRules
{
    public enum Issue { None, EndBeforeStart, StartInFuture, EndInFuture }

    public static bool IsPopRow(int? andonId) => andonId is not null;

    /// <summary>종료 없음 = 진행 중. 미래 시각은 실적이 아니라 계획이라 받지 않는다(<paramref name="now"/> = DB 시각).</summary>
    public static Issue Validate(DateTime start, DateTime? end, DateTime now)
    {
        if (start > now) return Issue.StartInFuture;
        if (end is { } e)
        {
            if (e <= start) return Issue.EndBeforeStart;
            if (e > now)    return Issue.EndInFuture;
        }
        return Issue.None;
    }

    /// <summary>같은 라인의 두 구간이 겹치는가. 종료 없음은 끝이 열린 구간이다.</summary>
    public static bool Overlaps(DateTime aStart, DateTime? aEnd, DateTime bStart, DateTime? bEnd)
        => aStart < (bEnd ?? DateTime.MaxValue) && bStart < (aEnd ?? DateTime.MaxValue);
}
