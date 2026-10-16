namespace AMES.Data.Scheduling;

/// <summary>
/// 생산 마감일 역산용 근무일 달력. SYS_FactoryCalendar 는 날짜당 행이 여러 개(교대별)일 수 있어
/// 그 날짜 행 중 하나라도 WORKDAY 또는 SPECIAL(특근일) 면 근무일로 본다. 행이 없는 날은 토·일만 휴일 —
/// 달력이 안 채워진 미래 구간에서도 동작해야 한다 (SYS-03 화면의 표시 규칙과 같다).
/// </summary>
public sealed class WorkdayCalendar
{
    readonly Dictionary<DateTime, bool> _byDate = new();

    public static readonly WorkdayCalendar Empty = new(Array.Empty<(DateTime, string?)>());

    public WorkdayCalendar(IEnumerable<(DateTime Date, string? DayType)> rows)
    {
        foreach (var (date, type) in rows)
        {
            var d = date.Date;
            bool work = string.Equals(type, "WORKDAY", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(type, "SPECIAL", StringComparison.OrdinalIgnoreCase);
            _byDate[d] = _byDate.TryGetValue(d, out var prev) ? prev || work : work;
        }
    }

    public bool IsWorkday(DateTime date)
    {
        var d = date.Date;
        if (_byDate.TryGetValue(d, out var work)) return work;
        return d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
    }

    /// <summary><paramref name="from"/> 은 세지 않고 근무일 <paramref name="n"/> 개를 거슬러 간 날.</summary>
    public DateTime SubtractWorkdays(DateTime from, int n)
    {
        var d = from.Date;
        while (n > 0)
        {
            d = d.AddDays(-1);
            if (IsWorkday(d)) n--;
        }
        return d;
    }
}
