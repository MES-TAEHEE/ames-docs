using AMES.Data.Services;
using Xunit;
using static AMES.Data.Services.LineTimeLayout;

namespace AMES.Data.Tests;

/// <summary>MD-028 교대 창·세그먼트 배치: 자정을 넘는 창, 패턴 가동 교대, 교대 경계 변경 시 재배치, 저장 분할.</summary>
public class LineTimeLayoutTests
{
    // 10-02 WORK_SHIFT.Attribute1
    static readonly List<ShiftWindow> Now = Windows([("A", "A", "0700-1630"), ("B", "B", "1630-0200"), ("C", "C", "0200-0700")]);
    static Func<string?, string> Pattern(params string[] operating) => code => operating.Contains(code) ? Operating : Idle;

    [Fact]
    public void Windows_extend_a_midnight_crossing_shift()
    {
        Assert.Equal([("A", 420, 990), ("B", 990, 1560), ("C", 120, 420)], Now.Select(w => (w.Code, w.Start, w.End)));
        Assert.Equal(1440, Now.Sum(w => w.Length));
    }

    [Fact]
    public void Windows_skip_invalid_or_empty()
    {
        var w = Windows([("A", "A", "0700-0700"), ("B", "B", "x"), ("C", "C", null), ("D", "D", "0000-2400")]);
        Assert.Equal(["D:0-1440"], w.Select(x => $"{x.Code}:{x.Start}-{x.End}"));
    }

    [Fact]
    public void Shift_list_parses_comma_codes()
    {
        Assert.Equal(["A", "B"], ParseShiftList(" A , B "));
        Assert.Empty(ParseShiftList(null));
    }

    [Fact]
    public void Clock_wraps_past_midnight_and_keeps_2400()
    {
        Assert.Equal("16:30", ToClock(990));
        Assert.Equal("24:00", ToClock(1440));
        Assert.Equal("02:00", ToClock(1560));
    }

    [Fact]
    public void Empty_pattern_fills_each_window_with_its_default_state()
    {
        var r = Realign(Now, [], Pattern("A", "B"));
        Assert.Equal(["A:420-990:OPERATING", "B:990-1560:OPERATING", "C:120-420:IDLE"], r.Select(b => $"{b.Shift}:{b.Start}-{b.End}:{b.State}"));
    }

    [Fact]
    public void Old_boundaries_are_rebased_keeping_painted_bands_at_their_clock_time()
    {
        // 구 경계 A 08–16(가동, 09:14–09:20 계획비가동), B 16–24(가동), C 00–08(유휴) · 2교대(A,B)
        Band[] stored =
        [
            new(480, 554, Operating, null, "A", null), new(554, 560, "PLANNED_DOWNTIME", "R1", "A", "x"), new(560, 960, Operating, null, "A", null),
            new(960, 1440, Operating, null, "B", null),
            new(0, 480, Idle, null, "C", null),
        ];
        var r = Realign(Now, stored, Pattern("A", "B"));
        Assert.Equal(
            ["A:420-554:OPERATING:", "A:554-560:PLANNED_DOWNTIME:R1", "A:560-990:OPERATING:", "B:990-1560:OPERATING:", "C:120-420:IDLE:"],
            r.Select(b => $"{b.Shift}:{b.Start}-{b.End}:{b.State}:{b.Reason}"));
    }

    [Fact]
    public void Painted_band_inside_a_crossing_window_is_found_after_midnight()
    {
        Band[] stored = [new(30, 60, "BREAK", null, "B", null)];   // 00:30–01:00 휴식
        var b = Realign(Now, stored, Pattern("A", "B")).Where(x => x.Shift == "B").ToList();
        Assert.Equal(["990-1470:OPERATING", "1470-1500:BREAK", "1500-1560:OPERATING"], b.Select(x => $"{x.Start}-{x.End}:{x.State}"));
    }

    [Fact]
    public void Realign_is_stable_on_its_own_saved_output()
    {
        Band[] stored = [new(600, 630, "INSPECT", null, "A", null), new(0, 60, "MEAL", null, "B", null)];
        var first = Realign(Now, stored, Pattern("A", "B"));
        var saved = first.SelectMany(Split).ToList();
        var second = Realign(Now, saved, Pattern("A", "B"));
        Assert.True(SameLayout(saved, second.SelectMany(Split)));
    }

    [Fact]
    public void Split_cuts_at_midnight()
    {
        Assert.Equal(["990-1440", "0-120"], Split(new Band(990, 1560, Operating, null, "B", null)).Select(b => $"{b.Start}-{b.End}"));
        Assert.Equal(["10-30"], Split(new Band(1450, 1470, Operating, null, "B", null)).Select(b => $"{b.Start}-{b.End}"));
        Assert.Equal(["420-990"], Split(new Band(420, 990, Operating, null, "A", null)).Select(b => $"{b.Start}-{b.End}"));
    }

    [Fact]
    public void Outdated_when_saved_on_old_shift_boundaries()
    {
        Band[] old = [new(480, 960, Operating, null, "A", null), new(960, 1440, Operating, null, "B", null), new(0, 480, Idle, null, "C", null)];
        Assert.True(IsOutdated(Now, old, Pattern("A", "B")));
    }

