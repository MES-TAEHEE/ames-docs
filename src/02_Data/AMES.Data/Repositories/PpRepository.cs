using System.Data;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// Production Planning (PP) module queries — used by the Office Web.
/// Each method maps to one PP-XX screen. Lookups return rows in display
/// order; aggregates return summary DTOs.
/// </summary>
public sealed class PpRepository
{
    private readonly AmesConnectionFactory _f;
    public PpRepository(AmesConnectionFactory f) => _f = f;

    // ── DTOs (PP-only, kept local to avoid AMES.Contracts churn) ────────
    public sealed record ForecastRow(int ForecastId, string? Batch, string? CustomerId,
        string ItemNo, string? ItemName, DateTime? ForecastMonth, decimal ForecastQty,
        string? Confidence, string? Source);

    public sealed record SoRow(int SoId, string? SoNumber, int? SoLineNo, string? CustomerId,
        string ItemNo, string? ItemName, decimal OrderQty, decimal ShippedQty,
        DateTime? OrderDate, DateTime? RequestedDeliveryDate, DateTime? PromisedDate, string? Status);

    public sealed record SupplyPlanRow(int PlanId, string? PlanCode, DateTime? PlanPeriod,
        string? Status, int LineCount, decimal TotalPlannedQty);

    public sealed record MrpRunRow(int MrpRunId, DateTime? RunAt, DateTime? HorizonStart,
        DateTime? HorizonEnd, int WosConsidered, int PrsCreated, int ShortageCount,
        int DurationMs, string? Status);

    public sealed record PrRow(int PrId, string? PrNumber, string ItemNo, string? ItemName,
        string? VendorId, decimal RequiredQty, DateTime? RequiredDate, string? Status,
        string? SapPoNumber);

    public sealed record WoLite(int WoId, string? WoNumber, string ItemNo, string? ItemName,
        decimal OrderQty, decimal CompletedQty, string? LineId, DateTime? DueDate,
        string? Status, DateTime? ReleasedAt);

    public sealed record CalendarRow(int OverrideId, DateTime? OverrideDate, string? LineId,
        string? DayType, string? PatternId, decimal? CapacityFactor, string? Reason);

    public sealed record ScheduleRow(int ScheduleId, string? LineId, DateTime? ScheduleDate,
        int? WoId, string? WoNumber, int? StartMin, int? EndMin, decimal PlannedQty, string? Status);

    public sealed record OeeRow(int OeeSnapshotId, string? LineId, DateTime? PeriodDate, string? ShiftCode,
        int LoadingMin, int PlannedDownMin, int UnplannedDownMin, int OperatingMin,
        decimal TotalProducedQty, decimal GoodQty,
        decimal Availability, decimal Performance, decimal Quality, decimal OEE);

    public sealed record DowntimeRow(int DowntimeId, string? LineId, DateTime? StartTs, DateTime? EndTs,
        int DurationMin, string? ReasonCode, string? CauseCode, string? Comment, int? WoId);

    public sealed record LineStateRow(string? LineId, DateTime? MinuteTs, string? State,
        string? PlanState, bool RunFlag, int? WoId);

    public sealed record OtdRow(int SoId, string? SoNumber, string? CustomerId, string ItemNo,
        decimal OrderQty, decimal ShippedQty, DateTime? RequestedDeliveryDate,
        DateTime? PromisedDate, int DaysLate, string Status);

    // ── PP-001 Forecast ──────────────────────────────────────────────────
    public List<ForecastRow> ListForecast(int monthsBack = 6, int monthsAhead = 6)
    {
        const string sql = """
            SELECT TOP 200 f.ForecastID, f.ForecastBatch, f.CustomerID,
                   f.ItemNo, i.ItemName, f.ForecastMonth,
                   ISNULL(f.ForecastQty,0) AS ForecastQty,
                   f.Confidence, f.Source
            FROM   dbo.PP_Forecast f
            LEFT JOIN dbo.MD_Item i ON i.ItemNo = f.ItemNo
            WHERE  f.ForecastMonth BETWEEN DATEADD(month, -@B, GETDATE())
                                      AND DATEADD(month,  @A, GETDATE())
            ORDER BY f.ForecastMonth, f.CustomerID, f.ItemNo;
            """;
        return Query(sql, r => new ForecastRow(
            (int)r["ForecastID"], r["ForecastBatch"] as string, r["CustomerID"] as string,
            r["ItemNo"] as string ?? "", r["ItemName"] as string,
            r["ForecastMonth"] as DateTime?,
            r.GetDecimal(r.GetOrdinal("ForecastQty")),
            r["Confidence"] as string, r["Source"] as string),
            ("@B", monthsBack), ("@A", monthsAhead));
    }

