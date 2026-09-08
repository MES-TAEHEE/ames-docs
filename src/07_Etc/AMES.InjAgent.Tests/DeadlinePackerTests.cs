using AMES.Data.Repositories;
using AMES.Data.Scheduling;
using Xunit;
using static AMES.Data.Scheduling.DeadlinePacker;
using static AMES.Data.Scheduling.SlotPacker;

namespace AMES.InjAgent.Tests;

/// <summary>
/// PP-003 자동 배치 — 단계 목록 + 날짜별 능력 → 오늘부터 앞으로 채우되 마감일에서 멈추고, 넘치면 납기일까지 Late,
/// 그래도 남으면 Shortfall. 순수 함수, DB 없음 — 능력은 인메모리 DayStateCache 로 준다.
/// 표준 하루 = 08:00~12:00 + 13:00~18:00 = 540분, 사이클 60초 = 1분/EA → 540 EA/일.
/// </summary>
public class DeadlinePackerTests
{
    static Interval I(int s, int e) => new(s, e);
    static readonly Interval[] Std = { I(480, 720), I(780, 1080) };
    static readonly DateTime Mon = new(2026, 9, 7);   // 월
    static readonly DateTime Tue = Mon.AddDays(1);
    static readonly DateTime Wed = Mon.AddDays(2);
    static readonly DateTime Fri = Mon.AddDays(4);
    static readonly DateTime Sat = Mon.AddDays(-2);

    static LineScheduleRepository.DayCapacity Day(params Interval[] bands) =>
        new("PAT", 480, bands, Array.Empty<Interval>(), bands.Sum(b => b.EndMin - b.StartMin), 0, null);

    static DayStateCache Days(Func<string, DateTime, LineScheduleRepository.DayCapacity>? load = null) =>
        new(load ?? ((_, _) => Day(Std)));

    static StepDemand Step(int seq, string line, decimal qty, int? cycleSec = 60, int? dailyCap = null) =>
        new(seq, line, qty, cycleSec, dailyCap);

    static Result Pack(IReadOnlyList<StepDemand> steps, DateTime? deadline, DateTime? due,
                       DateTime? today = null, int nowMin = 0, WorkdayCalendar? cal = null, IDayState? days = null) =>
        DeadlinePacker.Pack(steps, today ?? Mon, nowMin, deadline, due, cal ?? WorkdayCalendar.Empty, days ?? Days());

    [Fact]
    public void Fits_today_within_deadline_without_late()
    {
        var r = Pack(new[] { Step(1, "A", 300) }, deadline: Wed, due: Fri);

        Assert.Equal(new[] { (Mon, 480, 720, 240m), (Mon, 780, 840, 60m) },
                     r.Placements.Select(p => (p.Date, p.StartMin, p.EndMin, p.Qty)).ToArray());
        Assert.All(r.Placements, p => Assert.False(p.Late));
        Assert.Empty(r.Shortfalls);
    }

    [Fact]
    public void Splits_across_days_when_one_day_is_not_enough()
    {
        var r = Pack(new[] { Step(1, "A", 1000) }, deadline: Wed, due: Fri);

        Assert.Equal(1000m, r.Placements.Sum(p => p.Qty));
        Assert.Equal(540m, r.Placements.Where(p => p.Date == Mon).Sum(p => p.Qty));
        Assert.Equal(460m, r.Placements.Where(p => p.Date == Tue).Sum(p => p.Qty));
        Assert.Empty(r.Shortfalls);
    }

    [Fact]
    public void Past_deadline_is_late_until_due_then_shortfall()
    {
        var r = Pack(new[] { Step(1, "A", 3000) }, deadline: Wed, due: Fri);   // 5일 × 540 = 2700

        Assert.Equal(2700m, r.Placements.Sum(p => p.Qty));
        Assert.All(r.Placements.Where(p => p.Date <= Wed), p => Assert.False(p.Late));
        Assert.All(r.Placements.Where(p => p.Date >  Wed), p => Assert.True(p.Late));
        Assert.Equal(1080m, r.Placements.Where(p => p.Late).Sum(p => p.Qty));
        Assert.Equal(new[] { new StepShortfall(1, "A", 300m) }, r.Shortfalls);
    }

