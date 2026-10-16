using AMES.Api.Endpoints;
using Xunit;

namespace AMES.Api.Tests;

public class PdaTestDatabaseGuardTests
{
    [Theory]
    [InlineData("Development", "AMES_DEV", false)]
    [InlineData("Production", "AMES_TEST", false)]
    [InlineData("Development", "AMES_TEST", true)]
    [InlineData("Development", "AMES_TEST_PDA", true)]
    [InlineData("Development", "AMES_TESTING", false)]
    public void AllowsReset_RequiresDevelopmentAndDedicatedTestDatabase(string environment, string database, bool expected)
        => Assert.Equal(expected, PdaTestDatabaseGuard.AllowsReset(environment, database));
}
