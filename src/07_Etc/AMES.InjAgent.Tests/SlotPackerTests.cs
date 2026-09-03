using AMES.Data.Scheduling;
using Xunit;
using static AMES.Data.Scheduling.SlotPacker;

namespace AMES.InjAgent.Tests;

/// <summary>
/// PP-003 자동 배치 — 가동 밴드·기존 슬롯·소요분으로 (시작, 끝) 을 고른다. 순수 함수, DB 없음.
/// 규칙은 PP-LSB 보드와 같다: 가동 밴드 안 · 기존 WO/PM 과 겹치지 않음 · 축은 첫 교대 시작 기준.
/// </summary>
public class SlotPackerTests
{
    static Interval I(int s, int e) => new(s, e);

    [Fact]
    public void Empty_day_places_at_first_operating_band_start()
    {
        var got = Place(new[] { I(480, 720), I(780, 1080) }, Array.Empty<Interval>(), 60, dayStart: 480);

        Assert.Equal(I(480, 540), got);
    }

    [Fact]
    public void Appends_after_notBefore_not_in_earlier_free_gap()
    {
        var occupied = new[] { I(600, 720) };

        var got = Place(new[] { I(480, 720), I(780, 1080) }, occupied, 60, dayStart: 480, notBeforeMin: 720);

        Assert.Equal(I(780, 840), got);
    }

    [Fact]
    public void Skips_gap_too_small_before_pm_hole()
    {
        var pm = new[] { I(510, 570) };

        var got = Place(new[] { I(480, 720) }, pm, 60, dayStart: 480);

        Assert.Equal(I(570, 630), got);
    }

    [Fact]
    public void Moves_to_next_band_when_current_lacks_room()
    {
        var occupied = new[] { I(480, 700) };

        var got = Place(new[] { I(480, 720), I(780, 1080) }, occupied, 60, dayStart: 480);

        Assert.Equal(I(780, 840), got);
    }

    [Fact]
    public void Returns_null_when_no_band_has_room()
    {
        var occupied = new[] { I(480, 700), I(780, 1050) };

        var got = Place(new[] { I(480, 720), I(780, 1080) }, occupied, 60, dayStart: 480);

        Assert.Null(got);
    }

    [Fact]
    public void Night_shift_band_after_midnight_follows_evening_band_on_axis()
    {
        // 야간 교대 22:00→06:00: 밴드는 [1320,1440] + [0,360], 축 원점 22:00
        var occupied = new[] { I(1320, 1440) };

        var got = Place(new[] { I(0, 360), I(1320, 1440) }, occupied, 90, dayStart: 1320);

        Assert.Equal(I(0, 90), got);
    }

    [Fact]
    public void Slot_never_crosses_midnight_even_when_axis_is_continuous()
    {
        // 22:00~24:00 밴드에 남은 30분, 00:00~06:00 밴드는 비어 있음 → 자정을 넘기지 않고 다음 밴드로
        var occupied = new[] { I(1320, 1410) };

        var got = Place(new[] { I(0, 360), I(1320, 1440) }, occupied, 60, dayStart: 1320);

        Assert.Equal(I(0, 60), got);
    }

    [Fact]
    public void Zero_or_negative_duration_returns_null()
    {
        Assert.Null(Place(new[] { I(480, 720) }, Array.Empty<Interval>(), 0, dayStart: 480));
        Assert.Null(Place(new[] { I(480, 720) }, Array.Empty<Interval>(), -5, dayStart: 480));
    }
}