    [Fact]
    public void Next_step_starts_after_previous_step_last_slot()
    {
        var r = Pack(new[] { Step(1, "A", 300), Step(2, "B", 300) }, deadline: Wed, due: Fri);

        var second = r.Placements.Where(p => p.StepSeq == 2).OrderBy(p => p.Date).ThenBy(p => p.StartMin).ToList();
        Assert.Equal((Mon, 840, 1080, 240m), (second[0].Date, second[0].StartMin, second[0].EndMin, second[0].Qty));
        Assert.Equal((Tue, 480, 540, 60m),  (second[1].Date, second[1].StartMin, second[1].EndMin, second[1].Qty));
    }

    [Fact]
    public void Downstream_target_shrinks_to_upstream_placed_qty()
    {
        // A 라인은 하루 100분뿐이고 납기가 오늘 → 1단계 100 EA 만 배치
        var days = Days((line, _) => line == "A" ? Day(I(480, 580)) : Day(Std));
        var r = Pack(new[] { Step(1, "A", 300), Step(2, "B", 300) }, deadline: Mon, due: Mon, days: days);

        Assert.Equal(100m, r.Placements.Where(p => p.StepSeq == 1).Sum(p => p.Qty));
        Assert.Equal(100m, r.Placements.Where(p => p.StepSeq == 2).Sum(p => p.Qty));
        Assert.Equal(new[] { new StepShortfall(1, "A", 200m), new StepShortfall(2, "B", 200m) }, r.Shortfalls);
    }

    [Fact]
    public void Skips_non_workdays()
    {
        var r = Pack(new[] { Step(1, "A", 300) }, deadline: null, due: Tue, today: Sat);

        Assert.All(r.Placements, p => Assert.Equal(Mon, p.Date));
        Assert.Empty(r.Shortfalls);
    }

    [Fact]
    public void Holiday_row_in_calendar_is_skipped_too()
    {
        var cal = new WorkdayCalendar(new[] { (Mon, (string?)"HOLIDAY") });
        var r = Pack(new[] { Step(1, "A", 300) }, deadline: null, due: Tue, cal: cal);

        Assert.All(r.Placements, p => Assert.Equal(Tue, p.Date));
    }

    [Fact]
    public void Today_starts_after_now()
    {
        var r = Pack(new[] { Step(1, "A", 60) }, deadline: Wed, due: Fri, nowMin: 600);

        Assert.Equal((Mon, 600, 660), (r.Placements[0].Date, r.Placements[0].StartMin, r.Placements[0].EndMin));
    }

    [Fact]
    public void Before_day_start_today_is_unconstrained()
    {
        // 01:00 에 계획 — 오늘 창(08:00~)은 아직 아무것도 지나지 않았다
        var r = Pack(new[] { Step(1, "A", 60) }, deadline: Wed, due: Fri, nowMin: 60);

        Assert.Equal((Mon, 480, 540), (r.Placements[0].Date, r.Placements[0].StartMin, r.Placements[0].EndMin));
    }

    [Fact]
    public void Night_line_planned_during_the_day_still_gets_tonight()
    {
        // 야간 라인(22:00 시작): 09:00 에 계획하면 오늘 밤 22:00 밴드부터
        var night = new LineScheduleRepository.DayCapacity("PAT", 1320, new[] { I(0, 360), I(1320, 1440) }, Array.Empty<Interval>(), 480, 0, null);
        var r = Pack(new[] { Step(1, "A", 200) }, deadline: Wed, due: Fri, nowMin: 540, days: Days((_, _) => night));

        Assert.Equal(new[] { (Mon, 1320, 1440, 120m), (Mon, 0, 80, 80m) },
                     r.Placements.Select(p => (p.Date, p.StartMin, p.EndMin, p.Qty)).ToArray());
    }

    [Fact]
    public void Appends_after_existing_wo_on_that_day()
    {
        var days = Days((_, _) => Day(Std) with { Occupied = new[] { I(480, 600) }, WoLoadMin = 120, LastWoEnd = 600 });
        var r = Pack(new[] { Step(1, "A", 60) }, deadline: Wed, due: Fri, days: days);

        Assert.Equal((Mon, 600, 660), (r.Placements[0].Date, r.Placements[0].StartMin, r.Placements[0].EndMin));
    }

