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
        try { conn.Open(); }
        catch { DbHealth.RecordFailure(); conn.Dispose(); throw; }
        DbHealth.RecordSuccess();
        return conn;
    }

    /// <summary>
    /// Returns an unopened connection (for async open or transactions).
    /// </summary>
    public SqlConnection CreateConnection() => new(_connectionString);

    /// <summary>
    /// One-line description of the database this factory points at, for startup logs.
    /// Never contains the password. Probes the server once (short timeout) and never throws.
    /// </summary>
    public string Describe()
    {
        var b = new SqlConnectionStringBuilder(_connectionString);
        var target = $"Server={b.DataSource}, Database={b.InitialCatalog}, User={b.UserID}";
        try
        {
            b.ConnectTimeout = Math.Min(b.ConnectTimeout, 10);
            using var conn = new SqlConnection(b.ConnectionString);
            conn.Open();
            using var cmd = new SqlCommand("SELECT @@SERVERNAME, DB_NAME()", conn);
            using var r = cmd.ExecuteReader();
            r.Read();
            return $"{target} → connected (@@SERVERNAME={r.GetString(0)}, DB_NAME={r.GetString(1)})";
        }
        catch (Exception ex)
        {
            return $"{target} → CONNECT FAILED: {ex.Message}";
        }
    }
}
