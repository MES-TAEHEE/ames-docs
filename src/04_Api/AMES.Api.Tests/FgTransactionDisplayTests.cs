using AMES.Pda.Services;
using Xunit;

namespace AMES.Api.Tests;

public class FgTransactionDisplayTests
{
    [Theory]
    [InlineData("OUT", "OUT", "OUTBOUND")]
    [InlineData("LOAD", "OUT", "OUTBOUND")]
    [InlineData(" load ", "OUT", "OUTBOUND")]
    [InlineData("IN", "IN", "IN")]
    [InlineData("RETURN", "RETURN", "RETURN")]
    [InlineData("MOVE", "MOVE", "MOVE")]
    [InlineData(null, "", "ALL")]
    public void Actual_and_legacy_outbound_share_the_same_filter_and_label(string? input, string direction, string label)
    {
        Assert.Equal(direction, FgTransactionDisplay.Direction(input));
        Assert.Equal(label, FgTransactionDisplay.Label(input));
    }

    [Theory]
    [InlineData("Loading", "Outbound")]
    [InlineData("LOAD", "Outbound")]
    [InlineData("Outbound", "Outbound")]
    [InlineData("Return", "Return")]
    public void Legacy_loading_status_is_displayed_as_outbound(string input, string expected)
        => Assert.Equal(expected, FgTransactionDisplay.Status(input));
}
