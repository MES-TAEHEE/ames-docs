using System.Data;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// Read-only aggregates for INJ-02 (per line) and INJ-07 (all lines).
/// Inline SELECTs for now — will become SP_* once DBA finalises the views.
/// </summary>
public sealed class DashboardRepository
{
    private readonly AmesConnectionFactory _factory;
    public DashboardRepository(AmesConnectionFactory f) => _factory = f;

    /// <summary>Per-line summary rows for the supervisor view (INJ-07 / IMG-07 / ...).</summary>
    /// <param name="processCode">INJ / IMG / PNT / QC — matches LINE-{code}-* IDs.</param>
    public List<LineSummaryDto> GetLinesForProcess(string processCode)
    {
        var sql = $"""
            ;WITH today AS (
              SELECT LineID, SUM(GoodQty) AS Good
              FROM   dbo.PR_ProductionResult
              WHERE  CAST(EntryAt AS DATE) = CAST(SYSDATETIME() AS DATE)
              GROUP  BY LineID
            ),
            defs AS (
              SELECT r.LineID, SUM(d.Qty) AS Bad
              FROM   dbo.PR_DefectDetail   d
              JOIN   dbo.PR_ProductionResult r ON r.ResultID = d.ResultID
              WHERE  CAST(d.DetectedAt AS DATE) = CAST(SYSDATETIME() AS DATE)
              GROUP  BY r.LineID
            ),
            eq AS (
              SELECT e.LineID, MAX(es.EquipStatusLogID) AS MaxId
              FROM   dbo.MD_Equipment           e
              LEFT JOIN dbo.PR_EquipStatusLog   es ON es.LineID = e.LineID
              WHERE  ISNULL(e.ActiveFlag,1) = 1
              GROUP  BY e.LineID
            )
            SELECT l.LineID, l.LineName,
                   COALESCE(es.Status,'OFFLINE') AS EquipStatus,
                   ISNULL(t.Good,0)              AS TodayGood,
                   ISNULL(df.Bad,0)              AS TodayDefect
            FROM   dbo.MD_Line l
            LEFT JOIN today                  t  ON t.LineID = l.LineID
            LEFT JOIN defs                   df ON df.LineID = l.LineID
            LEFT JOIN eq                     ej ON ej.LineID = l.LineID
            LEFT JOIN dbo.PR_EquipStatusLog  es ON es.EquipStatusLogID = ej.MaxId
            WHERE  l.LineID LIKE @Prefix
            ORDER  BY l.LineID;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Prefix", SqlDbType.VarChar, 30).Value = $"LINE-{processCode}-%";
        using var rdr  = cmd.ExecuteReader();
        var list = new List<LineSummaryDto>();
        while (rdr.Read())
        {
            list.Add(new LineSummaryDto
            {
                LineId      = (string)rdr["LineID"],
                LineName    = rdr["LineName"] as string ?? string.Empty,
                EquipStatus = rdr["EquipStatus"] as string,
                TodayGood   = Convert.ToInt32(rdr["TodayGood"]),
                TodayDefect = Convert.ToInt32(rdr["TodayDefect"]),
            });
        }
        return list;
    }
}