    // ── PP-002 SAP Import (customer orders just synced from SAP) ────────
    public List<SoRow> ListSapImports(int daysBack = 30)
    {
        const string sql = """
            SELECT TOP 100 s.SoID, s.SoNumber, s.SoLineNo, s.CustomerID,
                   s.ItemNo, i.ItemName,
                   ISNULL(s.OrderQty,0)   AS OrderQty,
                   ISNULL(s.ShippedQty,0) AS ShippedQty,
                   s.OrderDate, s.RequestedDeliveryDate, s.PromisedDate, s.Status
            FROM   dbo.PP_CustomerOrder s
            LEFT JOIN dbo.MD_Item i ON i.ItemNo = s.ItemNo
            WHERE  s.SapSyncedAt > DATEADD(day, -@D, SYSDATETIME())
               OR  s.OrderDate   > DATEADD(day, -@D, GETDATE())
            ORDER BY s.OrderDate DESC, s.SoNumber;
            """;
        return Query(sql, MapSo, ("@D", daysBack));
    }

    // ── PP-003 Plan Confirm — list supply plans with line count rollup ──
    public List<SupplyPlanRow> ListSupplyPlans(int topN = 30)
    {
        var sql = $$"""
            SELECT TOP ({{topN}}) p.PlanID, p.PlanCode, p.PlanPeriod, p.Status,
                   (SELECT COUNT(*)            FROM dbo.PP_SupplyPlanDetail d WHERE d.PlanID = p.PlanID) AS LineCount,
                   ISNULL((SELECT SUM(d.PlannedQty) FROM dbo.PP_SupplyPlanDetail d WHERE d.PlanID = p.PlanID), 0) AS TotalPlannedQty
            FROM   dbo.PP_SupplyPlan p
            ORDER BY p.PlanPeriod DESC, p.PlanID DESC;
            """;
        return Query(sql, r => new SupplyPlanRow(
            (int)r["PlanID"], r["PlanCode"] as string, r["PlanPeriod"] as DateTime?,
            r["Status"] as string, (int)r["LineCount"],
            r.GetDecimal(r.GetOrdinal("TotalPlannedQty"))));
    }

    // ── PP-004 Work Order: covered by WorkOrderRepository ───────────────

    // ── PP-005 MRP — last N runs ────────────────────────────────────────
    public List<MrpRunRow> ListMrpRuns(int topN = 20)
    {
        var sql = $$"""
            SELECT TOP ({{topN}}) MrpRunID, RunAt, HorizonStart, HorizonEnd,
                   ISNULL(WosConsidered,0) AS WosConsidered,
                   ISNULL(PrsCreated,0)    AS PrsCreated,
                   ISNULL(ShortageCount,0) AS ShortageCount,
                   ISNULL(DurationMs,0)    AS DurationMs,
                   Status
            FROM   dbo.PP_MRPLog
            ORDER BY RunAt DESC, MrpRunID DESC;
            """;
        return Query(sql, r => new MrpRunRow(
            (int)r["MrpRunID"], r["RunAt"] as DateTime?,
            r["HorizonStart"] as DateTime?, r["HorizonEnd"] as DateTime?,
            (int)r["WosConsidered"], (int)r["PrsCreated"], (int)r["ShortageCount"],
            (int)r["DurationMs"], r["Status"] as string));
    }

