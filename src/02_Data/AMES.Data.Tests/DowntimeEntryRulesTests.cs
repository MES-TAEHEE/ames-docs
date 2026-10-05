using AMES.Data.Services;
using Xunit;
using static AMES.Data.Services.DowntimeEntryRules;

namespace AMES.Data.Tests;

/// <summary>PP-DTL 비가동 등록·수정 규칙: 안돈(POP) 행 판별, 시각 검증, 같은 라인 구간 겹침.</summary>
public class DowntimeEntryRulesTests
{
    static readonly DateTime Now = new(2026, 10, 5, 10, 0, 0);

    [Fact] public void Row_with_andon_id_is_pop()    => Assert.True(IsPopRow(12));
    [Fact] public void Row_without_andon_id_is_web() => Assert.False(IsPopRow(null));

    [Fact]
    public void Open_entry_started_in_past_is_valid()
        => Assert.Equal(Issue.None, Validate(Now.AddHours(-1), null, Now));

    [Fact]
    public void End_equal_to_start_is_rejected()
        => Assert.Equal(Issue.EndBeforeStart, Validate(Now.AddHours(-1), Now.AddHours(-1), Now));

    [Fact]
    public void Start_after_now_is_rejected()
        => Assert.Equal(Issue.StartInFuture, Validate(Now.AddMinutes(5), null, Now));

    [Fact]
    public void End_after_now_is_rejected()
        => Assert.Equal(Issue.EndInFuture, Validate(Now.AddHours(-1), Now.AddMinutes(5), Now));

    [Fact]
    public void Touching_ranges_do_not_overlap()
        => Assert.False(Overlaps(Now.AddHours(-2), Now.AddHours(-1), Now.AddHours(-1), Now));

    [Fact]
    public void Open_range_overlaps_anything_after_its_start()
        => Assert.True(Overlaps(Now.AddHours(-2), null, Now.AddMinutes(-10), Now.AddMinutes(-5)));

    [Fact]
    public void Range_before_open_range_does_not_overlap()
        => Assert.False(Overlaps(Now.AddHours(-2), null, Now.AddHours(-4), Now.AddHours(-3)));
}
