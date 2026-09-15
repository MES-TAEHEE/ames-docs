using AMES.Data.Services;
using Xunit;
using static AMES.Data.Services.PrStatusRules;

namespace AMES.InjAgent.Tests;

/// <summary>PP-006 구매요청 상태 기계: Draft/Failed →(Send) Sent →(Approve) Approved, Draft/Failed/Sent →(Fail) Failed. Approved 는 종결.</summary>
public class PrStatusRulesTests
{
    [Theory]
    [InlineData(Draft)]
    [InlineData(Failed)]
    public void Send_moves_draft_or_failed_to_sent(string from)
        => Assert.Equal(Sent, Next(from, PrAction.Send));

    [Theory]
    [InlineData(Sent)]
    [InlineData(Approved)]
    public void Send_is_rejected_from_sent_or_approved(string from)
        => Assert.Throws<InvalidOperationException>(() => Next(from, PrAction.Send));

    [Fact]
    public void Approve_requires_sent()
    {
        Assert.Equal(Approved, Next(Sent, PrAction.Approve));
        Assert.Throws<InvalidOperationException>(() => Next(Draft, PrAction.Approve));
        Assert.Throws<InvalidOperationException>(() => Next(Failed, PrAction.Approve));
        Assert.Throws<InvalidOperationException>(() => Next(Approved, PrAction.Approve));
    }

    [Theory]
    [InlineData(Draft)]
    [InlineData(Failed)]
    [InlineData(Sent)]
    public void Fail_is_allowed_before_approval(string from)
        => Assert.Equal(Failed, Next(from, PrAction.Fail));

    [Fact]
    public void Approved_is_terminal()
        => Assert.Throws<InvalidOperationException>(() => Next(Approved, PrAction.Fail));

    [Fact]
    public void Selectable_for_batch_send_means_can_send()
    {
        Assert.True(CanSend(Draft));
        Assert.True(CanSend(Failed));
        Assert.False(CanSend(Sent));
        Assert.False(CanSend(Approved));
    }

    [Theory]
    [InlineData(null, Draft)]
    [InlineData("", Draft)]
    [InlineData("Pending", Draft)]
    [InlineData("Rejected", Failed)]
    [InlineData("Sent", Sent)]
    public void Normalize_maps_legacy_and_empty_statuses(string? raw, string expected)
        => Assert.Equal(expected, Normalize(raw));
}
