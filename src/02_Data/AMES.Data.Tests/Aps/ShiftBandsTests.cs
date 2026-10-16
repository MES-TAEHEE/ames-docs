using AMES.Data.Aps;
using AMES.Data.Repositories;
using AMES.Data.Scheduling;
using Xunit;
using static AMES.Data.Scheduling.SlotPacker;

namespace AMES.Data.Tests.Aps;

/// <summary>
/// 스펙 §4.3·§8.3 + 2026-10-07 교대 모델 — WORK_SHIFT A(10)/B(20)/C(30) 3교대 패턴을 교대별(SortOrder 순) 구간으로 나누고,
/// 점유 구간을 뺀 잔여에 분 단위로 앞에서부터 채운다. ShiftOf = 슬롯 시작 시각이 속한 교대. 축 원점 dayStart = 480(A 교대 시작). 순수 함수, DB 없음.
/// </summary>
public class ShiftBandsTests
{
    static Interval I(int s, int e) => new(s, e);

    // A 08:00~16:00 · B 16:00~24:00 · C 00:00~02:00 (전부 OPERATING) — 교대 코드 포함
    static readonly (Interval Band, int ShiftSort, string ShiftCode)[] ThreeShifts =
    {
        (I(480, 960), 10, "A"), (I(960, 1440), 20, "B"), (I(0, 120), 30, "C"),
    };
    // A 교대에 휴게 12:00~13:00 이 있는 2교대 (OPERATING 세그먼트만 넘어온다)
    static readonly (Interval Band, int ShiftSort, string ShiftCode)[] WithBreak =
    {
        (I(480, 720), 10, "A"), (I(780, 960), 10, "A"), (I(960, 1440), 20, "B"),
    };

    [Fact]
    public void SplitByShift_returns_each_shift_in_sort_order_with_axis_ordered_bands()
    {
        var b = ShiftBands.SplitByShift(ThreeShifts, Array.Empty<Interval>(), dayStart: 480);
        Assert.Equal(new[] { "A", "B", "C" }, b.Select(s => s.Code).ToArray());
        Assert.Equal(new[] { (480, 960) }, b[0].Bands.Select(x => (x.StartMin, x.EndMin)).ToArray());
        Assert.Equal(new[] { (0, 120) }, b[2].Bands.Select(x => (x.StartMin, x.EndMin)).ToArray());
        Assert.Equal((8d, 8d, 2d), (b[0].Hours, b[1].Hours, b[2].Hours));
    }

    [Fact]
    public void SplitByShift_keeps_all_bands_of_a_shift_and_subtracts_occupancy()
    {
        var occ = new[] { I(500, 520), I(1000, 1100) };
        var b = ShiftBands.SplitByShift(WithBreak, occ, 480);
        Assert.Equal(new[] { (480, 500), (520, 720), (780, 960) }, b[0].Bands.Select(x => (x.StartMin, x.EndMin)).ToArray());
        Assert.Equal(new[] { (960, 1000), (1100, 1440) }, b[1].Bands.Select(x => (x.StartMin, x.EndMin)).ToArray());
    }

    /// <summary>리뷰 F6: 교대 코드는 대소문자를 가리지 않는다(PP_ApsPlanLineShift PK 는 CI 콜레이션) — 'a' 와 'A' 세그먼트는 한 교대.</summary>
    [Fact]
    public void SplitByShift_merges_codes_that_differ_only_by_case()
    {
        var ops = new (Interval Band, int ShiftSort, string ShiftCode)[] { (I(480, 720), 10, "a"), (I(780, 960), 10, "A"), (I(960, 1440), 20, "B") };
        var b = ShiftBands.SplitByShift(ops, Array.Empty<Interval>(), 480);
        Assert.Equal(new[] { "A", "B" }, b.Select(s => s.Code).ToArray());
        Assert.Equal(new[] { (480, 720), (780, 960) }, b[0].Bands.Select(x => (x.StartMin, x.EndMin)).ToArray());
    }

    [Fact]
    public void Empty_operating_gives_no_shifts()
    {
        Assert.Empty(ShiftBands.SplitByShift(Array.Empty<(Interval, int, string)>(), Array.Empty<Interval>(), 480));
    }

