using System.Data;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// Reports (RPT) module — aggregates over PR_/PP_/QC_/FG_/MNT_/WH_ tables.
/// Read-only. One method per RPT-XX screen, all rolled-up.
/// </summary>
public sealed class RptRepository
{
    private readonly AmesConnectionFactory _f;
    public RptRepository(AmesConnectionFactory f) => _f = f;

    // ── DTOs ─────────────────────────────────────────────────────────────
    public sealed record DailyProdRow(DateTime Day, string? LineId,
        int Entries, int GoodQty, int DefectQty, decimal YieldPct);

    public sealed record DefectParetoRow(string? DefectCode, int TotalQty,
        int EventCount, decimal PercentOfTotal, decimal CumulativePct);

    public sealed record DailyShipmentRow(DateTime Day, int Orders, decimal OrderedQty,
        decimal AllocatedQty, int CustomerCount);

    public sealed record OtdRow(DateTime Day, int TotalOrders, int OnTime, int Late,
        decimal OnTimePct);

    public sealed record InventoryRow(string? ItemNo, string? Location, int LotCount,
        decimal Qty, int HoldLots, decimal HoldQty);

    public sealed record EquipmentOeeRow(string? EquipId, string? LineId,
        decimal AvgAvailability, decimal AvgPerformance, decimal AvgQuality,
        decimal AvgOee, int Days);

    public sealed record MonthlyKpiRow(int Year, int Month, int ProductionGoodQty,
        int DefectQty, decimal YieldPct, int ShipmentOrders, int FailureCount,
        decimal AvgOee);

    public sealed record ScheduleAdherenceRow(DateTime Day, string? LineId,
        decimal PlannedQty, decimal ProducedQty, decimal AdherencePct);

    public sealed record ReportCatalogEntry(string Key, string Title,
        string Path, string Category, string Description);

    // ── RPT-01 Daily Production ──────────────────────────────────────────
    public List<DailyProdRow> ListDailyProduction(int daysBack = 14)
    {
        const string sql = """
            SELECT  CAST(EntryAt AS DATE)           AS Day,
                    LineID,
                    COUNT(*)                        AS Entries,
                    ISNULL(SUM(GoodQty), 0)         AS GoodQty,
                    ISNULL(SUM(CASE WHEN DefectFlag=1 THEN 1 ELSE 0 END), 0) AS DefectQty
            FROM    dbo.PR_ProductionResult
            WHERE   EntryAt >= DATEADD(DAY, -@D, SYSDATETIME())
            GROUP   BY CAST(EntryAt AS DATE), LineID
            ORDER   BY Day DESC, LineID;
            """;
        var rows = Query(sql, r => new
        {
            Day    = (DateTime)r["Day"],
            LineId = r["LineID"] as string,
            Ent    = (int)r["Entries"],
            Good   = (int)r["GoodQty"],
            Def    = (int)r["DefectQty"]
        }, ("@D", daysBack));

        return rows.Select(x =>
        {
            var total = x.Good + x.Def;
            var yld = total > 0 ? (decimal)x.Good / total * 100m : 0m;
            return new DailyProdRow(x.Day, x.LineId, x.Ent, x.Good, x.Def, yld);
        }).ToList();
    }

    // ── RPT-02 Defect Pareto ─────────────────────────────────────────────
    public List<DefectParetoRow> ListDefectPareto(int daysBack = 30, int topN = 20)
    {
        const string sql = """
            SELECT TOP (@N)
                   DefectCode,
                   ISNULL(SUM(Qty),0) AS TotalQty,
                   COUNT(*)           AS EventCount
            FROM   dbo.PR_DefectDetail
            WHERE  DetectedAt >= DATEADD(DAY, -@D, SYSDATETIME())
              AND  DefectCode IS NOT NULL
            GROUP BY DefectCode
            ORDER BY TotalQty DESC;
            """;
        var raw = Query(sql, r => new
        {
            Code = r["DefectCode"] as string,
            Qty  = (int)r["TotalQty"],
            Cnt  = (int)r["EventCount"]
        }, ("@N", topN), ("@D", daysBack));

        var total = raw.Sum(x => x.Qty);
        decimal cum = 0;
        var list = new List<DefectParetoRow>();
        foreach (var x in raw)
        {
            var pct = total > 0 ? (decimal)x.Qty / total * 100m : 0m;
            cum += pct;
            list.Add(new DefectParetoRow(x.Code, x.Qty, x.Cnt, pct, cum));
        }
        return list;
    }

