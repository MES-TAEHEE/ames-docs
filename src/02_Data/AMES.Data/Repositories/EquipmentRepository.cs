using System.Data;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>Reads MD_Equipment + the most recent PR_EquipStatusLog row per equipment.</summary>
public sealed class EquipmentRepository
{
    private readonly AmesConnectionFactory _factory;
    public EquipmentRepository(AmesConnectionFactory f) => _factory = f;

    /// <summary>
    /// Returns the first equipment for a line plus its latest status snapshot.
    /// Good enough for INJ-02 single-equipment dashboard; multi-equip needs ListForLine.
    /// </summary>
    public EquipmentDto? GetPrimaryForLine(string lineId)
    {
        const string sql = """
            WITH latest AS (
              SELECT EquipID, MAX(EquipStatusLogID) AS MaxId
              FROM   dbo.PR_EquipStatusLog
              GROUP  BY EquipID
            )
            SELECT TOP 1 e.EquipID, e.EquipName, e.LineID,
                   COALESCE(es.Status, e.Status, 'OFFLINE') AS Status,
                   es.StartedAt
            FROM   dbo.MD_Equipment e
            LEFT  JOIN latest          l  ON l.EquipID = e.EquipID
            LEFT  JOIN dbo.PR_EquipStatusLog es ON es.EquipStatusLogID = l.MaxId
            WHERE  e.LineID = @L
              AND  ISNULL(e.ActiveFlag,1) = 1
            ORDER  BY e.EquipID;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@L", SqlDbType.VarChar, 20).Value = lineId;
        using var rdr = cmd.ExecuteReader();
        if (!rdr.Read()) return null;
        return new EquipmentDto
        {
            EquipId     = (string)rdr["EquipID"],
            EquipName   = rdr["EquipName"] as string ?? string.Empty,
            LineId      = rdr["LineID"]    as string,
            Status      = rdr["Status"]    as string,
            StatusSince = rdr["StartedAt"] as DateTime?,
        };
    }

    /// <summary>
    /// Writes a single new PR_EquipStatusLog row. Used by INJ-06 (downtime),
    /// INJ-08 (Andon stop), and the future PLC bridge.
    /// </summary>
    public void LogStatus(string equipId, string lineId, string status,
                          string reason, int? woId, string employeeNo)
    {
        const string sql = """
            INSERT INTO dbo.PR_EquipStatusLog
                (EquipID, LineID, Status, ReasonCode, WoID, StartedAt, CreatedBy, CreatedTS)
            VALUES (@E, @L, @S, @R, @W, SYSDATETIME(), @By, SYSDATETIME());

            UPDATE dbo.MD_Equipment SET Status = @S, ModifiedTS = SYSDATETIME() WHERE EquipID = @E;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@E",  SqlDbType.VarChar, 20).Value = equipId;
        cmd.Parameters.Add("@L",  SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@S",  SqlDbType.VarChar,  8).Value = status;
        cmd.Parameters.Add("@R",  SqlDbType.VarChar, 30).Value = reason;
        cmd.Parameters.Add("@W",  SqlDbType.Int        ).Value = (object?)woId ?? DBNull.Value;
        cmd.Parameters.Add("@By", SqlDbType.VarChar, 50).Value = employeeNo;
        cmd.ExecuteNonQuery();
    }
}
