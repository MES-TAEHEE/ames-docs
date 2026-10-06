using AMES.Data.Aps.Domain;
using Xunit;

namespace AMES.Data.Tests.Aps;

public class StockRollTests
{
    [Fact]
    public void Assembly_part_rolls_like_original()   // 82301-P8140LBF: 16 + (59 − 57) = 18 (09-23), 18 + 10 − 11 = 17 (09-24)
    {
        var daily = new[] { ("2026-09-16", 20.0, 19.0, 0.0), ("2026-09-18", 39.0, 38.0, 0.0), ("2026-09-23", 10.0, 11.0, 0.0) };
        Assert.Equal((18.0, 8, true), StockRoll.Roll(16, daily, "2026-09-15", "2026-09-23"));
        Assert.Equal((17.0, 9, true), StockRoll.Roll(16, daily, "2026-09-15", "2026-09-24"));
    }

    [Fact]
    public void Defect_adds_with_its_sign()            // 82301-P8430LB5 09-23: 18 + 36 − 34 + 1 = 21
    {
        Assert.Equal((21.0, 1, true), StockRoll.Roll(18, new[] { ("2026-09-23", 36.0, 34.0, 1.0) }, "2026-09-23", "2026-09-24"));
        Assert.Equal(75, StockRoll.Roll(72, new[] { ("2026-09-23", 97.0, 93.0, -1.0) }, "2026-09-23", "2026-09-24").qty);
    }

    /// 원본(plan-all stockCounted=false 행)은 실사에 없는 품번을 굴리지 않고 0 으로 둔다.
    [Fact]
    public void Missing_baseline_is_zero_and_not_counted()
    {
        Assert.Equal((0.0, 9, false), StockRoll.Roll(null, new[] { ("2026-09-23", 5.0, 2.0, 0.0) }, "2026-09-15", "2026-09-24"));
    }
}