    // ── RPT-03 Daily Shipment ────────────────────────────────────────────
    public List<DailyShipmentRow> ListDailyShipment(int daysBack = 14)
    {
        const string sql = """
            SELECT  CAST(so.ShipDate AS DATE) AS Day,
                    COUNT(DISTINCT so.ShipmentOrderID)   AS Orders,
                    ISNULL(SUM(sol.OrderedQty), 0)       AS OrderedQty,
                    ISNULL(SUM(sol.AllocatedQty), 0)     AS AllocatedQty,
                    COUNT(DISTINCT so.CustomerCode)      AS CustomerCount
            FROM    dbo.FG_ShipmentOrder      so
            LEFT JOIN dbo.FG_ShipmentOrderLine sol ON sol.ShipmentOrderID = so.ShipmentOrderID
            WHERE   so.ShipDate >= DATEADD(DAY, -@D, CAST(SYSDATETIME() AS DATE))
            GROUP BY CAST(so.ShipDate AS DATE)
            ORDER BY Day DESC;
            """;
        return Query(sql, r => new DailyShipmentRow(
            (DateTime)r["Day"], (int)r["Orders"],
            r["OrderedQty"] as decimal? ?? 0m,
            r["AllocatedQty"] as decimal? ?? 0m,
            (int)r["CustomerCount"]),
            ("@D", daysBack));
    }

    // ── RPT-04 OTD (on-time delivery) ────────────────────────────────────
    public List<OtdRow> ListOtd(int daysBack = 30)
    {
        const string sql = """
            SELECT  CAST(ShipDate AS DATE) AS Day,
                    COUNT(*) AS TotalOrders,
                    SUM(CASE WHEN OTDFlag IN ('OnTime','OK') THEN 1 ELSE 0 END) AS OnTime,
                    SUM(CASE WHEN OTDFlag IN ('Late','NG')   THEN 1 ELSE 0 END) AS Late
            FROM    dbo.FG_ShipmentOrder
            WHERE   ShipDate >= DATEADD(DAY, -@D, CAST(SYSDATETIME() AS DATE))
              AND   ShipDate IS NOT NULL
            GROUP BY CAST(ShipDate AS DATE)
            ORDER BY Day DESC;
            """;
        var rows = Query(sql, r => new
        {
            Day = (DateTime)r["Day"],
            Tot = (int)r["TotalOrders"],
            On  = (int)r["OnTime"],
            Lt  = (int)r["Late"]
        }, ("@D", daysBack));

        return rows.Select(x => new OtdRow(x.Day, x.Tot, x.On, x.Lt,
            x.Tot > 0 ? (decimal)x.On / x.Tot * 100m : 0m)).ToList();
    }

    // ── RPT-05 Inventory Status ──────────────────────────────────────────
    public List<InventoryRow> ListInventory(int topN = 100)
    {
        const string sql = """
            SELECT TOP (@N)
                   ItemNo, Location,
                   COUNT(*)                                     AS LotCount,
                   ISNULL(SUM(Qty), 0)                          AS Qty,
                   SUM(CASE WHEN HoldFlag=1 THEN 1 ELSE 0 END)  AS HoldLots,
                   ISNULL(SUM(CASE WHEN HoldFlag=1 THEN Qty ELSE 0 END), 0) AS HoldQty
            FROM   dbo.FG_Stock
            WHERE  ISNULL(Status,'') NOT IN ('SHIPPED','SCRAPPED')
            GROUP BY ItemNo, Location
            ORDER BY Qty DESC;
            """;
        return Query(sql, r => new InventoryRow(
            r["ItemNo"] as string, r["Location"] as string,
            (int)r["LotCount"], r["Qty"] as decimal? ?? 0m,
            (int)r["HoldLots"], r["HoldQty"] as decimal? ?? 0m),
            ("@N", topN));
    }

