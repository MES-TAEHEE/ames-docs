using AMES.Data.Aps.Domain;
using Xunit;

namespace AMES.Data.Tests.Aps;

/// REBUILD CalendarTests 3건(SundayOffCalendar) + 주입 달력(토·일 휴무, 특근일) 3건.
public class ApsCalendarTests
{
    static readonly IApsCalendar Sun = SundayOffCalendar.Instance;

    /// 토·일 휴무 + 특근일 집합 — AMES WorkdayCalendar(행 없는 날 토·일 휴일, SPECIAL 근무) 와 같은 모양.
    sealed class WeekendOff(params string[] specialWorkdays) : IApsCalendar
    {
        readonly HashSet<DateOnly> _special = specialWorkdays.Select(ApsCalendar.Parse).ToHashSet();
        public bool IsWorkday(DateOnly d) => _special.Contains(d) || (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday);
    }

    // ── REBUILD CalendarTests ──────────────────────────────────────────

    [Fact]
    public void Five_days_from_thursday_skip_sunday()
    {
        Assert.Equal(new[] { "2026-09-24", "2026-09-25", "2026-09-26", "2026-09-28", "2026-09-29" }, ApsCalendar.WorkDates(Sun, "2026-09-24", 5));
        Assert.Equal("2026-09-23", ApsCalendar.PrevDate("2026-09-24"));
    }

    [Fact]
    public void Sunday_base_date_starts_on_monday()
    {
        Assert.Equal(new[] { "2026-09-28", "2026-09-29", "2026-09-30" }, ApsCalendar.WorkDates(Sun, "2026-09-27", 3));
    }

    [Fact]
    public void Saturday_absorbs_following_sunday()
    {
        var dates = ApsCalendar.WorkDates(Sun, "2026-09-24", 5);
        Assert.Equal(new[] { "2026-09-26", "2026-09-27" }, ApsCalendar.FoldedDates(Sun, "2026-09-26", dates));
        Assert.Equal(new[] { "2026-09-24" }, ApsCalendar.FoldedDates(Sun, "2026-09-24", dates));
        Assert.Equal(new[] { "2026-09-29" }, ApsCalendar.FoldedDates(Sun, "2026-09-29", dates));   // 기간 마지막 날 뒤는 합치지 않는다
    }

    // ── 주입 달력 ──────────────────────────────────────────────────────

    [Fact]
    public void WorkDates_follow_injected_calendar_and_special_workday()
    {
        Assert.Equal(new[] { "2026-09-24", "2026-09-25", "2026-09-28", "2026-09-29", "2026-09-30" }, ApsCalendar.WorkDates(new WeekendOff(), "2026-09-24", 5));
        Assert.Equal(new[] { "2026-09-24", "2026-09-25", "2026-09-26", "2026-09-28", "2026-09-29" }, ApsCalendar.WorkDates(new WeekendOff("2026-09-26"), "2026-09-24", 5));
        Assert.Equal(new[] { "2026-09-28", "2026-09-29" }, ApsCalendar.WorkDates(new WeekendOff(), "2026-09-26", 2));   // 토요일 기준일 → 월요일부터
    }

    [Fact]
    public void FoldedDates_fold_every_off_day_before_next_work_date()
    {
        var cal = new WeekendOff();
        var dates = ApsCalendar.WorkDates(cal, "2026-09-24", 5);   // 24 25 28 29 30
        Assert.Equal(new[] { "2026-09-25", "2026-09-26", "2026-09-27" }, ApsCalendar.FoldedDates(cal, "2026-09-25", dates));
        Assert.Equal(new[] { "2026-09-24" }, ApsCalendar.FoldedDates(cal, "2026-09-24", dates));
        Assert.Equal(new[] { "2026-09-30" }, ApsCalendar.FoldedDates(cal, "2026-09-30", dates));
        Assert.Equal(new[] { "2026-10-05" }, ApsCalendar.FoldedDates(cal, "2026-10-05", dates));   // dates 에 없는 날은 자기 자신만
    }

    [Fact]
    public void PrevWorkDate_skips_off_days_of_injected_calendar()
    {
        Assert.Equal("2026-09-25", ApsCalendar.PrevWorkDate(new WeekendOff(), "2026-09-28"));               // 월 → 금
        Assert.Equal("2026-09-26", ApsCalendar.PrevWorkDate(Sun, "2026-09-28"));                            // 일요일만 휴무 → 토
        Assert.Equal("2026-09-26", ApsCalendar.PrevWorkDate(new WeekendOff("2026-09-26"), "2026-09-28"));   // 특근 토요일
        Assert.Equal("2026-09-23", ApsCalendar.PrevWorkDate(new WeekendOff(), "2026-09-24"));               // 평일 → 전날
    }
}