    [Fact]
    public void Not_outdated_after_saving_on_current_boundaries_or_when_nothing_to_judge()
    {
        Band[] old = [new(480, 960, Operating, null, "A", null), new(960, 1440, Operating, null, "B", null), new(0, 480, Idle, null, "C", null)];
        var saved = Realign(Now, old, Pattern("A", "B")).SelectMany(Split).ToList();
        Assert.False(IsOutdated(Now, saved, Pattern("A", "B")));
        Assert.False(IsOutdated(Now, [], Pattern("A", "B")));
        Assert.False(IsOutdated([], old, Pattern("A", "B")));
    }

    [Fact]
    public void Outdated_when_shift_times_change_again()
    {
        var saved = Realign(Now, [], Pattern("A", "B")).SelectMany(Split).ToList();
        var moved = Windows([("A", "A", "0600-1530"), ("B", "B", "1530-0100"), ("C", "C", "0100-0600")]);
        Assert.True(IsOutdated(moved, saved, Pattern("A", "B")));
    }

    // 10-02 WORK_SHIFT.Attribute2(실제 생산 시간)까지 반영한 창
    static readonly List<ShiftWindow> Work = Windows([("A", "A", "0700-1630", "0730-1630"), ("B", "B", "1630-0200", "1630-0130"), ("C", "C", "0200-0700", "0200-0700")]);

    [Fact]
    public void Work_time_is_clipped_into_the_window_and_crosses_midnight()
    {
        Assert.Equal(["A:450-990", "B:990-1530", "C:120-420"], Work.Select(w => $"{w.Code}:{w.WorkStart}-{w.WorkEnd}"));
        var after = Windows([("B", "B", "1630-0200", "0000-0130")]);
        Assert.Equal((1440, 1530), (after[0].WorkStart, after[0].WorkEnd));
        var bad = Windows([("A", "A", "0700-1630", "x")]);
        Assert.Null(bad[0].WorkStart);
    }

    [Fact]
    public void Default_is_idle_outside_work_time_of_a_running_shift()
    {
        var r = Realign(Work, [], Pattern("A", "B"));
        Assert.Equal(
            ["A:420-450:IDLE", "A:450-990:OPERATING", "B:990-1530:OPERATING", "B:1530-1560:IDLE", "C:120-420:IDLE"],
            r.Select(b => $"{b.Shift}:{b.Start}-{b.End}:{b.State}"));
    }

    [Fact]
    public void Idle_before_work_time_is_not_treated_as_painted_and_stays_idle()
    {
        Band[] stored = [new(420, 450, Idle, null, "A", null), new(450, 990, Operating, null, "A", null), new(990, 1440, Operating, null, "B", null),
                         new(0, 90, Operating, null, "B", null), new(90, 120, Idle, null, "B", null), new(120, 420, Idle, null, "C", null)];
        Assert.False(IsOutdated(Work, stored, Pattern("A", "B")));
    }

    [Fact]
    public void Coverage_is_clean_for_current_shifts_and_saved_layout()
    {
        Assert.Empty(CoverageIssues(Now));
        var saved = Realign(Work, [], Pattern("A", "B")).SelectMany(Split).Select(b => (b.Start, b.End));
        Assert.Empty(CoverageIssues(saved));
    }

    [Fact]
    public void Coverage_reports_gaps_and_overlaps_in_clock_order()
    {
        var w = Windows([("A", "A", "0800-1630"), ("B", "B", "1600-0200"), ("C", "C", "0200-0700")]);
        var issues = CoverageIssues(w);
        Assert.Equal(["420-480:gap", "960-990:overlap"], issues.Select(x => $"{x.Start}-{x.End}:{(x.Overlap ? "overlap" : "gap")}"));
        Assert.Equal("07:00–08:00, 16:00–16:30", Describe(issues));
    }

    [Fact]
    public void Coverage_of_nothing_is_one_full_day_gap()
    {
        Assert.Equal(["0-1440:False"], CoverageIssues(Array.Empty<(int, int)>()).Select(x => $"{x.Start}-{x.End}:{x.Overlap}"));
    }

    [Fact]
    public void Repository_rejects_a_schedule_with_a_gap_before_touching_the_database()
    {
        // 접속할 수 없는 서버 — 검사가 접속보다 먼저라 InvalidOperationException 이 나야 한다(SqlException 이면 검사를 건너뛴 것)
        var md = new AMES.Data.Repositories.MasterDataRepository(new AMES.Data.Connection.AmesConnectionFactory("Server=invalid.invalid;Connect Timeout=1"));
        var ex = Assert.Throws<InvalidOperationException>(() =>
            md.SaveLineTimeSegments("LP-X", [(0, 420, Idle, null, "C", null), (480, 1440, Operating, null, "A", null)], "test"));
        Assert.Contains("07:00–08:00", ex.Message);
    }

    [Fact]
    public void Same_layout_ignores_order()
    {
        Band[] a = [new(0, 10, Idle, null, "C", null), new(10, 20, Operating, null, "A", null)];
        Assert.True(SameLayout(a, a.Reverse()));
        Assert.False(SameLayout(a, [a[0]]));
    }
}
