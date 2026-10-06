using AMES.Data.Services;
using Xunit;
using static AMES.Data.Services.FailureTimeline;

namespace AMES.Data.Tests;

/// <summary>고장 대응 이력(MNT-002 타임라인·MNT-009 대응 단계): 등록 행 + 조치 이력(ARRIVED/ACK/REPAIRED).</summary>
public class FailureTimelineTests
{
    static readonly DateTime Rep = new(2026, 10, 1, 10, 0, 0);

    [Fact]
    public void Andon_failure_closed_without_repair_row_ends_with_resolved_step()
    {
        var steps = Build(Rep, "W001", "RESOLVED", Rep.AddMinutes(40), new[]
        {
            new FailureTimeline.Action("ACK",     Rep.AddMinutes(12), "W003", "Andon responder acknowledged"),
            new FailureTimeline.Action("ARRIVED", Rep.AddMinutes(5),  "W003", "Andon responder arrived: W003"),
        });
        Assert.Equal(new[] { Kind.Reported, Kind.Arrived, Kind.Ack, Kind.Resolved }, steps.Select(s => s.Kind));
        Assert.Equal(new int?[] { 0, 5, 12, 40 }, steps.Select(s => s.Minutes));
        Assert.All(steps.Take(3), s => Assert.Null(s.Note));   // 안돈 고정 영문 설명은 보이지 않는다
    }

    [Fact]
    public void Repaired_row_is_the_resolution_and_keeps_its_note()
    {
        var steps = Build(Rep, null, "RESOLVED", Rep.AddMinutes(90), new[]
        {
            new FailureTimeline.Action("REPAIRED", Rep.AddMinutes(90), null, "베어링 마모 → 교체"),
        });
        Assert.Equal(new[] { Kind.Reported, Kind.Repaired }, steps.Select(s => s.Kind));
        Assert.Equal("베어링 마모 → 교체", steps[1].Note);
    }

    [Fact]
    public void Open_failure_has_no_resolved_step()
        => Assert.Single(Build(Rep, null, "OPEN", null, Array.Empty<FailureTimeline.Action>()));

    [Fact]
    public void Unknown_action_type_is_kept_with_raw_code()
    {
        var s = Build(Rep, null, "IN_PROGRESS", null, new[] { new FailureTimeline.Action("PARTS", Rep.AddMinutes(3), null, "부품 요청") })[1];
        Assert.Equal((Kind.Other, "PARTS", "부품 요청"), (s.Kind, s.RawType, s.Note));
    }

    [Fact]
    public void Action_before_report_counts_as_zero_minutes()
        => Assert.Equal(0, Build(Rep, null, "OPEN", null, new[] { new FailureTimeline.Action("ARRIVED", Rep.AddMinutes(-2), null, null) })[1].Minutes);

    [Fact]
    public void Unknown_report_time_leaves_minutes_empty()
    {
        var steps = Build(null, null, "OPEN", null, new[] { new FailureTimeline.Action("ARRIVED", Rep, null, null) });
        Assert.Equal(Kind.Arrived, Assert.Single(steps).Kind);
        Assert.Null(steps[0].Minutes);
    }

    [Theory]
    [InlineData("OPEN",        new string[0],                Stage.Waiting)]
    [InlineData("IN_PROGRESS", new[] { "ARRIVED" },          Stage.Arrived)]
    [InlineData("IN_PROGRESS", new[] { "ARRIVED", "ACK" },   Stage.Acked)]
    [InlineData("RESOLVED",    new string[0],                Stage.Resolved)]
    [InlineData("CLOSED",      new[] { "ARRIVED" },          Stage.Resolved)]
    [InlineData("OPEN",        new[] { "REPAIRED" },         Stage.Waiting)]   // 해결은 등록 행 상태가 정본
    public void Stage_follows_status_then_latest_action(string status, string[] types, Stage expected)
        => Assert.Equal(expected, CurrentStage(status, types));

    [Fact]
    public void Elapsed_is_until_now_while_open_and_until_resolution_after()
    {
        var now = Rep.AddMinutes(75);
        Assert.Equal(75, ElapsedMinutes(Rep, "IN_PROGRESS", null, now));
        Assert.Equal(30, ElapsedMinutes(Rep, "RESOLVED", Rep.AddMinutes(30), now));
        Assert.Null(ElapsedMinutes(Rep, "RESOLVED", null, now));
        Assert.Null(ElapsedMinutes(null, "OPEN", null, now));
    }
}
