using AMES.Data.Scheduling;
using Xunit;

namespace AMES.InjAgent.Tests;

/// <summary>
/// 생산 마감일 역산용 근무일 달력. SYS_FactoryCalendar 행이 있으면 그 날짜 행 중 하나라도 WORKDAY·SPECIAL(특근일) 이면 근무일,
/// 행이 없는 날은 토·일만 휴일 (SYS-03 달력 화면과 같은 규칙). 순수 함수, DB 없음.
/// </summary>
public class WorkdayCalendarTests
{
    static readonly DateTime Mon = new(2026, 9, 7);   // 2026-09-07 월요일
    static readonly DateTime Sat = new(2026, 9, 5);
    static readonly DateTime Sun = new(2026, 9, 6);

    [Fact]
    public void Without_rows_only_weekend_is_off()
    {
        var cal = WorkdayCalendar.Empty;

        Assert.True(cal.IsWorkday(Mon));
        Assert.False(cal.IsWorkday(Sat));
        Assert.False(cal.IsWorkday(Sun));
    }

    [Fact]
    public void Holiday_row_turns_a_weekday_off()
    {
        var cal = new WorkdayCalendar(new[] { (Mon, (string?)"HOLIDAY") });

        Assert.False(cal.IsWorkday(Mon));
    }

    [Fact]
    public void Any_workday_row_on_the_date_wins_over_holiday_row()
    {
        var cal = new WorkdayCalendar(new[] { (Mon, (string?)"HOLIDAY"), (Mon, (string?)"workday") });

        Assert.True(cal.IsWorkday(Mon));
    }

    [Fact]
    public void Workday_row_opens_a_saturday()
    {
        var cal = new WorkdayCalendar(new[] { (Sat, (string?)"WORKDAY") });

        Assert.True(cal.IsWorkday(Sat));
    }

    [Fact]
    public void Special_workday_row_opens_a_saturday()
    {
        var cal = new WorkdayCalendar(new[] { (Sat, (string?)"SPECIAL") });   // SYS-03 의 특근일

        Assert.True(cal.IsWorkday(Sat));
    }

    [Fact]
    public void Subtract_three_workdays_from_monday_is_previous_wednesday()
    {
        Assert.Equal(new DateTime(2026, 9, 2), WorkdayCalendar.Empty.SubtractWorkdays(Mon, 3));
    }

    [Fact]
    public void Subtract_skips_holidays_in_between()
    {
        var cal = new WorkdayCalendar(new[] { (new DateTime(2026, 9, 3), (string?)"HOLIDAY") });   // 목요일 휴일

        Assert.Equal(new DateTime(2026, 9, 1), cal.SubtractWorkdays(Mon, 3));
    }

    [Fact]
    public void Subtract_zero_returns_the_same_date()
    {
        Assert.Equal(Mon, WorkdayCalendar.Empty.SubtractWorkdays(Mon.AddHours(15), 0));
    }
}
