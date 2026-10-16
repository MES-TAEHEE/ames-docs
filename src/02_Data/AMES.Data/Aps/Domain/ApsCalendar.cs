namespace AMES.Data.Aps.Domain;

/// 근무일 판정 — 골든은 SundayOffCalendar(일요일만 휴무), AMES 는 WorkdayCalendarAdapter(Phase C).
public interface IApsCalendar
{
    bool IsWorkday(DateOnly d);
}

/// 원본 SEG 규칙: 일요일만 뺀다 (dates 에 토요일은 있고 일요일은 없다).
public sealed class SundayOffCalendar : IApsCalendar
{
    public static readonly SundayOffCalendar Instance = new();
    public bool IsWorkday(DateOnly d) => d.DayOfWeek != DayOfWeek.Sunday;
}

/// 구 Calendar 의 static 헬퍼 — 달력 규칙이 필요한 메서드는 IApsCalendar 를 첫 인자로 받는다.
public static class ApsCalendar
{
    public static DateOnly Parse(string iso) => DateOnly.ParseExact(iso, "yyyy-MM-dd");
    public static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd");

    /// 달력일 −1. 골든 bundle.prevDate 규약이라 달력을 보지 않는다 (D-1 표시는 PrevWorkDate 를 쓴다).
    public static string PrevDate(string iso) => Iso(Parse(iso).AddDays(-1));

    /// 기준일부터 휴무일을 빼고 days 개.
    public static List<string> WorkDates(IApsCalendar cal, string baseDate, int days)
    {
        var list = new List<string>();
        var d = Parse(baseDate);
        while (list.Count < days)
        {
            if (cal.IsWorkday(d)) list.Add(Iso(d));
            d = d.AddDays(1);
        }
        return list;
    }

    /// 그 근무일에 합쳐지는 달력 날짜들: 자기 자신 + 다음 근무일 전까지의 휴무일. 기간 마지막 날 뒤는 합치지 않는다.
    public static List<string> FoldedDates(IApsCalendar cal, string date, IReadOnlyList<string> dates)
    {
        var i = dates.ToList().IndexOf(date);
        var result = new List<string> { date };
        if (i < 0 || i == dates.Count - 1) return result;
        var d = Parse(date).AddDays(1);
        var next = Parse(dates[i + 1]);
        while (d < next)
        {
            if (!cal.IsWorkday(d)) result.Add(Iso(d));
            d = d.AddDays(1);
        }
        return result;
    }

    /// date 이전 첫 근무일 (신규 — 휴무일 납기 접기·D-1 실적 표시용).
    public static string PrevWorkDate(IApsCalendar cal, string date)
    {
        var d = Parse(date).AddDays(-1);
        while (!cal.IsWorkday(d)) d = d.AddDays(-1);
        return Iso(d);
    }
}
