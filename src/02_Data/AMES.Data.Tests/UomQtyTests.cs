using System.Globalization;
using AMES.Contracts.Formatting;
using Xunit;

namespace AMES.Data.Tests;

/// <summary>단위 소수 자릿수(MD_Uom.DecimalPrec) 기준 수량 서식.</summary>
public class UomQtyTests
{
    static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");
    static readonly UomPrecisionMap Map = new(new Dictionary<string, int> { ["EA"] = 0, ["KG"] = 3, ["LB"] = 2 });

    [Theory]
    [InlineData(1234, "EA", "1,234")]
    [InlineData(12.5, "KG", "12.500")]
    [InlineData(1234.5, "LB", "1,234.50")]
    [InlineData(0, "KG", "0.000")]
    [InlineData(-3, "LB", "-3.00")]
    public void Known_unit_uses_fixed_digits(decimal value, string unit, string expected)
        => Assert.Equal(expected, Map.Qty(value, unit, En));

    [Theory]
    [InlineData(0.5, "EA", "0.5")]
    [InlineData(1.23456, "KG", "1.23456")]
    [InlineData(2.1, "LB", "2.10")]
    public void Longer_fraction_is_not_hidden_by_rounding(decimal value, string unit, string expected)
        => Assert.Equal(expected, Map.Qty(value, unit, En));

    [Theory]
    [InlineData(1234, null, "1,234")]
    [InlineData(1.25, "", "1.25")]
    [InlineData(0.1234567, "MM", "0.123457")]
    public void Unknown_unit_shows_only_needed_digits(decimal value, string? unit, string expected)
        => Assert.Equal(expected, Map.Qty(value, unit, En));

    [Fact]
    public void Unit_lookup_ignores_case_and_spaces()
        => Assert.Equal(3, Map.Digits(" kg "));

    [Fact]
    public void QtyUnit_appends_trimmed_unit_or_omits_blank()
    {
        Assert.Equal("12.500 KG", Map.QtyUnit(12.5m, "KG ", En));
        Assert.Equal("7", Map.QtyUnit(7m, null, En));
    }

    [Fact]
    public void Digits_above_max_are_capped()
        => Assert.Equal("1.000000", UomQty.Format(1m, 9, En));
}
