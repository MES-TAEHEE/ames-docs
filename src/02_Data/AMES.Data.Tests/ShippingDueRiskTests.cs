using AMES.Contracts.Dto;
using AMES.Data.Scheduling;
using Xunit;

namespace AMES.Data.Tests;

public sealed class ShippingDueRiskTests
{
    [Theory]
    [InlineData(-1, ShippingDueRiskLevel.Overdue)]
    [InlineData(0, ShippingDueRiskLevel.DueSoon)]
    [InlineData(1, ShippingDueRiskLevel.DueSoon)]
    [InlineData(4, ShippingDueRiskLevel.DueSoon)]
    [InlineData(5, ShippingDueRiskLevel.Normal)]
    public void Uses_due_date_not_production_cutoff_and_includes_next_factory_workday(int offset, ShippingDueRiskLevel expected)
    {
        var today = new DateTime(2026, 10, 30); // Friday; Monday is a factory holiday.
        var calendar = new WorkdayCalendar([(new(2026, 11, 2), "HOLIDAY")]);
        var next = ShippingDueRisk.NextWorkday(today, calendar);
        Assert.Equal(new DateTime(2026, 11, 3), next);
        Assert.Equal(expected, ShippingDueRisk.Evaluate(today.AddHours(23), next, today.AddDays(offset), 100, 20, "Open"));
    }

    [Fact]
    public void Special_weekend_shift_is_a_workday_and_date_is_not_rewritten()
    {
        var today = new DateTime(2026, 10, 30);
        var calendar = new WorkdayCalendar([(today.AddDays(1), "HOLIDAY"), (today.AddDays(1), "SPECIAL")]);
        Assert.Equal(today.AddDays(1), ShippingDueRisk.NextWorkday(today, calendar));
        Assert.Equal(ShippingDueRiskLevel.Normal, ShippingDueRisk.Evaluate(today, today.AddDays(1), today.AddDays(2), 100, 0, "Open"));
    }

    [Theory]
    [InlineData("Cancelled")]
    [InlineData(" canceled ")]
    [InlineData("CLOSED")]
    [InlineData("Shipped")]
    public void Excludes_closed_cancelled_and_shipped(string status) => Assert.Equal(ShippingDueRiskLevel.None,
        ShippingDueRisk.Evaluate(DateTime.Today, DateTime.Today.AddDays(1), DateTime.Today.AddDays(-1), 100, 0, status));

    [Theory]
    [InlineData(100, 100, ShippingDueRiskLevel.None)]
    [InlineData(100, 120, ShippingDueRiskLevel.None)]
    [InlineData(0, 0, ShippingDueRiskLevel.None)]
    [InlineData(100, 20, ShippingDueRiskLevel.Overdue)]
    [InlineData(100, 0, ShippingDueRiskLevel.Overdue)]
    [InlineData(100, -1, ShippingDueRiskLevel.Unknown)]
    [InlineData(null, 0, ShippingDueRiskLevel.Unknown)]
    [InlineData(100, null, ShippingDueRiskLevel.Unknown)]
    public void Only_actual_shipped_qty_reduces_balance(int? ordered, int? shipped, ShippingDueRiskLevel expected) =>
        Assert.Equal(expected, ShippingDueRisk.Evaluate(DateTime.Today, DateTime.Today.AddDays(1), DateTime.Today.AddDays(-1), ordered, shipped, "Loading"));

    [Fact]
    public void Missing_due_or_identity_is_unknown_not_normal()
    {
        Assert.Equal(ShippingDueRiskLevel.Unknown, ShippingDueRisk.Evaluate(DateTime.Today, DateTime.Today.AddDays(1), null, 10, 0, null));
        Assert.Equal(ShippingDueRiskLevel.Unknown, ShippingDueRisk.Evaluate(DateTime.Today, DateTime.Today.AddDays(1), DateTime.Today, 10, 0, "Open", false));
    }
}
