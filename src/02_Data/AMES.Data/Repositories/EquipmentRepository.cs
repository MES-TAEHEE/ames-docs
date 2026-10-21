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
        const string sql = "dbo.SP_POP_EQUIP_GetPrimaryForLine";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
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
        const string sql = "dbo.SP_POP_EQUIP_LogStatus";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@E",  SqlDbType.VarChar, 20).Value = equipId;
        cmd.Parameters.Add("@L",  SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@S",  SqlDbType.VarChar,  8).Value = status;
        cmd.Parameters.Add("@R",  SqlDbType.VarChar, 30).Value = reason;
        cmd.Parameters.Add("@W",  SqlDbType.Int        ).Value = (object?)woId ?? DBNull.Value;
        cmd.Parameters.Add("@By", SqlDbType.VarChar, 20).Value = employeeNo;
        cmd.ExecuteNonQuery();
    }
}
