using System.Data;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// Writes PR_ProductionResult (INJ-04 each cycle) + tbl_Lot when a cycle
/// produces output. Also serves hourly + daily roll-ups for INJ-02/07.
/// </summary>
public sealed class ProductionRepository
{
    private readonly AmesConnectionFactory _factory;
    public ProductionRepository(AmesConnectionFactory f) => _factory = f;

    /// <summary>
    /// Records one production cycle. Increments WO CompletedQty + mold shot count + creates a lot row.
    /// Returns the new ResultID + the post-update completed qty.
    /// </summary>
    public (int ResultId, int LotId, decimal NewCompletedQty) RecordCycle(
        int     woId,
        string  itemNo,
        string  lineId,
        int     goodQty,
        int     cycleSec,
        string? moldId,
        string  operatorId,
        int?    sessionId,
        string  employeeNo,
        bool    defectFlag)
    {
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            // 1) tbl_Lot row first (parent for both production + future defect rows)
            int lotId;
            using (var cmd = new SqlCommand("""
                INSERT INTO dbo.tbl_Lot
                    (LotCode, ItemNo, WoID, LineID, ProcessCode, BatchSize, RemainingQty,
                     ProducedAt, Status, QualityFlag, CreatedBy, CreatedTS)
                OUTPUT INSERTED.LotID
                VALUES
                    (@LotCode, @ItemNo, @WoID, @LineID, 'INJ', @Qty, @Qty,
                     SYSDATETIME(), 'OPEN', 'PENDING', @By, SYSDATETIME());
                """, conn, tx))
            {
                var lotCode = $"L{DateTime.Now:yyMMddHHmmssfff}-{lineId}";
                cmd.Parameters.Add("@LotCode", SqlDbType.VarChar, 40).Value = lotCode;
                cmd.Parameters.Add("@ItemNo",  SqlDbType.VarChar, 20).Value = itemNo;
                cmd.Parameters.Add("@WoID",    SqlDbType.Int       ).Value = woId;
                cmd.Parameters.Add("@LineID",  SqlDbType.VarChar, 20).Value = lineId;
                cmd.Parameters.Add("@Qty",     SqlDbType.Decimal   ).Value = (decimal)goodQty;
                cmd.Parameters.Add("@By",      SqlDbType.VarChar, 50).Value = employeeNo;
                lotId = (int)cmd.ExecuteScalar()!;
            }

            // 2) PR_ProductionResult
            int resultId;
            using (var cmd = new SqlCommand("""
                INSERT INTO dbo.PR_ProductionResult
                    (EntryNo, WoID, LotID, LineID, ProcessCode, GoodQty, CycleSec,
                     MoldID, OperatorID, SessionID, DefectFlag, EntryAt, CreatedBy, CreatedTS)
                OUTPUT INSERTED.ResultID
                VALUES
                    (@EntryNo, @WoID, @LotID, @LineID, 'INJ', @Good, @CT,
                     @Mold, @Op, @Sess, @DF, SYSDATETIME(), @By, SYSDATETIME());
                """, conn, tx))
            {
                cmd.Parameters.Add("@EntryNo", SqlDbType.VarChar, 28).Value = $"INJ-{DateTime.Now:yyyyMMdd}-{lineId}-{DateTime.Now:HHmmssfff}";
                cmd.Parameters.Add("@WoID",    SqlDbType.Int           ).Value = woId;
                cmd.Parameters.Add("@LotID",   SqlDbType.Int           ).Value = lotId;
                cmd.Parameters.Add("@LineID",  SqlDbType.VarChar, 20   ).Value = lineId;
                cmd.Parameters.Add("@Good",    SqlDbType.Int           ).Value = goodQty;
                cmd.Parameters.Add("@CT",      SqlDbType.Int           ).Value = cycleSec;
                cmd.Parameters.Add("@Mold",    SqlDbType.VarChar, 20   ).Value = (object?)moldId ?? DBNull.Value;
                cmd.Parameters.Add("@Op",      SqlDbType.NVarChar, 450 ).Value = operatorId;
                cmd.Parameters.Add("@Sess",    SqlDbType.Int           ).Value = (object?)sessionId ?? DBNull.Value;
                cmd.Parameters.Add("@DF",      SqlDbType.Bit           ).Value = defectFlag;
                cmd.Parameters.Add("@By",      SqlDbType.VarChar, 50   ).Value = employeeNo;
                resultId = (int)cmd.ExecuteScalar()!;
            }

            // 3) PP_WorkOrder.CompletedQty bump
            decimal newCompleted;
            using (var cmd = new SqlCommand("""
                UPDATE dbo.PP_WorkOrder
                SET    CompletedQty = ISNULL(CompletedQty,0) + @Q,
                       Status       = CASE WHEN ISNULL(CompletedQty,0) + @Q >= ISNULL(OrderQty,0)
                                            THEN 'Closed' ELSE Status END,
                       ActualEnd    = CASE WHEN ISNULL(CompletedQty,0) + @Q >= ISNULL(OrderQty,0)
                                            THEN SYSDATETIME() ELSE ActualEnd END,
                       ModifiedTS   = SYSDATETIME()
                OUTPUT INSERTED.CompletedQty
                WHERE  WoID = @WoID;
                """, conn, tx))
            {
                cmd.Parameters.Add("@WoID", SqlDbType.Int).Value = woId;
                cmd.Parameters.Add("@Q",    SqlDbType.Int).Value = goodQty;
                newCompleted = (decimal)(cmd.ExecuteScalar() ?? 0m);
            }

            // 4) MD_Mold shot +1
            if (!string.IsNullOrEmpty(moldId))
            {
                using var cmd = new SqlCommand("""
                    UPDATE dbo.MD_Mold
                    SET    CurrentShots = ISNULL(CurrentShots,0) + 1,
                           ModifiedTS   = SYSDATETIME()
                    WHERE  MoldID = @Mold;
                    """, conn, tx);
                cmd.Parameters.Add("@Mold", SqlDbType.VarChar, 20).Value = moldId;
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
            return (resultId, lotId, newCompleted);
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>
    /// Today's GoodQty for one WO (used by INJ-04 "Today Good" stat).
    /// </summary>
    public int GetTodayGoodForWo(int woId)
    {
        const string sql = """
            SELECT ISNULL(SUM(GoodQty),0)
            FROM   dbo.PR_ProductionResult
            WHERE  WoID = @WoID
              AND  CAST(EntryAt AS DATE) = CAST(SYSDATETIME() AS DATE);
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@WoID", SqlDbType.Int).Value = woId;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    /// <summary>
    /// Hourly good + defect totals for a line for the current calendar day.
    /// 24 rows always returned (zero-padded).
    /// </summary>
    public List<HourlyOutputDto> GetHourlyToday(string lineId)
    {
        const string sql = """
            WITH g AS (
              SELECT DATEPART(hour, EntryAt) AS H, SUM(GoodQty) AS G
              FROM   dbo.PR_ProductionResult
              WHERE  LineID = @Line
                AND  CAST(EntryAt AS DATE) = CAST(SYSDATETIME() AS DATE)
              GROUP  BY DATEPART(hour, EntryAt)
            ),
            d AS (
              SELECT DATEPART(hour, d.DetectedAt) AS H, SUM(d.Qty) AS D
              FROM   dbo.PR_DefectDetail d
              JOIN   dbo.PR_ProductionResult r ON r.ResultID = d.ResultID
              WHERE  r.LineID = @Line
                AND  CAST(d.DetectedAt AS DATE) = CAST(SYSDATETIME() AS DATE)
              GROUP  BY DATEPART(hour, d.DetectedAt)
            )
            SELECT  hr.H AS Hour, ISNULL(g.G,0) AS Good, ISNULL(d.D,0) AS Defect
            FROM   (SELECT 0 AS H UNION ALL SELECT  1 UNION ALL SELECT  2 UNION ALL SELECT  3
                    UNION ALL SELECT  4 UNION ALL SELECT  5 UNION ALL SELECT  6 UNION ALL SELECT  7
                    UNION ALL SELECT  8 UNION ALL SELECT  9 UNION ALL SELECT 10 UNION ALL SELECT 11
                    UNION ALL SELECT 12 UNION ALL SELECT 13 UNION ALL SELECT 14 UNION ALL SELECT 15
                    UNION ALL SELECT 16 UNION ALL SELECT 17 UNION ALL SELECT 18 UNION ALL SELECT 19
                    UNION ALL SELECT 20 UNION ALL SELECT 21 UNION ALL SELECT 22 UNION ALL SELECT 23) hr
            LEFT JOIN g ON g.H = hr.H
            LEFT JOIN d ON d.H = hr.H
            ORDER BY hr.H;
            """;

        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Line", SqlDbType.VarChar, 20).Value = lineId;
        using var rdr = cmd.ExecuteReader();
        var list = new List<HourlyOutputDto>(24);
        while (rdr.Read())
            list.Add(new HourlyOutputDto
            {
                Hour      = (int)rdr["Hour"],
                GoodQty   = Convert.ToInt32(rdr["Good"]),
                DefectQty = Convert.ToInt32(rdr["Defect"]),
            });
        return list;
    }
}
