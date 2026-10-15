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
            SELECT S.SessionJson,
                CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.AspNetUserRoles R
                    WHERE R.UserId=P.UserID AND R.RoleId=N'ROLE-SYSADMIN') THEN 1 ELSE 0 END) AS IsAdmin
            FROM dbo.PR_PopSession S
            LEFT JOIN dbo.SYS_UserProfile P ON P.EmployeeNo=S.OperatorID
            LEFT JOIN dbo.AspNetUsers U ON U.Id=P.UserID
            LEFT JOIN dbo.MD_Worker W ON W.EmployeeNo=S.OperatorID
            WHERE S.BearerHash=@Hash AND S.LoggedOutAt IS NULL AND S.ExpiresAt>SYSDATETIME()
              AND ((JSON_VALUE(S.SessionJson,'$.IsWorker')='true' AND W.ActiveFlag=1 AND P.UserID IS NULL)
                OR (JSON_VALUE(S.SessionJson,'$.IsWorker')='false' AND U.Id IS NOT NULL
                    AND UPPER(P.AccountStatus)='ACTIVE'
                    AND (U.LockoutEnd IS NULL OR U.LockoutEnd<=SYSUTCDATETIME())));
            """, conn);
        cmd.Parameters.Add("@Hash", SqlDbType.Char, 64).Value = Hash(token);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read() || reader.IsDBNull(0)) return null;
        var s = JsonSerializer.Deserialize<PopSessionDto>(reader.GetString(0));
        if (s is null) return null;
        return new PopSessionDto
        {
            SessionId=s.SessionId,OperatorId=s.OperatorId,EmployeeNo=s.EmployeeNo,EmployeeName=s.EmployeeName,
            TerminalId=s.TerminalId,LineId=s.LineId,ShiftCode=s.ShiftCode,AuthMethod=s.AuthMethod,
            StartedAt=s.StartedAt,ExpiresAt=s.ExpiresAt,IsWorker=s.IsWorker,HasPin=s.HasPin,
            IsAdmin=reader.GetBoolean(1)
        };
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