    // ── PP-006 Purchase Request ─────────────────────────────────────────
    public List<PrRow> ListPurchaseRequests(int topN = 50)
    {
        var sql = $$"""
            SELECT TOP ({{topN}}) p.PrID, p.PrNumber, p.ItemNo, i.ItemName,
                   p.VendorID, ISNULL(p.RequiredQty,0) AS RequiredQty,
                   p.RequiredDate, p.Status, p.SapPoNumber
            FROM   dbo.PP_PurchaseRequest p
            LEFT JOIN dbo.MD_Item i ON i.ItemNo = p.ItemNo
            ORDER BY ISNULL(p.RequiredDate, '9999-01-01'), p.PrID DESC;
            """;
        return Query(sql, r => new PrRow(
            (int)r["PrID"], r["PrNumber"] as string,
            r["ItemNo"] as string ?? "", r["ItemName"] as string,
            r["VendorID"] as string, r.GetDecimal(r.GetOrdinal("RequiredQty")),
            r["RequiredDate"] as DateTime?, r["Status"] as string,
            r["SapPoNumber"] as string));
    }

    // ── PP-007 WO Release — draft/planned WOs awaiting release ─────────
    public List<WoLite> ListReleasable(int topN = 50)
    {
        var sql = $$"""
            SELECT TOP ({{topN}}) w.WoID, w.WoNumber, w.ItemNo, i.ItemName,
                   ISNULL(w.OrderQty,0)     AS OrderQty,
                   ISNULL(w.CompletedQty,0) AS CompletedQty,
                   w.LineID, w.DueDate, ISNULL(w.Status,'Draft') AS Status, w.ReleasedAt
            FROM   dbo.PP_WorkOrder w
            LEFT JOIN dbo.MD_Item i ON i.ItemNo = w.ItemNo
            ORDER BY CASE WHEN ISNULL(w.Status,'Draft') IN ('Draft','Planned') THEN 0 ELSE 1 END,
                     ISNULL(w.DueDate,'9999-01-01'), w.WoID;
            """;
        return Query(sql, MapWoLite);
    }

    /// <summary>PP-007 관리 화면용: 전체 상태 WO 조회. Closed는 최근 <paramref name="recentClosedDays"/>일만.</summary>
    public List<WoLite> ListAllWo(string? lineId = null, string? status = null, int recentClosedDays = 30)
    {
        const string sql = """
            SELECT w.WoID, w.WoNumber, w.ItemNo, i.ItemName,
                   ISNULL(w.OrderQty,0)     AS OrderQty,
                   ISNULL(w.CompletedQty,0) AS CompletedQty,
                   w.LineID, w.DueDate, ISNULL(w.Status,'Draft') AS Status, w.ReleasedAt
            FROM   dbo.PP_WorkOrder w
            LEFT JOIN dbo.MD_Item i ON i.ItemNo = w.ItemNo
            WHERE  (@LineID IS NULL OR w.LineID = @LineID)
              AND  (@Status IS NULL OR ISNULL(w.Status,'Draft') = @Status)
              AND  (ISNULL(w.Status,'Draft') <> 'Closed'
                    OR w.ActualEnd >= DATEADD(day, -@Days, CAST(GETDATE() AS date)))
            ORDER BY CASE ISNULL(w.Status,'Draft')
                          WHEN 'Draft'       THEN 0
                          WHEN 'Planned'     THEN 1
                          WHEN 'Released'    THEN 2
                          WHEN 'In Progress' THEN 3
                          ELSE 4 END,
                     ISNULL(w.DueDate,'9999-01-01'), w.WoID;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@LineID", SqlDbType.VarChar, 20).Value = (object?)lineId  ?? DBNull.Value;
        cmd.Parameters.Add("@Status", SqlDbType.VarChar, 20).Value = (object?)status  ?? DBNull.Value;
        cmd.Parameters.Add("@Days",   SqlDbType.Int          ).Value = recentClosedDays;
        using var rdr  = cmd.ExecuteReader();
        var list = new List<WoLite>();
        while (rdr.Read()) list.Add(MapWoLite(rdr));
        return list;
    }

    /// <summary>WO 상태를 Released로 변경하고 ReleasedAt을 기록합니다 (PP-007).</summary>
    public void ReleaseWo(int woId)
    {
        const string sql = """
            UPDATE dbo.PP_WorkOrder
            SET    Status     = 'Released',
                   ReleasedAt = SYSDATETIME(),
                   ModifiedTS = SYSDATETIME()
            WHERE  WoID   = @WoID
              AND  Status IN ('Draft','Planned');
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@WoID", SqlDbType.Int).Value = woId;
        cmd.ExecuteNonQuery();
    }

