using System.Data;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>Reads MD_Mold + writes PR_MoldChange (INJ-06 start/end).</summary>
public sealed class MoldRepository
{
    private readonly AmesConnectionFactory _factory;
    public MoldRepository(AmesConnectionFactory f) => _factory = f;

    public MoldDto? GetById(string moldId)
    {
        const string sql = """
            SELECT TOP 1 MoldID, MoldName, ISNULL(RatedShots,0) AS RatedShots,
                   ISNULL(CurrentShots,0) AS CurrentShots,
                   ISNULL(CavityCount,1)  AS CavityCount,
                   LastMaintDate, Status
            FROM   dbo.MD_Mold
            WHERE  MoldID = @M;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@M", SqlDbType.VarChar, 20).Value = moldId;
        using var rdr = cmd.ExecuteReader();
        if (!rdr.Read()) return null;
        return MapRow(rdr);
    }

    /// <summary>Picker list for INJ-06 replacement mold (Available + not Retired).</summary>
    public List<MoldDto> ListAvailable()
    {
        const string sql = """
            SELECT MoldID, MoldName, ISNULL(RatedShots,0) AS RatedShots,
                   ISNULL(CurrentShots,0) AS CurrentShots,
                   ISNULL(CavityCount,1)  AS CavityCount,
                   LastMaintDate, Status
            FROM   dbo.MD_Mold
            WHERE  ISNULL(Status,'Available') IN ('Available','Maintenance')
            ORDER  BY MoldID;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        using var rdr  = cmd.ExecuteReader();
        var list = new List<MoldDto>();
        while (rdr.Read()) list.Add(MapRow(rdr));
        return list;
    }

    /// <summary>Opens a mold-change row. Returns the new MoldChangeID.</summary>
    public int StartChange(string equipId, string lineId, string? oldMoldId,
                           string newMoldId, int oldFinalShots, string operatorId,
                           string employeeNo, string reason)
    {
        const string sql = """
            INSERT INTO dbo.PR_MoldChange
                (EquipID, LineID, OldMoldID, NewMoldID, OldMoldFinalShots,
                 NewMoldStartShots, Reason, StartedAt, ChangedBy, CreatedBy, CreatedTS)
            OUTPUT INSERTED.MoldChangeID
            VALUES (@E, @L, @Old, @New, @OF, 0, @R, SYSDATETIME(), @Op, @By, SYSDATETIME());
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@E",   SqlDbType.VarChar,  20 ).Value = equipId;
        cmd.Parameters.Add("@L",   SqlDbType.VarChar,  20 ).Value = lineId;
        cmd.Parameters.Add("@Old", SqlDbType.VarChar,  20 ).Value = (object?)oldMoldId ?? DBNull.Value;
        cmd.Parameters.Add("@New", SqlDbType.VarChar,  20 ).Value = newMoldId;
        cmd.Parameters.Add("@OF",  SqlDbType.Int          ).Value = oldFinalShots;
        cmd.Parameters.Add("@R",   SqlDbType.VarChar,  20 ).Value = reason;
        cmd.Parameters.Add("@Op",  SqlDbType.NVarChar, 450).Value = operatorId;
        cmd.Parameters.Add("@By",  SqlDbType.VarChar,  50 ).Value = employeeNo;
        return (int)cmd.ExecuteScalar()!;
    }

    /// <summary>
    /// Closes the change: stamps CompletedAt + DowntimeMin, resets new mold shots to 0,
    /// marks old mold as Maintenance and new mold as Mounted.
    /// </summary>
    public void CompleteChange(int moldChangeId, string? oldMoldId, string newMoldId)
    {
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            using (var cmd = new SqlCommand("""
                UPDATE dbo.PR_MoldChange
                SET    CompletedAt = SYSDATETIME(),
                       DowntimeMin = DATEDIFF(MINUTE, StartedAt, SYSDATETIME()),
                       ModifiedTS  = SYSDATETIME()
                WHERE  MoldChangeID = @ID;
                """, conn, tx))
            {
                cmd.Parameters.Add("@ID", SqlDbType.Int).Value = moldChangeId;
                cmd.ExecuteNonQuery();
            }
            using (var cmd = new SqlCommand("""
                UPDATE dbo.MD_Mold SET CurrentShots = 0, Status = 'Mounted',
                       ModifiedTS = SYSDATETIME() WHERE MoldID = @M;
                """, conn, tx))
            {
                cmd.Parameters.Add("@M", SqlDbType.VarChar, 20).Value = newMoldId;
                cmd.ExecuteNonQuery();
            }
            if (!string.IsNullOrEmpty(oldMoldId))
            {
                using var cmd = new SqlCommand("""
                    UPDATE dbo.MD_Mold SET Status = 'Maintenance',
                           ModifiedTS = SYSDATETIME() WHERE MoldID = @M;
                    """, conn, tx);
                cmd.Parameters.Add("@M", SqlDbType.VarChar, 20).Value = oldMoldId;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    private static MoldDto MapRow(IDataReader r) => new()
    {
        MoldId        = (string)r["MoldID"],
        MoldName      = r["MoldName"] as string ?? string.Empty,
        RatedShots    = Convert.ToInt32(r["RatedShots"]),
        CurrentShots  = Convert.ToInt32(r["CurrentShots"]),
        CavityCount   = Convert.ToInt32(r["CavityCount"]),
        LastMaintDate = r["LastMaintDate"] as DateTime?,
        Status        = r["Status"]        as string,
    };
}
