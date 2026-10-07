using AMES.Data.Aps;
using AMES.Data.Aps.Contracts;
using Xunit;

namespace AMES.Data.Tests.Aps;

/// <summary>교대 목록 ↔ 주/야 호환 파생·복원(스펙 §1·§3). 파생 = 첫 교대 / 나머지 합, 복원 = Day→첫 교대·Night→둘째 교대(교대가 하나면 합산).</summary>
public class ApsShiftCompatTests
{
    static readonly ShiftHours[] ABC = { new("A", 8), new("B", 8), new("C", 6) };
    static readonly ShiftHours[] OnlyA = { new("A", 8) };

    [Fact]
    public void Derive_is_first_shift_and_sum_of_the_rest()
    {
        var (day, night) = ApsShiftCompat.Derive(new ShiftQty[] { new("A", 100), new("B", 40), new("C", 10) });
        Assert.Equal((100d, 50d), (day, night));
        Assert.Equal((0d, 0d), ApsShiftCompat.Derive(Array.Empty<ShiftQty>()));
    }

    [Fact]
    public void Restore_maps_day_to_first_and_night_to_second_shift()
    {
        Assert.Equal(new[] { ("A", 100d), ("B", 40d), ("C", 0d) }, ApsShiftCompat.Restore(100, 40, ABC).Select(s => (s.Code, s.Qty)).ToArray());
        Assert.Equal(new[] { ("A", 140d) }, ApsShiftCompat.Restore(100, 40, OnlyA).Select(s => (s.Code, s.Qty)).ToArray());   // 교대 하나면 합산
        Assert.Empty(ApsShiftCompat.Restore(100, 40, Array.Empty<ShiftHours>()));
    }

    [Fact]
    public void Align_keeps_known_codes_and_folds_unknown_codes_into_the_first_shift()
    {
        var stored = new ShiftQty[] { new("A", 50), new("N", 30) };   // 패턴이 바뀌어 N 은 더 이상 없다
        var aligned = ApsShiftCompat.Align(stored, 0, 0, ABC);
        Assert.Equal(new[] { ("A", 80d), ("B", 0d), ("C", 0d) }, aligned.Select(s => (s.Code, s.Qty)).ToArray());
        Assert.Equal(80d, aligned.Sum(s => s.Qty));
    }

    [Fact]
    public void Align_without_stored_list_restores_from_day_night()
    {
        var aligned = ApsShiftCompat.Align(null, 60, 20, ABC);
        Assert.Equal(new[] { ("A", 60d), ("B", 20d), ("C", 0d) }, aligned.Select(s => (s.Code, s.Qty)).ToArray());
    }
}
