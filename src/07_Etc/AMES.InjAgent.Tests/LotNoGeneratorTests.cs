using AMES.Data.Services;
using Xunit;

namespace AMES.InjAgent.Tests;

/// <summary>인코딩 순수 함수 테스트 — DB 불필요, 항상 실행된다.</summary>
public class LotNoGeneratorTests
{
    [Theory]
    [InlineData(2026, 'A')]
    [InlineData(2027, 'B')]
    [InlineData(2051, 'Z')]
    [InlineData(2052, 'A')]   // 26년 주기 순환
    [InlineData(2077, 'Z')]
    [InlineData(2000, 'A')]   // 2000-2026=-26 → mod 결과 0 → 'A' (음수 mod 분기 커버)
    public void EncodeYear_cycles_every_26_years(int year, char expected)
        => Assert.Equal(expected, LotNoGenerator.EncodeYear(year));

    [Theory]
    [InlineData(1, '1')]
    [InlineData(9, '9')]
    [InlineData(10, 'A')]
    [InlineData(12, 'C')]
    public void EncodeMonth_digits_then_ABC(int month, char expected)
        => Assert.Equal(expected, LotNoGenerator.EncodeMonth(month));

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void EncodeMonth_out_of_range_throws(int month)
        => Assert.Throws<ArgumentOutOfRangeException>(() => LotNoGenerator.EncodeMonth(month));

    [Theory]
    [InlineData(1, '1')]
    [InlineData(9, '9')]
    [InlineData(10, 'A')]
    [InlineData(31, 'V')]
    public void EncodeDay_digits_then_A_to_V(int day, char expected)
        => Assert.Equal(expected, LotNoGenerator.EncodeDay(day));

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    public void EncodeDay_out_of_range_throws(int day)
        => Assert.Throws<ArgumentOutOfRangeException>(() => LotNoGenerator.EncodeDay(day));

    [Fact]
    public void BuildHeader_composes_5_chars()
        => Assert.Equal("A91I1", LotNoGenerator.BuildHeader(new DateTime(2026, 9, 1), "I1"));

    [Fact]
    public void BuildHeader_rejects_non_2char_prefix()
        => Assert.Throws<ArgumentException>(() => LotNoGenerator.BuildHeader(new DateTime(2026, 9, 1), "I"));

    [Fact]
    public void BuildHeader_rejects_too_long_prefix()
        => Assert.Throws<ArgumentException>(() => LotNoGenerator.BuildHeader(new DateTime(2026, 9, 1), "I12"));

    [Fact]
    public void BuildHeader_rejects_null_prefix()
        => Assert.Throws<ArgumentNullException>(() => LotNoGenerator.BuildHeader(new DateTime(2026, 9, 1), null!));
}
