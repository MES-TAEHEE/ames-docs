using Microsoft.Data.SqlClient;

namespace AMES.Data.Connection;

/// <summary>
/// Creates SqlConnection instances against AMES_DEV.
/// In production this would wrap a connection pool + retry policy.
/// All clients (WinForms POP, MAUI PDA, Blazor Web) get connections through here.
/// </summary>
public sealed class AmesConnectionFactory
{
    private readonly string _connectionString;

    public AmesConnectionFactory(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("connection string must be provided", nameof(connectionString));
        _connectionString = connectionString;
    }

    /// <summary>
    /// Returns an open connection. Caller owns disposal.
    /// </summary>
    public SqlConnection OpenConnection()
    {
        var conn = new SqlConnection(_connectionString);
        conn.Open();
        return conn;
    }

    /// <summary>
    /// Returns an unopened connection (for async open or transactions).
    /// </summary>
    public SqlConnection CreateConnection() => new(_connectionString);
}
