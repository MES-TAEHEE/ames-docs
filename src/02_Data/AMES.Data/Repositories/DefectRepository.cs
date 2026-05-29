using System.Data;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// Reads MD_DefectCode (picker list on INJ-05) and writes PR_DefectDetail
/// per cycle. Defect-rate evaluation lives in the calling form / service.
/// </summary>
public sealed class DefectRepository
{
    private readonly AmesConnectionFactory _factory;
    public DefectRepository(AmesConnectionFactory f) => _factory = f;

    /// <summary>The 6 (or however many) defect codes for the INJ process.</summary>
    public List<DefectCodeDto> ListForProcess(string processCode)
    {
        const string sql = """
            SELECT DefectCode, DefectName, DefectNameEn, ProcessCode,
                   SeverityLevel, DefaultCauseCode
            FROM   dbo.MD_DefectCode
            WHERE  ProcessCode = @P
              AND  ISNULL(Status,'Active') = 'Active'
            ORDER  BY DefectCode;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@P", SqlDbType.VarChar, 10).Value = processCode;
        using var rdr = cmd.ExecuteReader();
        var list = new List<DefectCodeDto>();
        while (rdr.Read())
            list.Add(new DefectCodeDto
            {
                DefectCode       = (string)rdr["DefectCode"],
                DefectName       = rdr["DefectName"]      as string ?? string.Empty,
                DefectNameEn     = rdr["DefectNameEn"]    as string,
                ProcessCode      = rdr["ProcessCode"]     as string,
                SeverityLevel    = rdr["SeverityLevel"]   as string,
                DefaultCauseCode = rdr["DefaultCauseCode"] as string,
            });
        return list;
    }

    /// <summary>Inserts one PR_DefectDetail row.</summary>
    public int RecordDefect(
        int     resultId,
        int     woId,
        int?    lotId,
        string  defectCode,
        int     qty,
        string  operatorId,
        string  employeeNo,
        string? note = null)
    {
        const string sql = """
            INSERT INTO dbo.PR_DefectDetail
                (ResultID, WoID, LotID, ProcessCode, DefectCode, Qty,
                 ReasonNote, DetectedAt, RegisteredBy, CreatedBy, CreatedTS)
            OUTPUT INSERTED.DefectID
            VALUES
                (@R, @W, @L, 'INJ', @C, @Q, @N, SYSDATETIME(), @Op, @By, SYSDATETIME());
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@R",  SqlDbType.Int).Value = resultId;
        cmd.Parameters.Add("@W",  SqlDbType.Int).Value = woId;
        cmd.Parameters.Add("@L",  SqlDbType.Int).Value = (object?)lotId ?? DBNull.Value;
        cmd.Parameters.Add("@C",  SqlDbType.VarChar,  16 ).Value = defectCode;
        cmd.Parameters.Add("@Q",  SqlDbType.Int           ).Value = qty;
        cmd.Parameters.Add("@N",  SqlDbType.NVarChar, 500 ).Value = (object?)note ?? DBNull.Value;
        cmd.Parameters.Add("@Op", SqlDbType.NVarChar, 450 ).Value = operatorId;
        cmd.Parameters.Add("@By", SqlDbType.VarChar,  50  ).Value = employeeNo;
        return (int)cmd.ExecuteScalar()!;
    }

    /// <summary>
    /// (Good, Defect) totals for one WO, summed across the WO's whole lifetime.
    /// Used to compute live defect rate on INJ-05.
    /// </summary>
    public (int Good, int Defect) GetWoTotals(int woId)
    {
        const string sql = """
            SELECT
              (SELECT ISNULL(SUM(GoodQty),0) FROM dbo.PR_ProductionResult WHERE WoID = @W) AS G,
              (SELECT ISNULL(SUM(Qty),    0) FROM dbo.PR_DefectDetail    WHERE WoID = @W) AS D;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@W", SqlDbType.Int).Value = woId;
        using var rdr = cmd.ExecuteReader();
        if (!rdr.Read()) return (0, 0);
        return (Convert.ToInt32(rdr["G"]), Convert.ToInt32(rdr["D"]));
    }
}
