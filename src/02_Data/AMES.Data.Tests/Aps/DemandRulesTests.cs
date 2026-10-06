using AMES.Data.Aps;
using AMES.Data.Aps.Domain;
using Xunit;

namespace AMES.Data.Tests.Aps;

/// <summary>
/// PP_CustomerOrder → 품번 × 일자 수요 (스펙 §5). 순수 함수, DB 없음. 기준일 2026-10-05(월), 근무일 5개(월~금), 토·일 휴무 달력 주입.
/// </summary>
public class DemandRulesTests
{
    sealed class WeekendOff : IApsCalendar
    {
        public bool IsWorkday(DateOnly d) => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
    }

    static readonly IApsCalendar Cal = new WeekendOff();
    static readonly DateOnly Base = new(2026, 10, 5);
    static readonly string[] Dates = { "2026-10-05", "2026-10-06", "2026-10-07", "2026-10-08", "2026-10-09" };
    static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ITEM-A", "ITEM-B" };

    static int _seq;

    static DemandRules.OrderRow O(string item, string? due, decimal qty, decimal shipped = 0,
                                  string? status = "Confirmed", string? cust = "C1", string? soNo = null)
    {
        int id = ++_seq;
        return new(id, soNo ?? $"SO-{id:000}", 1, cust, item,
                   due is null ? null : DateOnly.ParseExact(due, "yyyy-MM-dd"), qty, shipped, status);
    }

    static DemandRules.Result Run(bool includeOpen = false, string? cust = null, params DemandRules.OrderRow[] orders)
        => DemandRules.Build(orders, Base, Dates, Cal, includeOpen, cust, Known);

    [Fact]
    public void Confirmed_counts_and_open_only_with_flag_other_statuses_never()
    {
        var orders = new[]
        {
            O("ITEM-A", "2026-10-06", 10),
            O("ITEM-A", "2026-10-06", 5, status: "Open"),
            O("ITEM-A", "2026-10-06", 3, status: "Partial"),
            O("ITEM-A", "2026-10-06", 4, status: "Shipped"),
            O("ITEM-A", "2026-10-06", 6, status: "Cancelled"),
        };

        var def  = Run(orders: orders);
        var open = Run(includeOpen: true, orders: orders);

        Assert.Equal(new double[] { 0, 10, 0, 0, 0 }, def.Demand["ITEM-A"]);
        Assert.Equal(new double[] { 0, 15, 0, 0, 0 }, open.Demand["ITEM-A"]);
        Assert.Empty(def.Warnings);
        Assert.Empty(open.Warnings);
    }

    [Fact]
    public void Remaining_is_order_minus_shipped_and_nonpositive_is_dropped_silently()
    {
        var r = Run(orders: new[]
        {
            O("ITEM-A", "2026-10-05", 10, shipped: 4),
            O("ITEM-B", "2026-10-05", 10, shipped: 10),
            O("ITEM-B", "2026-10-05", 5,  shipped: 8),
        });

        Assert.Equal(new double[] { 6, 0, 0, 0, 0 }, r.Demand["ITEM-A"]);
        Assert.False(r.Demand.ContainsKey("ITEM-B"));
        Assert.Empty(r.Warnings);
    }

    [Fact]
    public void Overdue_due_goes_to_first_day()
    {
        var r = Run(orders: new[] { O("ITEM-A", "2026-09-30", 12) });

        Assert.Equal(new double[] { 12, 0, 0, 0, 0 }, r.Demand["ITEM-A"]);
    }

    [Fact]
    public void Holiday_due_folds_to_previous_workday_or_first_day_when_that_is_before_base()
    {
        var r = Run(orders: new[]
        {
            O("ITEM-A", "2026-10-10", 7),    // 토 → 금 10-09
            O("ITEM-A", "2026-10-11", 8),    // 일 → 금 10-09
            O("ITEM-B", "2026-10-04", 9),    // 일 → 금 10-02 < 기준일 → 첫날
        });

        Assert.Equal(new double[] { 0, 0, 0, 0, 15 }, r.Demand["ITEM-A"]);
        Assert.Equal(new double[] { 9, 0, 0, 0, 0 }, r.Demand["ITEM-B"]);
    }

    [Fact]
    public void Null_due_is_dropped_with_warning_naming_the_order()
    {
        var r = Run(orders: new[] { O("ITEM-A", null, 30, soNo: "SO-NULL") });

        Assert.False(r.Demand.ContainsKey("ITEM-A"));
        var w = Assert.Single(r.Warnings);
        Assert.Contains("SO-NULL", w);
        Assert.Contains("ITEM-A", w);
        Assert.Contains("30", w);
    }

    [Fact]
    public void Unknown_item_is_dropped_with_warning_naming_item_and_qty()
    {
        var r = Run(orders: new[] { O("NOPE-1", "2026-10-06", 7, soNo: "SO-X") });

        Assert.Empty(r.Demand);
        var w = Assert.Single(r.Warnings);
        Assert.Contains("NOPE-1", w);
        Assert.Contains("7", w);
        Assert.Contains("SO-X", w);
    }

    [Fact]
    public void Customer_filter_keeps_only_that_customer_case_insensitively()
    {
        var orders = new[] { O("ITEM-A", "2026-10-06", 10, cust: "C1"), O("ITEM-A", "2026-10-06", 4, cust: "C2") };

        var all = Run(orders: orders);
        var c2  = Run(cust: "c2", orders: orders);

        Assert.Equal(14, all.Demand["ITEM-A"][1]);
        Assert.Equal(4,  c2.Demand["ITEM-A"][1]);
    }

    [Fact]
    public void Due_beyond_last_date_is_ignored_without_warning()
    {
        var r = Run(orders: new[] { O("ITEM-A", "2026-10-12", 10) });

        Assert.Empty(r.Demand);
        Assert.Empty(r.Warnings);
    }

    [Fact]
    public void Keys_are_case_insensitive_and_arrays_match_dates_length()
    {
        var r = Run(orders: new[] { O("item-a", "2026-10-07", 1), O("ITEM-A", "2026-10-07", 2) });

        var arr = Assert.Single(r.Demand).Value;
        Assert.Equal(Dates.Length, arr.Length);
        Assert.Equal(3, arr[2]);
    }
}
