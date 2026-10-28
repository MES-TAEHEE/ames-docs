using AMES.Data.Services;
using Xunit;
using static AMES.Data.Services.MoldUsageRules;

namespace AMES.Data.Tests;

// 10-28 MNT 결과서 MNT004-TC-009·MNT009-TC-010 — 소진율 표시는 소수 2자리에서 버려 등급과 어긋나지 않는다
public class MoldUsageRulesTests
{
    [Theory]
    [InlineData(6999, "69.99", Ok)]
    [InlineData(7000, "70.00", Warning)]
    [InlineData(8999, "89.99", Warning)]
    [InlineData(9000, "90.00", Critical)]
    public void Usage_display_and_grade_agree_at_the_boundaries(int current, string shown, string grade)
    {
        var u = Usage(current, 10000)!.Value;
        Assert.Equal(shown, Shown(u).ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(grade, Grade(u, null));
    }

    [Fact]
    public void Shown_value_is_truncated_not_rounded()
    {
        var u = Usage(69996, 100000)!.Value;      // 69.996%
        Assert.Equal(69.99m, Shown(u));
        Assert.Equal(Ok, Grade(u, null));
    }

    [Fact]
    public void Without_rated_shots_the_stored_level_is_used()
    {
        Assert.Null(Usage(5000, null));
        Assert.Null(Usage(5000, 0));
        Assert.Equal(Critical, Grade(null, "CRITICAL"));
        Assert.Null(Grade(null, "BOGUS"));
        Assert.Null(Grade(null, null));
    }
}
