using AMES.Data.Aps;
using AMES.Data.Repositories;
using AMES.Data.Scheduling;
using Xunit;
using static AMES.Data.Scheduling.SlotPacker;

namespace AMES.Data.Tests.Aps;

/// <summary>
/// 스펙 §4.3·§8.3 — WORK_SHIFT A(10)/B(20)/C(30) 3교대 패턴을 주간(SortOrder 최소 교대) / 야간(나머지) 구간으로 나누고,
/// 점유 구간을 뺀 잔여에 분 단위로 앞에서부터 채운다. 축 원점 dayStart = 480(A 교대 시작). 순수 함수, DB 없음.
/// </summary>
public class ShiftBandsTests
{
    static Interval I(int s, int e) => new(s, e);

    // A 08:00~16:00 · B 16:00~24:00 · C 00:00~08:00 (전부 OPERATING)
    static readonly (Interval Band, int ShiftSort)[] ThreeShifts =
    {
        (I(480, 960), 10), (I(960, 1440), 20), (I(0, 480), 30),
    };
    // A 교대에 휴게 12:00~13:00 이 있는 2교대 (OPERATING 세그먼트만 넘어온다)
    static readonly (Interval Band, int ShiftSort)[] WithBreak =
    {
        (I(480, 720), 10), (I(780, 960), 10), (I(960, 1440), 20),
    };

    [Fact]
    public void Three_shifts_split_into_first_sort_day_and_rest_night_in_axis_order()
    {
        var b = ShiftBands.Split(ThreeShifts, Array.Empty<Interval>(), dayStart: 480);

        Assert.Equal(new[] { I(480, 960) }, b.Day);
        Assert.Equal(new[] { I(960, 1440), I(0, 480) }, b.Night);   // 00:00 밴드는 축 기준으로 22:00 뒤
        Assert.Equal(8d, b.DayHours);
        Assert.Equal(16d, b.NightHours);
        Assert.Equal(480, b.DayStart);
    }

    [Fact]
    public void Day_keeps_all_operating_bands_of_the_first_shift()
    {
        var b = ShiftBands.Split(WithBreak, Array.Empty<Interval>(), 480);

        Assert.Equal(new[] { I(480, 720), I(780, 960) }, b.Day);
        Assert.Equal(7d, b.DayHours);
    }

    [Fact]
    public void Occupied_is_subtracted_from_both_day_and_night()
    {
        var occ = new[] { I(480, 600), I(900, 1000) };

        var b = ShiftBands.Split(ThreeShifts, occ, 480);

        Assert.Equal(new[] { I(600, 900) }, b.Day);
        Assert.Equal(new[] { I(1000, 1440), I(0, 480) }, b.Night);
        Assert.Equal(5d, b.DayHours);
    }

    [Fact]
    public void Empty_operating_gives_empty_bands()
    {
        var b = ShiftBands.Split(Array.Empty<(Interval, int)>(), Array.Empty<Interval>(), 480);

        Assert.Empty(b.Day);
        Assert.Empty(b.Night);
        Assert.Equal(0d, b.DayHours + b.NightHours);
    }

    [Fact]
    public void IsDay_uses_full_bands_before_occupancy()
    {
        var full = ShiftBands.Split(WithBreak, Array.Empty<Interval>(), 480);

        Assert.True(ShiftBands.IsDay(full, 480));
        Assert.True(ShiftBands.IsDay(full, 959));
        Assert.True(ShiftBands.IsDay(full, 740));    // 휴게 중 시작 — 축상 첫 야간 밴드 앞이면 주간
        Assert.False(ShiftBands.IsDay(full, 960));
        Assert.False(ShiftBands.IsDay(full, 1200));
    }

    [Fact]
    public void IsDay_treats_c_shift_as_night()
    {
        var full = ShiftBands.Split(ThreeShifts, Array.Empty<Interval>(), 480);

        Assert.False(ShiftBands.IsDay(full, 100));
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
