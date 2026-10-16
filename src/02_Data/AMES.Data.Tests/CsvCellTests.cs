using AMES.Contracts.Formatting;
using Xunit;

namespace AMES.Data.Tests;

/// <summary>CSV 내보내기 셀 — 엑셀 수식 주입 방지와 따옴표 규칙.</summary>
public class CsvCellTests
{
    [Theory]
    [InlineData("=HYPERLINK(\"http://x\")", "\"'=HYPERLINK(\"\"http://x\"\")\"")]
    [InlineData("+cmd", "'+cmd")]
    [InlineData("-2+3", "'-2+3")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\t=1", "'\t=1")]
    public void Formula_like_values_become_text(string input, string expected)
        => Assert.Equal(expected, CsvCell.Escape(input));

    [Theory]
    [InlineData("-5", "-5")]
    [InlineData("+1.5", "+1.5")]
    [InlineData("-1,234.5", "\"-1,234.5\"")]
    public void Plain_numbers_stay_numbers(string input, string expected)
        => Assert.Equal(expected, CsvCell.Escape(input));

    [Theory]
    [InlineData("ABC-123", "ABC-123")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("line1\nline2", "\"line1\nline2\"")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Ordinary_values_follow_quote_rules(string? input, string expected)
        => Assert.Equal(expected, CsvCell.Escape(input));
}
