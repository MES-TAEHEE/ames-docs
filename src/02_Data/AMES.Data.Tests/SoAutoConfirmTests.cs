using AMES.Data.Services;
using Xunit;

namespace AMES.Data.Tests;

/// <summary>PP-002 수주 자동 확정 플래그(SYS_Config PP_SO_AUTO_CONFIRM) 해석 규칙.</summary>
public class SoAutoConfirmTests
{
    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData(" True ")]
    [InlineData("1")]
    public void On_values_are_on(string value)
        => Assert.True(SoAutoConfirm.IsOn(value, isActive: true));

    [Theory]
    [InlineData("false")]
    [InlineData("0")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("yes")]
    public void Other_values_are_off(string? value)
        => Assert.False(SoAutoConfirm.IsOn(value, isActive: true));

    [Fact]
    public void Inactive_row_is_off_even_when_true()
        => Assert.False(SoAutoConfirm.IsOn("true", isActive: false));

    [Fact]
    public void Missing_row_is_off()
        => Assert.False(SoAutoConfirm.IsOn(null));

    [Fact]
    public void Stored_values_round_trip()
    {
        Assert.Equal("true",  SoAutoConfirm.ToStored(true));
        Assert.Equal("false", SoAutoConfirm.ToStored(false));
        Assert.Equal("PP_SO_AUTO_CONFIRM", SoAutoConfirm.ConfigKey);
    }
}
