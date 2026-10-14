using System.Data;
using AMES.Data.Connection;
using AMES.Data.Services;
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

    // ── 전기일(근무일) ──────────────────────────────────────────────────
    // 생산 보고서의 "하루"는 자정이 아니라 SYS_Config.DAY_CUTOFF_TIME(예 07:00)부터 다음 날 그 시각까지다(10-09 사용자 결정 B3).
    // 실적은 저장 때 같은 규칙으로 정한 PR_ProductionResult.ProdDate 로 묶고, 전기일 컬럼이 없는 불량 상세는 발생 시각을 경계만큼 당겨 묶는다.

    /// <summary>전기일 경계 시각 — 설정이 없거나 틀리면 자정(실적 저장의 ProdCalendar.ResolveNow 와 같은 규칙).</summary>
    public TimeSpan ReadDayCutoff()
    {
        var v = Query("SELECT TOP 1 ConfigValue FROM dbo.SYS_Config WHERE ConfigKey = @K ORDER BY ConfigID;",
            r => r["ConfigValue"] as string, ("@K", "SYS_Config.ConfigKey", ProdCalendar.CutoffConfigKey));
        return ProdCalendar.TryParseCutoff(v.FirstOrDefault(), out var c) ? c : TimeSpan.Zero;
    }

    /// <summary>지금(DB 시각)의 전기일.</summary>
    public DateTime ProdToday() => ProdCalendar.ProdDateOf(DbClock.Now, ReadDayCutoff());

    // 확정 전(RAW·로봇 NG) LOT 의 불량은 실적 행이 없어 PR_DefectDetail.ResultID 가 NULL 이다(LotDefectWriter) —
    // 실적에 불량을 붙이는 집계는 그 행을 놓치므로 LOT 의 라인·발생 전기일로 따로 더한다(ListDefectAgg 와 같은 귀속).
    //
    // 품번(10-13 사용자 결정): 실적은 그 LOT 의 품번(tbl_Lot.ItemNo — 실제로 만든 품번: INJ 코어·IMG 완제품, 실적 INSERT 5곳 모두 LotID 를 넣는다)
    // → LOT 없는 옛 행만 WO 단계 품번 → WO 품번. 계획은 WO 단계 품번(COALESCE(PP_WorkOrderRouting.ItemNo, PP_WorkOrder.ItemNo) — POP 의
    // 열린 WO 해석과 같은 규칙)이라 사출 라인의 계획·실적이 코어 품번 한 행에 모인다. 전에는 둘 다 WO 헤더 품번(완제품)이라
    // WO 없는 생산은 "알 수 없는 품목", 사출 라인은 완제품으로 보였다.
    // 한계: APS BOM 규칙 WO 는 INJ 단계에 자식 품번이 여럿 실리지만 단계 품번 칸은 첫 자식 하나라 그 WO 의 계획은 첫 자식으로 모인다.

    /// <summary>전기일 하루치 실적을 라인×품목×시간대로 집계. 불량은 PR_DefectDetail.Qty 합, 상세가 없으면 DefectFlag 를 1건으로 센다.</summary>
    public List<ProdHourRow> ListProductionByHour(DateTime day, string? lineId = null)
    {
        const string sql = """
            SELECT  x.LineID, it.ItemNo, i.ItemName, i.ItemNameEN,
                    DATEPART(hour, x.TS)                AS Hr,
                    ISNULL(SUM(x.Entry), 0)             AS Entries,
                    ISNULL(SUM(x.GoodQty), 0)           AS GoodQty,
                    ISNULL(SUM(x.DefectQty), 0)         AS DefectQty
            FROM (
                SELECT r.LineID, r.WoID, r.LotID, r.EntryAt AS TS, 1 AS Entry, ISNULL(r.GoodQty, 0) AS GoodQty,
                       ISNULL(d.Qty, CASE WHEN r.DefectFlag = 1 THEN 1 ELSE 0 END) AS DefectQty
                FROM   dbo.PR_ProductionResult r
                OUTER APPLY (SELECT SUM(dd.Qty) AS Qty FROM dbo.PR_DefectDetail dd WHERE dd.ResultID = r.ResultID) d
                WHERE  COALESCE(r.ProdDate, CAST(r.EntryAt AS DATE)) = @D
                  AND (@L IS NULL OR r.LineID = @L)
                UNION ALL
                SELECT tl.LineID, dd.WoID, dd.LotID, dd.DetectedAt, 0, 0, ISNULL(dd.Qty, 0)
                FROM   dbo.PR_DefectDetail dd
                LEFT JOIN dbo.tbl_Lot tl ON tl.LotID = dd.LotID
                WHERE  dd.ResultID IS NULL
                  AND  CAST(DATEADD(MINUTE, -@Cut, dd.DetectedAt) AS DATE) = @D
                  AND (@L IS NULL OR tl.LineID = @L)
            ) x
            LEFT JOIN dbo.tbl_Lot      lot ON lot.LotID = x.LotID
            LEFT JOIN dbo.PP_WorkOrder w   ON w.WoID    = x.WoID
            OUTER APPLY (SELECT TOP 1 rt.ItemNo FROM dbo.PP_WorkOrderRouting rt
                         WHERE rt.WoID = x.WoID AND rt.LineID = x.LineID AND rt.ItemNo IS NOT NULL ORDER BY rt.StepSeq) st
            CROSS APPLY (SELECT COALESCE(lot.ItemNo, st.ItemNo, w.ItemNo) AS ItemNo) it
            LEFT JOIN dbo.MD_Item      i   ON i.ItemNo  = it.ItemNo
            GROUP BY x.LineID, it.ItemNo, i.ItemName, i.ItemNameEN, DATEPART(hour, x.TS)
            ORDER BY x.LineID, it.ItemNo, Hr;
            """;
        return Query(sql, r => new ProdHourRow(
            r["LineID"] as string, r["ItemNo"] as string, r["ItemName"] as string, r["ItemNameEN"] as string,
            (int)r["Hr"], (int)r["Entries"], (int)r["GoodQty"], (int)r["DefectQty"]),
            ("@D", null, day.Date), ("@L", "PR_ProductionResult.LineID", (object?)lineId ?? DBNull.Value),
            ("@Cut", null, (int)ReadDayCutoff().TotalMinutes));
    }

    /// <summary>해당 일자 라인 스케줄 계획수량을 라인×품목(WO 단계 품번)으로 집계 (취소 슬롯 제외).</summary>
    public List<PlanRow> ListPlanByDate(DateTime day, string? lineId = null)
    {
        const string sql = """
            SELECT  s.LineID, it.ItemNo, i.ItemName, i.ItemNameEN, ISNULL(SUM(s.PlannedQty), 0) AS PlannedQty
            FROM    dbo.PP_LineSchedule s
            LEFT JOIN dbo.PP_WorkOrder w ON w.WoID   = s.WoID
            OUTER APPLY (SELECT TOP 1 rt.ItemNo FROM dbo.PP_WorkOrderRouting rt
                         WHERE rt.WoID = s.WoID AND rt.LineID = s.LineID AND rt.ItemNo IS NOT NULL ORDER BY rt.StepSeq) st
            CROSS APPLY (SELECT COALESCE(st.ItemNo, w.ItemNo) AS ItemNo) it
            LEFT JOIN dbo.MD_Item      i ON i.ItemNo = it.ItemNo
            WHERE   s.ScheduleDate = @D
              AND   ISNULL(s.Status, '') <> 'CANCELLED'
              AND  (@L IS NULL OR s.LineID = @L)
            GROUP BY s.LineID, it.ItemNo, i.ItemName, i.ItemNameEN
            ORDER BY s.LineID, it.ItemNo;
            """;
        return Query(sql, r => new PlanRow(
            r["LineID"] as string, r["ItemNo"] as string, r["ItemName"] as string, r["ItemNameEN"] as string,
            r["PlannedQty"] as decimal? ?? 0m),
            ("@D", "PP_LineSchedule.ScheduleDate", day.Date), ("@L", "PP_LineSchedule.LineID", (object?)lineId ?? DBNull.Value));
    }

    /// <summary>실적이 있는 첫·마지막 전기일.</summary>
    public (DateTime? Min, DateTime? Max) ProductionDateExtent()
    {
        var rows = Query("""
            SELECT MIN(COALESCE(ProdDate, CAST(EntryAt AS DATE))) AS Mn, MAX(COALESCE(ProdDate, CAST(EntryAt AS DATE))) AS Mx
            FROM   dbo.PR_ProductionResult;
            """, r => (r["Mn"] as DateTime?, r["Mx"] as DateTime?));
        return rows.Count > 0 ? rows[0] : (null, null);
    }

    public List<DailyProdRow> ListDailyProduction(int daysBack = 14)
    {
        var cut=ReadDayCutoff();
        var today=ProdCalendar.ProdDateOf(DbClock.ReadNow(_f),cut);
        const string sql = """
            WITH production AS (
                SELECT COALESCE(r.ProdDate,CAST(r.EntryAt AS date)) AS Day, r.LineID,
                       COUNT(*) AS Entries, ISNULL(SUM(r.GoodQty), 0) AS GoodQty,
                       ISNULL(SUM(ISNULL(d.Qty, CASE WHEN r.DefectFlag=1 THEN 1 ELSE 0 END)), 0) AS DefectQty
                FROM dbo.PR_ProductionResult r
                OUTER APPLY (SELECT SUM(dd.Qty) AS Qty FROM dbo.PR_DefectDetail dd WHERE dd.ResultID=r.ResultID) d
                WHERE COALESCE(r.ProdDate,CAST(r.EntryAt AS date)) BETWEEN DATEADD(day,-@D,@Today) AND @Today
                GROUP BY COALESCE(r.ProdDate,CAST(r.EntryAt AS date)), r.LineID
            ), lot_defects AS (
                SELECT CAST(DATEADD(minute,-@Cut,d.DetectedAt) AS date) AS Day, l.LineID, SUM(d.Qty) AS DefectQty
                FROM dbo.PR_DefectDetail d
                LEFT JOIN dbo.tbl_Lot l ON l.LotID=d.LotID
                WHERE d.ResultID IS NULL AND CAST(DATEADD(minute,-@Cut,d.DetectedAt) AS date) BETWEEN DATEADD(day,-@D,@Today) AND @Today
                GROUP BY CAST(DATEADD(minute,-@Cut,d.DetectedAt) AS date), l.LineID
            )
            SELECT COALESCE(p.Day, l.Day) AS Day, COALESCE(p.LineID, l.LineID) AS LineID,
                   ISNULL(p.Entries, 0) AS Entries, ISNULL(p.GoodQty, 0) AS GoodQty,
                   ISNULL(p.DefectQty, 0) + ISNULL(l.DefectQty, 0) AS DefectQty
            FROM production p FULL JOIN lot_defects l ON l.Day=p.Day AND (l.LineID=p.LineID OR l.LineID IS NULL AND p.LineID IS NULL)
            ORDER BY Day DESC, LineID;
            """;
        var rows = Query(sql, r => new
        {
            Day    = (DateTime)r["Day"],
            LineId = r["LineID"] as string,
            Ent    = (int)r["Entries"],
            Good   = (int)r["GoodQty"],
            Def    = (int)r["DefectQty"]
        }, ("@D", null, daysBack), ("@Today", "PR_ProductionResult.ProdDate", today), ("@Cut", null, (int)cut.TotalMinutes));

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

    /// <summary>
    /// 기간(전기일) 내 불량 상세를 불량코드×공정×라인으로 집계. 라인은 실적(PR_ProductionResult) 조인이되,
    /// 미확정(RAW)·로봇 NG LOT 의 불량은 ResultID 가 없으므로 LOT 의 라인으로 대신 귀속시킨다.
    /// </summary>
    public List<DefectAggRow> ListDefectAgg(DateTime from, DateTime to, string? lineId = null)
    {
        const string sql = """
            SELECT  d.DefectCode, d.ProcessCode, COALESCE(r.LineID, tl.LineID) AS LineID,
                    ISNULL(SUM(d.Qty), 0) AS Qty, COUNT(*) AS Events
            FROM    dbo.PR_DefectDetail d
            LEFT JOIN dbo.PR_ProductionResult r ON r.ResultID = d.ResultID
            LEFT JOIN dbo.tbl_Lot             tl ON tl.LotID  = d.LotID
            WHERE   d.DetectedAt >= @F AND d.DetectedAt < @T
              AND  (@L IS NULL OR COALESCE(r.LineID, tl.LineID) = @L)
            GROUP BY d.DefectCode, d.ProcessCode, COALESCE(r.LineID, tl.LineID);
            """;
        var cut = ReadDayCutoff();
        return Query(sql, r => new DefectAggRow(
            r["DefectCode"] as string, r["ProcessCode"] as string, r["LineID"] as string,
            (int)r["Qty"], (int)r["Events"]),
            ("@F", "PR_DefectDetail.DetectedAt", from.Date + cut), ("@T", "PR_DefectDetail.DetectedAt", to.Date.AddDays(1) + cut), ("@L", "tbl_Lot.LineID", (object?)lineId ?? DBNull.Value));
    }

    /// <summary>기간(전기일) 내 생산 실적 합계(양품 + 불량) — 불량률 분모(검사 수량).</summary>
    public (int Good, int Defect) ProductionTotals(DateTime from, DateTime to, string? lineId = null)
    {
        const string sql = """
            SELECT  ISNULL(SUM(x.Good), 0) AS Good, ISNULL(SUM(x.Defect), 0) AS Defect
            FROM (
                SELECT ISNULL(r.GoodQty, 0) AS Good,
                       ISNULL(d.Qty, CASE WHEN r.DefectFlag = 1 THEN 1 ELSE 0 END) AS Defect
                FROM   dbo.PR_ProductionResult r
                OUTER APPLY (SELECT SUM(dd.Qty) AS Qty FROM dbo.PR_DefectDetail dd WHERE dd.ResultID = r.ResultID) d
                WHERE  COALESCE(r.ProdDate, CAST(r.EntryAt AS DATE)) BETWEEN @F AND @T
                  AND (@L IS NULL OR r.LineID = @L)
                UNION ALL
                SELECT 0, ISNULL(dd.Qty, 0)
                FROM   dbo.PR_DefectDetail dd
                LEFT JOIN dbo.tbl_Lot tl ON tl.LotID = dd.LotID
                WHERE  dd.ResultID IS NULL
                  AND  CAST(DATEADD(MINUTE, -@Cut, dd.DetectedAt) AS DATE) BETWEEN @F AND @T
                  AND (@L IS NULL OR tl.LineID = @L)
            ) x;
            """;
        var rows = Query(sql, r => ((int)r["Good"], (int)r["Defect"]),
            ("@F", null, from.Date), ("@T", null, to.Date), ("@L", "PR_ProductionResult.LineID", (object?)lineId ?? DBNull.Value),
            ("@Cut", null, (int)ReadDayCutoff().TotalMinutes));
        return rows.Count > 0 ? rows[0] : (0, 0);
    }

    /// <summary>불량이 처음·마지막으로 발생한 전기일.</summary>
    public (DateTime? Min, DateTime? Max) DefectDateExtent()
    {
        var rows = Query("SELECT MIN(DetectedAt) AS Mn, MAX(DetectedAt) AS Mx FROM dbo.PR_DefectDetail;",
            r => (r["Mn"] as DateTime?, r["Mx"] as DateTime?));
        if (rows.Count == 0) return (null, null);
        var cut = ReadDayCutoff();
        return (rows[0].Item1 is { } mn ? ProdCalendar.ProdDateOf(mn, cut) : null,
                rows[0].Item2 is { } mx ? ProdCalendar.ProdDateOf(mx, cut) : null);
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
        }, ("@N", null, topN), ("@D", null, daysBack));

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
        int LineSeq, string? ItemNo, string? ItemName, string? ItemNameEn, decimal OrderedQty, decimal AllocatedQty, decimal? UnitCost,
        string? Uom = null);

    /// <summary>출하 예정일(ShipDate) 기준 하루치 출하 오더를 라인 단위로 조회한다.</summary>
    public List<ShipmentDetailRow> ListShipmentDetail(DateTime day, string? customerCode = null)
    {
        const string sql = """
            SELECT  o.ShipmentOrderID, o.ShipOrderNumber, o.CustomerCode,
                    o.CarrierCode,
                    o.DestPlant, o.DestDock, o.ShipDate, o.Status, o.OTDFlag,
                    o.DepartureAt AS DepartureTS, o.LoadingConfirmedAt AS LoadConfirmedAt, o.ConfirmedAt AS OrderConfirmedAt,
                    o.LicensePlate, o.LoadingOTDStatus AS OTDStatus,
                    ISNULL(sl.LineSeq, 0) AS LineSeq, sl.ItemNo, i.ItemName, i.ItemNameEN,
                    ISNULL(sl.OrderedQty, 0) AS OrderedQty, ISNULL(sl.AllocatedQty, 0) AS AllocatedQty, i.UnitCost, i.DefaultUOM
            FROM    dbo.FG_ShipmentOrder o
            OUTER APPLY OPENJSON(COALESCE(o.ItemsJSON, N'[]')) WITH
            (
                LineSeq int '$.lineSeq',
                ItemNo varchar(20) '$.itemNo',
                OrderedQty decimal(14,3) '$.orderedQty',
                AllocatedQty decimal(14,3) '$.allocatedQty'
            ) sl
            LEFT JOIN dbo.MD_Item i
              ON i.ItemNo COLLATE DATABASE_DEFAULT = sl.ItemNo COLLATE DATABASE_DEFAULT
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
            r["OrderedQty"] as decimal? ?? 0m, r["AllocatedQty"] as decimal? ?? 0m, r["UnitCost"] as decimal?, r["DefaultUOM"] as string),
            ("@D", null, day.Date), ("@C", "FG_ShipmentOrder.CustomerCode", (object?)customerCode ?? DBNull.Value));
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
            OUTER APPLY OPENJSON(COALESCE(so.ItemsJSON, N'[]')) WITH
            (
                OrderedQty decimal(14,3) '$.orderedQty',
                AllocatedQty decimal(14,3) '$.allocatedQty'
            ) sol
            WHERE   so.ShipDate >= DATEADD(DAY, -@D, CAST(SYSDATETIME() AS DATE))
            GROUP BY CAST(so.ShipDate AS DATE)
            ORDER BY Day DESC;
            """;
        return Query(sql, r => new DailyShipmentRow(
            (DateTime)r["Day"], (int)r["Orders"],
            r["OrderedQty"] as decimal? ?? 0m,
            r["AllocatedQty"] as decimal? ?? 0m,
            (int)r["CustomerCount"]),
            ("@D", null, daysBack));
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
        }, ("@D", null, daysBack));

        return rows.Select(x => new OtdRow(x.Day, x.Tot, x.On, x.Lt,
            x.Tot > 0 ? (decimal)x.On / x.Tot * 100m : 0m)).ToList();
    }

    // ── RPT-005 Inventory Status ─────────────────────────────────────────
    // ── RPT-005 Inventory (SKU 단위: 통합 WH_Inventory) ──
    public sealed record InventorySkuRow(string Source, string ItemNo, string? ItemName, string? ItemNameEn, string? ItemType,
        string? Location, decimal Qty, decimal Reserved, decimal? UnitCost, decimal? SafetyStock, decimal? MaxStock, int Lots,
        string? ItemCategory = null, string? Uom = null);

    /// <summary>통합 재고를 품목×위치로 집계. AreaCode로 WH/FG를 구분한다.</summary>
    public List<InventorySkuRow> ListInventorySku()
    {
        const string sql = """
            SELECT  x.Source, x.ItemNo, COALESCE(NULLIF(i.ItemName, ''), x.PartName) AS ItemName, i.ItemNameEN, i.ItemType, x.Location,
                    x.Qty, x.Reserved, i.UnitCost, i.SafetyStock, i.MaxStock, x.Lots,
                    i.ItemCategory, i.DefaultUOM
            FROM (
                SELECT CASE WHEN L.AreaCode='FG_AREA' THEN 'FG' ELSE 'WH' END AS Source,
                       w.PartNo AS ItemNo,w.LocationNo AS Location,MAX(w.PartName) AS PartName,
                       ISNULL(SUM(w.Qty),0) AS Qty,CAST(0 AS decimal(18,3)) AS Reserved,
                       CAST(NULL AS decimal(18,3)) AS UnitCost,COUNT(*) AS Lots
                FROM dbo.WH_Inventory w
                JOIN dbo.MD_Location L
                  ON L.LocationID COLLATE DATABASE_DEFAULT = w.LocationNo COLLATE DATABASE_DEFAULT
                WHERE w.Qty<>0 AND L.AreaCode IN ('MAT_AREA','FG_AREA')
                GROUP BY CASE WHEN L.AreaCode='FG_AREA' THEN 'FG' ELSE 'WH' END,w.PartNo,w.LocationNo
            ) x
            LEFT JOIN dbo.MD_Item i
              ON i.ItemNo COLLATE DATABASE_DEFAULT = x.ItemNo COLLATE DATABASE_DEFAULT
            ORDER BY x.Source, x.ItemNo, x.Location;
            """;
        return Query(sql, r => new InventorySkuRow(
            (string)r["Source"], r["ItemNo"] as string ?? "", r["ItemName"] as string, r["ItemNameEN"] as string, r["ItemType"] as string,
            r["Location"] as string, r["Qty"] as decimal? ?? 0m, r["Reserved"] as decimal? ?? 0m,
            r["UnitCost"] as decimal?, r["SafetyStock"] as decimal?, r["MaxStock"] as decimal?, (int)r["Lots"],
            r["ItemCategory"] as string, r["DefaultUOM"] as string));
    }

    /// <summary>최근 N일 자재 출고 수량(품목별) — 회전율 분자. WH_InventoryTransaction 의 음수 변동 합.</summary>
    public Dictionary<string, decimal> IssuedQtyByItem(int days = 30)
    {
        const string sql = """
            SELECT PartNo AS ItemNo, ISNULL(SUM(-QtyChange), 0) AS Issued
            FROM   dbo.WH_InventoryTransaction
            WHERE  QtyChange < 0 AND TransactionTime >= DATEADD(DAY, -@D, SYSDATETIME())
            GROUP BY PartNo;
            """;
        return Query(sql, r => (Item: r["ItemNo"] as string ?? "", Issued: r["Issued"] as decimal? ?? 0m), ("@D", null, days))
            .Where(x => x.Item.Length > 0).ToDictionary(x => x.Item, x => x.Issued);
    }

    public List<InventoryRow> ListInventory(int topN = 100)
    {
        // 완제품 재고도 공통 LOT 재고에서 FG 위치 범위로 조회한다.
        const string sql = """
            SELECT TOP (@N)
                   W.PartNo AS ItemNo, W.LocationNo AS Location,
                   COUNT(*)                                     AS LotCount,
                   ISNULL(SUM(W.Qty), 0)                        AS Qty,
                   0                                            AS HoldLots,
                   CAST(0 AS decimal(18,3))                     AS HoldQty
            FROM   dbo.WH_Inventory W
            JOIN dbo.MD_Location L
              ON L.LocationID COLLATE DATABASE_DEFAULT = W.LocationNo COLLATE DATABASE_DEFAULT
            WHERE W.Qty>0 AND UPPER(L.AreaCode)='FG_AREA'
            GROUP BY W.PartNo, W.LocationNo
            ORDER BY Qty DESC;
            """;
        return Query(sql, r => new InventoryRow(
            r["ItemNo"] as string, r["Location"] as string,
            (int)r["LotCount"], r["Qty"] as decimal? ?? 0m,
            (int)r["HoldLots"], r["HoldQty"] as decimal? ?? 0m),
            ("@N", null, topN));
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
            ("@D", null, daysBack));
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
                SELECT DATEFROMPARTS(YEAR(x.PD), MONTH(x.PD), 1) AS M, SUM(ISNULL(x.GoodQty,0)) AS Good
                FROM (SELECT COALESCE(ProdDate, CAST(EntryAt AS DATE)) AS PD, GoodQty FROM dbo.PR_ProductionResult) x
                WHERE x.PD >= @F AND x.PD < DATEADD(MONTH, 1, @T)
                GROUP BY DATEFROMPARTS(YEAR(x.PD), MONTH(x.PD), 1)),
            def AS (
                SELECT DATEFROMPARTS(YEAR(x.PD), MONTH(x.PD), 1) AS M, SUM(ISNULL(x.Qty,0)) AS Def
                FROM (SELECT CAST(DATEADD(MINUTE, -@Cut, DetectedAt) AS DATE) AS PD, Qty FROM dbo.PR_DefectDetail
                      WHERE DetectedAt >= DATEADD(MINUTE, @Cut, @F) AND DetectedAt < DATEADD(MINUTE, @Cut, DATEADD(MONTH, 1, @T))) x
                GROUP BY DATEFROMPARTS(YEAR(x.PD), MONTH(x.PD), 1)),
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
            onhand AS (SELECT ISNULL(SUM(Qty),0) AS OnHand FROM dbo.WH_Inventory)
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
            ("@F", "MNT_FailureRegister.ReportedAt", new DateTime(fromMonth.Year, fromMonth.Month, 1)), ("@T", null, new DateTime(toMonth.Year, toMonth.Month, 1)),
            ("@Cut", null, (int)ReadDayCutoff().TotalMinutes));
    }

    public List<MonthlyKpiRow> ListMonthlyKpi(int monthsBack = 6)
    {
        const string sql = """
            DECLARE @start DATE = DATEADD(MONTH, -@M, DATEFROMPARTS(YEAR(SYSDATETIME()), MONTH(SYSDATETIME()), 1));

            ;WITH prod AS (
                SELECT YEAR(EntryAt) AS Y, MONTH(EntryAt) AS M,
                       SUM(GoodQty)                                              AS GoodQty,
                       SUM(ISNULL(d.Qty, CASE WHEN r.DefectFlag=1 THEN 1 ELSE 0 END)) AS DefectCt
                FROM dbo.PR_ProductionResult r
                OUTER APPLY (SELECT SUM(dd.Qty) AS Qty FROM dbo.PR_DefectDetail dd WHERE dd.ResultID=r.ResultID) d
                WHERE r.EntryAt >= @start
                GROUP BY YEAR(r.EntryAt), MONTH(r.EntryAt)
            ), lot_defects AS (
                SELECT YEAR(DetectedAt) AS Y, MONTH(DetectedAt) AS M, SUM(Qty) AS DefectCt
                FROM dbo.PR_DefectDetail
                WHERE ResultID IS NULL AND DetectedAt >= @start
                GROUP BY YEAR(DetectedAt), MONTH(DetectedAt)
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
            SELECT  COALESCE(p.Y, l.Y, s.Y, f.Y, o.Y) AS Y,
                    COALESCE(p.M, l.M, s.M, f.M, o.M) AS M,
                    ISNULL(p.GoodQty, 0)         AS GoodQty,
                    ISNULL(p.DefectCt, 0) + ISNULL(l.DefectCt, 0) AS DefectQty,
                    ISNULL(s.Orders, 0)          AS Orders,
                    ISNULL(f.Cnt, 0)             AS Failures,
                    ISNULL(o.AvgOee, 0)          AS AvgOee
            FROM    prod p
            FULL JOIN lot_defects l ON l.Y=p.Y AND l.M=p.M
            FULL JOIN ship s ON s.Y = COALESCE(p.Y, l.Y) AND s.M = COALESCE(p.M, l.M)
            FULL JOIN fail f ON f.Y = COALESCE(p.Y, l.Y, s.Y) AND f.M = COALESCE(p.M, l.M, s.M)
            FULL JOIN oee  o ON o.Y = COALESCE(p.Y, l.Y, s.Y, f.Y) AND o.M = COALESCE(p.M, l.M, s.M, f.M)
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
        }, ("@M", null, monthsBack));
    }

    // ── RPT-008 Schedule Adherence ───────────────────────────────────────
    // ── RPT-008 Schedule Adherence (WO × 라인 단위) ────────────────────────
    public sealed record ScheduleWoRow(int WoId, string? WoNumber, string? LineId, string? ItemNo, string? ItemName, string? ItemNameEn,
        string? WoStatus, decimal PlanQty, DateTime FirstPlanDate, DateTime PlanDate, decimal ActualQty, DateTime? ActualDate, int Slots);

    /// <summary>
    /// 기간 내 라인 스케줄 슬롯(취소 제외)을 WO×라인으로 묶고 실적(양품+불량)을 선입선출로 맞춘다(10-13 사용자 결정).
    ///   · 날짜는 전기일 — 실적 ProdDate(없으면 EntryAt 날짜), 실적 행 없는 LOT 불량은 DetectedAt − 경계 시각. 계획일(ScheduleDate)도 근무일이다.
    ///   · 실적은 먼저 계획된 슬롯부터 채운다: 기간 이전 슬롯 몫(PriorQty)을 먼저 빼고, 남은 양이 이 기간 몫이다.
    ///     ActualQty = 이 기간 몫(기간 뒤 슬롯이 있으면 계획량까지만 — 초과분은 다음 슬롯 몫).
    ///     ActualDate = 누적 실적이 목표(이전 몫 + 기간 계획)에 닿은 전기일(그 뒤 역분개로 내려갔다 다시 닿았으면 마지막으로 닿은 날),
    ///                  아직 못 닿았으면 마지막 실적일.
    /// 전에는 기간 밖 실적까지 합치고 마지막 실적의 달력 날짜를 완료일로 써서, 자정 넘긴 B교대 실적이 "지연 +1",
    /// 같은 WO 의 다음 슬롯 생산이 앞 슬롯 완료일을 늦췄다.
    /// </summary>
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
            other AS (
                SELECT s.WoID, s.LineID,
                       SUM(CASE WHEN s.ScheduleDate < @F THEN ISNULL(s.PlannedQty,0) ELSE 0 END) AS PriorQty,
                       SUM(CASE WHEN s.ScheduleDate > @T THEN ISNULL(s.PlannedQty,0) ELSE 0 END) AS LaterQty
                FROM   dbo.PP_LineSchedule s
                JOIN   sch ON sch.WoID = s.WoID AND sch.LineID = s.LineID
                WHERE  ISNULL(s.Status,'') <> 'CANCELLED' AND (s.ScheduleDate < @F OR s.ScheduleDate > @T)
                GROUP BY s.WoID, s.LineID
            ),
            daily AS (
                SELECT x.WoID, x.LineID, x.PD, SUM(x.Qty) AS Qty
                FROM (
                    SELECT r.WoID, r.LineID, COALESCE(r.ProdDate, CAST(r.EntryAt AS DATE)) AS PD,
                           ISNULL(r.GoodQty,0) + ISNULL(d.Qty, CASE WHEN r.DefectFlag = 1 THEN 1 ELSE 0 END) AS Qty
                    FROM   dbo.PR_ProductionResult r
                    JOIN   sch ON sch.WoID = r.WoID AND sch.LineID = r.LineID
                    OUTER APPLY (SELECT SUM(dd.Qty) AS Qty FROM dbo.PR_DefectDetail dd WHERE dd.ResultID = r.ResultID) d
                    UNION ALL
                    SELECT dd.WoID, tl.LineID, CAST(DATEADD(MINUTE, -@Cut, dd.DetectedAt) AS DATE), ISNULL(dd.Qty,0)
                    FROM   dbo.PR_DefectDetail dd
                    JOIN   dbo.tbl_Lot tl ON tl.LotID = dd.LotID
                    JOIN   sch ON sch.WoID = dd.WoID AND sch.LineID = tl.LineID
                    WHERE  dd.ResultID IS NULL
                ) x
                GROUP BY x.WoID, x.LineID, x.PD
            ),
            cum AS (
                SELECT d.WoID, d.LineID, d.PD,
                       SUM(d.Qty) OVER (PARTITION BY d.WoID, d.LineID ORDER BY d.PD ROWS UNBOUNDED PRECEDING) AS CumQty
                FROM   daily d
            ),
            act AS (
                SELECT sch.WoID, sch.LineID, ISNULL(o.PriorQty,0) AS PriorQty, ISNULL(o.LaterQty,0) AS LaterQty,
                       ISNULL(t.TotalQty,0) AS TotalQty, t.LastPD,
                       (SELECT MAX(c.PD) FROM cum c WHERE c.WoID = sch.WoID AND c.LineID = sch.LineID
                          AND c.CumQty < ISNULL(o.PriorQty,0) + sch.PlanQty) AS LastBelow
                FROM   sch
                LEFT JOIN other o ON o.WoID = sch.WoID AND o.LineID = sch.LineID
                OUTER APPLY (SELECT SUM(d.Qty) AS TotalQty, MAX(d.PD) AS LastPD FROM daily d
                             WHERE d.WoID = sch.WoID AND d.LineID = sch.LineID) t
            )
            SELECT sch.WoID, w.WoNumber, sch.LineID, it.ItemNo, i.ItemName, i.ItemNameEN, w.Status AS WoStatus,
                   sch.PlanQty, sch.FirstPlanDate, sch.PlanDate,
                   CASE WHEN a.TotalQty <= a.PriorQty THEN 0
                        WHEN a.LaterQty > 0 AND a.TotalQty - a.PriorQty > sch.PlanQty THEN sch.PlanQty
                        ELSE a.TotalQty - a.PriorQty END AS ActualQty,
                   CASE WHEN a.TotalQty - a.PriorQty >= sch.PlanQty
                        THEN (SELECT MIN(c.PD) FROM cum c WHERE c.WoID = sch.WoID AND c.LineID = sch.LineID
                                AND c.CumQty >= a.PriorQty + sch.PlanQty AND (a.LastBelow IS NULL OR c.PD > a.LastBelow))
                        ELSE a.LastPD END AS ActualDate,
                   sch.Slots
            FROM   sch
            JOIN   act a ON a.WoID = sch.WoID AND a.LineID = sch.LineID
            LEFT JOIN dbo.PP_WorkOrder w ON w.WoID = sch.WoID
            OUTER APPLY (SELECT TOP 1 rt.ItemNo FROM dbo.PP_WorkOrderRouting rt
                         WHERE rt.WoID = sch.WoID AND rt.LineID = sch.LineID AND rt.ItemNo IS NOT NULL ORDER BY rt.StepSeq) st
            CROSS APPLY (SELECT COALESCE(st.ItemNo, w.ItemNo) AS ItemNo) it
            LEFT JOIN dbo.MD_Item i ON i.ItemNo = it.ItemNo
            ORDER BY sch.PlanDate, sch.LineID, w.WoNumber;
            """;
        return Query(sql, r => new ScheduleWoRow(
            (int)r["WoID"], r["WoNumber"] as string, r["LineID"] as string, r["ItemNo"] as string, r["ItemName"] as string, r["ItemNameEN"] as string,
            r["WoStatus"] as string, Convert.ToDecimal(r["PlanQty"]), (DateTime)r["FirstPlanDate"], (DateTime)r["PlanDate"],
            Convert.ToDecimal(r["ActualQty"]), r["ActualDate"] as DateTime?, (int)r["Slots"]),
            ("@F", "PP_LineSchedule.ScheduleDate", from.Date), ("@T", "PP_LineSchedule.ScheduleDate", to.Date), ("@L", "PP_LineSchedule.LineID", (object?)lineId ?? DBNull.Value),
            ("@Cut", null, (int)ReadDayCutoff().TotalMinutes));
    }

    public (DateTime? Min, DateTime? Max) ScheduleDateExtent()
    {
        var rows = Query("SELECT MIN(ScheduleDate) AS Mn, MAX(ScheduleDate) AS Mx FROM dbo.PP_LineSchedule WHERE WoID IS NOT NULL AND ISNULL(Status,'') <> 'CANCELLED';",
            r => (r["Mn"] as DateTime?, r["Mx"] as DateTime?));
        return rows.Count > 0 ? rows[0] : (null, null);
    }

    /// <summary>
    /// 라인×전기일 계획 대비 실적(Api /api/rpt). 계획 = 그 근무일의 WO 슬롯(취소 제외), 실적 = 그 전기일 양품 + 불량
    /// (실적 ProdDate, 실적 행 없는 LOT 불량은 DetectedAt − 경계 시각 — RPT 일정 준수와 같은 기준, 10-13). 기간은 오늘 전기일부터 거꾸로 daysBack 일.
    /// </summary>
    public List<ScheduleAdherenceRow> ListScheduleAdherence(int daysBack = 14)
    {
        const string sql = """
            ;WITH sch AS (
                SELECT ScheduleDate AS Day, LineID,
                       SUM(ISNULL(PlannedQty,0)) AS PlannedQty
                FROM   dbo.PP_LineSchedule
                WHERE  WoID IS NOT NULL AND ISNULL(Status,'') <> 'CANCELLED'
                  AND  ScheduleDate >= DATEADD(DAY, -@D, @Today) AND ScheduleDate <= @Today
                GROUP BY ScheduleDate, LineID
            ),
            prod AS (
                SELECT x.Day, x.LineID, SUM(x.Qty) AS ProducedQty
                FROM (
                    SELECT COALESCE(r.ProdDate, CAST(r.EntryAt AS DATE)) AS Day, r.LineID,
                           ISNULL(r.GoodQty,0) + ISNULL(d.Qty, CASE WHEN r.DefectFlag = 1 THEN 1 ELSE 0 END) AS Qty
                    FROM   dbo.PR_ProductionResult r
                    OUTER APPLY (SELECT SUM(dd.Qty) AS Qty FROM dbo.PR_DefectDetail dd WHERE dd.ResultID = r.ResultID) d
                    WHERE  COALESCE(r.ProdDate, CAST(r.EntryAt AS DATE)) BETWEEN DATEADD(DAY, -@D, @Today) AND @Today
                    UNION ALL
                    SELECT CAST(DATEADD(MINUTE, -@Cut, dd.DetectedAt) AS DATE), tl.LineID, ISNULL(dd.Qty,0)
                    FROM   dbo.PR_DefectDetail dd
                    LEFT JOIN dbo.tbl_Lot tl ON tl.LotID = dd.LotID
                    WHERE  dd.ResultID IS NULL
                      AND  CAST(DATEADD(MINUTE, -@Cut, dd.DetectedAt) AS DATE) BETWEEN DATEADD(DAY, -@D, @Today) AND @Today
                ) x
                GROUP BY x.Day, x.LineID
            )
            SELECT COALESCE(s.Day, p.Day) AS Day,
                   COALESCE(s.LineID, p.LineID) AS LineID,
                   ISNULL(s.PlannedQty, 0) AS PlannedQty,
                   ISNULL(p.ProducedQty, 0) AS ProducedQty
            FROM   sch s
            FULL JOIN prod p ON p.Day = s.Day AND p.LineID = s.LineID
            ORDER BY Day DESC, LineID;
            """;
        var cut = ReadDayCutoff();
        return Query(sql, r =>
        {
            // SUM(int) 는 int 로 온다 — as decimal? 로 읽으면 늘 null(0) 이었다
            var planned  = Convert.ToDecimal(r["PlannedQty"]);
            var produced = Convert.ToDecimal(r["ProducedQty"]);
            var adh = planned > 0 ? produced / planned * 100m : 0m;
            return new ScheduleAdherenceRow(
                (DateTime)r["Day"], r["LineID"] as string,
                planned, produced, adh);
        }, ("@D", null, daysBack), ("@Today", "PP_LineSchedule.ScheduleDate", ProdCalendar.ProdDateOf(DbClock.Now, cut)), ("@Cut", null, (int)cut.TotalMinutes));
    }

    // ── RPT-009 Report Catalog (static metadata) ─────────────────────────
    // ── RPT-010 Report Builder (화이트리스트 기반 ad-hoc 집계) ─────────────────
    public sealed record AdhocRow(string Key, string? Label, Dictionary<string, decimal> M, string? LabelEn = null);

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

        // 교대는 공통코드 WORK_SHIFT 창(Attribute1)으로 판정한다 — 실적 입력(ProdCalendar.ShiftOf)과 같은 규칙, 창에 안 걸리면 —
        var pars = new List<(string Name, object Value)>();
        string keyExpr, labelExpr = "NULL", labelEnExpr = "NULL", body;
        switch (source)
        {
            case "PROD":
                keyExpr = dim switch
                {
                    "Line"  => "ISNULL(x.LineID,'—')",
                    "Date"  => "CONVERT(varchar(10), x.PD, 120)",
                    "Shift" => $"ISNULL({AMES.Data.Services.ProdCalendar.ShiftCaseSql("x.TS", LoadShifts(), "Sh", pars)},'—')",
                    _       => "ISNULL(it.ItemNo,'—')",
                };
                if (dim == "Item") { labelExpr = "MAX(i.ItemName)"; labelEnExpr = "MAX(i.ItemNameEN)"; }
                // 품목 = LOT 품번 → WO 단계 품번 → WO 품번 (RPT-001 과 같은 규칙)
                body = $"""
                    SELECT {keyExpr} AS K, {labelExpr} AS Lbl, {labelEnExpr} AS LblEn,
                           ISNULL(SUM(x.GoodQty),0) + ISNULL(SUM(x.DefectQty),0) AS Production,
                           ISNULL(SUM(x.GoodQty),0) AS GoodQty,
                           ISNULL(SUM(x.DefectQty),0) AS DefectQty,
                           ISNULL(SUM(x.Entry),0) AS Entries
                    FROM (
                        SELECT r.LineID, r.WoID, r.LotID, r.EntryAt AS TS, COALESCE(r.ProdDate, CAST(r.EntryAt AS DATE)) AS PD, 1 AS Entry,
                               ISNULL(r.GoodQty,0) AS GoodQty, ISNULL(d.Qty, CASE WHEN r.DefectFlag=1 THEN 1 ELSE 0 END) AS DefectQty
                        FROM dbo.PR_ProductionResult r
                        OUTER APPLY (SELECT SUM(dd.Qty) AS Qty FROM dbo.PR_DefectDetail dd WHERE dd.ResultID = r.ResultID) d
                        WHERE COALESCE(r.ProdDate, CAST(r.EntryAt AS DATE)) BETWEEN @F AND @T
                        UNION ALL
                        SELECT tl.LineID, dd.WoID, dd.LotID, dd.DetectedAt, CAST(DATEADD(MINUTE, -@Cut, dd.DetectedAt) AS DATE), 0,
                               0, ISNULL(dd.Qty,0)
                        FROM dbo.PR_DefectDetail dd
                        LEFT JOIN dbo.tbl_Lot tl ON tl.LotID = dd.LotID
                        WHERE dd.ResultID IS NULL
                          AND CAST(DATEADD(MINUTE, -@Cut, dd.DetectedAt) AS DATE) BETWEEN @F AND @T
                    ) x
                    LEFT JOIN dbo.tbl_Lot lot ON lot.LotID = x.LotID
                    LEFT JOIN dbo.PP_WorkOrder w ON w.WoID = x.WoID
                    OUTER APPLY (SELECT TOP 1 rt.ItemNo FROM dbo.PP_WorkOrderRouting rt
                                 WHERE rt.WoID = x.WoID AND rt.LineID = x.LineID AND rt.ItemNo IS NOT NULL ORDER BY rt.StepSeq) st
                    CROSS APPLY (SELECT COALESCE(lot.ItemNo, st.ItemNo, w.ItemNo) AS ItemNo) it
                    LEFT JOIN dbo.MD_Item i ON i.ItemNo = it.ItemNo
                    GROUP BY {keyExpr}
                    """;
                break;
            case "DEFECT":
                keyExpr = dim switch
                {
                    "Process"    => "ISNULL(d.ProcessCode,'—')",
                    "DefectCode" => "ISNULL(d.DefectCode,'—')",
                    "Line"       => "ISNULL(COALESCE(r.LineID, tl.LineID),'—')",
                    _            => "CONVERT(varchar(10), CAST(DATEADD(minute, -@Cut, d.DetectedAt) AS DATE), 120)",
                };
                if (dim == "DefectCode") { labelExpr = "MAX(c.DefectName)"; labelEnExpr = "MAX(c.DefectNameEn)"; }
                body = $"""
                    SELECT {keyExpr} AS K, {labelExpr} AS Lbl, {labelEnExpr} AS LblEn,
                           ISNULL(SUM(ISNULL(d.Qty,0)),0) AS DefectQty, COUNT(*) AS Events
                    FROM dbo.PR_DefectDetail d
                    LEFT JOIN dbo.PR_ProductionResult r ON r.ResultID = d.ResultID
                    LEFT JOIN dbo.tbl_Lot tl ON tl.LotID = d.LotID
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
                if (dim == "Item") { labelExpr = "MAX(i.ItemName)"; labelEnExpr = "MAX(i.ItemNameEN)"; }
                body = $"""
                    SELECT {keyExpr} AS K, {labelExpr} AS Lbl, {labelEnExpr} AS LblEn,
                           COUNT(DISTINCT o.ShipmentOrderID) AS Orders,
                           ISNULL(SUM(ISNULL(l.OrderedQty,0)),0) AS OrderedQty,
                           ISNULL(SUM(ISNULL(l.AllocatedQty,0)),0) AS AllocatedQty
                    FROM dbo.FG_ShipmentOrder o
                    OUTER APPLY OPENJSON(COALESCE(o.ItemsJSON, N'[]')) WITH
                    (
                        ItemNo varchar(20) '$.itemNo',
                        OrderedQty decimal(14,3) '$.orderedQty',
                        AllocatedQty decimal(14,3) '$.allocatedQty'
                    ) l
                    LEFT JOIN dbo.MD_Item i
                      ON i.ItemNo COLLATE DATABASE_DEFAULT = l.ItemNo COLLATE DATABASE_DEFAULT
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
                    SELECT {keyExpr} AS K, {labelExpr} AS Lbl, {labelEnExpr} AS LblEn,
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
                    SELECT {keyExpr} AS K, {labelExpr} AS Lbl, {labelEnExpr} AS LblEn,
                           ISNULL(SUM(ISNULL(d.DurationMin,0)),0) AS DowntimeMin, COUNT(*) AS Events
                    FROM dbo.PP_LineDowntimeLog d
                    WHERE d.StartTS >= @F AND d.StartTS < @T
                    GROUP BY {keyExpr}
                    """;
                break;
        }

        // 생산 실적·불량은 전기일(RPT-001·003 과 같은 하루, 10-13), 출하·OEE·비가동은 달력 날짜
        var cut = ReadDayCutoff();
        (string, string?, object?)[] range = source switch
        {
            "PROD"   => [("@F", "PR_ProductionResult.ProdDate", from.Date), ("@T", "PR_ProductionResult.ProdDate", to.Date),
                         ("@Cut", null, (int)cut.TotalMinutes)],
            "DEFECT" => [("@F", "PR_DefectDetail.DetectedAt", from.Date + cut), ("@T", "PR_DefectDetail.DetectedAt", to.Date.AddDays(1) + cut),
                         ("@Cut", null, (int)cut.TotalMinutes)],
            _        => [("@F", "PR_ProductionResult.EntryAt", from.Date), ("@T", "PR_ProductionResult.EntryAt", to.Date.AddDays(1))],
        };
        var measures = AdhocMeasures[source];
        return Query(body + " ORDER BY K;", r =>
        {
            var m = new Dictionary<string, decimal>();
            foreach (var name in measures) m[name] = r[name] is DBNull ? 0m : Convert.ToDecimal(r[name]);
            return new AdhocRow(r["K"] as string ?? "—", r["Lbl"] as string, m, r["LblEn"] as string);
        }, [.. range, .. pars.Select(x => (x.Name, (string?)null, (object?)x.Value))]);
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
    private List<(string Code, string? Window)> LoadShifts()
        => Query(AMES.Data.Services.ProdCalendar.ShiftsSql, r => (r["CodeValue"] as string ?? "", r["Attribute1"] as string))
               .Where(s => s.Item1.Length > 0).ToList();

    private List<T> Query<T>(string sql, Func<IDataReader, T> map, params (string Name, string? Column, object? Value)[] pars)
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        foreach (var (n, c, v) in pars) ColumnParams.AddFilter(cmd, n, c, v);
        using var rdr  = cmd.ExecuteReader();
        var list = new List<T>();
        while (rdr.Read()) list.Add(map(rdr));
        return list;
    }
}
