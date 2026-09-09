using AMES.Data.Services;
using Xunit;

namespace AMES.InjAgent.Tests;

/// <summary>전기일(DAY_CUTOFF)·교대(WORK_SHIFT) 판정 순수 함수 — DB 불필요.</summary>
public class ProdCalendarTests
{
    static readonly IReadOnlyList<(string Code, string? Window)> ThreeShifts =
    [
        ("A", "0800-1600"),
        ("B", "1600-2400"),
        ("C", "0000-0800"),
    ];

    // ── 전기일 ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2026-09-10 06:59:59", "2026-09-09")]   // 전기 시각 전 → 전날
    [InlineData("2026-09-10 07:00:00", "2026-09-10")]   // 전기 시각 정각부터 당일
    [InlineData("2026-09-10 23:30:00", "2026-09-10")]
    [InlineData("2026-09-10 00:00:00", "2026-09-09")]
    public void ProdDateOf_rolls_over_at_cutoff(string ts, string expected)
        => Assert.Equal(DateTime.Parse(expected),
                        ProdCalendar.ProdDateOf(DateTime.Parse(ts), new TimeSpan(7, 0, 0)));

    [Fact]
    public void ProdDateOf_midnight_cutoff_is_calendar_date()
        => Assert.Equal(new DateTime(2026, 9, 10),
                        ProdCalendar.ProdDateOf(new DateTime(2026, 9, 10, 0, 0, 0), TimeSpan.Zero));

    [Theory]
    [InlineData("07:00", 7 * 60)]
    [InlineData("0700",  7 * 60)]
    [InlineData("7:30",  7 * 60 + 30)]
    [InlineData(" 06:00 ", 6 * 60)]
    public void TryParseCutoff_accepts_HHmm_and_HHMM(string attr, int expectedMin)
    {
        Assert.True(ProdCalendar.TryParseCutoff(attr, out var cutoff));
        Assert.Equal(expectedMin, (int)cutoff.TotalMinutes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("25:00")]
    [InlineData("07:60")]
    public void TryParseCutoff_rejects_bad_values(string? attr)
        => Assert.False(ProdCalendar.TryParseCutoff(attr, out _));

    // ── 교대 ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("08:00", "A")]
    [InlineData("15:59", "A")]
    [InlineData("16:00", "B")]
    [InlineData("23:59", "B")]   // 2400 = 자정까지
    [InlineData("00:00", "C")]
    [InlineData("07:59", "C")]
    public void ShiftOf_picks_window_containing_time(string time, string expected)
        => Assert.Equal(expected, ProdCalendar.ShiftOf(DateTime.Parse($"2026-09-10 {time}"), ThreeShifts));

    [Theory]
    [InlineData("23:00", "N")]
    [InlineData("03:59", "N")]
    [InlineData("04:00", null)]
    public void ShiftOf_handles_window_wrapping_midnight(string time, string? expected)
    {
        IReadOnlyList<(string, string?)> shifts = [("N", "2000-0400")];
        Assert.Equal(expected, ProdCalendar.ShiftOf(DateTime.Parse($"2026-09-10 {time}"), shifts));
    }

    [Fact]
    public void ShiftOf_returns_null_when_no_window_matches()
    {
        IReadOnlyList<(string, string?)> shifts = [("A", "0800-1600")];
        Assert.Null(ProdCalendar.ShiftOf(new DateTime(2026, 9, 10, 20, 0, 0), shifts));
    }

    [Fact]
    public void ShiftOf_skips_unparseable_windows_and_keeps_order()
    {
        IReadOnlyList<(string, string?)> shifts = [("X", null), ("Y", "bad"), ("A", "0800-1600"), ("Z", "0800-1600")];
        Assert.Equal("A", ProdCalendar.ShiftOf(new DateTime(2026, 9, 10, 9, 0, 0), shifts));
    }

    [Fact]
    public void ShiftOf_returns_null_for_empty_list()
        => Assert.Null(ProdCalendar.ShiftOf(DateTime.Now, Array.Empty<(string, string?)>()));

    // ── 통합 판정 ──────────────────────────────────────────────────────

    [Fact]
    public void Resolve_combines_cutoff_and_shift()
    {
        var (prodDate, shift) = ProdCalendar.Resolve(new DateTime(2026, 9, 10, 2, 30, 0), "07:00", ThreeShifts);
        Assert.Equal(new DateTime(2026, 9, 9), prodDate);
        Assert.Equal("C", shift);
    }

    [Fact]
    public void Resolve_falls_back_to_calendar_date_when_cutoff_missing()
    {
        var (prodDate, _) = ProdCalendar.Resolve(new DateTime(2026, 9, 10, 2, 30, 0), null, ThreeShifts);
        Assert.Equal(new DateTime(2026, 9, 10), prodDate);
    }
}