    // ── RPT-06 Equipment OEE ─────────────────────────────────────────────
    public List<EquipmentOeeRow> ListEquipmentOee(int daysBack = 30)
    {
        const string sql = """
            SELECT  EquipID, LineID,
                    AVG(Availability)         AS AvgA,
                    AVG(Performance)          AS AvgP,
                    AVG(Quality)              AS AvgQ,
                    AVG(OEE)                  AS AvgOee,
                    COUNT(DISTINCT AggDate)   AS Days
            FROM    dbo.MNT_OEELog
            WHERE   AggDate >= DATEADD(DAY, -@D, CAST(SYSDATETIME() AS DATE))
            GROUP BY EquipID, LineID
            ORDER BY AvgOee DESC;
            """;
        return Query(sql, r => new EquipmentOeeRow(
            r["EquipID"] as string, r["LineID"] as string,
            r["AvgA"] as decimal? ?? 0m, r["AvgP"] as decimal? ?? 0m,
            r["AvgQ"] as decimal? ?? 0m, r["AvgOee"] as decimal? ?? 0m,
            (int)r["Days"]),
            ("@D", daysBack));
    }

    // ── RPT-07 Monthly KPI ───────────────────────────────────────────────
    public List<MonthlyKpiRow> ListMonthlyKpi(int monthsBack = 6)
    {
        const string sql = """
            DECLARE @start DATE = DATEADD(MONTH, -@M, DATEFROMPARTS(YEAR(SYSDATETIME()), MONTH(SYSDATETIME()), 1));

            ;WITH prod AS (
                SELECT YEAR(EntryAt) AS Y, MONTH(EntryAt) AS M,
                       SUM(GoodQty)                                              AS GoodQty,
                       SUM(CASE WHEN DefectFlag=1 THEN 1 ELSE 0 END)             AS DefectCt
                FROM   dbo.PR_ProductionResult
                WHERE  EntryAt >= @start
                GROUP BY YEAR(EntryAt), MONTH(EntryAt)
            ),
            ship AS (
                SELECT YEAR(ShipDate) AS Y, MONTH(ShipDate) AS M,
                       COUNT(*) AS Orders
                FROM   dbo.FG_ShipmentOrder
                WHERE  ShipDate >= @start
                GROUP BY YEAR(ShipDate), MONTH(ShipDate)
            ),
            fail AS (
                SELECT YEAR(ReportedAt) AS Y, MONTH(ReportedAt) AS M, COUNT(*) AS Cnt
                FROM   dbo.MNT_FailureRegister
                WHERE  ReportedAt >= @start
                GROUP BY YEAR(ReportedAt), MONTH(ReportedAt)
            ),
            oee AS (
                SELECT YEAR(AggDate) AS Y, MONTH(AggDate) AS M, AVG(OEE) AS AvgOee
                FROM   dbo.MNT_OEELog
                WHERE  AggDate >= @start
                GROUP BY YEAR(AggDate), MONTH(AggDate)
            )
            SELECT  COALESCE(p.Y, s.Y, f.Y, o.Y) AS Y,
                    COALESCE(p.M, s.M, f.M, o.M) AS M,
                    ISNULL(p.GoodQty, 0)         AS GoodQty,
                    ISNULL(p.DefectCt, 0)        AS DefectQty,
                    ISNULL(s.Orders, 0)          AS Orders,
                    ISNULL(f.Cnt, 0)             AS Failures,
                    ISNULL(o.AvgOee, 0)          AS AvgOee
            FROM    prod p
            FULL JOIN ship s ON s.Y = p.Y AND s.M = p.M
            FULL JOIN fail f ON f.Y = COALESCE(p.Y, s.Y) AND f.M = COALESCE(p.M, s.M)
            FULL JOIN oee  o ON o.Y = COALESCE(p.Y, s.Y, f.Y) AND o.M = COALESCE(p.M, s.M, f.M)
            ORDER BY Y, M;
            """;
        return Query(sql, r =>
        {
            var good = (int)r["GoodQty"];
            var def  = (int)r["DefectQty"];
            var total = good + def;
            return new MonthlyKpiRow(
                (int)r["Y"], (int)r["M"], good, def,
                total > 0 ? (decimal)good / total * 100m : 0m,
                (int)r["Orders"], (int)r["Failures"],
                r["AvgOee"] as decimal? ?? 0m);
        }, ("@M", monthsBack));
    }

