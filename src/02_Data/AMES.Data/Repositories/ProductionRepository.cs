using System.Data;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using AMES.Data.Services;
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
    /// Records one production cycle as a single batch lot. Increments the step CompletedQty
    /// on (WoID, LineID) and creates a lot row. Returns the new ResultID + the post-update completed qty.
    /// Throws InvalidOperationException if the WO has no routing step on lineId.
    ///
    /// Mold shots are NOT touched here — shot counts come from the PLC shot counter only
    /// (see InjLotRepository.CreateRawLot). INJ manual entry uses
    /// InjLotRepository.CreateManualLots instead, which keeps the 1 lot = 1 pcs model.
    /// </summary>
    public (int ResultId, int LotId, decimal NewCompletedQty) RecordCycle(
        int     woId,
        string  itemNo,
        string  lineId,
        string  processCode,
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
                    (@LotCode, @ItemNo, @WoID, @LineID, @Proc, @Qty, @Qty,
                     SYSDATETIME(), 'OPEN', 'PENDING', @By, SYSDATETIME());
                """, conn, tx))
            {
                var lotCode = LotNoGenerator.NextLotNo(conn, tx, lineId, DateTime.Now);
                cmd.Parameters.Add("@LotCode", SqlDbType.VarChar, 40).Value = lotCode;
                cmd.Parameters.Add("@ItemNo",  SqlDbType.VarChar, 20).Value = itemNo;
                cmd.Parameters.Add("@WoID",    SqlDbType.Int       ).Value = woId;
                cmd.Parameters.Add("@LineID",  SqlDbType.VarChar, 20).Value = lineId;
                cmd.Parameters.Add("@Proc",    SqlDbType.VarChar, 10).Value = processCode;
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
                    (@EntryNo, @WoID, @LotID, @LineID, @Proc, @Good, @CT,
                     @Mold, @Op, @Sess, @DF, SYSDATETIME(), @By, SYSDATETIME());
                """, conn, tx))
            {
                var entryNo = $"{processCode}-{DateTime.Now:yyyyMMdd}-{lineId}-{DateTime.Now:HHmmssfff}";
                if (entryNo.Length > 28) entryNo = entryNo[..28];
                cmd.Parameters.Add("@EntryNo", SqlDbType.VarChar, 28).Value = entryNo;
                cmd.Parameters.Add("@WoID",    SqlDbType.Int           ).Value = woId;
                cmd.Parameters.Add("@LotID",   SqlDbType.Int           ).Value = lotId;
                cmd.Parameters.Add("@LineID",  SqlDbType.VarChar, 20   ).Value = lineId;
                cmd.Parameters.Add("@Proc",    SqlDbType.VarChar, 10   ).Value = processCode;
                cmd.Parameters.Add("@Good",    SqlDbType.Int           ).Value = goodQty;
                cmd.Parameters.Add("@CT",      SqlDbType.Int           ).Value = cycleSec;
                cmd.Parameters.Add("@Mold",    SqlDbType.VarChar, 20   ).Value = (object?)moldId ?? DBNull.Value;
                cmd.Parameters.Add("@Op",      SqlDbType.NVarChar, 450 ).Value = operatorId;
                cmd.Parameters.Add("@Sess",    SqlDbType.Int           ).Value = (object?)sessionId ?? DBNull.Value;
                cmd.Parameters.Add("@DF",      SqlDbType.Bit           ).Value = defectFlag;
                cmd.Parameters.Add("@By",      SqlDbType.VarChar, 50   ).Value = employeeNo;
                resultId = (int)cmd.ExecuteScalar()!;
            }

            // 3) 단계 실적 반영 (WoID + LineID 로 단계 행 특정)
            var stepId = WorkOrderRepository.FindStepId(conn, tx, woId, lineId)
                ?? throw new InvalidOperationException($"WO {woId} has no routing step on line {lineId}.");
            var newCompleted = WorkOrderRepository.BumpStepCompleted(conn, tx, stepId, goodQty, operatorId);

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
    /// IMG-MAIN 좌측 패널 — 스테이션 BOP 품번 × 당일 PLAN/GOOD/NG.
    /// InjLotRepository.GetDailyItemSummary 와 같은 CTE 구조지만 원천이 실적 행(PR_ProductionResult)과
    /// 불량 행(PR_DefectDetail)이다. BOP 에 없어도 오늘 일정·실적·불량이 있는 품번은 InBop=false 로 붙인다.
    /// </summary>
    public List<ItemDailyDto> GetDailyItemSummary(string lineId, string stationCode, string processCode)
    {
        const string sql = """
            DECLARE @Today date = CAST(SYSDATETIME() AS date);

            WITH bop AS (
                SELECT DISTINCT b.ItemNo
                FROM   dbo.MD_Bop b
                WHERE  b.StationCode = @Station AND ISNULL(b.ActiveFlag,1) = 1
            ),
            sched AS (
                SELECT w.ItemNo, SUM(ISNULL(s.PlannedQty,0)) AS PlanQty
                FROM   dbo.PP_LineSchedule s
                JOIN   dbo.PP_WorkOrder    w ON w.WoID = s.WoID
                WHERE  s.LineID = @Line AND s.ScheduleDate = @Today AND s.EntryType = 'WO'
                GROUP  BY w.ItemNo
            ),
            good AS (
                SELECT w.ItemNo, SUM(ISNULL(p.GoodQty,0)) AS GoodQty
                FROM   dbo.PR_ProductionResult p
                JOIN   dbo.PP_WorkOrder        w ON w.WoID = p.WoID
                WHERE  p.LineID = @Line
                  AND  p.EntryAt >= @Today AND p.EntryAt < DATEADD(day, 1, @Today)
                GROUP  BY w.ItemNo
            ),
            ng AS (
                SELECT w.ItemNo, SUM(ISNULL(d.Qty,0)) AS NgQty
                FROM   dbo.PR_DefectDetail d
                JOIN   dbo.PP_WorkOrder    w ON w.WoID = d.WoID
                WHERE  d.ProcessCode = @Proc
                  AND  d.DetectedAt >= @Today AND d.DetectedAt < DATEADD(day, 1, @Today)
                  AND  EXISTS (SELECT 1 FROM dbo.PP_WorkOrderRouting r
                               WHERE  r.WoID = w.WoID AND r.LineID = @Line)
                GROUP  BY w.ItemNo
            ),
            itemkeys AS (
                SELECT ItemNo FROM bop
                UNION SELECT ItemNo FROM sched
                UNION SELECT ItemNo FROM good
                UNION SELECT ItemNo FROM ng
            )
            SELECT k.ItemNo,
                   COALESCE(i.ItemName, N'') AS ItemName,
                   ISNULL(p.PlanQty, 0)      AS PlanQty,
                   ISNULL(g.GoodQty, 0)      AS GoodQty,
                   ISNULL(n.NgQty, 0)        AS NgQty,
                   CASE WHEN b.ItemNo IS NULL THEN 0 ELSE 1 END AS InBop,
                   CASE WHEN EXISTS (
                        SELECT 1
                        FROM   dbo.PP_WorkOrderRouting r
                        JOIN   dbo.PP_WorkOrder        w ON w.WoID = r.WoID
                        WHERE  r.LineID = @Line AND w.ItemNo = k.ItemNo
                          AND  r.Status IN ('Released','In Progress')
                          AND  ISNULL(w.Status,'Draft') <> 'Cancelled') THEN 1 ELSE 0 END AS HasOpenWo
            FROM   itemkeys k
            LEFT JOIN dbo.MD_Item i ON i.ItemNo = k.ItemNo
            LEFT JOIN bop   b ON b.ItemNo = k.ItemNo
            LEFT JOIN sched p ON p.ItemNo = k.ItemNo
            LEFT JOIN good  g ON g.ItemNo = k.ItemNo
            LEFT JOIN ng    n ON n.ItemNo = k.ItemNo
            ORDER  BY InBop DESC, k.ItemNo;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Line",    SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@Station", SqlDbType.VarChar, 20).Value = stationCode;
        cmd.Parameters.Add("@Proc",    SqlDbType.VarChar, 10).Value = processCode;
        using var rdr = cmd.ExecuteReader();
        var list = new List<ItemDailyDto>();
        while (rdr.Read())
        {
            list.Add(new ItemDailyDto
            {
                ItemNo    = (string)rdr["ItemNo"],
                ItemName  = (string)rdr["ItemName"],
                PlanQty   = Convert.ToDecimal(rdr["PlanQty"]),
                GoodQty   = Convert.ToInt32(rdr["GoodQty"]),
                NgQty     = Convert.ToInt32(rdr["NgQty"]),
                InBop     = Convert.ToInt32(rdr["InBop"]) == 1,
                HasOpenWo = Convert.ToInt32(rdr["HasOpenWo"]) == 1,
            });
        }
        return list;
    }

    /// <summary>오늘 이 라인에 기록된 실적 행, 최신순 (IMG-MAIN 우측 이력).</summary>
    public List<ProductionEntryDto> GetTodayEntries(string lineId, int top = 100)
    {
        const string sql = """
            DECLARE @Today date = CAST(SYSDATETIME() AS date);
            SELECT TOP (@Top)
                   p.ResultID, p.EntryAt, p.GoodQty,
                   COALESCE(w.ItemNo, l.ItemNo, '') AS ItemNo,
                   w.WoNumber, l.LotCode
            FROM   dbo.PR_ProductionResult p
            LEFT JOIN dbo.PP_WorkOrder w ON w.WoID  = p.WoID
            LEFT JOIN dbo.tbl_Lot      l ON l.LotID = p.LotID
            WHERE  p.LineID = @Line
              AND  p.EntryAt >= @Today AND p.EntryAt < DATEADD(day, 1, @Today)
            ORDER  BY p.EntryAt DESC, p.ResultID DESC;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Line", SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@Top",  SqlDbType.Int).Value = top;
        using var rdr = cmd.ExecuteReader();
        var list = new List<ProductionEntryDto>();
        while (rdr.Read())
        {
            list.Add(new ProductionEntryDto
            {
                ResultId = Convert.ToInt32(rdr["ResultID"]),
                EntryAt  = Convert.ToDateTime(rdr["EntryAt"]),
                GoodQty  = rdr["GoodQty"] is DBNull ? 0 : Convert.ToInt32(rdr["GoodQty"]),
                ItemNo   = (string)rdr["ItemNo"],
                WoNumber = rdr["WoNumber"] as string,
                LotCode  = rdr["LotCode"]  as string,
            });
        }
        return list;
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
