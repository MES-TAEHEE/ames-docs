using AMES.Contracts.Dto;
using AMES.Data.Services;
using Xunit;

namespace AMES.Data.Tests;

// 10-16 사용자 결정 — PM 상태는 예정일로 계산·「예정」 PM 작업지시·사이클 PM 다음 예정일·점검 항목 완료 규칙
public class PmRulesTests
{
    static readonly DateTime Today = new(2026, 10, 16);

    [Theory]
    [InlineData(null,  7)]
    [InlineData("",    7)]
    [InlineData(" 3 ", 3)]
    [InlineData("0",   0)]
    [InlineData("365", 365)]
    [InlineData("366", 7)]
    [InlineData("-1",  7)]
    [InlineData("x",   7)]
    public void Lead_days_fall_back_to_seven(string? value, int expected) => Assert.Equal(expected, PmRules.ParseLeadDays(value));

    [Theory]
    [InlineData("OK",        -1, "OVERDUE")]
    [InlineData("OK",         0, "DUE")]
    [InlineData("DUE",        7, "DUE")]
    [InlineData("OVERDUE",    8, "OK")]    // 옛 저장값은 무시하고 날짜로
    [InlineData("DONE",      -3, "OVERDUE")]
    [InlineData("cancelled", -3, "CANCELLED")]
    public void Status_is_calculated_from_the_due_date(string stored, int days, string expected)
        => Assert.Equal(expected, PmRules.DisplayStatus(stored, Today.AddDays(days), Today, 7));

    [Fact]
    public void Status_without_due_date_is_ok() => Assert.Equal(PmRules.Ok, PmRules.DisplayStatus(null, null, Today, 7));

    [Theory]
    [InlineData("ISSUED",      8, true)]
    [InlineData("ASSIGNED",    8, true)]
    [InlineData("ISSUED",      7, false)]
    [InlineData("IN_PROGRESS", 30, false)]   // 착수했으면 예정이 아니다
    [InlineData("COMPLETED",   30, false)]
    public void Planned_work_orders_are_not_started_and_far_from_due(string status, int days, bool expected)
        => Assert.Equal(expected, PmRules.IsPlanned(status, Today.AddDays(days), Today, 7));

    [Fact]
    public void Planned_needs_a_due_date() => Assert.False(PmRules.IsPlanned("ISSUED", null, Today, 7));

    [Theory]
    [InlineData("TIME",  30,   "2026-11-15")]
    [InlineData("time ", 1,    "2026-10-17")]
    [InlineData("TIME",  0,    null)]
    [InlineData("TIME",  null, null)]
    [InlineData("CYCLE", 5000, null)]
    [InlineData(null,    null, null)]
    public void Only_time_cycles_get_the_next_due_date_automatically(string? basis, int? value, string? expected)
    {
        Assert.Equal(expected is null ? null : DateTime.Parse(expected), PmRules.AutoNextDue(basis, value, Today));
        Assert.Equal(expected is null, PmRules.NeedsManualNextDue(basis, value));
    }

    [Fact]
    public void Manual_next_due_must_be_after_the_completion_date()
    {
        Assert.False(PmRules.ValidManualNextDue(null, Today));
        Assert.False(PmRules.ValidManualNextDue(Today, Today.AddHours(15)));
        Assert.True(PmRules.ValidManualNextDue(Today.AddDays(1), Today.AddHours(15)));
    }

    [Fact]
    public void Cancelled_and_maint_are_recognised()
    {
        Assert.Equal("CANCELLED", PmRules.Stored(true));
        Assert.Equal("OK", PmRules.Stored(false));
        Assert.True(PmRules.IsMaint(" maint"));
        Assert.False(PmRules.IsMaint("EQUIP"));
    }

    [Theory]
    [InlineData(MwoResultCodes.Ok,       0, 0, true)]
    [InlineData(MwoResultCodes.Ok,       1, 0, false)]
    [InlineData(MwoResultCodes.Ok,       0, 1, false)]
    [InlineData(MwoResultCodes.Partial,  2, 1, true)]
    [InlineData(MwoResultCodes.FollowUp, 0, 1, true)]
    public void Ok_needs_every_item_checked_and_passed(string result, int pending, int failed, bool expected)
        => Assert.Equal(expected, MwoChecklistRules.Allows(result, pending, failed));

    [Theory]
    [InlineData(0, 0, MwoResultCodes.Ok)]
    [InlineData(2, 0, MwoResultCodes.Partial)]
    [InlineData(2, 1, MwoResultCodes.FollowUp)]
    public void Default_result_follows_the_checklist(int pending, int failed, string expected)
        => Assert.Equal(expected, MwoChecklistRules.DefaultResult(pending, failed));

    [Theory]
    [InlineData(MwoResultCodes.FollowUp, 1, false, true)]
    [InlineData(MwoResultCodes.Partial,  2, false, true)]
    [InlineData(MwoResultCodes.FollowUp, 0, false, false)]
    [InlineData(MwoResultCodes.FollowUp, 1, true,  false)]   // 고장은 자기 후속 작업지시를 받는다
    [InlineData(MwoResultCodes.Ok,       1, false, false)]
    public void Failed_items_get_a_follow_up_work_order(string result, int failed, bool hasFailures, bool expected)
        => Assert.Equal(expected, MwoChecklistRules.IssuesFollowUp(result, failed, hasFailures));
}
