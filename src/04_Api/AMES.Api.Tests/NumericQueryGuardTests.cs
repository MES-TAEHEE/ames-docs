using AMES.Api.Auth;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace AMES.Api.Tests;

public class NumericQueryGuardTests
{
    [Theory]
    [InlineData("topN", "-1")]
    [InlineData("topN", "1001")]
    [InlineData("daysBack", "99999")]
    [InlineData("quarter", "5")]
    [InlineData("year", "1900")]
    public void Rejects_out_of_range_values(string name, string value)
        => Assert.Equal(name, NumericQueryGuard.InvalidParameter(new QueryCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues> { [name] = value })));

    [Fact]
    public void Accepts_valid_values()
        => Assert.Null(NumericQueryGuard.InvalidParameter(new QueryCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues> { ["topN"] = "100", ["daysBack"] = "0" })));
}
