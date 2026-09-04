using System.Data;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// Maintenance (MNT) module queries — used by the Office Web.
/// One method per MNT-XXX screen, plus aggregates for MNT-009 dashboard.
/// </summary>
public sealed class MntRepository
{
    private readonly AmesConnectionFactory _f;
    public MntRepository(AmesConnectionFactory f) => _f = f;

    // ── DTOs (MNT-only, kept local) ─────────────────────────────────────
    public sealed record EquipCardRow(string EquipId, string? EquipName, string? LineId,
        string? EquipType, string? MakerModel, DateTime? InstallDate, string? Status,
        decimal? TodayOee, decimal? RuntimeHours, long? CycleCount,
        DateTime? NextPmDate, string? MountedMoldId, int? OpenWoId, DateTime? PlcConnTs);

    public sealed record FailureRow(int FailureId, string? FailureNumber, string? EquipId,
        string? FailureType, string? Symptom, string? Urgency, string? Source,
        string? Status, DateTime? ReportedAt, DateTime? ResolvedAt, int? WorkOrderId,
        string? EquipName = null, string? LineId = null, string? ReportedBy = null,
        string? AndonRefId = null, int? DowntimeId = null);

    public sealed record OeeRow(int OeeLogId, string? OeeRecordNumber, string? EquipId, string? LineId,
        string? AggLevel, DateTime? AggDate, string? ShiftCode,
        int? PlannedTimeMin, int? DowntimeMin,
        decimal? Availability, decimal? Performance, decimal? Quality, decimal? Oee,
        decimal? GoodQty, decimal? TotalQty, string? EquipName = null);

    public sealed record MoldRow(string MoldId, string? MoldName, int? RatedShots, int? CurrentShots,
        int? CavityCount, int? Tonnage, string? StorageLoc, DateTime? LastMaintDate, string? Status,
        int? LifetimeShots, string? MountedEquipId, string? ThresholdLevel, int? RefurbishCount,
        int? CumulativeShots = null);

    public sealed record PmRow(int PmScheduleId, string? PmPlanNumber, string? EquipId, string? PmType,
        string? CycleBasis, int? CycleValue, DateTime? LastPmDate, DateTime? NextDueDate,
        string? ChecklistId, string? AssignedTechId, string? Status, int? ActiveWoId, int DaysToDue);

    public sealed record DowntimeRow(int DowntimeId, string? LineId, DateTime? StartTs, DateTime? EndTs,
        int? DurationMin, string? ReasonCode, string? CauseCode, string? Comment, int? WoId,
        string? LineName = null, string? LineNameEn = null, string? LoggedBy = null, string? AndonId = null,
        string? CreatedBy = null, DateTime? CreatedTs = null, string? ModifiedBy = null, DateTime? ModifiedTs = null);

    public sealed record MwoRow(int WorkOrderId, string? WoNumber, string? WoType, string? EquipId,
        string? Priority, string? SourceType, string? AssignedTechId, string? Status,
        DateTime? IssuedAt, DateTime? StartedAt, DateTime? CompletedAt, int? LaborMinutes,
        int TaskCount, int TaskDone);

    public sealed record SparePartRow(string PartNo, string? PartName, string? Category, string? Uom,
        int? SafetyStock, int? ReorderPoint, int? ReorderQty, int? LeadTimeDays,
        string? StorageLoc, string? SupplierId, int OnHand);

    public sealed record SparePartsTxnRow(int SparePartsTxnId, string? PartNo, string? PartName,
        string? MoveType, int? Qty, int? BalanceAfter, decimal? UnitPrice, string? StorageLoc,
        string? RefType, string? RefId, DateTime? TxnAt, string? Note);

    public sealed record DashboardKpi(int EquipTotal, int EquipRun, int EquipDown, int EquipIdle,
        int OpenFailures, int OpenWos, int PmDueIn7d, int LowStockParts,
        decimal AvgOeeToday, int DowntimeMin24h, DateTime? OeeDate = null);