    // ── PP-CAL Calendar overrides ───────────────────────────────────────
    public List<CalendarRow> ListCalendarOverrides(int daysBack = 7, int daysAhead = 30)
    {
        const string sql = """
            SELECT TOP 100 OverrideID, OverrideDate, LineID, DayType, PatternID,
                   CapacityFactor, Reason
            FROM   dbo.PP_ProductionCalendarOverride
            WHERE  OverrideDate BETWEEN DATEADD(day, -@B, GETDATE())
                                   AND DATEADD(day,  @A, GETDATE())
            ORDER BY OverrideDate;
            """;
        return Query(sql, r => new CalendarRow(
            (int)r["OverrideID"], r["OverrideDate"] as DateTime?,
            r["LineID"] as string, r["DayType"] as string,
            r["PatternID"] as string, r["CapacityFactor"] as decimal?,
            r["Reason"] as string),
            ("@B", daysBack), ("@A", daysAhead));
    }

    // ── PP-LSB Line Schedule ────────────────────────────────────────────
    public List<ScheduleRow> ListSchedule(int daysAhead = 7)
    {
        const string sql = """
            SELECT TOP 200 s.ScheduleID, s.LineID, s.ScheduleDate,
                   s.WoID, w.WoNumber, s.StartMin, s.EndMin,
                   ISNULL(s.PlannedQty,0) AS PlannedQty, s.Status
            FROM   dbo.PP_LineSchedule s
            LEFT JOIN dbo.PP_WorkOrder w ON w.WoID = s.WoID
            WHERE  s.ScheduleDate BETWEEN CAST(GETDATE() AS DATE)
                                      AND DATEADD(day, @A, CAST(GETDATE() AS DATE))
            ORDER BY s.ScheduleDate, s.LineID, s.StartMin;
            """;
        return Query(sql, r => new ScheduleRow(
            (int)r["ScheduleID"], r["LineID"] as string, r["ScheduleDate"] as DateTime?,
            r["WoID"] as int?, r["WoNumber"] as string,
            r["StartMin"] as int?, r["EndMin"] as int?,
            r.GetDecimal(r.GetOrdinal("PlannedQty")), r["Status"] as string),
            ("@A", daysAhead));
    }

    // ── PP-OEE Line OEE ─────────────────────────────────────────────────
    public List<OeeRow> ListOee(int daysBack = 14)
    {
        const string sql = """
            SELECT TOP 100 OeeSnapshotID, LineID, PeriodDate, ShiftCode,
                   ISNULL(LoadingMin,0)        AS LoadingMin,
                   ISNULL(PlannedDownMin,0)    AS PlannedDownMin,
                   ISNULL(UnplannedDownMin,0)  AS UnplannedDownMin,
                   ISNULL(OperatingMin,0)      AS OperatingMin,
                   ISNULL(TotalProducedQty,0)  AS TotalProducedQty,
                   ISNULL(GoodQty,0)           AS GoodQty,
                   ISNULL(Availability,0)      AS Availability,
                   ISNULL(Performance,0)       AS Performance,
                   ISNULL(Quality,0)           AS Quality,
                   ISNULL(OEE,0)               AS OEE
            FROM   dbo.PP_LineOEE
            WHERE  PeriodDate > DATEADD(day, -@D, GETDATE())
            ORDER BY PeriodDate DESC, LineID, ShiftCode;
            """;
        return Query(sql, r => new OeeRow(
            (int)r["OeeSnapshotID"], r["LineID"] as string,
            r["PeriodDate"] as DateTime?, r["ShiftCode"] as string,
            (int)r["LoadingMin"], (int)r["PlannedDownMin"], (int)r["UnplannedDownMin"],
            (int)r["OperatingMin"],
            r.GetDecimal(r.GetOrdinal("TotalProducedQty")),
            r.GetDecimal(r.GetOrdinal("GoodQty")),
            r.GetDecimal(r.GetOrdinal("Availability")),
            r.GetDecimal(r.GetOrdinal("Performance")),
            r.GetDecimal(r.GetOrdinal("Quality")),
            r.GetDecimal(r.GetOrdinal("OEE"))),
            ("@D", daysBack));
    }

