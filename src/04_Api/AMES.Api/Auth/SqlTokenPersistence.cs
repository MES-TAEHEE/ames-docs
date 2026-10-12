using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Api.Auth;

public sealed class SqlTokenPersistence(AmesConnectionFactory factory)
{
    private static string Hash(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public void Save(string token, PopSessionDto session)
    {
        using var conn = factory.OpenConnection();
        using var cmd = new SqlCommand("""
            UPDATE dbo.PR_PopSession SET BearerHash=@Hash, SessionJson=@Json
            WHERE SessionID=@Id AND LoggedOutAt IS NULL AND ExpiresAt>SYSDATETIME();
            """, conn);
        cmd.Parameters.Add("@Hash", SqlDbType.Char, 64).Value = Hash(token);
        cmd.Parameters.Add("@Json", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(session);
        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = session.SessionId;
        if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("Login session is no longer active.");
    }

    public PopSessionDto? Load(string token)
    {
        using var conn = factory.OpenConnection();
        using var cmd = new SqlCommand("""
            SELECT SessionJson FROM dbo.PR_PopSession
            WHERE BearerHash=@Hash AND LoggedOutAt IS NULL AND ExpiresAt>SYSDATETIME();
            """, conn);
        cmd.Parameters.Add("@Hash", SqlDbType.Char, 64).Value = Hash(token);
        return cmd.ExecuteScalar() is string json ? JsonSerializer.Deserialize<PopSessionDto>(json) : null;
    }

    public void Revoke(string token)
    {
        using var conn = factory.OpenConnection();
        using var cmd = new SqlCommand("""
            UPDATE dbo.PR_PopSession SET BearerHash=NULL, SessionJson=NULL
            WHERE BearerHash=@Hash;
            """, conn);
        cmd.Parameters.Add("@Hash", SqlDbType.Char, 64).Value = Hash(token);
        cmd.ExecuteNonQuery();
    }
}