    // ── MNT-001 Equipment Card ──────────────────────────────────────────
    public List<EquipCardRow> ListEquipment(string? lineId = null)
    {
        const string sql = """
            SELECT  e.EquipID, e.EquipName, e.LineID, e.EquipType, e.MakerModel, e.InstallDate,
                    COALESCE(es.Status, e.Status, 'UNKNOWN') AS Status,
                    es.TodayOEE, es.RuntimeHours, es.CycleCount, es.NextPMDate,
                    es.MountedMoldID, es.OpenWoID, es.PLCConnTS
            FROM    dbo.MD_Equipment e
            LEFT JOIN dbo.MNT_EquipmentStatus es ON es.EquipID = e.EquipID
            WHERE   ISNULL(e.ActiveFlag,1) = 1
              AND  (@L IS NULL OR e.LineID = @L)
            ORDER BY e.LineID, e.EquipID;
            """;
        return Query(sql, r => new EquipCardRow(
            (string)r["EquipID"], r["EquipName"] as string, r["LineID"] as string,
            r["EquipType"] as string, r["MakerModel"] as string, r["InstallDate"] as DateTime?,
            r["Status"] as string, r["TodayOEE"] as decimal?, r["RuntimeHours"] as decimal?,
            r["CycleCount"] as long?, r["NextPMDate"] as DateTime?,
            r["MountedMoldID"] as string, r["OpenWoID"] as int?, r["PLCConnTS"] as DateTime?),
            ("@L", (object?)lineId ?? DBNull.Value));
    }

    // ── MNT-002 Failure Register ────────────────────────────────────────
    private const string FailureSelect = """
        SELECT f.FailureID, f.FailureNumber, f.EquipID, f.FailureType, f.Symptom, f.Urgency,
               f.Source, f.Status, f.ReportedAt, f.ResolvedAt, f.WorkOrderID,
               e.EquipName, e.LineID, f.ReportedBy, f.AndonRefID, f.DowntimeID
        FROM   dbo.MNT_FailureRegister f
        LEFT JOIN dbo.MD_Equipment e ON e.EquipID = f.EquipID
        """;

    private static FailureRow MapFailure(IDataReader r) => new(
        (int)r["FailureID"], r["FailureNumber"] as string, r["EquipID"] as string,
        r["FailureType"] as string, r["Symptom"] as string, r["Urgency"] as string,
        r["Source"] as string, r["Status"] as string,
        r["ReportedAt"] as DateTime?, r["ResolvedAt"] as DateTime?, r["WorkOrderID"] as int?,
        r["EquipName"] as string, r["LineID"] as string, r["ReportedBy"] as string,
        r["AndonRefID"] as string, r["DowntimeID"] as int?);

    public List<FailureRow> ListFailures(int topN = 100, string? statusFilter = null)
    {
        const string sql = $"""
            {FailureSelect}
            WHERE  (@S IS NULL OR f.Status = @S)
            ORDER  BY f.ReportedAt DESC, f.FailureID DESC
            OFFSET 0 ROWS FETCH NEXT @N ROWS ONLY;
            """;
        return Query(sql, MapFailure, ("@N", topN), ("@S", (object?)statusFilter ?? DBNull.Value));
    }

    /// <summary>발생일 기준 기간 조회 (to 는 당일 포함). MNT-002 화면용 — 필터는 화면에서 건다.</summary>
    public List<FailureRow> ListFailuresRange(DateTime from, DateTime to)
    {
        const string sql = $"""
            {FailureSelect}
            WHERE  f.ReportedAt >= @F AND f.ReportedAt < @T
            ORDER  BY f.ReportedAt DESC, f.FailureID DESC;
            """;
        return Query(sql, MapFailure, ("@F", from.Date), ("@T", to.Date.AddDays(1)));
    }