    // ── PP-DTL Downtime Log ─────────────────────────────────────────────
    public List<DowntimeRow> ListDowntime(int daysBack = 7)
    {
        const string sql = """
            SELECT TOP 100 DowntimeID, LineID, StartTS, EndTS,
                   ISNULL(DurationMin,0) AS DurationMin,
                   ReasonCode, CauseCode, Comment, WoID
            FROM   dbo.PP_LineDowntimeLog
            WHERE  StartTS > DATEADD(day, -@D, SYSDATETIME())
            ORDER BY StartTS DESC;
            """;
        return Query(sql, r => new DowntimeRow(
            (int)r["DowntimeID"], r["LineID"] as string,
            r["StartTS"] as DateTime?, r["EndTS"] as DateTime?,
            (int)r["DurationMin"], r["ReasonCode"] as string,
            r["CauseCode"] as string, r["Comment"] as string,
            r["WoID"] as int?),
            ("@D", daysBack));
    }

    /// <summary>PP-DTL 필터 조회 — 라인·기간·상태·사유코드 조합.</summary>
    public List<DowntimeRow> ListDowntimeFiltered(
        string?  lineId       = null,
        DateTime? from        = null,
        DateTime? to          = null,
        string?  status       = null,   // "open" | "closed" | null=all
        string?  reasonCode   = null,
        int      limit        = 500)
    {
        var sql = $"""
            SELECT TOP (@Limit) DowntimeID, LineID, StartTS, EndTS,
                   ISNULL(DurationMin,0) AS DurationMin,
                   ReasonCode, CauseCode, Comment, WoID
            FROM   dbo.PP_LineDowntimeLog
            WHERE  1=1
            {(lineId     != null ? "AND LineID     = @LineId "      : "")}
            {(from       != null ? "AND StartTS   >= @From "        : "")}
            {(to         != null ? "AND StartTS   <  @To "          : "")}
            {(status == "open"   ? "AND EndTS     IS NULL "         : "")}
            {(status == "closed" ? "AND EndTS     IS NOT NULL "     : "")}
            {(reasonCode != null ? "AND ReasonCode = @ReasonCode "  : "")}
            ORDER BY StartTS DESC;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Limit", SqlDbType.Int).Value = limit;
        if (lineId     != null) cmd.Parameters.Add("@LineId",     SqlDbType.VarChar, 20).Value = lineId;
        if (from       != null) cmd.Parameters.Add("@From",       SqlDbType.DateTime2).Value   = from.Value;
        if (to         != null) cmd.Parameters.Add("@To",         SqlDbType.DateTime2).Value   = to.Value.Date.AddDays(1);
        if (reasonCode != null) cmd.Parameters.Add("@ReasonCode", SqlDbType.VarChar, 30).Value = reasonCode;
        using var rdr = cmd.ExecuteReader();
        var list = new List<DowntimeRow>();
        while (rdr.Read())
            list.Add(new DowntimeRow(
                (int)rdr["DowntimeID"], rdr["LineID"] as string,
                rdr["StartTS"] as DateTime?, rdr["EndTS"] as DateTime?,
                (int)rdr["DurationMin"], rdr["ReasonCode"] as string,
                rdr["CauseCode"] as string, rdr["Comment"] as string,
                rdr["WoID"] as int?));
        return list;
    }

    /// <summary>PP-ODM 비계획 비가동 확정 시 PP_LineDowntimeLog에 이벤트 기록.</summary>
    public void AddDowntimeEvent(string lineId, DateTime startTs, DateTime endTs,
        string? reasonCode, string? causeCode, string? comment, int? woId = null)
    {
        const string sql = """
            INSERT INTO dbo.PP_LineDowntimeLog
                   (LineID, StartTS, EndTS, DurationMin, ReasonCode, CauseCode, Comment, WoID)
            VALUES (@LineId, @Start, @End,
                    DATEDIFF(minute, @Start, @End),
                    @Reason, @Cause, @Comment, @WoId);
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@LineId",  SqlDbType.VarChar,   20).Value  = lineId;
        cmd.Parameters.Add("@Start",   SqlDbType.DateTime2).Value      = startTs;
        cmd.Parameters.Add("@End",     SqlDbType.DateTime2).Value      = endTs;
        cmd.Parameters.Add("@Reason",  SqlDbType.VarChar,   30).Value  = (object?)reasonCode ?? DBNull.Value;
        cmd.Parameters.Add("@Cause",   SqlDbType.VarChar,   30).Value  = (object?)causeCode  ?? DBNull.Value;
        cmd.Parameters.Add("@Comment", SqlDbType.NVarChar, 500).Value  = (object?)comment    ?? DBNull.Value;
        cmd.Parameters.Add("@WoId",    SqlDbType.Int).Value            = (object?)woId       ?? DBNull.Value;
        cmd.ExecuteNonQuery();
    }