    [Fact]
    public void Two_orders_sharing_a_day_state_queue_one_after_another()
    {
        var days = Days();
        var a = Pack(new[] { Step(1, "A", 300) }, deadline: Wed, due: Fri, days: days);
        var b = Pack(new[] { Step(1, "A", 300) }, deadline: Wed, due: Fri, days: days);

        Assert.Equal((Mon, 480), (a.Placements[0].Date, a.Placements[0].StartMin));
        Assert.Equal((Mon, 840), (b.Placements[0].Date, b.Placements[0].StartMin));
        Assert.Equal(300m, b.Placements.Sum(p => p.Qty));
    }

    [Fact]
    public void Without_cycle_and_daily_cap_step_is_one_60min_block_for_all_qty()
    {
        // 월요일은 30분밖에 안 남음 → 통째로 화요일로
        var days = Days((_, d) => d == Mon ? Day(I(480, 510)) : Day(Std));
        var r = Pack(new[] { Step(1, "A", 500, cycleSec: null) }, deadline: Wed, due: Fri, days: days);

        Assert.Single(r.Placements);
        Assert.Equal((Tue, 480, 540, 500m), (r.Placements[0].Date, r.Placements[0].StartMin, r.Placements[0].EndMin, r.Placements[0].Qty));
    }

    [Fact]
    public void Daily_cap_scales_minutes_by_that_days_operating_time()
    {
        var r = Pack(new[] { Step(1, "A", 270, cycleSec: null, dailyCap: 540) }, deadline: Wed, due: Fri);   // 1분/EA

        Assert.Equal(270m, r.Placements.Sum(p => p.Qty));
        Assert.Equal(270, r.Placements.Sum(p => p.EndMin - p.StartMin));
        Assert.All(r.Placements, p => Assert.Equal(Mon, p.Date));
    }

    [Fact]
    public void Floors_partial_ea_and_trims_the_slot()
    {
        // 90초 사이클 = 1.5분/EA, 하루 100분 → 66 EA(99분), 나머지 34 EA 는 다음 날 51분
        var days = Days((_, _) => Day(I(480, 580)));
        var r = Pack(new[] { Step(1, "A", 100, cycleSec: 90) }, deadline: Fri, due: Fri, days: days);

        Assert.Equal(new[] { (Mon, 480, 579, 66m), (Tue, 480, 531, 34m) },
                     r.Placements.Select(p => (p.Date, p.StartMin, p.EndMin, p.Qty)).ToArray());
    }

    [Fact]
    public void Past_due_order_is_scheduled_from_today_all_late()
    {
        // 납기가 이미 지난 수주 — 납기까지 탐색하면 구간이 없어 전량 미배치가 되므로 오늘부터 60일 안에 넣고 전부 Late
        var r = Pack(new[] { Step(1, "A", 300) }, deadline: Mon.AddDays(-14), due: Mon.AddDays(-10));

        Assert.Equal(300m, r.Placements.Sum(p => p.Qty));
        Assert.All(r.Placements, p => { Assert.Equal(Mon, p.Date); Assert.True(p.Late); });
        Assert.Empty(r.Shortfalls);
    }

    [Fact]
    public void Due_tomorrow_still_stops_at_due_date()
    {
        var r = Pack(new[] { Step(1, "A", 3000) }, deadline: Mon, due: Tue);   // 2일 × 540 = 1080

        Assert.Equal(1080m, r.Placements.Sum(p => p.Qty));
        Assert.Equal(new[] { new StepShortfall(1, "A", 1920m) }, r.Shortfalls);
    }

    [Fact]
    public void No_capacity_and_no_dates_terminates_with_full_shortfall()
    {
        var days = Days((_, _) => Day());   // 가동 밴드 없음
        var r = Pack(new[] { Step(1, "A", 10) }, deadline: null, due: null, days: days);

        Assert.Empty(r.Placements);
        Assert.Equal(new[] { new StepShortfall(1, "A", 10m) }, r.Shortfalls);
    }

    [Fact]
    public void Zero_qty_step_places_nothing_and_reports_nothing()
    {
        var r = Pack(new[] { Step(1, "A", 0) }, deadline: Wed, due: Fri);

        Assert.Empty(r.Placements);
        Assert.Empty(r.Shortfalls);
    }
}