    public (DateTime? Min, DateTime? Max) FailureDateExtent()
    {
        var rows = Query("SELECT MIN(ReportedAt) AS Mn, MAX(ReportedAt) AS Mx FROM dbo.MNT_FailureRegister;",
            r => (r["Mn"] as DateTime?, r["Mx"] as DateTime?));
        return rows.Count > 0 ? rows[0] : (null, null);
    }

    // ── MNT-003 OEE Analysis (equipment level) ──────────────────────────
    private const string OeeSelect = """
        SELECT  o.OEELogID, o.OEERecordNumber, o.EquipID, o.LineID, o.AggLevel, o.AggDate, o.ShiftCode,
                o.PlannedTimeMin, o.DowntimeMin, o.Availability, o.Performance, o.Quality, o.OEE,
                o.GoodQty, o.TotalQty, e.EquipName
        FROM    dbo.MNT_OEELog o
        LEFT JOIN dbo.MD_Equipment e ON e.EquipID = o.EquipID
        """;

    private static OeeRow MapOee(IDataReader r) => new(
        (int)r["OEELogID"], r["OEERecordNumber"] as string, r["EquipID"] as string,
        r["LineID"] as string, r["AggLevel"] as string, r["AggDate"] as DateTime?,
        r["ShiftCode"] as string,
        r["PlannedTimeMin"] as int?, r["DowntimeMin"] as int?,
        r["Availability"] as decimal?, r["Performance"] as decimal?,
        r["Quality"] as decimal?, r["OEE"] as decimal?,
        r["GoodQty"] as decimal?, r["TotalQty"] as decimal?,
        r["EquipName"] as string);

    public List<OeeRow> ListOee(int daysBack = 14, string? equipId = null)
    {
        const string sql = $"""
            {OeeSelect}
            WHERE   o.AggDate >= DATEADD(DAY, -@D, CAST(SYSDATETIME() AS DATE))
              AND  (@E IS NULL OR o.EquipID = @E)
            ORDER BY o.AggDate DESC, o.EquipID, o.ShiftCode;
            """;
        return Query(sql, MapOee, ("@D", daysBack), ("@E", (object?)equipId ?? DBNull.Value));
    }

    /// <summary>집계일 기준 기간 조회 (양끝 포함). MNT-003 화면용 — 라인·설비·교대 필터는 화면에서 건다.</summary>
    public List<OeeRow> ListOeeRange(DateTime from, DateTime to)
    {
        const string sql = $"""
            {OeeSelect}
            WHERE   o.AggDate >= @F AND o.AggDate <= @T
            ORDER BY o.AggDate DESC, o.EquipID, o.ShiftCode;
            """;
        return Query(sql, MapOee, ("@F", from.Date), ("@T", to.Date));
    }

    public (DateTime? Min, DateTime? Max) OeeDateExtent()
    {
        var rows = Query("SELECT MIN(AggDate) AS Mn, MAX(AggDate) AS Mx FROM dbo.MNT_OEELog;",
            r => (r["Mn"] as DateTime?, r["Mx"] as DateTime?));
        return rows.Count > 0 ? rows[0] : (null, null);
    }

    // ── MNT-004 Mold Management ─────────────────────────────────────────
    public List<MoldRow> ListMolds()
    {
        const string sql = """
            SELECT  m.MoldID, m.MoldName, m.RatedShots, m.CurrentShots, m.CavityCount,
                    m.Tonnage, m.StorageLoc, m.LastMaintDate, m.Status,
                    sc.LifetimeShots, sc.MountedEquipID, sc.ThresholdLevel, sc.RefurbishCount,
                    m.CumulativeShots
            FROM    dbo.MD_Mold m
            LEFT JOIN dbo.MNT_MoldShotCount sc ON sc.MoldID = m.MoldID
            ORDER BY m.MoldID;
            """;
        return Query(sql, r => new MoldRow(
            (string)r["MoldID"], r["MoldName"] as string,
            r["RatedShots"] as int?, r["CurrentShots"] as int?, r["CavityCount"] as int?,
            r["Tonnage"] as int?, r["StorageLoc"] as string,
            r["LastMaintDate"] as DateTime?, r["Status"] as string,
            r["LifetimeShots"] as int?, r["MountedEquipID"] as string,
            r["ThresholdLevel"] as string, r["RefurbishCount"] as int?,
            r["CumulativeShots"] as int?));
    }