    /// <summary>PP-ODM 비가동 사유 수정/보완 (사유 미입력 또는 오기입 수정).</summary>
    public void UpdateDowntimeReason(int downtimeId, string? reasonCode, string? causeCode, string? comment)
    {
        const string sql = """
            UPDATE dbo.PP_LineDowntimeLog
            SET ReasonCode = @Reason,
                CauseCode  = @Cause,
                Comment    = @Comment
            WHERE DowntimeID = @Id;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Id",      SqlDbType.Int).Value            = downtimeId;
        cmd.Parameters.Add("@Reason",  SqlDbType.VarChar,   30).Value  = (object?)reasonCode ?? DBNull.Value;
        cmd.Parameters.Add("@Cause",   SqlDbType.VarChar,   30).Value  = (object?)causeCode  ?? DBNull.Value;
        cmd.Parameters.Add("@Comment", SqlDbType.NVarChar, 500).Value  = (object?)comment    ?? DBNull.Value;
        cmd.ExecuteNonQuery();
    }

    /// <summary>PP-DTL/ODM 라인 목록 (PP_LineDowntimeLog 기준).</summary>
    public List<string> ListDowntimeLines()
    {
        const string sql = """
            SELECT DISTINCT LineID FROM dbo.PP_LineDowntimeLog
            WHERE LineID IS NOT NULL ORDER BY LineID;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        using var rdr  = cmd.ExecuteReader();
        var list = new List<string>();
        while (rdr.Read()) list.Add((string)rdr["LineID"]);
        return list;
    }

    // ── PP-ODM Downtime Monitor (last 4 hours of state log per line) ────
    public List<LineStateRow> ListLineStates(int hoursBack = 4)
    {
        const string sql = """
            SELECT TOP 500 LineID, MinuteTS, State, PlanState,
                   ISNULL(RunFlag, 0) AS RunFlag, WoID
            FROM   dbo.PP_LineStateLog
            WHERE  MinuteTS > DATEADD(hour, -@H, SYSDATETIME())
            ORDER BY MinuteTS DESC, LineID;
            """;
        return Query(sql, r => new LineStateRow(
            r["LineID"] as string, r["MinuteTS"] as DateTime?,
            r["State"] as string, r["PlanState"] as string,
            Convert.ToBoolean(r["RunFlag"]), r["WoID"] as int?),
            ("@H", hoursBack));
    }

    // ── PP-OTD On-Time Delivery ─────────────────────────────────────────
    public List<OtdRow> ListOtd(int daysBack = 30)
    {
        const string sql = """
            SELECT TOP 100 SoID, SoNumber, CustomerID, ItemNo,
                   ISNULL(OrderQty,0)   AS OrderQty,
                   ISNULL(ShippedQty,0) AS ShippedQty,
                   RequestedDeliveryDate, PromisedDate,
                   CASE WHEN ShippedQty >= OrderQty AND RequestedDeliveryDate IS NOT NULL
                        THEN DATEDIFF(day, RequestedDeliveryDate, GETDATE())
                        ELSE 0 END AS DaysLate,
                   ISNULL(Status,'?') AS Status
            FROM   dbo.PP_CustomerOrder
            WHERE  RequestedDeliveryDate > DATEADD(day, -@D, GETDATE())
            ORDER BY RequestedDeliveryDate;
            """;
        return Query(sql, r => new OtdRow(
            (int)r["SoID"], r["SoNumber"] as string, r["CustomerID"] as string,
            r["ItemNo"] as string ?? "",
            r.GetDecimal(r.GetOrdinal("OrderQty")),
            r.GetDecimal(r.GetOrdinal("ShippedQty")),
            r["RequestedDeliveryDate"] as DateTime?,
            r["PromisedDate"] as DateTime?,
            (int)r["DaysLate"], r["Status"] as string ?? "?"),
            ("@D", daysBack));
    }

