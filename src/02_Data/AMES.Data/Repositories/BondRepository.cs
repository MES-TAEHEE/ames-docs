using System.Data;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// IMG bond machine recipe (PR_BondSetup) + per-cycle PLC samples (PR_BondCycleLog)
/// + change audit trail (PR_BondSetupAudit).
/// </summary>
public sealed class BondRepository
{
    private readonly AmesConnectionFactory _factory;
    public BondRepository(AmesConnectionFactory f) => _factory = f;

    /// <summary>Latest APPLIED setup for a line, if any.</summary>
    public BondSetupDto? GetActiveForLine(string lineId)
    {
        const string sql = """
            SELECT TOP 1 BondSetupID, WoID, LineID, RecipeID,
                   ISNULL(PressureSp,0) AS PressureSp,
                   ISNULL(TempSp,0)     AS TempSp,
                   ISNULL(HoldSecSp,0)  AS HoldSecSp,
                   TensionSp, LoadedAt, Status
            FROM   dbo.PR_BondSetup
            WHERE  LineID = @L
            ORDER  BY LoadedAt DESC, BondSetupID DESC;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@L", SqlDbType.VarChar, 20).Value = lineId;
        using var rdr = cmd.ExecuteReader();
        if (!rdr.Read()) return null;
        return new BondSetupDto
        {
            BondSetupId = (int)rdr["BondSetupID"],
            LineId      = (string)rdr["LineID"],
            WoId        = rdr["WoID"]     as int?,
            RecipeId    = rdr["RecipeID"] as string,
            PressureSp  = (decimal)rdr["PressureSp"],
            TempSp      = (decimal)rdr["TempSp"],
            HoldSecSp   = Convert.ToInt32(rdr["HoldSecSp"]),
            TensionSp   = rdr["TensionSp"] as decimal?,
            LoadedAt    = rdr["LoadedAt"]  as DateTime?,
            Status      = rdr["Status"]    as string,
        };
    }

    /// <summary>Inserts a new setup row and returns its id.</summary>
    public int LoadRecipe(string lineId, int? woId, string recipeId,
                          decimal pressure, decimal temp, int holdSec, decimal? tension,
                          string operatorId, string employeeNo)
    {
        const string sql = """
            INSERT INTO dbo.PR_BondSetup
                (WoID, LineID, RecipeID, PressureSp, TempSp, HoldSecSp, TensionSp,
                 LoadedAt, LoadedBy, Status, CreatedBy, CreatedTS)
            OUTPUT INSERTED.BondSetupID
            VALUES (@W, @L, @R, @P, @T, @H, @Tn, SYSDATETIME(), @Op, 'APPLIED', @By, SYSDATETIME());
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@W",  SqlDbType.Int).Value = (object?)woId ?? DBNull.Value;
        cmd.Parameters.Add("@L",  SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@R",  SqlDbType.VarChar, 20).Value = recipeId;
        cmd.Parameters.Add("@P",  SqlDbType.Decimal).Value = pressure;
        cmd.Parameters.Add("@T",  SqlDbType.Decimal).Value = temp;
        cmd.Parameters.Add("@H",  SqlDbType.Int).Value     = holdSec;
        cmd.Parameters.Add("@Tn", SqlDbType.Decimal).Value = (object?)tension ?? DBNull.Value;
        cmd.Parameters.Add("@Op", SqlDbType.NVarChar, 450).Value = operatorId;
        cmd.Parameters.Add("@By", SqlDbType.VarChar, 50).Value   = employeeNo;
        return (int)cmd.ExecuteScalar()!;
    }

    /// <summary>Per-cycle PLC sample log written by IMG-03 Confirm.</summary>
    public void LogCycle(int resultId, int bondSetupId,
                         decimal pressureAvg, decimal tempAvg, int holdActualSec,
                         decimal? tensionAvg, bool withinSpec, string employeeNo)
    {
        const string sql = """
            INSERT INTO dbo.PR_BondCycleLog
                (ResultID, BondSetupID, PressureAvg, TempAvg, HoldActualSec,
                 TensionAvg, WithinSpec, SampledAt, CreatedBy, CreatedTS)
            VALUES (@R, @B, @P, @T, @H, @Tn, @W, SYSDATETIME(), @By, SYSDATETIME());
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@R",  SqlDbType.Int).Value = resultId;
        cmd.Parameters.Add("@B",  SqlDbType.Int).Value = bondSetupId;
        cmd.Parameters.Add("@P",  SqlDbType.Decimal).Value = pressureAvg;
        cmd.Parameters.Add("@T",  SqlDbType.Decimal).Value = tempAvg;
        cmd.Parameters.Add("@H",  SqlDbType.Int).Value     = holdActualSec;
        cmd.Parameters.Add("@Tn", SqlDbType.Decimal).Value = (object?)tensionAvg ?? DBNull.Value;
        cmd.Parameters.Add("@W",  SqlDbType.Bit).Value     = withinSpec;
        cmd.Parameters.Add("@By", SqlDbType.VarChar, 50).Value = employeeNo;
        cmd.ExecuteNonQuery();
    }
}
