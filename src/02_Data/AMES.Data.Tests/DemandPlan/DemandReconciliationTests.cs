using AMES.Data.Services.DemandPlan;
using Xunit;

namespace AMES.Data.Tests.DemandPlan;

/// <summary>수주·일별 발주·구매계획·주간 예측 대조 규칙. 순수 함수.</summary>
public class DemandReconciliationTests
{
    static readonly DateOnly D = new(2026, 11, 3);

    [Fact]
    public void Same_quantity_in_order_and_daily_po_is_not_an_issue()
    {
        var issues = DemandReconciliation.Days(new[] { new DemandReconciliation.DayRow("C1", "PN-A", D, 143, 143, 143) });
        Assert.Empty(issues);
    }

    [Fact]
    public void Different_quantities_are_reported_with_both_values()
    {
        var issue = Assert.Single(DemandReconciliation.Days(new[] { new DemandReconciliation.DayRow("C1", "PN-A", D, 143, 120, null) }));

        Assert.Equal(DemandReconciliation.KindPoVsDailyPo, issue.Kind);
        Assert.Equal(143m, issue.Left);
        Assert.Equal(120m, issue.Right);
    }

    [Fact]
    public void Order_without_daily_po_row_and_daily_po_without_order_are_both_flagged()
    {
        var issues = DemandReconciliation.Days(new[]
        {
            new DemandReconciliation.DayRow("C1", "PN-A", D, 80, null, null),
            new DemandReconciliation.DayRow("C1", "PN-B", D, null, 40, null),
        });

        Assert.Equal(new[] { DemandReconciliation.KindDailyPoMissing, DemandReconciliation.KindPoMissing },
                     issues.Select(i => i.Kind).ToArray());
    }

    [Fact]
    public void Daily_po_is_compared_with_the_plan_p_o_column_too()
    {
        var issue = Assert.Single(DemandReconciliation.Days(new[] { new DemandReconciliation.DayRow("C1", "PN-A", D, 40, 40, 30) }));
        Assert.Equal(DemandReconciliation.KindDailyVsPlanPo, issue.Kind);
    }
}