    /// <summary>PP-OTD 필터 조회 — 기간·고객·상태 조합.</summary>
    public List<OtdRow> ListOtdFiltered(
        DateTime? from       = null,
        DateTime? to         = null,
        string?  customerId  = null,
        string?  status      = null,   // "ontime" | "late" | "open" | null=all
        int      limit       = 500)
    {
        var sql = $"""
            SELECT TOP (@Limit)
                   SoID, SoNumber, CustomerID, ItemNo,
                   ISNULL(OrderQty,0)   AS OrderQty,
                   ISNULL(ShippedQty,0) AS ShippedQty,
                   RequestedDeliveryDate, PromisedDate,
                   CASE WHEN ShippedQty >= OrderQty AND RequestedDeliveryDate IS NOT NULL
                        THEN DATEDIFF(day, RequestedDeliveryDate, GETDATE())
                        ELSE 0 END AS DaysLate,
                   ISNULL(Status,'?') AS Status
            FROM   dbo.PP_CustomerOrder
            WHERE  1=1
            {(from       != null ? "AND RequestedDeliveryDate >= @From "       : "")}
            {(to         != null ? "AND RequestedDeliveryDate <  @To "         : "")}
            {(customerId != null ? "AND CustomerID = @Customer "               : "")}
            {(status == "ontime" ? "AND ShippedQty >= OrderQty AND DATEDIFF(day, RequestedDeliveryDate, GETDATE()) <= 0 " : "")}
            {(status == "late"   ? "AND DATEDIFF(day, RequestedDeliveryDate, GETDATE()) > 0 "                            : "")}
            {(status == "open"   ? "AND ShippedQty < OrderQty "                : "")}
            ORDER BY RequestedDeliveryDate DESC;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Limit", SqlDbType.Int).Value = limit;
        if (from       != null) cmd.Parameters.Add("@From",     SqlDbType.Date).Value        = from.Value.Date;
        if (to         != null) cmd.Parameters.Add("@To",       SqlDbType.Date).Value        = to.Value.Date.AddDays(1);
        if (customerId != null) cmd.Parameters.Add("@Customer", SqlDbType.VarChar, 30).Value = customerId;
        using var rdr = cmd.ExecuteReader();
        var list = new List<OtdRow>();
        while (rdr.Read())
            list.Add(new OtdRow(
                (int)rdr["SoID"], rdr["SoNumber"] as string, rdr["CustomerID"] as string,
                rdr["ItemNo"] as string ?? "",
                rdr.GetDecimal(rdr.GetOrdinal("OrderQty")),
                rdr.GetDecimal(rdr.GetOrdinal("ShippedQty")),
                rdr["RequestedDeliveryDate"] as DateTime?,
                rdr["PromisedDate"] as DateTime?,
                (int)rdr["DaysLate"], rdr["Status"] as string ?? "?"));
        return list;
    }

    /// <summary>PP-OTD 고객 목록 (필터 드롭다운용).</summary>
    public List<string> ListOtdCustomers()
    {
        const string sql = """
            SELECT DISTINCT CustomerID FROM dbo.PP_CustomerOrder
            WHERE CustomerID IS NOT NULL ORDER BY CustomerID;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        using var rdr  = cmd.ExecuteReader();
        var list = new List<string>();
        while (rdr.Read()) list.Add((string)rdr["CustomerID"]);
        return list;
    }

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
    private static SoRow MapSo(IDataReader r) => new(
        (int)r["SoID"], r["SoNumber"] as string, r["SoLineNo"] as int?, r["CustomerID"] as string,
        r["ItemNo"] as string ?? "", r["ItemName"] as string,
        r.GetDecimal(r.GetOrdinal("OrderQty")),
        r.GetDecimal(r.GetOrdinal("ShippedQty")),
        r["OrderDate"] as DateTime?, r["RequestedDeliveryDate"] as DateTime?,
        r["PromisedDate"] as DateTime?, r["Status"] as string);
    private static WoLite MapWoLite(IDataReader r) => new(
        (int)r["WoID"], r["WoNumber"] as string,
        r["ItemNo"] as string ?? "", r["ItemName"] as string,
        r.GetDecimal(r.GetOrdinal("OrderQty")),
        r.GetDecimal(r.GetOrdinal("CompletedQty")),
        r["LineID"] as string, r["DueDate"] as DateTime?,
        r["Status"] as string, r["ReleasedAt"] as DateTime?);
}
