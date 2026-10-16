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

    // ── 순차 채움(SHIFT_FILL_SEQ, 2026-10-08 사용자 결정): 앞 교대의 남은 시간이 찰 때까지 넣고 넘치는 양은 다음 교대로, 시간 있는 마지막 교대가 나머지를 받는다 ──

    [Fact]
    public void Sequential_puts_everything_in_the_first_shift_when_it_fits()
    {
        // 400 ÷ UPH 60 = 6.67h ≤ A 8h
        Assert.Equal(new[] { ("A", 400d), ("B", 0d) }, Q(ShiftSplit.Sequential(400, H(("A", 8), ("B", 8)), 8, 60)));
    }

    [Fact]
    public void Sequential_fills_the_first_shift_to_its_hours_and_spills_the_rest()
    {
        // A 8h × 60 = 480 (8 단위 내림 480) → B 224
        Assert.Equal(new[] { ("A", 480d), ("B", 224d) }, Q(ShiftSplit.Sequential(704, H(("A", 8), ("B", 8)), 8, 60)));
        // A 7.9h × 60 = 474 → 10 단위 내림 470, B 8h = 480 → 480, C 나머지 250
        Assert.Equal(new[] { ("A", 470d), ("B", 480d), ("C", 250d) }, Q(ShiftSplit.Sequential(1200, H(("A", 7.9), ("B", 8), ("C", 6)), 10, 60)));
    }

    [Fact]
    public void Sequential_skips_a_shift_that_cannot_hold_one_pack()
    {
        // A 0.1h × 60 = 6 < 8개들이 한 박스 → A 0, 전량 B
        Assert.Equal(new[] { ("A", 0d), ("B", 300d) }, Q(ShiftSplit.Sequential(300, H(("A", 0.1), ("B", 8)), 8, 60)));
    }

    [Fact]
    public void Sequential_last_shift_with_hours_takes_the_remainder_even_over_its_capacity()
    {
        // C 는 시간 0 → 건너뛰고 B 가 마지막: 1200 − 480 = 720 (B 능력 480 초과 — 부하 경고 몫)
        Assert.Equal(new[] { ("A", 480d), ("B", 720d), ("C", 0d) }, Q(ShiftSplit.Sequential(1200, H(("A", 8), ("B", 8), ("C", 0)), 8, 60)));
        Assert.Equal(new[] { ("A", 0d), ("B", 700d) }, Q(ShiftSplit.Sequential(700, H(("A", 0), ("B", 8)), 1, 60)));
    }

    [Fact]
    public void Sequential_without_uph_or_hours_takes_everything_in_the_first_shift()
    {
        Assert.Equal(new[] { ("A", 700d), ("B", 0d) }, Q(ShiftSplit.Sequential(700, H(("A", 8), ("B", 8)), 8, 0)));
        Assert.Equal(new[] { ("A", 700d), ("B", 0d) }, Q(ShiftSplit.Sequential(700, H(("A", 0), ("B", 0)), 8, 60)));
        Assert.Equal(new[] { ("A", 0d), ("B", 0d) }, Q(ShiftSplit.Sequential(0, H(("A", 8), ("B", 8)), 8, 60)));
    }

    [Fact]
    public void Sequential_pack_one_fills_to_whole_units_and_preserves_the_sum()
    {
        Assert.Equal(new[] { ("A", 60d), ("B", 40d) }, Q(ShiftSplit.Sequential(100, H(("A", 1), ("B", 2)), 1, 60)));
        foreach (var q in new[] { 1d, 7, 8, 9, 123, 999, 2000 })
            Assert.Equal(q, ShiftSplit.Sequential(q, H(("A", 7.9), ("B", 8.1), ("C", 5)), 8, 37.5).Sum(x => x.Qty), 9);
    }
}
