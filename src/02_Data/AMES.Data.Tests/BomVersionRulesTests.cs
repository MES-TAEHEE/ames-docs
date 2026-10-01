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

    [Theory]
    [InlineData(1, 10, 5, 20, true)]
    [InlineData(1, 10, 10, 20, true)]
    [InlineData(1, 10, 11, 20, false)]
    public void Overlap_is_inclusive_on_both_ends(int aF, int aT, int bF, int bT, bool expected)
        => Assert.Equal(expected, Overlaps(D(1, aF), D(1, aT), D(1, bF), D(1, bT)));
}
