using AMES.Data.Connection;

namespace AMES.Api.Endpoints;

internal static class PdaTestDatabaseGuard
{
    // Development mode alone is not a safety boundary: local API instances may use AMES_DEV.
    public static bool AllowsReset(WebApplication app, AmesConnectionFactory factory)
    {
        using var connection = factory.CreateConnection();
        return AllowsReset(app.Environment.EnvironmentName, connection.Database);
    }

    internal static bool AllowsReset(string environmentName, string database) =>
        environmentName.Equals("Development", StringComparison.OrdinalIgnoreCase)
        && (database.Equals("AMES_TEST", StringComparison.OrdinalIgnoreCase)
            || database.StartsWith("AMES_TEST_", StringComparison.OrdinalIgnoreCase));
}
