using AMES.Contracts.Formatting;
using Xunit;

namespace AMES.Data.Tests;

/// <summary>일시 입력칸 직접 입력 해석 — 표시 형식·ISO·숫자만·시각만.</summary>
public class DateTimeInputTests
{
    static readonly DateTime Today = new(2026, 11, 5);

    [Theory]
    [InlineData("2026-11-04 14:30", "yyyy-MM-dd HH:mm", "2026-11-04 14:30")]
    [InlineData("2026-11-04 9:05", "yyyy-MM-dd HH:mm", "2026-11-04 09:05")]
    [InlineData("11/04/2026 14:30", "MM/dd/yyyy HH:mm", "2026-11-04 14:30")]
    [InlineData("04/11/2026 14:30", "dd/MM/yyyy HH:mm", "2026-11-04 14:30")]
    [InlineData("2026-11-04 14:30", "MM/dd/yyyy HH:mm", "2026-11-04 14:30")]
    [InlineData("20261104 1430", null, "2026-11-04 14:30")]
    [InlineData("202611041430", null, "2026-11-04 14:30")]
    [InlineData("  2026-11-04   14:30 ", null, "2026-11-04 14:30")]
    [InlineData("2026-11-04 14:30:45", null, "2026-11-04 14:30")]
    [InlineData("2026-11-04", null, "2026-11-04 00:00")]
    [InlineData("14:30", null, "2026-11-05 14:30")]
    [InlineData("1430", null, "2026-11-05 14:30")]
    [InlineData("7:05", null, "2026-11-05 07:05")]
    [InlineData("705", null, "2026-11-05 07:05")]
    public void Accepts_common_forms(string text, string? fmt, string expected)
        => Assert.Equal(DateTime.Parse(expected), DateTimeInput.Parse(text, Today, fmt));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("2026-13-01 10:00")]
    [InlineData("25:00")]
    [InlineData("2026-11-04 24:10")]
    public void Rejects_invalid(string? text)
        => Assert.Null(DateTimeInput.Parse(text, Today, "yyyy-MM-dd HH:mm"));
}
