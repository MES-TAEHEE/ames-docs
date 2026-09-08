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

    // ── RPT-001 Daily Production ─────────────────────────────────────────
    public sealed record ProdHourRow(string? LineId, string? ItemNo, string? ItemName, string? ItemNameEn,
        int Hour, int Entries, int GoodQty, int DefectQty);
    public sealed record PlanRow(string? LineId, string? ItemNo, string? ItemName, string? ItemNameEn, decimal PlannedQty);

    /// <summary>하루치 실적을 라인×품목×시간대로 집계. 불량은 PR_DefectDetail.Qty 합, 상세가 없으면 DefectFlag 를 1건으로 센다.</summary>
    public List<ProdHourRow> ListProductionByHour(DateTime day, string? lineId = null)
    {
        const string sql = """
            SELECT  r.LineID, w.ItemNo, i.ItemName, i.ItemNameEN,
                    DATEPART(hour, r.EntryAt)                                              AS Hr,
                    COUNT(*)                                                               AS Entries,
                    ISNULL(SUM(r.GoodQty), 0)                                              AS GoodQty,
                    ISNULL(SUM(ISNULL(d.Qty, CASE WHEN r.DefectFlag = 1 THEN 1 ELSE 0 END)), 0) AS DefectQty
            FROM    dbo.PR_ProductionResult r
            LEFT JOIN dbo.PP_WorkOrder w ON w.WoID   = r.WoID
            LEFT JOIN dbo.MD_Item      i ON i.ItemNo = w.ItemNo
            OUTER APPLY (SELECT SUM(dd.Qty) AS Qty FROM dbo.PR_DefectDetail dd WHERE dd.ResultID = r.ResultID) d
            WHERE   r.EntryAt >= @D AND r.EntryAt < DATEADD(DAY, 1, @D)
              AND  (@L IS NULL OR r.LineID = @L)
            GROUP BY r.LineID, w.ItemNo, i.ItemName, i.ItemNameEN, DATEPART(hour, r.EntryAt)
            ORDER BY r.LineID, w.ItemNo, Hr;
            """;
        return Query(sql, r => new ProdHourRow(
            r["LineID"] as string, r["ItemNo"] as string, r["ItemName"] as string, r["ItemNameEN"] as string,
            (int)r["Hr"], (int)r["Entries"], (int)r["GoodQty"], (int)r["DefectQty"]),
            ("@D", day.Date), ("@L", (object?)lineId ?? DBNull.Value));
    }

    /// <summary>해당 일자 라인 스케줄 계획수량을 라인×품목으로 집계 (취소 슬롯 제외).</summary>
    public List<PlanRow> ListPlanByDate(DateTime day, string? lineId = null)
    {
        const string sql = """
            SELECT  s.LineID, w.ItemNo, i.ItemName, i.ItemNameEN, ISNULL(SUM(s.PlannedQty), 0) AS PlannedQty
            FROM    dbo.PP_LineSchedule s
            LEFT JOIN dbo.PP_WorkOrder w ON w.WoID   = s.WoID
            LEFT JOIN dbo.MD_Item      i ON i.ItemNo = w.ItemNo
            WHERE   s.ScheduleDate = @D
              AND   ISNULL(s.Status, '') <> 'CANCELLED'
              AND  (@L IS NULL OR s.LineID = @L)
            GROUP BY s.LineID, w.ItemNo, i.ItemName, i.ItemNameEN
            ORDER BY s.LineID, w.ItemNo;
            """;
        return Query(sql, r => new PlanRow(
            r["LineID"] as string, r["ItemNo"] as string, r["ItemName"] as string, r["ItemNameEN"] as string,
            r["PlannedQty"] as decimal? ?? 0m),
            ("@D", day.Date), ("@L", (object?)lineId ?? DBNull.Value));
    }

    public (DateTime? Min, DateTime? Max) ProductionDateExtent()
    {
        var rows = Query("SELECT MIN(EntryAt) AS Mn, MAX(EntryAt) AS Mx FROM dbo.PR_ProductionResult;",
            r => (r["Mn"] as DateTime?, r["Mx"] as DateTime?));
        return rows.Count > 0 ? rows[0] : (null, null);
    }

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

    // ── RPT-002 Defect Pareto ────────────────────────────────────────────
    // ── RPT-002 Defect Pareto (기간 조회) ─────────────────────────────────
    public sealed record DefectAggRow(string? DefectCode, string? ProcessCode, string? LineId, int Qty, int Events);

    /// <summary>기간 내 불량 상세를 불량코드×공정×라인으로 집계. 라인은 실적(PR_ProductionResult) 조인.</summary>
    public List<DefectAggRow> ListDefectAgg(DateTime from, DateTime to, string? lineId = null)
    {
        const string sql = """
            SELECT  d.DefectCode, d.ProcessCode, r.LineID,
                    ISNULL(SUM(d.Qty), 0) AS Qty, COUNT(*) AS Events
            FROM    dbo.PR_DefectDetail d
            LEFT JOIN dbo.PR_ProductionResult r ON r.ResultID = d.ResultID
            WHERE   d.DetectedAt >= @F AND d.DetectedAt < @T
              AND  (@L IS NULL OR r.LineID = @L)
            GROUP BY d.DefectCode, d.ProcessCode, r.LineID;
            """;
        return Query(sql, r => new DefectAggRow(
            r["DefectCode"] as string, r["ProcessCode"] as string, r["LineID"] as string,
            (int)r["Qty"], (int)r["Events"]),
            ("@F", from.Date), ("@T", to.Date.AddDays(1)), ("@L", (object?)lineId ?? DBNull.Value));
    }

    /// <summary>기간 내 생산 실적 합계(양품 + 불량) — 불량률 분모(검사 수량).</summary>
    public (int Good, int Defect) ProductionTotals(DateTime from, DateTime to, string? lineId = null)
    {
        const string sql = """
            SELECT  ISNULL(SUM(r.GoodQty), 0) AS Good,
                    ISNULL(SUM(ISNULL(d.Qty, CASE WHEN r.DefectFlag = 1 THEN 1 ELSE 0 END)), 0) AS Defect
            FROM    dbo.PR_ProductionResult r
            OUTER APPLY (SELECT SUM(dd.Qty) AS Qty FROM dbo.PR_DefectDetail dd WHERE dd.ResultID = r.ResultID) d
            WHERE   r.EntryAt >= @F AND r.EntryAt < @T
              AND  (@L IS NULL OR r.LineID = @L);
            """;
        var rows = Query(sql, r => ((int)r["Good"], (int)r["Defect"]),
            ("@F", from.Date), ("@T", to.Date.AddDays(1)), ("@L", (object?)lineId ?? DBNull.Value));
        return rows.Count > 0 ? rows[0] : (0, 0);
    }

    public (DateTime? Min, DateTime? Max) DefectDateExtent()
    {
        var rows = Query("SELECT MIN(DetectedAt) AS Mn, MAX(DetectedAt) AS Mx FROM dbo.PR_DefectDetail;",
            r => (r["Mn"] as DateTime?, r["Mx"] as DateTime?));
        return rows.Count > 0 ? rows[0] : (null, null);
    }

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

    // ── RPT-003 Daily Shipment ───────────────────────────────────────────
    // ── RPT-003 Daily Shipment (일자 상세) ────────────────────────────────
    public sealed record ShipmentDetailRow(int ShipmentOrderId, string? ShipOrderNumber, string? CustomerCode,
        string? CarrierCode, string? DestPlant, string? DestDock, DateTime? ShipDate, string? Status, string? OtdFlag,
        DateTime? DepartureTs, DateTime? LoadConfirmedAt, DateTime? OrderConfirmedAt, string? LicensePlate, string? LoadOtd,
        int LineSeq, string? ItemNo, string? ItemName, string? ItemNameEn, decimal OrderedQty, decimal AllocatedQty, decimal? UnitCost);

    /// <summary>출하 예정일(ShipDate) 기준 하루치 출하 오더를 라인 단위로. 상차 확인(FG_LoadingConfirm)·품목 원가 조인.</summary>
    public List<ShipmentDetailRow> ListShipmentDetail(DateTime day, string? customerCode = null)
    {
        const string sql = """
            SELECT  o.ShipmentOrderID, o.ShipOrderNumber, o.CustomerCode,
                    COALESCE(NULLIF(l.CarrierCode, ''), o.CarrierCode) AS CarrierCode,
                    o.DestPlant, o.DestDock, o.ShipDate, o.Status, o.OTDFlag,
                    l.DepartureTS, l.ConfirmedAt AS LoadConfirmedAt, o.ConfirmedAt AS OrderConfirmedAt,
                    l.LicensePlate, l.OTDStatus,
                    ISNULL(sl.LineSeq, 0) AS LineSeq, sl.ItemNo, i.ItemName, i.ItemNameEN,
                    ISNULL(sl.OrderedQty, 0) AS OrderedQty, ISNULL(sl.AllocatedQty, 0) AS AllocatedQty, i.UnitCost
            FROM    dbo.FG_ShipmentOrder o
            LEFT JOIN dbo.FG_ShipmentOrderLine sl ON sl.ShipmentOrderID = o.ShipmentOrderID
            LEFT JOIN dbo.MD_Item i ON i.ItemNo = sl.ItemNo
            OUTER APPLY (SELECT TOP 1 lc.CarrierCode, lc.DepartureTS, lc.ConfirmedAt, lc.LicensePlate, lc.OTDStatus
                         FROM dbo.FG_LoadingConfirm lc WHERE lc.ShipmentOrderID = o.ShipmentOrderID
                         ORDER BY lc.ConfirmedAt DESC, lc.LoadingID DESC) l
            WHERE   CAST(o.ShipDate AS DATE) = @D
              AND  (@C IS NULL OR o.CustomerCode = @C)
            ORDER BY o.ShipOrderNumber, sl.LineSeq;
            """;
        return Query(sql, r => new ShipmentDetailRow(
            (int)r["ShipmentOrderID"], r["ShipOrderNumber"] as string, r["CustomerCode"] as string,
            r["CarrierCode"] as string, r["DestPlant"] as string, r["DestDock"] as string,
            r["ShipDate"] as DateTime?, r["Status"] as string, r["OTDFlag"] as string,
            r["DepartureTS"] as DateTime?, r["LoadConfirmedAt"] as DateTime?, r["OrderConfirmedAt"] as DateTime?,
            r["LicensePlate"] as string, r["OTDStatus"] as string,
            (int)r["LineSeq"], r["ItemNo"] as string, r["ItemName"] as string, r["ItemNameEN"] as string,
            r["OrderedQty"] as decimal? ?? 0m, r["AllocatedQty"] as decimal? ?? 0m, r["UnitCost"] as decimal?),
            ("@D", day.Date), ("@C", (object?)customerCode ?? DBNull.Value));
    }

    public (DateTime? Min, DateTime? Max) ShipmentDateExtent()
    {
        var rows = Query("SELECT MIN(ShipDate) AS Mn, MAX(ShipDate) AS Mx FROM dbo.FG_ShipmentOrder;",
            r => (r["Mn"] as DateTime?, r["Mx"] as DateTime?));
        return rows.Count > 0 ? rows[0] : (null, null);
    }

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

    // ── RPT-004 OTD (on-time delivery) ───────────────────────────────────
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

    // ── RPT-005 Inventory Status ─────────────────────────────────────────
    // ── RPT-005 Inventory (SKU 단위: 자재창고 WH_Inventory ∪ 완성품창고 FG_Stock) ──
    public sealed record InventorySkuRow(string Source, string ItemNo, string? ItemName, string? ItemNameEn, string? ItemType,
        string? Location, decimal Qty, decimal Reserved, decimal? UnitCost, decimal? SafetyStock, decimal? MaxStock, int Lots);

    /// <summary>현재 재고를 품목×위치로 집계. Source = "WH"(자재, WH_Inventory) / "FG"(완성품, FG_Stock; 출하·폐기 제외).</summary>
    public List<InventorySkuRow> ListInventorySku()
    {
        const string sql = """
            SELECT  x.Source, x.ItemNo, i.ItemName, i.ItemNameEN, i.ItemType, x.Location,
                    x.Qty, x.Reserved, COALESCE(x.UnitCost, i.UnitCost) AS UnitCost, i.SafetyStock, i.MaxStock, x.Lots
            FROM (
                SELECT 'WH' AS Source, w.ItemNo, w.LocationID AS Location,
                       ISNULL(SUM(w.OnHandQty), 0) AS Qty, ISNULL(SUM(w.ReservedQty), 0) AS Reserved,
                       MAX(w.UnitCost) AS UnitCost, COUNT(*) AS Lots
                FROM   dbo.WH_Inventory w
                WHERE  ISNULL(w.Status, '') NOT IN ('CLOSED', 'SCRAPPED')
                GROUP BY w.ItemNo, w.LocationID
                UNION ALL
                SELECT 'FG', s.ItemNo, s.Location,
                       ISNULL(SUM(s.Qty), 0), ISNULL(SUM(CASE WHEN s.ReservationID IS NOT NULL OR s.Status = 'Reserved' THEN s.Qty ELSE 0 END), 0),
                       NULL, COUNT(*)
                FROM   dbo.FG_Stock s
                WHERE  UPPER(ISNULL(s.Status, '')) NOT IN ('SHIPPED', 'SCRAPPED')
                GROUP BY s.ItemNo, s.Location
            ) x
            LEFT JOIN dbo.MD_Item i ON i.ItemNo = x.ItemNo
            ORDER BY x.Source, x.ItemNo, x.Location;
            """;
        return Query(sql, r => new InventorySkuRow(
            (string)r["Source"], r["ItemNo"] as string ?? "", r["ItemName"] as string, r["ItemNameEN"] as string, r["ItemType"] as string,
            r["Location"] as string, r["Qty"] as decimal? ?? 0m, r["Reserved"] as decimal? ?? 0m,
            r["UnitCost"] as decimal?, r["SafetyStock"] as decimal?, r["MaxStock"] as decimal?, (int)r["Lots"]));
    }

    /// <summary>최근 N일 자재 출고 수량(품목별) — 회전율 분자. WH_InventoryTransaction 의 음수 변동 합.</summary>
    public Dictionary<string, decimal> IssuedQtyByItem(int days = 30)
    {
        const string sql = """
            SELECT ItemNo, ISNULL(SUM(-QtyChange), 0) AS Issued
            FROM   dbo.WH_InventoryTransaction
            WHERE  QtyChange < 0 AND TransactionTime >= DATEADD(DAY, -@D, SYSDATETIME())
            GROUP BY ItemNo;
            """;
        return Query(sql, r => (Item: r["ItemNo"] as string ?? "", Issued: r["Issued"] as decimal? ?? 0m), ("@D", days))
            .Where(x => x.Item.Length > 0).ToDictionary(x => x.Item, x => x.Issued);
    }

    public List<InventoryRow> ListInventory(int topN = 100)
    {
        // FG_Inventory 는 스키마에 없다(FG_Stock 이 정본) — API 호환을 위해 시그니처만 유지
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

    // ── RPT-006 Equipment OEE ────────────────────────────────────────────
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

    // ── RPT-007 Monthly KPI ──────────────────────────────────────────────
    // ── RPT-007 Monthly KPI Scorecard (월별 원천 지표) ──────────────────────
    public sealed record MonthlyMetricRow(DateTime Month,
        decimal GoodQty, decimal DefectQty, decimal PlanQty,
        decimal? Avail, decimal? Oee, decimal OperMin, int Failures,
        int Orders, int LateOrders, int Shipped, int ShippedOnTime, int Claims,
        decimal Issued, decimal OnHand);

    /// <summary>from~to 사이 모든 달(비어 있는 달 포함)의 원천 지표. 각 지표의 정의는 화면 주석 참조.</summary>
    public List<MonthlyMetricRow> ListMonthlyMetrics(DateTime fromMonth, DateTime toMonth)
    {
        const string sql = """
            DECLARE @today DATE = CAST(SYSDATETIME() AS DATE);
            ;WITH months AS (
                SELECT @F AS M
                UNION ALL SELECT DATEADD(MONTH, 1, M) FROM months WHERE DATEADD(MONTH, 1, M) <= @T
            ),
            prod AS (
                SELECT DATEFROMPARTS(YEAR(EntryAt), MONTH(EntryAt), 1) AS M, SUM(ISNULL(GoodQty,0)) AS Good
                FROM dbo.PR_ProductionResult WHERE EntryAt >= @F AND EntryAt < DATEADD(MONTH, 1, @T)
                GROUP BY DATEFROMPARTS(YEAR(EntryAt), MONTH(EntryAt), 1)),
            def AS (
                SELECT DATEFROMPARTS(YEAR(DetectedAt), MONTH(DetectedAt), 1) AS M, SUM(ISNULL(Qty,0)) AS Def
                FROM dbo.PR_DefectDetail WHERE DetectedAt >= @F AND DetectedAt < DATEADD(MONTH, 1, @T)
                GROUP BY DATEFROMPARTS(YEAR(DetectedAt), MONTH(DetectedAt), 1)),
            pln AS (
                SELECT DATEFROMPARTS(YEAR(ScheduleDate), MONTH(ScheduleDate), 1) AS M, SUM(ISNULL(PlannedQty,0)) AS PlanQty
                FROM dbo.PP_LineSchedule WHERE ISNULL(Status,'') <> 'CANCELLED' AND ScheduleDate >= @F AND ScheduleDate < DATEADD(MONTH, 1, @T)
                GROUP BY DATEFROMPARTS(YEAR(ScheduleDate), MONTH(ScheduleDate), 1)),
            oee AS (
                SELECT DATEFROMPARTS(YEAR(AggDate), MONTH(AggDate), 1) AS M,
                       SUM(Availability * NULLIF(PlannedTimeMin,0)) / NULLIF(SUM(CASE WHEN Availability IS NOT NULL THEN PlannedTimeMin END),0) AS Avail,
                       SUM(OEE * NULLIF(PlannedTimeMin,0)) / NULLIF(SUM(CASE WHEN OEE IS NOT NULL THEN PlannedTimeMin END),0) AS Oee,
                       SUM(ISNULL(PlannedTimeMin,0) - ISNULL(DowntimeMin,0)) AS OperMin
                FROM dbo.MNT_OEELog WHERE AggDate >= @F AND AggDate < DATEADD(MONTH, 1, @T)
                GROUP BY DATEFROMPARTS(YEAR(AggDate), MONTH(AggDate), 1)),
            fail AS (
                SELECT DATEFROMPARTS(YEAR(ReportedAt), MONTH(ReportedAt), 1) AS M, COUNT(*) AS Failures
                FROM dbo.MNT_FailureRegister WHERE ReportedAt >= @F AND ReportedAt < DATEADD(MONTH, 1, @T)
                GROUP BY DATEFROMPARTS(YEAR(ReportedAt), MONTH(ReportedAt), 1)),
            ord AS (
                SELECT DATEFROMPARTS(YEAR(RequestedDeliveryDate), MONTH(RequestedDeliveryDate), 1) AS M, COUNT(*) AS Orders,
                       SUM(CASE WHEN ISNULL(ShippedQty,0) < ISNULL(OrderQty,0) AND RequestedDeliveryDate < @today THEN 1 ELSE 0 END) AS LateOrders
                FROM dbo.PP_CustomerOrder WHERE RequestedDeliveryDate >= @F AND RequestedDeliveryDate < DATEADD(MONTH, 1, @T)
                GROUP BY DATEFROMPARTS(YEAR(RequestedDeliveryDate), MONTH(RequestedDeliveryDate), 1)),
            ship AS (
                SELECT DATEFROMPARTS(YEAR(ShipDate), MONTH(ShipDate), 1) AS M,
                       SUM(CASE WHEN UPPER(ISNULL(Status,'')) IN ('SHIPPED','LOADED','CLOSED') THEN 1 ELSE 0 END) AS Shipped,
                       SUM(CASE WHEN UPPER(ISNULL(Status,'')) IN ('SHIPPED','LOADED','CLOSED') AND UPPER(ISNULL(OTDFlag,'')) IN ('ONTIME','OK') THEN 1 ELSE 0 END) AS OnTime
                FROM dbo.FG_ShipmentOrder WHERE ShipDate >= @F AND ShipDate < DATEADD(MONTH, 1, @T)
                GROUP BY DATEFROMPARTS(YEAR(ShipDate), MONTH(ShipDate), 1)),
            claim AS (
                SELECT DATEFROMPARTS(YEAR(ReceivedAt), MONTH(ReceivedAt), 1) AS M, COUNT(*) AS Claims
                FROM dbo.FG_CustomerReturn WHERE ReceivedAt >= @F AND ReceivedAt < DATEADD(MONTH, 1, @T)
                GROUP BY DATEFROMPARTS(YEAR(ReceivedAt), MONTH(ReceivedAt), 1)),
            iss AS (
                SELECT DATEFROMPARTS(YEAR(TransactionTime), MONTH(TransactionTime), 1) AS M, SUM(-QtyChange) AS Issued
                FROM dbo.WH_InventoryTransaction WHERE QtyChange < 0 AND TransactionTime >= @F AND TransactionTime < DATEADD(MONTH, 1, @T)
                GROUP BY DATEFROMPARTS(YEAR(TransactionTime), MONTH(TransactionTime), 1)),
            onhand AS (SELECT ISNULL(SUM(OnHandQty),0) AS OnHand FROM dbo.WH_Inventory)
            SELECT m.M,
                   ISNULL(p.Good,0) AS GoodQty, ISNULL(d.Def,0) AS DefectQty, ISNULL(pl.PlanQty,0) AS PlanQty,
                   o.Avail, o.Oee, ISNULL(o.OperMin,0) AS OperMin, ISNULL(f.Failures,0) AS Failures,
                   ISNULL(r.Orders,0) AS Orders, ISNULL(r.LateOrders,0) AS LateOrders,
                   ISNULL(s.Shipped,0) AS Shipped, ISNULL(s.OnTime,0) AS ShippedOnTime, ISNULL(c.Claims,0) AS Claims,
                   ISNULL(i.Issued,0) AS Issued, h.OnHand
            FROM months m
            LEFT JOIN prod  p  ON p.M  = m.M
            LEFT JOIN def   d  ON d.M  = m.M
            LEFT JOIN pln   pl ON pl.M = m.M
            LEFT JOIN oee   o  ON o.M  = m.M
            LEFT JOIN fail  f  ON f.M  = m.M
            LEFT JOIN ord   r  ON r.M  = m.M
            LEFT JOIN ship  s  ON s.M  = m.M
            LEFT JOIN claim c  ON c.M  = m.M
            LEFT JOIN iss   i  ON i.M  = m.M
            CROSS JOIN onhand h
            ORDER BY m.M
            OPTION (MAXRECURSION 120);
            """;
        return Query(sql, r => new MonthlyMetricRow(
            (DateTime)r["M"],
            Convert.ToDecimal(r["GoodQty"]), Convert.ToDecimal(r["DefectQty"]), Convert.ToDecimal(r["PlanQty"]),
            r["Avail"] as decimal?, r["Oee"] as decimal?, Convert.ToDecimal(r["OperMin"]), Convert.ToInt32(r["Failures"]),
            Convert.ToInt32(r["Orders"]), Convert.ToInt32(r["LateOrders"]), Convert.ToInt32(r["Shipped"]), Convert.ToInt32(r["ShippedOnTime"]), Convert.ToInt32(r["Claims"]),
            Convert.ToDecimal(r["Issued"]), Convert.ToDecimal(r["OnHand"])),
            ("@F", new DateTime(fromMonth.Year, fromMonth.Month, 1)), ("@T", new DateTime(toMonth.Year, toMonth.Month, 1)));
    }

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

    // ── RPT-008 Schedule Adherence ───────────────────────────────────────
    // ── RPT-008 Schedule Adherence (WO × 라인 단위) ────────────────────────
    public sealed record ScheduleWoRow(int WoId, string? WoNumber, string? LineId, string? ItemNo, string? ItemName, string? ItemNameEn,
        string? WoStatus, decimal PlanQty, DateTime FirstPlanDate, DateTime PlanDate, decimal ActualQty, DateTime? ActualDate, int Slots);

    /// <summary>기간 내 라인 스케줄 슬롯(취소 제외)을 WO×라인으로 묶고, 그 WO·라인의 실적(양품+불량)과 마지막 실적일을 붙인다.</summary>
    public List<ScheduleWoRow> ListScheduleWo(DateTime from, DateTime to, string? lineId = null)
    {
        const string sql = """
            ;WITH sch AS (
                SELECT s.WoID, s.LineID, SUM(ISNULL(s.PlannedQty,0)) AS PlanQty,
                       MIN(s.ScheduleDate) AS FirstPlanDate, MAX(s.ScheduleDate) AS PlanDate, COUNT(*) AS Slots
                FROM   dbo.PP_LineSchedule s
                WHERE  s.WoID IS NOT NULL AND ISNULL(s.Status,'') <> 'CANCELLED'
                  AND  s.ScheduleDate >= @F AND s.ScheduleDate <= @T
                  AND (@L IS NULL OR s.LineID = @L)
                GROUP BY s.WoID, s.LineID
            ),
            act AS (
                SELECT r.WoID, r.LineID,
                       SUM(ISNULL(r.GoodQty,0) + ISNULL(d.Qty, CASE WHEN r.DefectFlag = 1 THEN 1 ELSE 0 END)) AS ActualQty,
                       MAX(CAST(r.EntryAt AS DATE)) AS ActualDate
                FROM   dbo.PR_ProductionResult r
                OUTER APPLY (SELECT SUM(dd.Qty) AS Qty FROM dbo.PR_DefectDetail dd WHERE dd.ResultID = r.ResultID) d
                WHERE  r.WoID IS NOT NULL
                GROUP BY r.WoID, r.LineID
            )
            SELECT sch.WoID, w.WoNumber, sch.LineID, w.ItemNo, i.ItemName, i.ItemNameEN, w.Status AS WoStatus,
                   sch.PlanQty, sch.FirstPlanDate, sch.PlanDate, ISNULL(a.ActualQty,0) AS ActualQty, a.ActualDate, sch.Slots
            FROM   sch
            LEFT JOIN act a ON a.WoID = sch.WoID AND a.LineID = sch.LineID
            LEFT JOIN dbo.PP_WorkOrder w ON w.WoID = sch.WoID
            LEFT JOIN dbo.MD_Item i ON i.ItemNo = w.ItemNo
            ORDER BY sch.PlanDate, sch.LineID, w.WoNumber;
            """;
        return Query(sql, r => new ScheduleWoRow(
            (int)r["WoID"], r["WoNumber"] as string, r["LineID"] as string, r["ItemNo"] as string, r["ItemName"] as string, r["ItemNameEN"] as string,
            r["WoStatus"] as string, Convert.ToDecimal(r["PlanQty"]), (DateTime)r["FirstPlanDate"], (DateTime)r["PlanDate"],
            Convert.ToDecimal(r["ActualQty"]), r["ActualDate"] as DateTime?, (int)r["Slots"]),
            ("@F", from.Date), ("@T", to.Date), ("@L", (object?)lineId ?? DBNull.Value));
    }

    public (DateTime? Min, DateTime? Max) ScheduleDateExtent()
    {
        var rows = Query("SELECT MIN(ScheduleDate) AS Mn, MAX(ScheduleDate) AS Mx FROM dbo.PP_LineSchedule WHERE WoID IS NOT NULL AND ISNULL(Status,'') <> 'CANCELLED';",
            r => (r["Mn"] as DateTime?, r["Mx"] as DateTime?));
        return rows.Count > 0 ? rows[0] : (null, null);
    }

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

    // ── RPT-009 Report Catalog (static metadata) ─────────────────────────
    // ── RPT-010 Report Builder (화이트리스트 기반 ad-hoc 집계) ─────────────────
    public sealed record AdhocRow(string Key, string? Label, Dictionary<string, decimal> M);

    /// <summary>데이터 소스·그룹 차원·기간으로 집계. SQL 조각은 모두 코드 안의 화이트리스트에서만 고르므로 사용자 입력이 SQL 에 섞이지 않는다.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> AdhocDims = new Dictionary<string, string[]>
    {
        ["PROD"]     = new[] { "Line", "Date", "Shift", "Item" },
        ["DEFECT"]   = new[] { "Process", "DefectCode", "Line", "Date" },
        ["SHIP"]     = new[] { "Customer", "Carrier", "Date", "Item" },
        ["OEE"]      = new[] { "Line", "Equip", "Date", "Shift" },
        ["DOWNTIME"] = new[] { "Line", "Reason", "Date" },
    };
    public static readonly IReadOnlyDictionary<string, string[]> AdhocMeasures = new Dictionary<string, string[]>
    {
        ["PROD"]     = new[] { "Production", "GoodQty", "DefectQty", "Entries" },
        ["DEFECT"]   = new[] { "DefectQty", "Events" },
        ["SHIP"]     = new[] { "Orders", "OrderedQty", "AllocatedQty" },
        ["OEE"]      = new[] { "Oee", "Availability", "Performance", "Quality", "PlannedMin", "DowntimeMin" },
        ["DOWNTIME"] = new[] { "DowntimeMin", "Events" },
    };

    public List<AdhocRow> AdhocQuery(string source, string dim, DateTime from, DateTime to)
    {
        if (!AdhocDims.TryGetValue(source, out var dims) || !dims.Contains(dim))
            throw new ArgumentException("unsupported source/dim");

        const string ShiftExpr = "CASE WHEN DATEPART(hour, {0}) BETWEEN 8 AND 15 THEN 'A' WHEN DATEPART(hour, {0}) BETWEEN 16 AND 23 THEN 'B' ELSE 'C' END";
        string keyExpr, labelExpr = "NULL", body;
        switch (source)
        {
            case "PROD":
                keyExpr = dim switch
                {
                    "Line"  => "ISNULL(r.LineID,'—')",
                    "Date"  => "CONVERT(varchar(10), CAST(r.EntryAt AS DATE), 120)",
                    "Shift" => string.Format(ShiftExpr, "r.EntryAt"),
                    _       => "ISNULL(w.ItemNo,'—')",
                };
                if (dim == "Item") labelExpr = "MAX(i.ItemName)";
                body = $"""
                    SELECT {keyExpr} AS K, {labelExpr} AS Lbl,
                           ISNULL(SUM(ISNULL(r.GoodQty,0)),0) + ISNULL(SUM(ISNULL(d.Qty, CASE WHEN r.DefectFlag=1 THEN 1 ELSE 0 END)),0) AS Production,
                           ISNULL(SUM(ISNULL(r.GoodQty,0)),0) AS GoodQty,
                           ISNULL(SUM(ISNULL(d.Qty, CASE WHEN r.DefectFlag=1 THEN 1 ELSE 0 END)),0) AS DefectQty,
                           COUNT(*) AS Entries
                    FROM dbo.PR_ProductionResult r
                    LEFT JOIN dbo.PP_WorkOrder w ON w.WoID = r.WoID
                    LEFT JOIN dbo.MD_Item i ON i.ItemNo = w.ItemNo
                    OUTER APPLY (SELECT SUM(dd.Qty) AS Qty FROM dbo.PR_DefectDetail dd WHERE dd.ResultID = r.ResultID) d
                    WHERE r.EntryAt >= @F AND r.EntryAt < @T
                    GROUP BY {keyExpr}
                    """;
                break;
            case "DEFECT":
                keyExpr = dim switch
                {
                    "Process"    => "ISNULL(d.ProcessCode,'—')",
                    "DefectCode" => "ISNULL(d.DefectCode,'—')",
                    "Line"       => "ISNULL(r.LineID,'—')",
                    _            => "CONVERT(varchar(10), CAST(d.DetectedAt AS DATE), 120)",
                };
                if (dim == "DefectCode") labelExpr = "MAX(c.DefectName)";
                body = $"""
                    SELECT {keyExpr} AS K, {labelExpr} AS Lbl,
                           ISNULL(SUM(ISNULL(d.Qty,0)),0) AS DefectQty, COUNT(*) AS Events
                    FROM dbo.PR_DefectDetail d
                    LEFT JOIN dbo.PR_ProductionResult r ON r.ResultID = d.ResultID
                    LEFT JOIN dbo.MD_DefectCode c ON c.DefectCode = d.DefectCode
                    WHERE d.DetectedAt >= @F AND d.DetectedAt < @T
                    GROUP BY {keyExpr}
                    """;
                break;
            case "SHIP":
                keyExpr = dim switch
                {
                    "Customer" => "ISNULL(o.CustomerCode,'—')",
                    "Carrier"  => "ISNULL(o.CarrierCode,'—')",
                    "Date"     => "CONVERT(varchar(10), CAST(o.ShipDate AS DATE), 120)",
                    _          => "ISNULL(l.ItemNo,'—')",
                };
                if (dim == "Item") labelExpr = "MAX(i.ItemName)";
                body = $"""
                    SELECT {keyExpr} AS K, {labelExpr} AS Lbl,
                           COUNT(DISTINCT o.ShipmentOrderID) AS Orders,
                           ISNULL(SUM(ISNULL(l.OrderedQty,0)),0) AS OrderedQty,
                           ISNULL(SUM(ISNULL(l.AllocatedQty,0)),0) AS AllocatedQty
                    FROM dbo.FG_ShipmentOrder o
                    LEFT JOIN dbo.FG_ShipmentOrderLine l ON l.ShipmentOrderID = o.ShipmentOrderID
                    LEFT JOIN dbo.MD_Item i ON i.ItemNo = l.ItemNo
                    WHERE o.ShipDate >= @F AND o.ShipDate < @T
                    GROUP BY {keyExpr}
                    """;
                break;
            case "OEE":
                keyExpr = dim switch
                {
                    "Line"  => "ISNULL(o.LineID,'—')",
                    "Equip" => "ISNULL(o.EquipID,'—')",
                    "Date"  => "CONVERT(varchar(10), o.AggDate, 120)",
                    _       => "ISNULL(o.ShiftCode,'—')",
                };
                if (dim == "Equip") labelExpr = "MAX(e.EquipName)";
                body = $"""
                    SELECT {keyExpr} AS K, {labelExpr} AS Lbl,
                           ISNULL(SUM(o.OEE          * NULLIF(o.PlannedTimeMin,0)) / NULLIF(SUM(CASE WHEN o.OEE          IS NOT NULL THEN o.PlannedTimeMin END),0),0) AS Oee,
                           ISNULL(SUM(o.Availability * NULLIF(o.PlannedTimeMin,0)) / NULLIF(SUM(CASE WHEN o.Availability IS NOT NULL THEN o.PlannedTimeMin END),0),0) AS Availability,
                           ISNULL(SUM(o.Performance  * NULLIF(o.PlannedTimeMin,0)) / NULLIF(SUM(CASE WHEN o.Performance  IS NOT NULL THEN o.PlannedTimeMin END),0),0) AS Performance,
                           ISNULL(SUM(o.Quality      * NULLIF(o.PlannedTimeMin,0)) / NULLIF(SUM(CASE WHEN o.Quality      IS NOT NULL THEN o.PlannedTimeMin END),0),0) AS Quality,
                           ISNULL(SUM(ISNULL(o.PlannedTimeMin,0)),0) AS PlannedMin,
                           ISNULL(SUM(ISNULL(o.DowntimeMin,0)),0) AS DowntimeMin
                    FROM dbo.MNT_OEELog o
                    LEFT JOIN dbo.MD_Equipment e ON e.EquipID = o.EquipID
                    WHERE o.AggDate >= @F AND o.AggDate < @T
                    GROUP BY {keyExpr}
                    """;
                break;
            default: // DOWNTIME
                keyExpr = dim switch
                {
                    "Line"   => "ISNULL(d.LineID,'—')",
                    "Reason" => "ISNULL(d.ReasonCode,'—')",
                    _        => "CONVERT(varchar(10), CAST(d.StartTS AS DATE), 120)",
                };
                body = $"""
                    SELECT {keyExpr} AS K, {labelExpr} AS Lbl,
                           ISNULL(SUM(ISNULL(d.DurationMin,0)),0) AS DowntimeMin, COUNT(*) AS Events
                    FROM dbo.PP_LineDowntimeLog d
                    WHERE d.StartTS >= @F AND d.StartTS < @T
                    GROUP BY {keyExpr}
                    """;
                break;
        }

        var measures = AdhocMeasures[source];
        return Query(body + " ORDER BY K;", r =>
        {
            var m = new Dictionary<string, decimal>();
            foreach (var name in measures) m[name] = r[name] is DBNull ? 0m : Convert.ToDecimal(r[name]);
            return new AdhocRow(r["K"] as string ?? "—", r["Lbl"] as string, m);
        }, ("@F", from.Date), ("@T", to.Date.AddDays(1)));
    }

    public List<ReportCatalogEntry> ListReportCatalog() => new()
    {
        new("RPT-001", "Daily Production",   "rpt/daily-production",   "Production", "라인별 일생산량 + 양품률"),
        new("RPT-002", "Defect Pareto",      "rpt/defect-pareto",      "Quality",    "불량 코드 파레토 (30일)"),
        new("RPT-003", "Daily Shipment",     "rpt/daily-shipment",     "Logistics",  "출하 주문 / 수량 / 고객 수"),
        new("RPT-004", "On-Time Delivery",   "rpt/on-time",            "Logistics",  "납기 준수율 (OTD%)"),
        new("RPT-005", "Inventory Status",   "rpt/inventory",          "Logistics",  "품목×위치 재고, Hold 비율"),
        new("RPT-006", "Equipment OEE",      "rpt/equipment-oee",      "Maintenance","설비별 A·P·Q·OEE"),
        new("RPT-007", "Monthly KPI",        "rpt/monthly-kpi",        "Executive",  "월간 통합 지표"),
        new("RPT-008", "Schedule Adherence", "rpt/schedule-adherence", "Production", "계획 vs 실생산"),
        new("RPT-009", "Report Center",      "rpt/report-center",      "Hub",        "전체 리포트 카탈로그"),
        new("RPT-010", "Report Builder",     "rpt/report-builder",     "Advanced",   "Ad-hoc 쿼리 빌더 (미리보기)")
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
