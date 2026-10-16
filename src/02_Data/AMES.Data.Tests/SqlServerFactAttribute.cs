using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

// Opt-in instance for disposable test databases; never use application DB settings.
public sealed class SqlServerFactAttribute : FactAttribute
{
    public const string ConnectionVariable = "AMES_TEST_SQLSERVER";

    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionVariable)))
            Skip = $"Set {ConnectionVariable} to a SQL Server test instance with CREATE DATABASE permission.";
    }

    internal static SqlConnectionStringBuilder Settings() => new(
        Environment.GetEnvironmentVariable(ConnectionVariable)
        ?? throw new InvalidOperationException($"{ConnectionVariable} is required."))
    {
        InitialCatalog = "master"
    };
}
