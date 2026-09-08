using AMES.Data.Scheduling;
using Xunit;
using static AMES.Data.Scheduling.SlotPacker;

namespace AMES.InjAgent.Tests;

/// <summary>
/// 하루 부분 채움 — Place 가 "통째로 들어가는 첫 자리" 라면 FillDay 는 "앞에서부터 있는 대로".
/// 축·밴드·회피 규칙은 Place 와 같고, 틈마다 Interval 하나를 낸다. 순수 함수, DB 없음.
/// </summary>
public class SlotPackerFillDayTests
{
    static Interval I(int s, int e) => new(s, e);
    static readonly Interval[] Std = { I(480, 720), I(780, 1080) };   // 08:00~12:00, 13:00~18:00 = 540분

    [Fact]
    public void Empty_day_fills_from_first_band_and_spills_into_next()
    {
        var got = FillDay(Std, Array.Empty<Interval>(), 300, dayStart: 480);

        Assert.Equal(new[] { I(480, 720), I(780, 840) }, got);
    }

    [Fact]
    public void Stops_when_max_is_reached_inside_a_band()
    {
        var got = FillDay(Std, Array.Empty<Interval>(), 100, dayStart: 480);

        Assert.Equal(new[] { I(480, 580) }, got);
    }

    [Fact]
    public void Skips_busy_intervals_and_uses_the_gaps_around_them()
    {
        var got = FillDay(Std, new[] { I(600, 660) }, 300, dayStart: 480);

        Assert.Equal(new[] { I(480, 600), I(660, 720), I(780, 900) }, got);
    }

    [Fact]
    public void Nothing_before_notBefore()
    {
        var got = FillDay(Std, Array.Empty<Interval>(), 300, dayStart: 480, notBeforeMin: 700);

        Assert.Equal(new[] { I(700, 720), I(780, 1060) }, got);
    }

    [Fact]
    public void Gaps_shorter_than_min_chunk_are_skipped()
    {
        var got = FillDay(Std, new[] { I(490, 720) }, 300, dayStart: 480, minChunkMin: 20);

        Assert.Equal(new[] { I(780, 1080) }, got);
    }

    [Fact]
    public void Night_shift_axis_orders_bands_after_midnight_last()
    {
        var bands = new[] { I(0, 360), I(1320, 1440) };   // 22:00~24:00 뒤에 00:00~06:00

        var got = FillDay(bands, Array.Empty<Interval>(), 200, dayStart: 1320);

        Assert.Equal(new[] { I(1320, 1440), I(0, 80) }, got);
    }

    [Fact]
    public void Full_day_returns_everything_that_fits()
    {
        var got = FillDay(Std, Array.Empty<Interval>(), 2000, dayStart: 480);

        Assert.Equal(new[] { I(480, 720), I(780, 1080) }, got);
    }

    [Fact]
    public void Zero_or_negative_max_returns_empty()
    {
        Assert.Empty(FillDay(Std, Array.Empty<Interval>(), 0, dayStart: 480));
        Assert.Empty(FillDay(Std, Array.Empty<Interval>(), -5, dayStart: 480));
    }
}
