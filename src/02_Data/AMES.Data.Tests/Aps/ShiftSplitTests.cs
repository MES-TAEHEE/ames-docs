using AMES.Data.Aps.Contracts;
using AMES.Data.Aps.Domain;
using Xunit;

namespace AMES.Data.Tests.Aps;

/// <summary>교대 가동 시간 비율 분할(2026-10-07 사용자 결정). 마지막(시간 있는) 교대가 나머지를 받아 합이 보존된다.</summary>
public class ShiftSplitTests
{
    static ShiftHours[] H(params (string c, double h)[] xs) => xs.Select(x => new ShiftHours(x.c, x.h)).ToArray();
    static (string, double)[] Q(List<ShiftQty> l) => l.Select(x => (x.Code, x.Qty)).ToArray();

    [Fact]
    public void Splits_by_hours_ratio_and_floors_all_but_the_last_shift_to_pack()
    {
        // 704 × 7.92/15.84 = 352 → 8 단위 내림 352, B = 704 − 352
        Assert.Equal(new[] { ("A", 352d), ("B", 352d) }, Q(ShiftSplit.Proportional(704, H(("A", 7.92), ("B", 7.92)), 8)));
        // 700 × 8/22 = 254.5 → 10 단위 내림 250, B = 700 × 10/22 = 318.2 → 310, C = 700 − 560 = 140
        Assert.Equal(new[] { ("A", 250d), ("B", 310d), ("C", 140d) }, Q(ShiftSplit.Proportional(700, H(("A", 8), ("B", 10), ("C", 4)), 10)));
    }

    [Fact]
    public void Pack_one_floors_to_whole_units()
    {
        Assert.Equal(new[] { ("A", 33d), ("B", 67d) }, Q(ShiftSplit.Proportional(100, H(("A", 1), ("B", 2)), 1)));
        Assert.Equal(new[] { ("A", 33d), ("B", 67d) }, Q(ShiftSplit.Proportional(100, H(("A", 1), ("B", 2)), 0)));
    }

    [Fact]
    public void Zero_hour_shift_gets_nothing_even_when_last()
    {
        Assert.Equal(new[] { ("A", 400d), ("B", 300d), ("C", 0d) }, Q(ShiftSplit.Proportional(700, H(("A", 8), ("B", 6), ("C", 0)), 1)));
        Assert.Equal(new[] { ("A", 0d), ("B", 700d) }, Q(ShiftSplit.Proportional(700, H(("A", 0), ("B", 8)), 1)));
    }

    [Fact]
    public void Single_shift_or_no_hours_takes_everything_in_the_first_shift()
    {
        Assert.Equal(new[] { ("A", 700d) }, Q(ShiftSplit.Proportional(700, H(("A", 8)), 8)));
        Assert.Equal(new[] { ("A", 700d), ("B", 0d) }, Q(ShiftSplit.Proportional(700, H(("A", 0), ("B", 0)), 8)));
    }

    [Fact]
    public void Non_positive_quantity_is_all_zero_and_sum_is_always_preserved()
    {
        Assert.Equal(new[] { ("A", 0d), ("B", 0d) }, Q(ShiftSplit.Proportional(0, H(("A", 8), ("B", 8)), 8)));
        foreach (var q in new[] { 1d, 7, 8, 9, 123, 999 })
            Assert.Equal(q, ShiftSplit.Proportional(q, H(("A", 7.9), ("B", 8.1), ("C", 5)), 8).Sum(x => x.Qty), 9);
    }
}
