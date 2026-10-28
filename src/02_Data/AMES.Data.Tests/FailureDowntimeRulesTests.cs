using AMES.Data.Services;
using Xunit;
using static AMES.Data.Services.FailureDowntimeRules;

namespace AMES.Data.Tests;

// 10-16 사용자 결정 — 고장 「라인 정지」 → 라인 비가동: CRITICAL 은 항상, 그 밖은 사용자 선택, 열린 비가동이 있으면 연결
public class FailureDowntimeRulesTests
{
    static readonly DateTime Now   = new(2026, 10, 16, 10, 0, 0);
    static readonly DateTime Start = Now.AddMinutes(-30);

    [Theory]
    [InlineData("CRITICAL", false, true)]
    [InlineData("critical", false, true)]
    [InlineData("MAJOR",    false, false)]
    [InlineData("MAJOR",    true,  true)]
    [InlineData("MINOR",    true,  true)]
    [InlineData(null,       false, false)]
    public void Critical_always_stops_the_line_others_follow_the_user(string? severity, bool requested, bool expected)
        => Assert.Equal(expected, Wanted(requested, severity));

    [Fact]
    public void Nothing_on_the_line_creates_a_new_downtime()
        => Assert.Equal(new Decision(Plan.Create, null, Issue.None), Decide(Start, Now, []));

    [Fact]
    public void An_open_downtime_on_the_line_is_linked_instead_of_opening_another()
    {
        var rows = new[] { new Existing(7, Start.AddMinutes(10), null), new Existing(5, Start.AddMinutes(-60), null) };
        Assert.Equal(new Decision(Plan.Link, 5, Issue.None), Decide(Start, Now, rows));
    }

    [Fact]
    public void A_closed_downtime_after_the_start_blocks_creation()
        => Assert.Equal(new Decision(Plan.Skip, null, Issue.Overlap),
            Decide(Start, Now, [new Existing(3, Start.AddMinutes(-10), Start.AddMinutes(5))]));

    [Fact]
    public void A_downtime_that_ended_before_the_start_does_not_matter()
        => Assert.Equal(Plan.Create, Decide(Start, Now, [new Existing(3, Start.AddHours(-2), Start.AddMinutes(-1))]).Plan);

    [Fact]
    public void A_future_start_is_not_recorded()
        => Assert.Equal(new Decision(Plan.Skip, null, Issue.StartInFuture), Decide(Now.AddMinutes(1), Now, []));

    [Fact]
    public void End_time_stays_between_start_and_now()
    {
        Assert.Equal(Start,              EndAt(Start, Start.AddMinutes(-5), Now));
        Assert.Equal(Now,                EndAt(Start, Now.AddHours(1), Now));
        Assert.Equal(Start.AddMinutes(9), EndAt(Start, Start.AddMinutes(9), Now));
    }

    // 10-28 MNT 결과서 E2E-TC-013 — MNT-003 고장 정지 분 = MNT-006 DurationMin(DATEDIFF(minute) — 넘은 분 경계 수)
    [Theory]
    [InlineData("10:00:59", "10:01:00", 1)]
    [InlineData("10:00:00", "10:00:59", 0)]
    [InlineData("10:00:30", "10:45:10", 45)]
    [InlineData("10:00:30", "09:59:00", 0)]
    public void Stop_minutes_count_minute_boundaries_like_datediff(string start, string end, int expected)
    {
        var d = new DateTime(2026, 10, 28);
        Assert.Equal(expected, StopMinutes(d + TimeSpan.Parse(start), d + TimeSpan.Parse(end), Now));
    }

    [Fact]
    public void Open_stop_counts_up_to_now()
        => Assert.Equal(30, StopMinutes(Start.AddSeconds(20), null, Now.AddSeconds(5)));

    [Theory]
    [InlineData(null, null, false)]
    [InlineData(3,    null, true)]
    [InlineData(null, 9,    true)]
    public void Andon_and_failure_downtime_rows_are_locked_in_pp_dtl(int? andonId, int? failureId, bool expected)
        => Assert.Equal(expected, DowntimeEntryRules.IsLockedRow(andonId, failureId));
}