    // ── MNT-005 PM Schedule ─────────────────────────────────────────────
    public List<PmRow> ListPmSchedule(int daysAhead = 30, int daysBack = 7)
    {
        const string sql = """
            SELECT  PMScheduleID, PMPlanNumber, EquipID, PMType, CycleBasis, CycleValue,
                    LastPMDate, NextDueDate, ChecklistID, AssignedTechID, Status, ActiveWoID,
                    DATEDIFF(DAY, CAST(SYSDATETIME() AS DATE), NextDueDate) AS DaysToDue
            FROM    dbo.MNT_PMSchedule
            WHERE   NextDueDate IS NULL
               OR   NextDueDate BETWEEN DATEADD(DAY, -@B, CAST(SYSDATETIME() AS DATE))
                                    AND DATEADD(DAY,  @A, CAST(SYSDATETIME() AS DATE))
            ORDER BY NextDueDate, EquipID;
            """;
        return Query(sql, r => new PmRow(
            (int)r["PMScheduleID"], r["PMPlanNumber"] as string, r["EquipID"] as string,
            r["PMType"] as string, r["CycleBasis"] as string, r["CycleValue"] as int?,
            r["LastPMDate"] as DateTime?, r["NextDueDate"] as DateTime?,
            r["ChecklistID"] as string, r["AssignedTechID"] as string,
            r["Status"] as string, r["ActiveWoID"] as int?,
            r["DaysToDue"] as int? ?? 0),
            ("@A", daysAhead), ("@B", daysBack));
    }

    // ── MNT-006 Downtime Log ────────────────────────────────────────────
    private const string DowntimeSelect = """
        SELECT  d.DowntimeID, d.LineID, d.StartTS, d.EndTS, d.DurationMin, d.ReasonCode, d.CauseCode,
                d.Comment, d.WoID, l.LineName, l.LineNameEn, d.LoggedBy, d.AndonID,
                d.CreatedBy, d.CreatedTS, d.ModifiedBy, d.ModifiedTS
        FROM    dbo.PP_LineDowntimeLog d
        LEFT JOIN dbo.MD_Line l ON l.LineID = d.LineID
        """;

    private static DowntimeRow MapDowntime(IDataReader r) => new(
        (int)r["DowntimeID"], r["LineID"] as string,
        r["StartTS"] as DateTime?, r["EndTS"] as DateTime?,
        r["DurationMin"] as int?, r["ReasonCode"] as string,
        r["CauseCode"] as string, r["Comment"] as string, r["WoID"] as int?,
        r["LineName"] as string, r["LineNameEn"] as string, r["LoggedBy"] as string, r["AndonID"] as string,
        r["CreatedBy"] as string, r["CreatedTS"] as DateTime?, r["ModifiedBy"] as string, r["ModifiedTS"] as DateTime?);

    public List<DowntimeRow> ListDowntime(int daysBack = 7)
    {
        const string sql = $"""
            {DowntimeSelect}
            WHERE   d.StartTS >= DATEADD(DAY, -@D, SYSDATETIME())
            ORDER BY d.StartTS DESC, d.DowntimeID DESC;
            """;
        return Query(sql, MapDowntime, ("@D", daysBack));
    }