    [Fact]
    public void ShiftOf_uses_full_bands_and_falls_back_to_the_previous_shift_on_the_axis()
    {
        var full = ShiftBands.SplitByShift(WithBreak, Array.Empty<Interval>(), 480);
        Assert.Equal("A", ShiftBands.ShiftOf(full, 480, 480));
        Assert.Equal("A", ShiftBands.ShiftOf(full, 740, 480));    // 휴게 중 시작 → 축상 앞 교대 A
        Assert.Equal("B", ShiftBands.ShiftOf(full, 960, 480));
        Assert.Equal("B", ShiftBands.ShiftOf(full, 1439, 480));
        var three = ShiftBands.SplitByShift(ThreeShifts, Array.Empty<Interval>(), 480);
        Assert.Equal("C", ShiftBands.ShiftOf(three, 100, 480));
        Assert.Equal("C", ShiftBands.ShiftOf(three, 300, 480));   // C 뒤 빈 시간 → 앞 교대 C
        Assert.Equal("C", ShiftBands.ShiftOf(three, 470, 480));   // 470 은 축(dayStart 480)상 1430분 — C(0~120 = 축 960~1080) 뒤 → C
        Assert.Equal("", ShiftBands.ShiftOf(Array.Empty<ShiftBands.ShiftBand>(), 500, 480));
    }

    [Theory]
    [InlineData(100, 60, 100)]
    [InlineData(100, 120, 50)]
    [InlineData(1, 7, 9)]      // 8.571 → 9
    [InlineData(10, 3, 200)]
    [InlineData(0, 60, 0)]
    [InlineData(70, 60, 70)]   // qty ÷ uph 를 먼저 나누면 1.1666…67 × 60 = 70.000…002 → 71 (decimal 반올림 오차)
    [InlineData(7, 60, 7)]
    public void MinutesFor_rounds_up(decimal qty, decimal uph, int expected)
    {
        Assert.Equal(expected, ShiftBands.MinutesFor(qty, uph));
    }

    [Fact]
    public void MinutesFor_rejects_zero_uph()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ShiftBands.MinutesFor(10, 0));
    }

    [Fact]
    public void Allocate_splits_across_remaining_bands_in_order()
    {
        var remaining = new[] { I(600, 720), I(780, 960) };

        var a = ShiftBands.Allocate(remaining, 200, 480);

        Assert.Equal(new[] { I(600, 720), I(780, 860) }, a.Slots);
        Assert.Equal(200, a.PlacedMin);
        Assert.Equal(0, a.ShortMin);
    }

    [Fact]
    public void Allocate_reports_shortfall_when_bands_are_too_short()
    {
        var remaining = new[] { I(600, 720), I(780, 960) };

        var a = ShiftBands.Allocate(remaining, 400, 480);

        Assert.Equal(300, a.PlacedMin);
        Assert.Equal(100, a.ShortMin);
        Assert.Equal(2, a.Slots.Count);
    }

    [Fact]
    public void Allocate_with_nothing_wanted_or_no_bands()
    {
        Assert.Empty(ShiftBands.Allocate(new[] { I(600, 720) }, 0, 480).Slots);
        var a = ShiftBands.Allocate(Array.Empty<Interval>(), 30, 480);
        Assert.Empty(a.Slots);
        Assert.Equal(30, a.ShortMin);
    }

    [Fact]
    public void Representative_is_ordinal_first_item_no()
    {
        Assert.Equal("83335-P8000RBQ", ShiftBands.Representative(new[] { "83345-P8000RBQ", "83335-P8000RBQ" }));
        Assert.Equal("A", ShiftBands.Representative(new[] { "b", "A" }));
        Assert.Throws<ArgumentException>(() => ShiftBands.Representative(Array.Empty<string>()));
    }

    [Fact]
    public void DayCapacity_with_keeps_shift_bands_through_Occupy()
    {
        var bands = ThreeShifts.ToList();
        var cap = new LineScheduleRepository.DayCapacity("P", 480, new[] { I(480, 960), I(960, 1440), I(0, 480) },
                                                          Array.Empty<Interval>(), 1440, 0, null, null, bands);
        var cache = new DeadlinePacker.DayStateCache((_, _) => cap);
        var d = new DateTime(2026, 10, 5);

        cache.Occupy("L", d, I(480, 540), "M1");
        var after = cache.Get("L", d);

        Assert.Same(bands, after.ShiftBands);
        Assert.Single(after.Occupied);
        Assert.Equal("M1", after.LastMoldId);
    }
}
