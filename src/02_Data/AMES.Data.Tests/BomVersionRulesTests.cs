using AMES.Data.Services;
using Xunit;
using static AMES.Data.Services.BomVersionRules;

namespace AMES.Data.Tests;

/// <summary>MD-004 BOM 승인: 겹치는 기존 승인 버전은 새 시작일 전날로 닫고, 닫을 수 없으면 거부한다.</summary>
public class BomVersionRulesTests
{
    static DateOnly D(int m, int d) => new(2026, m, d);

    [Fact]
    public void No_other_approved_version_approves_without_changes()
    {
        var plan = PlanApproval(new("NEW", D(10, 1), null), []);
        Assert.Null(plan.Conflict);
        Assert.Empty(plan.Close);
    }

    [Fact]
    public void Open_ended_old_version_is_closed_the_day_before_new_start()
    {
        var plan = PlanApproval(new("NEW", D(10, 1), null), [new("OLD", D(8, 28), null)]);
        Assert.Null(plan.Conflict);
        Assert.Equal([("OLD", D(9, 30))], plan.Close);
    }

    [Fact]
    public void Old_version_without_start_is_closed_too()
    {
        var plan = PlanApproval(new("NEW", D(10, 1), D(12, 31)), [new("OLD", null, null)]);
        Assert.Equal([("OLD", D(9, 30))], plan.Close);
    }

    [Fact]
    public void Non_overlapping_versions_are_left_alone()
    {
        var plan = PlanApproval(new("NEW", D(10, 1), null), [new("OLD", D(1, 1), D(9, 30)), new("NEXT", null, D(3, 1))]);
        Assert.Null(plan.Conflict);
        Assert.Empty(plan.Close);
    }

    [Fact]
    public void Old_version_starting_on_or_after_new_start_blocks_approval()
    {
        Assert.Equal("OLD", PlanApproval(new("NEW", D(10, 1), null), [new("OLD", D(10, 1), null)]).Conflict);
        Assert.Equal("OLD", PlanApproval(new("NEW", D(10, 1), null), [new("OLD", D(11, 1), null)]).Conflict);
    }

    [Fact]
    public void New_version_without_start_cannot_supersede_an_overlapping_one()
    {
        var plan = PlanApproval(new("NEW", null, null), [new("OLD", D(8, 28), null)]);
        Assert.Equal("OLD", plan.Conflict);
        Assert.Empty(plan.Close);
    }

    [Fact]
    public void One_blocking_version_rejects_the_whole_plan()
    {
        var plan = PlanApproval(new("NEW", D(10, 1), null), [new("A", D(8, 1), null), new("B", D(10, 5), null)]);
        Assert.Equal("B", plan.Conflict);
        Assert.Empty(plan.Close);
    }

    static VersionInfo VI(string id, string status, DateOnly? from, DateOnly? to)
        => new(id, "X", status, from, to, new DateTime(2026, 9, 1));

    [Fact]
    public void Default_start_continues_the_day_after_the_previous_end()
    {
        Assert.Equal(D(10, 1), DefaultStart([VI("V1", "APPROVED", D(8, 28), D(9, 30))], "x", D(10, 2)));
        Assert.Equal(D(12, 1), DefaultStart([VI("V1", "APPROVED", D(8, 28), D(9, 30)), VI("V2", "DRAFT", D(10, 1), D(11, 30))], "X", D(10, 2)));
    }

    [Fact]
    public void Default_start_is_today_when_previous_is_open_ended_missing_or_rejected()
    {
        Assert.Equal(D(10, 2), DefaultStart([VI("V1", "APPROVED", D(8, 28), null)], "X", D(10, 2)));
        Assert.Equal(D(10, 2), DefaultStart([], "X", D(10, 2)));
        Assert.Equal(D(10, 2), DefaultStart([VI("V1", "APPROVED", D(8, 28), null), VI("V9", "REJECTED", D(9, 1), D(9, 30))], "X", D(10, 2)));
    }

    [Theory]
    [InlineData("2", "V2.0")]
    [InlineData("2.1", "V2.1")]
    [InlineData("V2", "V2.0")]
    [InlineData("v2.1", "V2.1")]
    [InlineData(" V 3.0 ", "V3.0")]
    [InlineData("02.10", "V2.10")]
    [InlineData("999.999", "V999.999")]
    public void Version_no_is_normalized_to_V_major_dot_minor(string input, string expected)
    {
        Assert.True(TryNormalizeVersionNo(input, out var v));
        Assert.Equal(expected, v);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("V")]
    [InlineData("2.")]
    [InlineData(".1")]
    [InlineData("2.1.3")]
    [InlineData("V2a")]
    [InlineData("-1")]
    [InlineData("REV2")]
    [InlineData("1000")]
    public void Other_version_no_formats_are_rejected(string? input)
        => Assert.False(TryNormalizeVersionNo(input, out _));

    [Theory]
    [InlineData(1, 10, 5, 20, true)]
    [InlineData(1, 10, 10, 20, true)]
    [InlineData(1, 10, 11, 20, false)]
    public void Overlap_is_inclusive_on_both_ends(int aF, int aT, int bF, int bT, bool expected)
        => Assert.Equal(expected, Overlaps(D(1, aF), D(1, aT), D(1, bF), D(1, bT)));
}