    /// <summary>시작 시각 기준 기간 조회 (to 는 당일 포함). MNT-006 화면용 — 라인·상태·사유 필터는 화면에서 건다.</summary>
    public List<DowntimeRow> ListDowntimeRange(DateTime from, DateTime to)
    {
        const string sql = $"""
            {DowntimeSelect}
            WHERE   d.StartTS >= @F AND d.StartTS < @T
            ORDER BY d.StartTS DESC, d.DowntimeID DESC;
            """;
        return Query(sql, MapDowntime, ("@F", from.Date), ("@T", to.Date.AddDays(1)));
    }

    public (DateTime? Min, DateTime? Max) DowntimeDateExtent()
    {
        var rows = Query("SELECT MIN(StartTS) AS Mn, MAX(StartTS) AS Mx FROM dbo.PP_LineDowntimeLog;",
            r => (r["Mn"] as DateTime?, r["Mx"] as DateTime?));
        return rows.Count > 0 ? rows[0] : (null, null);
    }

    // ── MNT-007 Work Order (MWO) ────────────────────────────────────────
    /// <summary>달력용: 예정일(NextDueDate) 또는 마지막 PM 일(LastPMDate)이 기간 안에 드는 PM. DaysToDue 는 오늘 기준.</summary>
    public List<PmRow> ListPmCalendar(DateTime from, DateTime to)
    {
        const string sql = """
            SELECT  PMScheduleID, PMPlanNumber, EquipID, PMType, CycleBasis, CycleValue,
                    LastPMDate, NextDueDate, ChecklistID, AssignedTechID, Status, ActiveWoID,
                    DATEDIFF(DAY, CAST(SYSDATETIME() AS DATE), NextDueDate) AS DaysToDue
            FROM    dbo.MNT_PMSchedule
            WHERE   (NextDueDate BETWEEN @F AND @T) OR (LastPMDate BETWEEN @F AND @T)
            ORDER BY NextDueDate, EquipID;
            """;
        return Query(sql, r => new PmRow(
            (int)r["PMScheduleID"], r["PMPlanNumber"] as string, r["EquipID"] as string,
            r["PMType"] as string, r["CycleBasis"] as string, r["CycleValue"] as int?,
            r["LastPMDate"] as DateTime?, r["NextDueDate"] as DateTime?,
            r["ChecklistID"] as string, r["AssignedTechID"] as string,
            r["Status"] as string, r["ActiveWoID"] as int?,
            r["DaysToDue"] as int? ?? 0),
            ("@F", from.Date), ("@T", to.Date));
    }

    /// <summary>예정일(NextDueDate)이 기간 안에 드는 PM. DaysToDue 는 기간 시작일 기준 — MNT-009 "예정 예방보전" 패널용.</summary>
    public List<PmRow> ListPmDueRange(DateTime from, DateTime to)
    {
        const string sql = """
            SELECT  PMScheduleID, PMPlanNumber, EquipID, PMType, CycleBasis, CycleValue,
                    LastPMDate, NextDueDate, ChecklistID, AssignedTechID, Status, ActiveWoID,
                    DATEDIFF(DAY, @F, NextDueDate) AS DaysToDue
            FROM    dbo.MNT_PMSchedule
            WHERE   NextDueDate BETWEEN @F AND @T
            ORDER BY NextDueDate, EquipID;
            """;
        return Query(sql, r => new PmRow(
            (int)r["PMScheduleID"], r["PMPlanNumber"] as string, r["EquipID"] as string,
            r["PMType"] as string, r["CycleBasis"] as string, r["CycleValue"] as int?,
            r["LastPMDate"] as DateTime?, r["NextDueDate"] as DateTime?,
            r["ChecklistID"] as string, r["AssignedTechID"] as string,
            r["Status"] as string, r["ActiveWoID"] as int?,
            r["DaysToDue"] as int? ?? 0),
            ("@F", from.Date), ("@T", to.Date));
    }