    // ── RPT-08 Schedule Adherence ────────────────────────────────────────
    public List<ScheduleAdherenceRow> ListScheduleAdherence(int daysBack = 14)
    {
        const string sql = """
            ;WITH sch AS (
                SELECT ScheduleDate AS Day, LineID,
                       SUM(PlannedQty) AS PlannedQty
                FROM   dbo.PP_LineSchedule
                WHERE  ScheduleDate >= DATEADD(DAY, -@D, CAST(SYSDATETIME() AS DATE))
                GROUP BY ScheduleDate, LineID
            ),
            prod AS (
                SELECT CAST(EntryAt AS DATE) AS Day, LineID,
                       SUM(GoodQty) AS ProducedQty
                FROM   dbo.PR_ProductionResult
                WHERE  EntryAt >= DATEADD(DAY, -@D, SYSDATETIME())
                GROUP BY CAST(EntryAt AS DATE), LineID
            )
            SELECT COALESCE(s.Day, p.Day) AS Day,
                   COALESCE(s.LineID, p.LineID) AS LineID,
                   ISNULL(s.PlannedQty, 0) AS PlannedQty,
                   ISNULL(p.ProducedQty, 0) AS ProducedQty
            FROM   sch s
            FULL JOIN prod p ON p.Day = s.Day AND p.LineID = s.LineID
            ORDER BY Day DESC, LineID;
            """;
        return Query(sql, r =>
        {
            var planned  = r["PlannedQty"]  as decimal? ?? 0m;
            var produced = r["ProducedQty"] as decimal? ?? 0m;
            var adh = planned > 0 ? produced / planned * 100m : 0m;
            return new ScheduleAdherenceRow(
                (DateTime)r["Day"], r["LineID"] as string,
                planned, produced, adh);
        }, ("@D", daysBack));
    }

    // ── RPT-09 Report Catalog (static metadata) ──────────────────────────
    public List<ReportCatalogEntry> ListReportCatalog() => new()
    {
        new("RPT-01", "Daily Production",   "rpt/daily-production",   "Production", "라인별 일생산량 + 양품률"),
        new("RPT-02", "Defect Pareto",      "rpt/defect-pareto",      "Quality",    "불량 코드 파레토 (30일)"),
        new("RPT-03", "Daily Shipment",     "rpt/daily-shipment",     "Logistics",  "출하 주문 / 수량 / 고객 수"),
        new("RPT-04", "On-Time Delivery",   "rpt/on-time",            "Logistics",  "납기 준수율 (OTD%)"),
        new("RPT-05", "Inventory Status",   "rpt/inventory",          "Logistics",  "품목×위치 재고, Hold 비율"),
        new("RPT-06", "Equipment OEE",      "rpt/equipment-oee",      "Maintenance","설비별 A·P·Q·OEE"),
        new("RPT-07", "Monthly KPI",        "rpt/monthly-kpi",        "Executive",  "월간 통합 지표"),
        new("RPT-08", "Schedule Adherence", "rpt/schedule-adherence", "Production", "계획 vs 실생산"),
        new("RPT-09", "Report Center",      "rpt/report-center",      "Hub",        "전체 리포트 카탈로그"),
        new("RPT-10", "Report Builder",     "rpt/report-builder",     "Advanced",   "Ad-hoc 쿼리 빌더 (미리보기)")
    };

    // ── helpers ──────────────────────────────────────────────────────────
    private List<T> Query<T>(string sql, Func<IDataReader, T> map, params (string Name, object Value)[] pars)
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        foreach (var (n, v) in pars) cmd.Parameters.AddWithValue(n, v);
        using var rdr  = cmd.ExecuteReader();
        var list = new List<T>();
        while (rdr.Read()) list.Add(map(rdr));
        return list;
    }
}