    public List<MwoRow> ListWorkOrders(int topN = 100, string? statusFilter = null)
    {
        const string sql = """
            SELECT TOP (@N)
                   wo.WorkOrderID, wo.WoNumber, wo.WoType, wo.EquipID, wo.Priority, wo.SourceType,
                   wo.AssignedTechID, wo.Status, wo.IssuedAt, wo.StartedAt, wo.CompletedAt, wo.LaborMinutes,
                   ISNULL(tk.TaskCount, 0)  AS TaskCount,
                   ISNULL(tk.TaskDone , 0)  AS TaskDone
            FROM   dbo.MNT_WorkOrder wo
            LEFT JOIN (
                SELECT WorkOrderID,
                       COUNT(*)                                                       AS TaskCount,
                       SUM(CASE WHEN Result IN ('PASS','OK','DONE') THEN 1 ELSE 0 END) AS TaskDone
                FROM   dbo.MNT_WorkOrderTask
                GROUP  BY WorkOrderID
            ) tk ON tk.WorkOrderID = wo.WorkOrderID
            WHERE  (@S IS NULL OR wo.Status = @S)
            ORDER  BY wo.IssuedAt DESC, wo.WorkOrderID DESC;
            """;
        return Query(sql, r => new MwoRow(
            (int)r["WorkOrderID"], r["WoNumber"] as string, r["WoType"] as string,
            r["EquipID"] as string, r["Priority"] as string, r["SourceType"] as string,
            r["AssignedTechID"] as string, r["Status"] as string,
            r["IssuedAt"] as DateTime?, r["StartedAt"] as DateTime?, r["CompletedAt"] as DateTime?,
            r["LaborMinutes"] as int?,
            r["TaskCount"] as int? ?? 0, r["TaskDone"] as int? ?? 0),
            ("@N", topN), ("@S", (object?)statusFilter ?? DBNull.Value));
    }

    // ── MNT-008 Spare Parts ─────────────────────────────────────────────
    public List<SparePartRow> ListSpareParts()
    {
        // OnHand = last BalanceAfter per part from MNT_SparePartsTxn
        const string sql = """
            SELECT  p.PartNo, p.PartName, p.Category, p.UOM, p.SafetyStock, p.ReorderPoint,
                    p.ReorderQty, p.LeadTimeDays, p.StorageLoc, p.SupplierID,
                    ISNULL(b.OnHand, 0) AS OnHand
            FROM    dbo.MD_SparePart p
            OUTER APPLY (
                SELECT TOP 1 BalanceAfter AS OnHand
                FROM   dbo.MNT_SparePartsTxn t
                WHERE  t.PartNo = p.PartNo
                ORDER  BY t.TxnAt DESC, t.SparePartsTxnID DESC
            ) b
            WHERE   ISNULL(p.ActiveFlag,1) = 1
            ORDER BY p.PartNo;
            """;
        return Query(sql, r => new SparePartRow(
            (string)r["PartNo"], r["PartName"] as string, r["Category"] as string,
            r["UOM"] as string, r["SafetyStock"] as int?, r["ReorderPoint"] as int?,
            r["ReorderQty"] as int?, r["LeadTimeDays"] as int?,
            r["StorageLoc"] as string, r["SupplierID"] as string,
            r["OnHand"] as int? ?? 0));
    }

    public List<SparePartsTxnRow> ListSparePartsTxn(int topN = 50, string? partNo = null)
    {
        const string sql = """
            SELECT TOP (@N)
                   SparePartsTxnID, PartNo, PartName, MoveType, Qty, BalanceAfter,
                   UnitPrice, StorageLoc, RefType, RefID, TxnAt, Note
            FROM   dbo.MNT_SparePartsTxn
            WHERE  (@P IS NULL OR PartNo = @P)
            ORDER  BY TxnAt DESC, SparePartsTxnID DESC;
            """;
        return Query(sql, r => new SparePartsTxnRow(
            (int)r["SparePartsTxnID"], r["PartNo"] as string, r["PartName"] as string,
            r["MoveType"] as string, r["Qty"] as int?, r["BalanceAfter"] as int?,
            r["UnitPrice"] as decimal?, r["StorageLoc"] as string,
            r["RefType"] as string, r["RefID"] as string,
            r["TxnAt"] as DateTime?, r["Note"] as string),
            ("@N", topN), ("@P", (object?)partNo ?? DBNull.Value));
    }

    // ── MNT-009 Dashboard ───────────────────────────────────────────────
    public DashboardKpi GetDashboardKpis()
    {
        const string sql = """
            DECLARE @today DATE = CAST(SYSDATETIME() AS DATE);

            SELECT
              (SELECT COUNT(*) FROM dbo.MD_Equipment WHERE ISNULL(ActiveFlag,1)=1)                                       AS EquipTotal,
              (SELECT COUNT(*) FROM dbo.MNT_EquipmentStatus WHERE Status IN ('RUN','RUNNING','OPERATING'))               AS EquipRun,
              (SELECT COUNT(*) FROM dbo.MNT_EquipmentStatus WHERE Status IN ('DOWN','STOP','FAULT'))                     AS EquipDown,
              (SELECT COUNT(*) FROM dbo.MNT_EquipmentStatus WHERE Status IN ('IDLE','SETUP','READY'))                    AS EquipIdle,
              (SELECT COUNT(*) FROM dbo.MNT_FailureRegister WHERE Status IN ('OPEN','REGISTERED','IN_PROGRESS'))         AS OpenFailures,
              (SELECT COUNT(*) FROM dbo.MNT_WorkOrder       WHERE Status IN ('OPEN','ISSUED','IN_PROGRESS'))             AS OpenWos,
              (SELECT COUNT(*) FROM dbo.MNT_PMSchedule
                 WHERE NextDueDate BETWEEN @today AND DATEADD(DAY, 7, @today))                                           AS PmDueIn7d,
              (SELECT COUNT(*) FROM dbo.MD_SparePart p
                 OUTER APPLY (SELECT TOP 1 BalanceAfter FROM dbo.MNT_SparePartsTxn t
                              WHERE t.PartNo = p.PartNo ORDER BY t.TxnAt DESC, t.SparePartsTxnID DESC) b
                 WHERE ISNULL(b.BalanceAfter,0) <= ISNULL(p.ReorderPoint,0)
                   AND ISNULL(p.ActiveFlag,1)=1)                                                                          AS LowStockParts,
              -- 오늘 집계가 없으면(야간·휴일·집계 지연) 가장 최근 집계일 평균을 쓰고 그 날짜를 함께 돌려준다
              ISNULL((SELECT AVG(OEE) FROM dbo.MNT_OEELog WHERE AggDate = o.LastDate), 0)                                AS AvgOeeToday,
              o.LastDate                                                                                                  AS OeeDate,
              ISNULL((SELECT SUM(DurationMin) FROM dbo.PP_LineDowntimeLog
                       WHERE StartTS >= DATEADD(HOUR, -24, SYSDATETIME())), 0)                                           AS DowntimeMin24h
            FROM (SELECT MAX(AggDate) AS LastDate FROM dbo.MNT_OEELog WHERE AggDate <= @today) o;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        using var rdr  = cmd.ExecuteReader();
        if (!rdr.Read())
            return new DashboardKpi(0, 0, 0, 0, 0, 0, 0, 0, 0m, 0);
        return new DashboardKpi(
            (int)rdr["EquipTotal"], (int)rdr["EquipRun"], (int)rdr["EquipDown"], (int)rdr["EquipIdle"],
            (int)rdr["OpenFailures"], (int)rdr["OpenWos"], (int)rdr["PmDueIn7d"], (int)rdr["LowStockParts"],
            rdr["AvgOeeToday"] as decimal? ?? 0m,
            rdr["DowntimeMin24h"] as int? ?? 0,
            rdr["OeeDate"] as DateTime?);
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
}
