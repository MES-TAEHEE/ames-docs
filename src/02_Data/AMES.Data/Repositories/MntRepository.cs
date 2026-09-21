using System.Data;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;
using AMES.Data.Services;

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
        DateTime? NextPmDate, string? MountedMoldId, int? OpenWoId, DateTime? PlcConnTs,
        int? EquipStatusId = null);   // null = MD_Equipment 만 있고 설비 카드(MNT_EquipmentStatus) 미등록

    public sealed record FailureRow(int FailureId, string? FailureNumber, string? EquipId,
        string? FailureType, string? Symptom, string? Severity, string? Source,
        string? Status, DateTime? ReportedAt, DateTime? ResolvedAt, int? WorkOrderId,
        string? EquipName = null, string? LineId = null, string? ReportedBy = null,
        string? AndonRefId = null, int? DowntimeId = null,
        string? WoNumber = null, string? WoStatus = null);   // 연결된 정비 작업지시(있을 때)

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
        string? ChecklistId, string? AssignedTechId, string? Status, int? ActiveWoId, int DaysToDue,
        string? PmClass = null);   // 공통코드 PM_CLASS (EQUIP/MAINT)

    public sealed record DowntimeRow(int DowntimeId, string? LineId, DateTime? StartTs, DateTime? EndTs,
        int? DurationMin, string? ReasonCode, string? CauseCode, string? Comment, int? WoId,
        string? LineName = null, string? LineNameEn = null, string? LoggedBy = null, string? AndonId = null,
        string? CreatedBy = null, DateTime? CreatedTs = null, string? ModifiedBy = null, DateTime? ModifiedTs = null);

    public sealed record MwoRow(int WorkOrderId, string? WoNumber, string? WoType, string? EquipId,
        string? Priority, string? SourceType, string? AssignedTechId, string? Status,
        DateTime? IssuedAt, DateTime? StartedAt, DateTime? CompletedAt, int? LaborMinutes,
        int TaskCount, int TaskDone,
        string? SourceRefId = null, string? ActionDesc = null, string? ChecklistId = null,
        string? PartsUsedJson = null, string? ResultJson = null, DateTime? ClosedAt = null,
        string? EquipName = null);   // ResultJson = 완료 결과(MNT_WorkOrder.ChecklistResultsJSON)

    /// <summary>완료 처리 결과 — 어느 원천이 역방향으로 갱신됐는지 화면 메시지용.</summary>
    public sealed record WoCompleteOutcome(int WorkOrderId, string WoNumber, string? FailureNumber,
        string? PmPlanNumber, DateTime? PmNextDue, string? NextWoNumber);

    /// <summary>MNT_WorkOrder.ChecklistResultsJSON 에 남기는 완료 결과. 전용 컬럼이 없어 JSON 으로 둔다.</summary>
    public sealed record WoResult(string? Result, string? Action, string? RootCause, string? By, DateTime? At);

    public sealed record SparePartRow(string PartNo, string? PartName, string? Category, string? Uom,
        int? SafetyStock, int? ReorderPoint, int? ReorderQty, int? LeadTimeDays,
        string? SupplierId, int OnHand,
        string? ApplicableEquip = null,    // 공통코드 SPAREPARTS_EQUIP (1~9)
        string? SparePartNo = null,        // EOS-SP-{분류}{적용설비}-{yy}{순번4}
        decimal? UnitCost = null,          // 마스터 단가 — 재고 금액 = OnHand × UnitCost
        bool HasImage = false,             // 이미지 바이트는 싣지 않는다 — MasterDataRepository.GetSparePartImage 로 건별 조회
        string? ZoneCode = null, string? Slot = null,   // 보관 구역·칸(공통코드 MNT_ZONE · MNT_SLOT)
        string? Maker = null, bool ActiveFlag = true);  // MD-026 목록과 같은 열 구성용

    // 입출고 이력 — 부품번호·명칭은 마스터 조인. 재고는 처리 전/후 스냅샷만 남긴다.
    public sealed record SparePartsTxnRow(int SparePartsTxnId, string SparePartNo, string? PartNo, string? PartName,
        string MoveType, int Qty, int BalanceBefore, int BalanceAfter,
        string? RefType, string? RefId, DateTime TxnAt, string? Note, string? ActorId,
        long? SparePartItemId = null, string? SerialNo = null, string? VendorName = null,
        string? LocationId = null, string? ExtraLocation = null);

    /// <summary>재고 증감 결과 — 처리 전/후 현재고와 이력 ID.</summary>
    public sealed record StockMoveResult(int SparePartsTxnId, int BalanceBefore, int BalanceAfter);

    public static class SpareMoveTypes { public const string In = "IN"; public const string Out = "OUT"; public const string Adjust = "ADJ"; }

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
                    es.MountedMoldID, es.OpenWoID, es.PLCConnTS, es.EquipStatusID
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
            r["MountedMoldID"] as string, r["OpenWoID"] as int?, r["PLCConnTS"] as DateTime?,
            r["EquipStatusID"] as int?),
            ("@L", (object?)lineId ?? DBNull.Value));
    }

    // ── MNT-001 설비 카드 등록·수정·삭제 — MNT_EquipmentStatus (설비 1대당 1행, MD_Equipment 기준) ──
    public bool EquipStatusExists(string equipId)
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand("SELECT 1 FROM dbo.MNT_EquipmentStatus WHERE EquipID = @E", conn);
        cmd.Parameters.Add("@E", SqlDbType.VarChar, 20).Value = equipId;
        return cmd.ExecuteScalar() is not null;
    }

    /// <summary>설비 카드 등록. LineID 는 MD_Equipment 의 라인을 그대로 옮긴다. 같은 설비가 이미 있으면 예외.</summary>
    public int InsertEquipStatus(string equipId, string? status, decimal? runtimeHours, long? cycleCount,
        DateTime? nextPmDate, string? mountedMoldId, string actor)
    {
        const string sql = """
            IF EXISTS (SELECT 1 FROM dbo.MNT_EquipmentStatus WHERE EquipID = @E)
                THROW 51000, 'Equipment card already registered', 1;
            INSERT INTO dbo.MNT_EquipmentStatus
                (EquipID, LineID, Status, RuntimeHours, CycleCount, NextPMDate, MountedMoldID, CreatedBy, CreatedTS)
            SELECT @E, e.LineID, @St, @Run, @Cyc, @Pm, @Mold, @By, SYSDATETIME()
            FROM   dbo.MD_Equipment e WHERE e.EquipID = @E;
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@E",    SqlDbType.VarChar, 20).Value = equipId;
        cmd.Parameters.Add("@St",   SqlDbType.VarChar, 10).Value = (object?)status ?? DBNull.Value;
        cmd.Parameters.Add("@Run",  SqlDbType.Decimal).Value      = (object?)runtimeHours ?? DBNull.Value;
        cmd.Parameters["@Run"].Precision = 10; cmd.Parameters["@Run"].Scale = 1;
        cmd.Parameters.Add("@Cyc",  SqlDbType.BigInt).Value       = (object?)cycleCount ?? DBNull.Value;
        cmd.Parameters.Add("@Pm",   SqlDbType.Date).Value         = (object?)nextPmDate?.Date ?? DBNull.Value;
        cmd.Parameters.Add("@Mold", SqlDbType.VarChar, 20).Value = (object?)mountedMoldId ?? DBNull.Value;
        cmd.Parameters.Add("@By",   SqlDbType.VarChar, 50).Value = actor;
        var id = cmd.ExecuteScalar();
        if (id is null || id is DBNull) throw new InvalidOperationException($"MD_Equipment '{equipId}' not found");
        return Convert.ToInt32(id);
    }

    /// <summary>설비 카드 수정. EquipID 는 바꾸지 않는다(설비 1대당 1행). OEE·OpenWoID·PLC 연결은 시스템 값이라 손대지 않는다.</summary>
    public void UpdateEquipStatus(int equipStatusId, string? status, decimal? runtimeHours, long? cycleCount,
        DateTime? nextPmDate, string? mountedMoldId, string actor)
    {
        const string sql = """
            UPDATE dbo.MNT_EquipmentStatus
            SET    Status = @St, RuntimeHours = @Run, CycleCount = @Cyc, NextPMDate = @Pm, MountedMoldID = @Mold,
                   ModifiedBy = @By, ModifiedTS = SYSDATETIME()
            WHERE  EquipStatusID = @Id;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Id",   SqlDbType.Int).Value          = equipStatusId;
        cmd.Parameters.Add("@St",   SqlDbType.VarChar, 10).Value = (object?)status ?? DBNull.Value;
        cmd.Parameters.Add("@Run",  SqlDbType.Decimal).Value      = (object?)runtimeHours ?? DBNull.Value;
        cmd.Parameters["@Run"].Precision = 10; cmd.Parameters["@Run"].Scale = 1;
        cmd.Parameters.Add("@Cyc",  SqlDbType.BigInt).Value       = (object?)cycleCount ?? DBNull.Value;
        cmd.Parameters.Add("@Pm",   SqlDbType.Date).Value         = (object?)nextPmDate?.Date ?? DBNull.Value;
        cmd.Parameters.Add("@Mold", SqlDbType.VarChar, 20).Value = (object?)mountedMoldId ?? DBNull.Value;
        cmd.Parameters.Add("@By",   SqlDbType.NVarChar, 450).Value = actor;
        cmd.ExecuteNonQuery();
    }

    public void DeleteEquipStatus(int equipStatusId)
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand("DELETE FROM dbo.MNT_EquipmentStatus WHERE EquipStatusID = @Id", conn);
        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = equipStatusId;
        cmd.ExecuteNonQuery();
    }

    // ── MNT-002 Failure Register ────────────────────────────────────────
    private const string FailureSelect = """
        SELECT f.FailureID, f.FailureNumber, f.EquipID, f.FailureType, f.Symptom, f.Severity,
               f.Source, f.Status, f.ReportedAt, f.ResolvedAt, f.WorkOrderID,
               e.EquipName, e.LineID, f.ReportedBy, f.AndonRefID, f.DowntimeID,
               w.WoNumber, w.Status AS WoStatus
        FROM   dbo.MNT_FailureRegister f
        LEFT JOIN dbo.MD_Equipment  e ON e.EquipID      = f.EquipID
        LEFT JOIN dbo.MNT_WorkOrder w ON w.WorkOrderID  = f.WorkOrderID
        """;

    private static FailureRow MapFailure(IDataReader r) => new(
        (int)r["FailureID"], r["FailureNumber"] as string, r["EquipID"] as string,
        r["FailureType"] as string, r["Symptom"] as string, r["Severity"] as string,
        r["Source"] as string, r["Status"] as string,
        r["ReportedAt"] as DateTime?, r["ResolvedAt"] as DateTime?, r["WorkOrderID"] as int?,
        r["EquipName"] as string, r["LineID"] as string, r["ReportedBy"] as string,
        r["AndonRefID"] as string, r["DowntimeID"] as int?,
        r["WoNumber"] as string, r["WoStatus"] as string);

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

    // ── MNT-004 금형 교체 이력 (PR_MoldChange — POP 금형 교체가 기록, 웹은 조회만) ──
    public sealed record MoldChangeRow(int MoldChangeId, string? EquipId, string? EquipName, string? LineId, string? LineName,
        string? OldMoldId, string? OldMoldName, string? NewMoldId, string? NewMoldName, int? OldMoldFinalShots, int? NewMoldStartShots,
        string? Reason, int? MntWoId, string? WoNumber, int? DowntimeMin, DateTime? StartedAt, DateTime? CompletedAt, string? ChangedBy);

    /// <summary>최근 N건, 시작 시각 내림차순. moldId 를 주면 그 금형이 이전·신규 어느 쪽이든 관련된 이력만(서버 필터 — 최근 N건 안에서 거르면 오래된 이력이 잘린다).</summary>
    public List<MoldChangeRow> ListMoldChanges(int topN = 100, string? moldId = null)
    {
        const string sql = """
            SELECT  TOP (@N) c.MoldChangeID, c.EquipID, e.EquipName, c.LineID, l.LineName,
                    c.OldMoldID, om.MoldName AS OldMoldName, c.NewMoldID, nm.MoldName AS NewMoldName,
                    c.OldMoldFinalShots, c.NewMoldStartShots, c.Reason, c.MntWoID, w.WoNumber,
                    c.DowntimeMin, c.StartedAt, c.CompletedAt, c.ChangedBy
            FROM    dbo.PR_MoldChange c
            LEFT JOIN dbo.MD_Equipment e ON e.EquipID = c.EquipID
            LEFT JOIN dbo.MD_Line      l ON l.LineID  = c.LineID
            LEFT JOIN dbo.MD_Mold     om ON om.MoldID = c.OldMoldID
            LEFT JOIN dbo.MD_Mold     nm ON nm.MoldID = c.NewMoldID
            LEFT JOIN dbo.MNT_WorkOrder w ON w.WorkOrderID = c.MntWoID
            WHERE   (@M IS NULL OR c.OldMoldID = @M OR c.NewMoldID = @M)
            ORDER BY COALESCE(c.StartedAt, c.CreatedTS) DESC, c.MoldChangeID DESC;
            """;
        return Query(sql, r => new MoldChangeRow(
            (int)r["MoldChangeID"], r["EquipID"] as string, r["EquipName"] as string, r["LineID"] as string, r["LineName"] as string,
            r["OldMoldID"] as string, r["OldMoldName"] as string, r["NewMoldID"] as string, r["NewMoldName"] as string,
            r["OldMoldFinalShots"] as int?, r["NewMoldStartShots"] as int?, r["Reason"] as string, r["MntWoID"] as int?, r["WoNumber"] as string,
            r["DowntimeMin"] as int?, r["StartedAt"] as DateTime?, r["CompletedAt"] as DateTime?, r["ChangedBy"] as string),
            ("@N", topN), ("@M", (object?)moldId ?? DBNull.Value));
    }

    // ── MNT-005 PM Schedule ─────────────────────────────────────────────
    /// <summary>pmClass(EQUIP/MAINT) 를 주면 그 분류만 — MNT-005 설비 PM / MNT-010 보전 PM 화면 분리용.</summary>
    public List<PmRow> ListPmSchedule(int daysAhead = 30, int daysBack = 7, string? pmClass = null)
    {
        const string sql = """
            SELECT  PMScheduleID, PMPlanNumber, EquipID, PMClass, PMType, CycleBasis, CycleValue,
                    LastPMDate, NextDueDate, ChecklistID, AssignedTechID, Status, ActiveWoID,
                    DATEDIFF(DAY, CAST(SYSDATETIME() AS DATE), NextDueDate) AS DaysToDue
            FROM    dbo.MNT_PMSchedule
            WHERE   (NextDueDate IS NULL
               OR    NextDueDate BETWEEN DATEADD(DAY, -@B, CAST(SYSDATETIME() AS DATE))
                                     AND DATEADD(DAY,  @A, CAST(SYSDATETIME() AS DATE)))
              AND   (@C IS NULL OR PMClass = @C)
            ORDER BY NextDueDate, EquipID;
            """;
        return Query(sql, r => new PmRow(
            (int)r["PMScheduleID"], r["PMPlanNumber"] as string, r["EquipID"] as string,
            r["PMType"] as string, r["CycleBasis"] as string, r["CycleValue"] as int?,
            r["LastPMDate"] as DateTime?, r["NextDueDate"] as DateTime?,
            r["ChecklistID"] as string, r["AssignedTechID"] as string,
            r["Status"] as string, r["ActiveWoID"] as int?,
            r["DaysToDue"] as int? ?? 0, r["PMClass"] as string),
            ("@A", daysAhead), ("@B", daysBack), ("@C", (object?)pmClass ?? DBNull.Value));
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
    public List<PmRow> ListPmCalendar(DateTime from, DateTime to, string? pmClass = null)
    {
        const string sql = """
            SELECT  PMScheduleID, PMPlanNumber, EquipID, PMClass, PMType, CycleBasis, CycleValue,
                    LastPMDate, NextDueDate, ChecklistID, AssignedTechID, Status, ActiveWoID,
                    DATEDIFF(DAY, CAST(SYSDATETIME() AS DATE), NextDueDate) AS DaysToDue
            FROM    dbo.MNT_PMSchedule
            WHERE   ((NextDueDate BETWEEN @F AND @T) OR (LastPMDate BETWEEN @F AND @T))
              AND   (@C IS NULL OR PMClass = @C)
            ORDER BY NextDueDate, EquipID;
            """;
        return Query(sql, r => new PmRow(
            (int)r["PMScheduleID"], r["PMPlanNumber"] as string, r["EquipID"] as string,
            r["PMType"] as string, r["CycleBasis"] as string, r["CycleValue"] as int?,
            r["LastPMDate"] as DateTime?, r["NextDueDate"] as DateTime?,
            r["ChecklistID"] as string, r["AssignedTechID"] as string,
            r["Status"] as string, r["ActiveWoID"] as int?,
            r["DaysToDue"] as int? ?? 0, r["PMClass"] as string),
            ("@F", from.Date), ("@T", to.Date), ("@C", (object?)pmClass ?? DBNull.Value));
    }

    /// <summary>예정일(NextDueDate)이 기간 안에 드는 PM — MNT-009 "예정 예방보전" 패널용. DaysToDue 는 다른 PM 조회와 같이 오늘(서버 날짜) 기준.</summary>
    public List<PmRow> ListPmDueRange(DateTime from, DateTime to)
    {
        const string sql = """
            SELECT  PMScheduleID, PMPlanNumber, EquipID, PMClass, PMType, CycleBasis, CycleValue,
                    LastPMDate, NextDueDate, ChecklistID, AssignedTechID, Status, ActiveWoID,
                    DATEDIFF(DAY, CAST(SYSDATETIME() AS DATE), NextDueDate) AS DaysToDue
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
            r["DaysToDue"] as int? ?? 0, r["PMClass"] as string),
            ("@F", from.Date), ("@T", to.Date));
    }

    // ── MNT-005/010 PM 실행 이력 (MNT_PMExecution) — 완료 시 AdvancePm 이 남긴 스냅샷 행 ──
    /// <summary>PM 실행 이력 1행. 분류·계획번호·설비·유형·예정일은 완료 시점 스냅샷이라 일정이 지워져도 남는다. WoNumber·PartsUsedJson 은 작업지시 조인.</summary>
    public sealed record PmExecRow(int PmExecutionId, int? PmScheduleId, string? PmPlanNumber, string? EquipId, string? PmClass, string? PmType,
        DateTime? DueDate, int? WorkOrderId, string? WoNumber, DateTime? CompletedAt, int? LaborMinutes, string? TechnicianId,
        string? Result, string? ResultNote, string? PartsUsedJson, string? CreatedBy, DateTime? CreatedTs);

    private const string PmExecSelect = """
        SELECT  e.PMExecutionID, e.PMScheduleID, e.PMPlanNumber, e.EquipID, e.PMClass, e.PMType, e.DueDate,
                e.WorkOrderID, w.WoNumber, e.CompletedAt, e.LaborMinutes, e.TechnicianID, e.Result, e.ResultNote,
                w.PartsUsedJSON, e.CreatedBy, e.CreatedTS
        FROM    dbo.MNT_PMExecution e
        LEFT JOIN dbo.MNT_WorkOrder w ON w.WorkOrderID = e.WorkOrderID
        """;

    private static PmExecRow MapPmExec(IDataReader r) => new(
        (int)r["PMExecutionID"], r["PMScheduleID"] as int?, r["PMPlanNumber"] as string, r["EquipID"] as string,
        r["PMClass"] as string, r["PMType"] as string, r["DueDate"] as DateTime?, r["WorkOrderID"] as int?, r["WoNumber"] as string,
        r["CompletedAt"] as DateTime?, r["LaborMinutes"] as int?, r["TechnicianID"] as string, r["Result"] as string,
        r["ResultNote"] as string, r["PartsUsedJSON"] as string, r["CreatedBy"] as string, r["CreatedTS"] as DateTime?);

    /// <summary>달력용: 완료일이 기간 안(to 는 당일 포함)에 드는 실행 이력. pmClass 를 주면 그 분류만(MNT-005 EQUIP / MNT-010 MAINT).</summary>
    public List<PmExecRow> ListPmExecutions(DateTime from, DateTime to, string? pmClass = null)
    {
        const string sql = $"""
            {PmExecSelect}
            WHERE   e.CompletedAt >= @F AND e.CompletedAt < @T
              AND   (@C IS NULL OR e.PMClass = @C)
            ORDER BY e.CompletedAt, e.EquipID;
            """;
        return Query(sql, MapPmExec, ("@F", from.Date), ("@T", to.Date.AddDays(1)), ("@C", (object?)pmClass ?? DBNull.Value));
    }

    /// <summary>한 일정의 실행 이력, 최신순 상위 N — PM 상세 모달의 이력 목록.</summary>
    public List<PmExecRow> ListPmExecutionsFor(int pmScheduleId, int top = 10)
    {
        const string sql = $"""
            {PmExecSelect}
            WHERE   e.PMScheduleID = @Id
            ORDER BY e.CompletedAt DESC, e.PMExecutionID DESC
            OFFSET 0 ROWS FETCH NEXT @N ROWS ONLY;
            """;
        return Query(sql, MapPmExec, ("@Id", pmScheduleId), ("@N", top));
    }

    // ── MNT-005 설비 PM / MNT-010 보전 PM — 등록·수정·삭제 (테이블 공용, PMClass 로 구분) ──
    public bool PmPlanNumberExists(string planNo, int? excludeId = null)
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(
            "SELECT 1 FROM dbo.MNT_PMSchedule WHERE PMPlanNumber = @N AND (@X IS NULL OR PMScheduleID <> @X)", conn);
        cmd.Parameters.Add("@N", SqlDbType.VarChar, 30).Value = planNo;
        cmd.Parameters.Add("@X", SqlDbType.Int).Value = (object?)excludeId ?? DBNull.Value;
        return cmd.ExecuteScalar() is not null;
    }

    /// <summary>계획번호 자동 채번: PM-yyMM-nnn (그 달 접두어의 최대 순번 + 1). mnt-seed 와 같은 양식.</summary>
    public string NextPmPlanNumber(DateTime today)
    {
        var prefix = $"PM-{today:yyMM}-";
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand("""
            SELECT ISNULL(MAX(TRY_CAST(SUBSTRING(PMPlanNumber, LEN(@P) + 1, 10) AS int)), 0)
            FROM   dbo.MNT_PMSchedule WHERE PMPlanNumber LIKE @P + '%'
            """, conn);
        cmd.Parameters.Add("@P", SqlDbType.VarChar, 30).Value = prefix;
        var next = Convert.ToInt32(cmd.ExecuteScalar()) + 1;
        return $"{prefix}{next:D3}";
    }

    // ── MNT-002 고장 등록·수정·삭제 — 등록 시 정비 작업지시(WoType='CM') 를 같은 트랜잭션으로 발행 ──
    public bool FailureNumberExists(string failNo, int? excludeId = null)
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(
            "SELECT 1 FROM dbo.MNT_FailureRegister WHERE FailureNumber = @N AND (@X IS NULL OR FailureID <> @X)", conn);
        cmd.Parameters.Add("@N", SqlDbType.VarChar, 24).Value = failNo;
        cmd.Parameters.Add("@X", SqlDbType.Int).Value = (object?)excludeId ?? DBNull.Value;
        return cmd.ExecuteScalar() is not null;
    }

    /// <summary>고장번호 자동 채번: FAIL-yyMM-nnn (mnt-seed 와 같은 양식).</summary>
    public string NextFailureNumber(DateTime today)
    {
        var prefix = $"FAIL-{today:yyMM}-";
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand("""
            SELECT ISNULL(MAX(TRY_CAST(SUBSTRING(FailureNumber, LEN(@P) + 1, 10) AS int)), 0)
            FROM   dbo.MNT_FailureRegister WHERE FailureNumber LIKE @P + '%'
            """, conn);
        cmd.Parameters.Add("@P", SqlDbType.VarChar, 24).Value = prefix;
        return $"{prefix}{Convert.ToInt32(cmd.ExecuteScalar()) + 1:D3}";
    }

    // 심각도(DEFECT_SEVERITY) → 작업지시 우선순위. 기존 MWO 데이터 어휘(LOW/MED/HIGH)를 따른다.
    private static string WoPriorityFor(string? severity) => severity?.ToUpperInvariant() switch
    {
        "CRITICAL" => "HIGH",
        "MAJOR"    => "MED",
        _          => "LOW",
    };

    private static string WoDescFor(string failNo, string? failureType, string symptom)
    {
        var s = symptom.Trim();
        if (s.Length > 200) s = s[..200];
        return $"{failureType ?? "CM"} — {failNo}: {s}";
    }

    /// <summary>
    /// 고장 등록 = MNT_FailureRegister 1행 + 정비 작업지시(MNT_WorkOrder, WoType='CM', SourceType='FAILURE') 1행.
    /// 고장 행의 WorkOrderID 가 작업지시를 가리킨다. 우선순위는 심각도에서 정한다.
    /// </summary>
    public (int FailureId, string WoNumber) InsertFailure(string failNo, string equipId, string failureType, string symptom, string severity,
        string? source, DateTime reportedAt, DateTime? resolvedAt, string status, string? reportedBy, string actor)
    {
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            int failId;
            using (var cmd = new SqlCommand("""
                INSERT INTO dbo.MNT_FailureRegister
                    (FailureNumber, EquipID, FailureType, Symptom, Severity, Source, Status, ReportedBy, ReportedAt, ResolvedAt, CreatedBy, CreatedTS)
                VALUES (@No, @Eq, @Type, @Sym, @Sev, @Src, @St, @By, @Rep, @Res, @Actor, SYSDATETIME());
                SELECT CAST(SCOPE_IDENTITY() AS int);
                """, conn, tx))
            {
                cmd.Parameters.Add("@No",    SqlDbType.VarChar,   24).Value = failNo;
                cmd.Parameters.Add("@Eq",    SqlDbType.VarChar,   20).Value = equipId;
                cmd.Parameters.Add("@Type",  SqlDbType.VarChar,   15).Value = failureType;
                cmd.Parameters.Add("@Sym",   SqlDbType.NVarChar, 500).Value = symptom;
                cmd.Parameters.Add("@Sev",   SqlDbType.VarChar,   10).Value = severity;
                cmd.Parameters.Add("@Src",   SqlDbType.VarChar,   15).Value = (object?)source ?? DBNull.Value;
                cmd.Parameters.Add("@St",    SqlDbType.VarChar,   15).Value = status;
                cmd.Parameters.Add("@By",    SqlDbType.NVarChar, 450).Value = (object?)reportedBy ?? DBNull.Value;
                cmd.Parameters.Add("@Rep",   SqlDbType.DateTime2).Value      = reportedAt;
                cmd.Parameters.Add("@Res",   SqlDbType.DateTime2).Value      = (object?)resolvedAt ?? DBNull.Value;
                cmd.Parameters.Add("@Actor", SqlDbType.VarChar,   50).Value = actor;
                failId = Convert.ToInt32(cmd.ExecuteScalar());
            }

            var woNumber = NextMwoNumber(conn, tx, DbClock.Today);
            int woId;
            using (var cmd = new SqlCommand("""
                INSERT INTO dbo.MNT_WorkOrder
                    (WoNumber, WoType, EquipID, Priority, SourceType, SourceRefID, AssignedTechID,
                     ActionDesc, Status, IssuedAt, CreatedBy, CreatedTS)
                VALUES (@Wo, @Type, @Eq, @Pri, 'FAILURE', @Ref, NULL, @Desc, 'ISSUED', SYSDATETIME(), @By, SYSDATETIME());
                SELECT CAST(SCOPE_IDENTITY() AS int);
                """, conn, tx))
            {
                cmd.Parameters.Add("@Wo",   SqlDbType.VarChar,    28).Value = woNumber;
                cmd.Parameters.Add("@Type", SqlDbType.VarChar,    15).Value = MwoTypeCodes.Cm;
                cmd.Parameters.Add("@Eq",   SqlDbType.VarChar,    20).Value = equipId;
                cmd.Parameters.Add("@Pri",  SqlDbType.VarChar,    10).Value = WoPriorityFor(severity);
                cmd.Parameters.Add("@Ref",  SqlDbType.VarChar,    24).Value = failNo;
                cmd.Parameters.Add("@Desc", SqlDbType.NVarChar, 1000).Value = WoDescFor(failNo, failureType, symptom);
                cmd.Parameters.Add("@By",   SqlDbType.VarChar,    50).Value = actor;
                woId = Convert.ToInt32(cmd.ExecuteScalar());
            }

            using (var cmd = new SqlCommand("UPDATE dbo.MNT_FailureRegister SET WorkOrderID = @Wo WHERE FailureID = @Id", conn, tx))
            {
                cmd.Parameters.Add("@Wo", SqlDbType.Int).Value = woId;
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = failId;
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
            return (failId, woNumber);
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>고장 수정. 연결된 작업지시가 착수 전(ISSUED/OPEN)이면 설비·우선순위·설명을 같이 맞춘다.</summary>
    public void UpdateFailure(int id, string failNo, string equipId, string failureType, string symptom, string severity,
        string? source, DateTime reportedAt, DateTime? resolvedAt, string status, string? reportedBy, string actor)
    {
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            using (var cmd = new SqlCommand("""
                UPDATE dbo.MNT_FailureRegister
                SET    FailureNumber = @No, EquipID = @Eq, FailureType = @Type, Symptom = @Sym, Severity = @Sev, Source = @Src,
                       Status = @St, ReportedBy = @By, ReportedAt = @Rep, ResolvedAt = @Res,
                       ModifiedBy = @Actor, ModifiedTS = SYSDATETIME()
                WHERE  FailureID = @Id;
                """, conn, tx))
            {
                cmd.Parameters.Add("@Id",    SqlDbType.Int).Value            = id;
                cmd.Parameters.Add("@No",    SqlDbType.VarChar,   24).Value = failNo;
                cmd.Parameters.Add("@Eq",    SqlDbType.VarChar,   20).Value = equipId;
                cmd.Parameters.Add("@Type",  SqlDbType.VarChar,   15).Value = failureType;
                cmd.Parameters.Add("@Sym",   SqlDbType.NVarChar, 500).Value = symptom;
                cmd.Parameters.Add("@Sev",   SqlDbType.VarChar,   10).Value = severity;
                cmd.Parameters.Add("@Src",   SqlDbType.VarChar,   15).Value = (object?)source ?? DBNull.Value;
                cmd.Parameters.Add("@St",    SqlDbType.VarChar,   15).Value = status;
                cmd.Parameters.Add("@By",    SqlDbType.NVarChar, 450).Value = (object?)reportedBy ?? DBNull.Value;
                cmd.Parameters.Add("@Rep",   SqlDbType.DateTime2).Value      = reportedAt;
                cmd.Parameters.Add("@Res",   SqlDbType.DateTime2).Value      = (object?)resolvedAt ?? DBNull.Value;
                cmd.Parameters.Add("@Actor", SqlDbType.NVarChar, 450).Value = actor;
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new SqlCommand("""
                UPDATE w
                SET    w.EquipID = @Eq, w.Priority = @Pri, w.SourceRefID = @Ref, w.ActionDesc = @Desc,
                       w.ModifiedBy = @By, w.ModifiedTS = SYSDATETIME()
                FROM   dbo.MNT_WorkOrder w
                JOIN   dbo.MNT_FailureRegister f ON f.WorkOrderID = w.WorkOrderID
                WHERE  f.FailureID = @Id AND w.Status IN ('ISSUED', 'OPEN');
                """, conn, tx))
            {
                cmd.Parameters.Add("@Id",   SqlDbType.Int).Value             = id;
                cmd.Parameters.Add("@Eq",   SqlDbType.VarChar,    20).Value = equipId;
                cmd.Parameters.Add("@Pri",  SqlDbType.VarChar,    10).Value = WoPriorityFor(severity);
                cmd.Parameters.Add("@Ref",  SqlDbType.VarChar,    24).Value = failNo;
                cmd.Parameters.Add("@Desc", SqlDbType.NVarChar, 1000).Value = WoDescFor(failNo, failureType, symptom);
                cmd.Parameters.Add("@By",   SqlDbType.NVarChar,  450).Value = actor;
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>고장 삭제. 착수 전(ISSUED/OPEN) 작업지시는 함께 지우고, 진행·완료된 것은 남긴다.</summary>
    public void DeleteFailure(int id)
    {
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            using (var cmd = new SqlCommand("""
                DELETE w
                FROM   dbo.MNT_WorkOrder w
                JOIN   dbo.MNT_FailureRegister f ON f.WorkOrderID = w.WorkOrderID
                WHERE  f.FailureID = @Id AND w.Status IN ('ISSUED', 'OPEN');
                DELETE FROM dbo.MNT_FailureRegister WHERE FailureID = @Id;
                """, conn, tx))
            {
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>
    /// PM 등록 = MNT_PMSchedule 1행 + 정비 작업지시(MNT_WorkOrder, WoType='PM') 1행을 한 트랜잭션으로 만든다.
    /// 작업지시는 ISSUED 상태로 발행되고 PM 행의 ActiveWoID 가 이를 가리킨다.
    /// </summary>
    public (int PmScheduleId, string WoNumber) InsertPm(string pmClass, string planNo, string equipId, string pmType, string? cycleBasis, int? cycleValue,
        DateTime? lastPm, DateTime nextDue, string? checklistId, string? techId, string? status, string actor)
    {
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            int pmId;
            using (var cmd = new SqlCommand("""
                INSERT INTO dbo.MNT_PMSchedule
                    (PMPlanNumber, EquipID, PMClass, PMType, CycleBasis, CycleValue, LastPMDate, NextDueDate,
                     ChecklistID, AssignedTechID, Status, CreatedBy, CreatedTS)
                VALUES (@No, @Eq, @Cls, @Type, @Basis, @Val, @Last, @Due, @Chk, @Tech, @St, @By, SYSDATETIME());
                SELECT CAST(SCOPE_IDENTITY() AS int);
                """, conn, tx))
            {
                cmd.Parameters.Add("@No",    SqlDbType.VarChar,   30).Value = planNo;
                cmd.Parameters.Add("@Eq",    SqlDbType.VarChar,   20).Value = equipId;
                cmd.Parameters.Add("@Cls",   SqlDbType.VarChar,   10).Value = pmClass;
                cmd.Parameters.Add("@Type",  SqlDbType.VarChar,   60).Value = pmType;
                cmd.Parameters.Add("@Basis", SqlDbType.VarChar,   10).Value = (object?)cycleBasis ?? DBNull.Value;
                cmd.Parameters.Add("@Val",   SqlDbType.Int).Value            = (object?)cycleValue ?? DBNull.Value;
                cmd.Parameters.Add("@Last",  SqlDbType.Date).Value           = (object?)lastPm?.Date ?? DBNull.Value;
                cmd.Parameters.Add("@Due",   SqlDbType.Date).Value           = nextDue.Date;
                cmd.Parameters.Add("@Chk",   SqlDbType.VarChar,   20).Value = (object?)checklistId ?? DBNull.Value;
                cmd.Parameters.Add("@Tech",  SqlDbType.NVarChar, 450).Value = (object?)techId ?? DBNull.Value;
                cmd.Parameters.Add("@St",    SqlDbType.VarChar,   10).Value = (object?)status ?? DBNull.Value;
                cmd.Parameters.Add("@By",    SqlDbType.VarChar,   50).Value = actor;
                pmId = Convert.ToInt32(cmd.ExecuteScalar());
            }

            var woNumber = NextMwoNumber(conn, tx, DbClock.Today);
            int woId;
            using (var cmd = new SqlCommand("""
                INSERT INTO dbo.MNT_WorkOrder
                    (WoNumber, WoType, EquipID, Priority, SourceType, SourceRefID, AssignedTechID, ChecklistID,
                     ActionDesc, Status, IssuedAt, CreatedBy, CreatedTS)
                VALUES (@Wo, @Type, @Eq, 'MED', 'PM', @Ref, @Tech, @Chk, @Desc, 'ISSUED', SYSDATETIME(), @By, SYSDATETIME());
                SELECT CAST(SCOPE_IDENTITY() AS int);
                """, conn, tx))
            {
                cmd.Parameters.Add("@Wo",   SqlDbType.VarChar,    28).Value = woNumber;
                cmd.Parameters.Add("@Type", SqlDbType.VarChar,    15).Value = MwoTypeCodes.Pm;
                cmd.Parameters.Add("@Eq",   SqlDbType.VarChar,    20).Value = equipId;
                cmd.Parameters.Add("@Ref",  SqlDbType.VarChar,    24).Value = pmId.ToString();
                cmd.Parameters.Add("@Tech", SqlDbType.NVarChar,  450).Value = (object?)techId ?? DBNull.Value;
                cmd.Parameters.Add("@Chk",  SqlDbType.VarChar,    20).Value = (object?)checklistId ?? DBNull.Value;
                cmd.Parameters.Add("@Desc", SqlDbType.NVarChar, 1000).Value = $"{pmType} PM — {planNo} ({nextDue:yyyy-MM-dd})";
                cmd.Parameters.Add("@By",   SqlDbType.VarChar,    50).Value = actor;
                woId = Convert.ToInt32(cmd.ExecuteScalar());
            }

            using (var cmd = new SqlCommand("UPDATE dbo.MNT_PMSchedule SET ActiveWoID = @Wo WHERE PMScheduleID = @Id", conn, tx))
            {
                cmd.Parameters.Add("@Wo", SqlDbType.Int).Value = woId;
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = pmId;
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
            return (pmId, woNumber);
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>작업지시 번호 자동 채번: MWO-yyMM-nnn (mnt-seed 와 같은 양식).</summary>
    private static string NextMwoNumber(SqlConnection conn, SqlTransaction tx, DateTime today)
    {
        var prefix = $"MWO-{today:yyMM}-";
        using var cmd = new SqlCommand("""
            SELECT ISNULL(MAX(TRY_CAST(SUBSTRING(WoNumber, LEN(@P) + 1, 10) AS int)), 0)
            FROM   dbo.MNT_WorkOrder WHERE WoNumber LIKE @P + '%'
            """, conn, tx);
        cmd.Parameters.Add("@P", SqlDbType.VarChar, 28).Value = prefix;
        return $"{prefix}{Convert.ToInt32(cmd.ExecuteScalar()) + 1:D3}";
    }

    /// <summary>
    /// PMClass 는 화면이 정하므로 바꾸지 않는다(설비 PM 화면에서 보전 PM 으로 옮길 수 없음).
    /// 연결된 작업지시가 아직 착수 전(ISSUED/OPEN)이면 설비·담당자·템플릿·설명을 같이 맞춘다.
    /// </summary>
    public void UpdatePm(int id, string planNo, string equipId, string pmType, string? cycleBasis, int? cycleValue,
        DateTime? lastPm, DateTime nextDue, string? checklistId, string? techId, string? status, string actor)
    {
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            using (var cmd = new SqlCommand("""
                UPDATE dbo.MNT_PMSchedule
                SET    PMPlanNumber = @No, EquipID = @Eq, PMType = @Type, CycleBasis = @Basis, CycleValue = @Val,
                       LastPMDate = @Last, NextDueDate = @Due, ChecklistID = @Chk, AssignedTechID = @Tech, Status = @St,
                       ModifiedBy = @By, ModifiedTS = SYSDATETIME()
                WHERE  PMScheduleID = @Id;
                """, conn, tx))
            {
                cmd.Parameters.Add("@Id",    SqlDbType.Int).Value            = id;
                cmd.Parameters.Add("@No",    SqlDbType.VarChar,   30).Value = planNo;
                cmd.Parameters.Add("@Eq",    SqlDbType.VarChar,   20).Value = equipId;
                cmd.Parameters.Add("@Type",  SqlDbType.VarChar,   60).Value = pmType;
                cmd.Parameters.Add("@Basis", SqlDbType.VarChar,   10).Value = (object?)cycleBasis ?? DBNull.Value;
                cmd.Parameters.Add("@Val",   SqlDbType.Int).Value            = (object?)cycleValue ?? DBNull.Value;
                cmd.Parameters.Add("@Last",  SqlDbType.Date).Value           = (object?)lastPm?.Date ?? DBNull.Value;
                cmd.Parameters.Add("@Due",   SqlDbType.Date).Value           = nextDue.Date;
                cmd.Parameters.Add("@Chk",   SqlDbType.VarChar,   20).Value = (object?)checklistId ?? DBNull.Value;
                cmd.Parameters.Add("@Tech",  SqlDbType.NVarChar, 450).Value = (object?)techId ?? DBNull.Value;
                cmd.Parameters.Add("@St",    SqlDbType.VarChar,   10).Value = (object?)status ?? DBNull.Value;
                cmd.Parameters.Add("@By",    SqlDbType.NVarChar, 450).Value = actor;
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new SqlCommand("""
                UPDATE w
                SET    w.EquipID = @Eq, w.AssignedTechID = @Tech, w.ChecklistID = @Chk,
                       w.ActionDesc = @Desc, w.ModifiedBy = @By, w.ModifiedTS = SYSDATETIME()
                FROM   dbo.MNT_WorkOrder w
                JOIN   dbo.MNT_PMSchedule p ON p.ActiveWoID = w.WorkOrderID
                WHERE  p.PMScheduleID = @Id AND w.Status IN ('ISSUED', 'OPEN');
                """, conn, tx))
            {
                cmd.Parameters.Add("@Id",   SqlDbType.Int).Value             = id;
                cmd.Parameters.Add("@Eq",   SqlDbType.VarChar,    20).Value = equipId;
                cmd.Parameters.Add("@Tech", SqlDbType.NVarChar,  450).Value = (object?)techId ?? DBNull.Value;
                cmd.Parameters.Add("@Chk",  SqlDbType.VarChar,    20).Value = (object?)checklistId ?? DBNull.Value;
                cmd.Parameters.Add("@Desc", SqlDbType.NVarChar, 1000).Value = $"{pmType} PM — {planNo} ({nextDue:yyyy-MM-dd})";
                cmd.Parameters.Add("@By",   SqlDbType.NVarChar,  450).Value = actor;
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>PM 삭제. 연결된 작업지시가 착수 전(ISSUED/OPEN)이면 함께 지우고, 이미 진행·완료된 WO 는 남긴다.</summary>
    public void DeletePm(int id)
    {
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            using (var cmd = new SqlCommand("""
                DELETE w
                FROM   dbo.MNT_WorkOrder w
                JOIN   dbo.MNT_PMSchedule p ON p.ActiveWoID = w.WorkOrderID
                WHERE  p.PMScheduleID = @Id AND w.Status IN ('ISSUED', 'OPEN');
                DELETE FROM dbo.MNT_PMSchedule WHERE PMScheduleID = @Id;
                """, conn, tx))
            {
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    public List<MwoRow> ListWorkOrders(int topN = 100, string? statusFilter = null)
    {
        const string sql = """
            SELECT TOP (@N)
                   wo.WorkOrderID, wo.WoNumber, wo.WoType, wo.EquipID, wo.Priority, wo.SourceType, wo.SourceRefID,
                   wo.AssignedTechID, wo.ChecklistID, wo.ActionDesc, wo.PartsUsedJSON, wo.ChecklistResultsJSON,
                   wo.Status, wo.IssuedAt, wo.StartedAt, wo.CompletedAt, wo.ClosedAt, wo.LaborMinutes,
                   e.EquipName,
                   ISNULL(tk.TaskCount, 0)  AS TaskCount,
                   ISNULL(tk.TaskDone , 0)  AS TaskDone
            FROM   dbo.MNT_WorkOrder wo
            LEFT JOIN dbo.MD_Equipment e ON e.EquipID = wo.EquipID
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
            r["TaskCount"] as int? ?? 0, r["TaskDone"] as int? ?? 0,
            r["SourceRefID"] as string, r["ActionDesc"] as string, r["ChecklistID"] as string,
            r["PartsUsedJSON"] as string, r["ChecklistResultsJSON"] as string, r["ClosedAt"] as DateTime?,
            r["EquipName"] as string),
            ("@N", topN), ("@S", (object?)statusFilter ?? DBNull.Value));
    }

    // ── MNT-007 배정 · 착수 · 완료 (+ MNT-002 수리 완료 · MNT-005/010 PM 완료) ──────
    // 완료의 역방향 반영은 CompleteWoCore 한 곳에만 있다 — 세 화면이 모두 여기를 지나야 고장·PM 이 같은 규칙으로 닫힌다.

    /// <summary>담당자 배정. OPEN 은 ISSUED 로 올린다. PM 발행 WO 면 PM 일정의 담당자도 같이 바꾼다. 완료·마감된 WO 는 거부.</summary>
    public void AssignWorkOrder(int woId, string techId, string actor)
    {
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            int n;
            using (var cmd = new SqlCommand("""
                UPDATE dbo.MNT_WorkOrder
                SET    AssignedTechID = @Tech,
                       Status = CASE WHEN Status = 'OPEN' THEN 'ISSUED' ELSE Status END,
                       ModifiedBy = @By, ModifiedTS = SYSDATETIME()
                WHERE  WorkOrderID = @Id AND ISNULL(Status, '') NOT IN ('COMPLETED', 'CLOSED');
                """, conn, tx))
            {
                cmd.Parameters.Add("@Id",   SqlDbType.Int).Value            = woId;
                cmd.Parameters.Add("@Tech", SqlDbType.NVarChar, 450).Value = techId;
                cmd.Parameters.Add("@By",   SqlDbType.NVarChar, 450).Value = actor;
                n = cmd.ExecuteNonQuery();
            }
            if (n == 0) throw new InvalidOperationException($"Work order #{woId} is already completed or does not exist.");

            using (var cmd = new SqlCommand("""
                UPDATE dbo.MNT_PMSchedule SET AssignedTechID = @Tech, ModifiedBy = @By, ModifiedTS = SYSDATETIME()
                WHERE  ActiveWoID = @Id;
                """, conn, tx))
            {
                cmd.Parameters.Add("@Id",   SqlDbType.Int).Value            = woId;
                cmd.Parameters.Add("@Tech", SqlDbType.NVarChar, 450).Value = techId;
                cmd.Parameters.Add("@By",   SqlDbType.NVarChar, 450).Value = actor;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>착수. OPEN/ISSUED → IN_PROGRESS, StartedAt 기록. 연결된 고장은 IN_PROGRESS 로 따라간다.</summary>
    public void StartWorkOrder(int woId, string actor)
    {
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            int n;
            using (var cmd = new SqlCommand("""
                UPDATE dbo.MNT_WorkOrder
                SET    Status = 'IN_PROGRESS', StartedAt = ISNULL(StartedAt, SYSDATETIME()),
                       ModifiedBy = @By, ModifiedTS = SYSDATETIME()
                WHERE  WorkOrderID = @Id AND ISNULL(Status, 'ISSUED') IN ('OPEN', 'ISSUED');
                """, conn, tx))
            {
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value            = woId;
                cmd.Parameters.Add("@By", SqlDbType.NVarChar, 450).Value = actor;
                n = cmd.ExecuteNonQuery();
            }
            if (n == 0) throw new InvalidOperationException($"Work order #{woId} is not in ISSUED/OPEN state.");

            using (var cmd = new SqlCommand("""
                UPDATE dbo.MNT_FailureRegister SET Status = 'IN_PROGRESS', ModifiedBy = @By, ModifiedTS = SYSDATETIME()
                WHERE  WorkOrderID = @Id AND ISNULL(Status, 'OPEN') IN ('OPEN', 'REGISTERED');
                """, conn, tx))
            {
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value            = woId;
                cmd.Parameters.Add("@By", SqlDbType.NVarChar, 450).Value = actor;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>
    /// 작업지시 완료 = WO 갱신 + 원천 역방향 반영을 한 트랜잭션으로.
    ///   · 고장(WorkOrderID 로 연결): Status RESOLVED · ResolvedAt · MNT_FailureAction(REPAIRED) 1행
    ///   · PM(ActiveWoID 로 연결): MNT_PMExecution 1행 · LastPMDate = 완료일 ·
    ///     기간(TIME) 주기면 NextDueDate = 완료일 + 주기 로 미루고 다음 작업지시를 발행(ActiveWoID 갱신),
    ///     사이클(CYCLE) 주기면 예정일은 그대로 두고 Status = DONE
    /// 결과·조치·근본원인은 MNT_WorkOrder 에 전용 컬럼이 없어 ChecklistResultsJSON 에 JSON(WoResult)으로 남긴다.
    /// </summary>
    public WoCompleteOutcome CompleteWorkOrder(int woId, string result, string actionTaken, string? rootCause,
        int? laborMinutes, string? partsUsed, DateTime completedAt, string? techId, string actor)
    {
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            var o = CompleteWoCore(conn, tx, woId, result, actionTaken, rootCause, laborMinutes, partsUsed, completedAt, techId, actor);
            tx.Commit();
            return o;
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>
    /// MNT-002 수리 완료. 연결된 작업지시가 열려 있으면 그 완료(결과 OK)로 처리해 고장까지 닫고,
    /// 작업지시가 없거나 이미 완료됐으면 고장만 RESOLVED 로 닫고 조치 이력을 남긴다.
    /// </summary>
    public (string FailureNumber, string? WoNumber) ResolveFailure(int failureId, string rootCause, string actionTaken,
        DateTime resolvedAt, int? laborMinutes, string actor)
    {
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            string failNo; string? status, woStatus; int? woId;
            using (var cmd = new SqlCommand("""
                SELECT f.FailureNumber, f.Status, f.WorkOrderID, w.Status AS WoStatus
                FROM   dbo.MNT_FailureRegister f
                LEFT JOIN dbo.MNT_WorkOrder w ON w.WorkOrderID = f.WorkOrderID
                WHERE  f.FailureID = @Id;
                """, conn, tx))
            {
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = failureId;
                using var r = cmd.ExecuteReader();
                if (!r.Read()) throw new InvalidOperationException($"Failure #{failureId} not found.");
                failNo   = r["FailureNumber"] as string ?? $"F#{failureId}";
                status   = r["Status"] as string;
                woId     = r["WorkOrderID"] as int?;
                woStatus = r["WoStatus"] as string;
            }
            if (status is "RESOLVED" or "CLOSED")
                throw new InvalidOperationException($"{failNo} is already resolved.");

            if (woId is int w && woStatus is not (MwoStatusCodes.Completed or MwoStatusCodes.Closed))
            {
                var o = CompleteWoCore(conn, tx, w, MwoResultCodes.Ok, actionTaken, rootCause, laborMinutes, null, resolvedAt, null, actor);
                tx.Commit();
                return (failNo, o.WoNumber);
            }

            ResolveFailureRows(conn, tx, new[] { failureId }, rootCause, actionTaken, resolvedAt, null, actor);
            tx.Commit();
            return (failNo, null);
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>
    /// MNT-005/010 PM 완료. 활성 작업지시가 열려 있으면 그 완료로 처리하고(작업지시 화면과 같은 경로),
    /// 없거나 이미 완료됐으면(구 시연 데이터) 실행 이력·예정일만 갱신하고 기간 주기면 다음 작업지시를 발행한다.
    /// </summary>
    public WoCompleteOutcome CompletePm(int pmId, string result, string? note, int? laborMinutes, DateTime completedAt, string? techId, string actor)
    {
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            PmCore? pm; int? activeWo; string? woStatus;
            using (var cmd = new SqlCommand($"""
                SELECT {PmCoreCols}, w.Status AS WoStatus
                FROM   dbo.MNT_PMSchedule p
                LEFT JOIN dbo.MNT_WorkOrder w ON w.WorkOrderID = p.ActiveWoID
                WHERE  p.PMScheduleID = @Id;
                """, conn, tx))
            {
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = pmId;
                using var r = cmd.ExecuteReader();
                if (!r.Read()) throw new InvalidOperationException($"PM schedule #{pmId} not found.");
                pm       = MapPmCore(r);
                activeWo = r["ActiveWoID"] as int?;
                woStatus = r["WoStatus"] as string;
            }

            WoCompleteOutcome o;
            if (activeWo is int w && woStatus is not (MwoStatusCodes.Completed or MwoStatusCodes.Closed))
                o = CompleteWoCore(conn, tx, w, result, note ?? "", null, laborMinutes, null, completedAt, techId, actor);
            else
            {
                var (planNo, next, nextWo) = AdvancePm(conn, tx, pm, null, result, note, laborMinutes, completedAt, techId, actor);
                o = new WoCompleteOutcome(0, "", null, planNo, next, nextWo);
            }
            tx.Commit();
            return o;
        }
        catch { tx.Rollback(); throw; }
    }

    private WoCompleteOutcome CompleteWoCore(SqlConnection conn, SqlTransaction tx, int woId, string result, string actionTaken,
        string? rootCause, int? laborMinutes, string? partsUsed, DateTime completedAt, string? techId, string actor)
    {
        string woNumber; string? status;
        using (var cmd = new SqlCommand("SELECT WoNumber, Status FROM dbo.MNT_WorkOrder WHERE WorkOrderID = @Id", conn, tx))
        {
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = woId;
            using var r = cmd.ExecuteReader();
            if (!r.Read()) throw new InvalidOperationException($"Work order #{woId} not found.");
            woNumber = r["WoNumber"] as string ?? $"MWO#{woId}";
            status   = r["Status"] as string;
        }
        if (status is MwoStatusCodes.Completed or MwoStatusCodes.Closed)
            throw new InvalidOperationException($"{woNumber} is already completed.");

        var resultJson = System.Text.Json.JsonSerializer.Serialize(new WoResult(result, actionTaken, rootCause, actor, completedAt), JsonReadable);
        var partsJson  = PartsToJson(partsUsed);

        using (var cmd = new SqlCommand("""
            UPDATE dbo.MNT_WorkOrder
            SET    Status = 'COMPLETED', CompletedAt = @At,
                   StartedAt = ISNULL(StartedAt, DATEADD(MINUTE, -ISNULL(@Labor, 0), @At)),
                   LaborMinutes = @Labor, PartsUsedJSON = @Parts, ChecklistResultsJSON = @Res,
                   AssignedTechID = ISNULL(@Tech, AssignedTechID),
                   ModifiedBy = @By, ModifiedTS = SYSDATETIME()
            WHERE  WorkOrderID = @Id;
            """, conn, tx))
        {
            cmd.Parameters.Add("@Id",    SqlDbType.Int).Value            = woId;
            cmd.Parameters.Add("@At",    SqlDbType.DateTime2).Value      = completedAt;
            cmd.Parameters.Add("@Labor", SqlDbType.Int).Value            = (object?)laborMinutes ?? DBNull.Value;
            cmd.Parameters.Add("@Parts", SqlDbType.NVarChar, -1).Value   = (object?)partsJson ?? DBNull.Value;
            cmd.Parameters.Add("@Res",   SqlDbType.NVarChar, -1).Value   = resultJson;
            cmd.Parameters.Add("@Tech",  SqlDbType.NVarChar, 450).Value = (object?)techId ?? DBNull.Value;
            cmd.Parameters.Add("@By",    SqlDbType.NVarChar, 450).Value = actor;
            cmd.ExecuteNonQuery();
        }

        // 고장 역방향 — 이 WO 를 가리키는 열린 고장 전부
        var failures = new List<(int Id, string No)>();
        using (var cmd = new SqlCommand("""
            SELECT FailureID, FailureNumber FROM dbo.MNT_FailureRegister
            WHERE  WorkOrderID = @Id AND ISNULL(Status, 'OPEN') NOT IN ('RESOLVED', 'CLOSED');
            """, conn, tx))
        {
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = woId;
            using var r = cmd.ExecuteReader();
            while (r.Read()) failures.Add(((int)r["FailureID"], r["FailureNumber"] as string ?? $"F#{r["FailureID"]}"));
        }
        if (failures.Count > 0)
            ResolveFailureRows(conn, tx, failures.Select(f => f.Id), rootCause, actionTaken, completedAt, techId, actor);

        // PM 역방향 — 이 WO 를 활성 WO 로 가진 일정(구 데이터는 SourceRefID 로 보조 매칭)
        PmCore? pm = null;
        using (var cmd = new SqlCommand($"""
            SELECT {PmCoreCols}
            FROM   dbo.MNT_PMSchedule p
            WHERE  p.ActiveWoID = @Id
               OR (p.ActiveWoID IS NULL AND EXISTS (
                      SELECT 1 FROM dbo.MNT_WorkOrder w
                      WHERE  w.WorkOrderID = @Id AND w.SourceType = 'PM' AND TRY_CAST(w.SourceRefID AS int) = p.PMScheduleID))
            ORDER  BY CASE WHEN p.ActiveWoID = @Id THEN 0 ELSE 1 END;
            """, conn, tx))
        {
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = woId;
            using var r = cmd.ExecuteReader();
            if (r.Read()) pm = MapPmCore(r);
        }
        string? pmNo = null; DateTime? nextDue = null; string? nextWo = null;
        if (pm is not null)
            (pmNo, nextDue, nextWo) = AdvancePm(conn, tx, pm, woId, result, actionTaken, laborMinutes, completedAt, techId, actor);

        return new WoCompleteOutcome(woId, woNumber, failures.Count > 0 ? string.Join(", ", failures.Select(f => f.No)) : null, pmNo, nextDue, nextWo);
    }

    /// <summary>고장 RESOLVED + 조치 이력(MNT_FailureAction 'REPAIRED') — 안돈의 ARRIVED/ACK 와 같은 테이블에 쌓인다.</summary>
    private static void ResolveFailureRows(SqlConnection conn, SqlTransaction tx, IEnumerable<int> failureIds,
        string? rootCause, string actionTaken, DateTime resolvedAt, string? techId, string actor)
    {
        var desc = string.IsNullOrWhiteSpace(rootCause) ? actionTaken.Trim() : $"{rootCause.Trim()} → {actionTaken.Trim()}";
        if (desc.Length > 500) desc = desc[..500];
        foreach (var id in failureIds)
        {
            using var cmd = new SqlCommand("""
                UPDATE dbo.MNT_FailureRegister
                SET    Status = 'RESOLVED', ResolvedAt = @At, ModifiedBy = @By, ModifiedTS = SYSDATETIME()
                WHERE  FailureID = @Id;
                INSERT INTO dbo.MNT_FailureAction (FailureID, ActionType, Description, TechnicianID, ActionAt, CreatedBy, CreatedTS)
                VALUES (@Id, 'REPAIRED', @Desc, @Tech, @At, @By, SYSDATETIME());
                """, conn, tx);
            cmd.Parameters.Add("@Id",   SqlDbType.Int).Value            = id;
            cmd.Parameters.Add("@At",   SqlDbType.DateTime2).Value      = resolvedAt;
            cmd.Parameters.Add("@Desc", SqlDbType.NVarChar, 500).Value = desc;
            cmd.Parameters.Add("@Tech", SqlDbType.NVarChar, 450).Value = (object?)techId ?? DBNull.Value;
            cmd.Parameters.Add("@By",   SqlDbType.NVarChar, 450).Value = actor;
            cmd.ExecuteNonQuery();
        }
    }

    private sealed record PmCore(int Id, string? PlanNo, string? EquipId, string? PmType, string? CycleBasis, int? CycleValue,
        string? ChecklistId, string? TechId, string? PmClass, DateTime? NextDue);

    private const string PmCoreCols =
        "p.PMScheduleID, p.PMPlanNumber, p.EquipID, p.PMType, p.CycleBasis, p.CycleValue, p.ChecklistID, p.AssignedTechID, p.ActiveWoID, p.PMClass, p.NextDueDate";

    private static PmCore MapPmCore(IDataReader r) => new(
        (int)r["PMScheduleID"], r["PMPlanNumber"] as string, r["EquipID"] as string, r["PMType"] as string,
        r["CycleBasis"] as string, r["CycleValue"] as int?, r["ChecklistID"] as string, r["AssignedTechID"] as string,
        r["PMClass"] as string, r["NextDueDate"] as DateTime?);

    /// <summary>
    /// PM 실행 이력 + 일정 전진. 기간(TIME) 주기면 다음 예정일 = 완료일 + 주기 로 두고 다음 작업지시(ISSUED)를 발행해 ActiveWoID 를 잇는다.
    /// 사이클(CYCLE) 주기는 날짜로 다음 예정을 정할 수 없어 예정일은 그대로, Status = DONE 으로 둔다(ActiveWoID 는 비움).
    /// 이력 행에는 분류·계획번호·설비·유형·이행한 예정일을 스냅샷으로 남긴다 — 일정이 지워지거나 바뀌어도 달력의 완료 칩은 그대로다.
    /// </summary>
    private (string? PlanNo, DateTime? NextDue, string? NextWoNumber) AdvancePm(SqlConnection conn, SqlTransaction tx, PmCore pm, int? woId,
        string result, string? note, int? laborMinutes, DateTime completedAt, string? techId, string actor)
    {
        var tech = techId ?? pm.TechId;
        var resultNote = note is { Length: > 500 } ? note[..500] : note;

        using (var cmd = new SqlCommand("""
            INSERT INTO dbo.MNT_PMExecution
                (PMScheduleID, PMPlanNumber, EquipID, PMClass, PMType, DueDate, WorkOrderID, CompletedAt, LaborMinutes,
                 TechnicianID, Result, ResultNote, CreatedBy, CreatedTS)
            VALUES (@Pm, @PlanNo, @Eq, @Cls, @Type, @Due, @Wo, @At, @Labor, @Tech, @Res, @Note, @By, SYSDATETIME());
            """, conn, tx))
        {
            cmd.Parameters.Add("@Pm",     SqlDbType.Int).Value          = pm.Id;
            cmd.Parameters.Add("@Cls",    SqlDbType.VarChar, 10).Value  = (object?)pm.PmClass ?? DBNull.Value;
            cmd.Parameters.Add("@PlanNo", SqlDbType.VarChar, 30).Value  = (object?)pm.PlanNo ?? DBNull.Value;
            cmd.Parameters.Add("@Eq",     SqlDbType.VarChar, 20).Value  = (object?)pm.EquipId ?? DBNull.Value;
            cmd.Parameters.Add("@Type",   SqlDbType.VarChar, 60).Value  = (object?)pm.PmType ?? DBNull.Value;
            cmd.Parameters.Add("@Due",    SqlDbType.Date).Value         = (object?)pm.NextDue?.Date ?? DBNull.Value;
            cmd.Parameters.Add("@Wo",     SqlDbType.Int).Value          = (object?)woId ?? DBNull.Value;
            cmd.Parameters.Add("@Labor",  SqlDbType.Int).Value          = (object?)laborMinutes ?? DBNull.Value;
            cmd.Parameters.Add("@At",   SqlDbType.DateTime2).Value      = completedAt;
            cmd.Parameters.Add("@Tech", SqlDbType.NVarChar, 450).Value = (object?)tech ?? DBNull.Value;
            cmd.Parameters.Add("@Res",  SqlDbType.VarChar,   15).Value = result;
            cmd.Parameters.Add("@Note", SqlDbType.NVarChar, 500).Value = (object?)resultNote ?? DBNull.Value;
            cmd.Parameters.Add("@By",   SqlDbType.VarChar,   50).Value = actor.Length > 50 ? actor[..50] : actor;
            cmd.ExecuteNonQuery();
        }

        DateTime? next = string.Equals(pm.CycleBasis, "TIME", StringComparison.OrdinalIgnoreCase) && pm.CycleValue is > 0
            ? completedAt.Date.AddDays(pm.CycleValue.Value) : null;

        using (var cmd = new SqlCommand("""
            UPDATE dbo.MNT_PMSchedule
            SET    LastPMDate = @Last, NextDueDate = ISNULL(@Next, NextDueDate), Status = @St, ActiveWoID = NULL,
                   AssignedTechID = ISNULL(@Tech, AssignedTechID), ModifiedBy = @By, ModifiedTS = SYSDATETIME()
            WHERE  PMScheduleID = @Pm;
            """, conn, tx))
        {
            cmd.Parameters.Add("@Pm",   SqlDbType.Int).Value            = pm.Id;
            cmd.Parameters.Add("@Last", SqlDbType.Date).Value           = completedAt.Date;
            cmd.Parameters.Add("@Next", SqlDbType.Date).Value           = (object?)next ?? DBNull.Value;
            cmd.Parameters.Add("@St",   SqlDbType.VarChar,   10).Value = next is null ? "DONE" : "OK";
            cmd.Parameters.Add("@Tech", SqlDbType.NVarChar, 450).Value = (object?)tech ?? DBNull.Value;
            cmd.Parameters.Add("@By",   SqlDbType.NVarChar, 450).Value = actor;
            cmd.ExecuteNonQuery();
        }

        if (next is not { } nd) return (pm.PlanNo, null, null);

        var nextWoNumber = NextMwoNumber(conn, tx, DbClock.Today);
        int nextWoId;
        using (var cmd = new SqlCommand("""
            INSERT INTO dbo.MNT_WorkOrder
                (WoNumber, WoType, EquipID, Priority, SourceType, SourceRefID, AssignedTechID, ChecklistID,
                 ActionDesc, Status, IssuedAt, CreatedBy, CreatedTS)
            VALUES (@Wo, @Type, @Eq, 'MED', 'PM', @Ref, @Tech, @Chk, @Desc, 'ISSUED', SYSDATETIME(), @By, SYSDATETIME());
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, conn, tx))
        {
            cmd.Parameters.Add("@Wo",   SqlDbType.VarChar,    28).Value = nextWoNumber;
            cmd.Parameters.Add("@Type", SqlDbType.VarChar,    15).Value = MwoTypeCodes.Pm;
            cmd.Parameters.Add("@Eq",   SqlDbType.VarChar,    20).Value = (object?)pm.EquipId ?? DBNull.Value;
            cmd.Parameters.Add("@Ref",  SqlDbType.VarChar,    24).Value = pm.Id.ToString();
            cmd.Parameters.Add("@Tech", SqlDbType.NVarChar,  450).Value = (object?)tech ?? DBNull.Value;
            cmd.Parameters.Add("@Chk",  SqlDbType.VarChar,    20).Value = (object?)pm.ChecklistId ?? DBNull.Value;
            cmd.Parameters.Add("@Desc", SqlDbType.NVarChar, 1000).Value = $"{pm.PmType} PM — {pm.PlanNo} ({nd:yyyy-MM-dd})";
            cmd.Parameters.Add("@By",   SqlDbType.VarChar,    50).Value = actor.Length > 50 ? actor[..50] : actor;
            nextWoId = Convert.ToInt32(cmd.ExecuteScalar());
        }
        using (var cmd = new SqlCommand("UPDATE dbo.MNT_PMSchedule SET ActiveWoID = @Wo WHERE PMScheduleID = @Pm", conn, tx))
        {
            cmd.Parameters.Add("@Wo", SqlDbType.Int).Value = nextWoId;
            cmd.Parameters.Add("@Pm", SqlDbType.Int).Value = pm.Id;
            cmd.ExecuteNonQuery();
        }
        return (pm.PlanNo, nd, nextWoNumber);
    }

    /// <summary>사용 부품 입력(줄바꿈·쉼표·세미콜론 구분)을 JSON 문자열 배열로. 비어 있으면 null.</summary>
    private static string? PartsToJson(string? partsUsed)
    {
        if (string.IsNullOrWhiteSpace(partsUsed)) return null;
        var parts = partsUsed.Split(new[] { '\n', '\r', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? null : System.Text.Json.JsonSerializer.Serialize(parts, JsonReadable);
    }

    // 한글을 \uXXXX 로 이스케이프하지 않는다 — DB 에서 바로 읽히게
    private static readonly System.Text.Json.JsonSerializerOptions JsonReadable =
        new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>ChecklistResultsJSON 을 완료 결과로 해석. 다른 형식(체크리스트 JSON)이면 null.</summary>
    public static WoResult? ParseResult(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return System.Text.Json.JsonSerializer.Deserialize<WoResult>(json); }
        catch { return null; }
    }

    // ── MNT-008 Spare Parts ─────────────────────────────────────────────
    public List<SparePartRow> ListSpareParts()
    {
        // 현재고는 마스터 OnHandQty (입출고가 한 트랜잭션으로 갱신)
        const string sql = """
            SELECT  p.PartNo, p.PartName, p.Category, p.UOM, p.SafetyStock, p.ReorderPoint,
                    p.ReorderQty, p.LeadTimeDays, p.SupplierID,
                    p.OnHandQty AS OnHand, p.ApplicableEquip, p.SparePartNo, p.UnitCost,
                    CAST(CASE WHEN p.SparePartImage IS NULL THEN 0 ELSE 1 END AS bit) AS HasImage,
                    p.ZoneCode, p.Slot, p.Maker, CAST(ISNULL(p.ActiveFlag,1) AS bit) AS ActiveFlag
            FROM    dbo.MD_SparePart p
            WHERE   ISNULL(p.ActiveFlag,1) = 1
            ORDER BY p.SparePartNo;
            """;
        return Query(sql, r => new SparePartRow(
            (string)r["PartNo"], r["PartName"] as string, r["Category"] as string,
            r["UOM"] as string, r["SafetyStock"] as int?, r["ReorderPoint"] as int?,
            r["ReorderQty"] as int?, r["LeadTimeDays"] as int?,
            r["SupplierID"] as string,
            r["OnHand"] as int? ?? 0, r["ApplicableEquip"] as string, r["SparePartNo"] as string,
            r["UnitCost"] as decimal?,
            r["HasImage"] is bool hi && hi,
            r["ZoneCode"] as string, r["Slot"] as string, r["Maker"] as string,
            r["ActiveFlag"] is bool af ? af : true));
    }

    /// <summary>입출고 이력 최근 N건. sparePartNo 를 주면 그 부품만.</summary>
    public List<SparePartsTxnRow> ListSparePartsTxn(int topN = 50, string? sparePartNo = null)
    {
        const string sql = """
            SELECT TOP (@N)
                   t.SparePartsTxnID, t.SparePartNo, p.PartNo, p.PartName, t.MoveType, t.Qty, t.BalanceBefore, t.BalanceAfter,
                   t.RefType, t.RefID, t.TxnAt, t.Note, t.ActorID,
                   t.SparePartItemID, i.SerialNo, v.VendorName,
                   COALESCE(i.LocationID,
                       CASE WHEN p.ZoneCode='SP_EXTRA' THEN 'SP-EXTRA'
                            WHEN NULLIF(p.ZoneCode,'') IS NOT NULL AND NULLIF(p.Slot,'') IS NOT NULL THEN CONCAT(p.ZoneCode,'-',p.Slot)
                            ELSE COALESCE(NULLIF(p.ZoneCode,''), NULLIF(p.Slot,'')) END) AS LocationID,
                   CASE WHEN p.ZoneCode='SP_EXTRA' THEN p.ExtraLocation END AS ExtraLocation
            FROM   dbo.MNT_SparePartsTxn t
            LEFT JOIN dbo.MD_SparePart p ON p.SparePartNo = t.SparePartNo
            LEFT JOIN dbo.MNT_SparePartItem i ON i.SparePartItemID = t.SparePartItemID
            LEFT JOIN dbo.MD_Vendor v ON v.VendorID = p.SupplierID
            WHERE  (@P IS NULL OR t.SparePartNo = @P)
            ORDER  BY t.TxnAt DESC, t.SparePartsTxnID DESC;
            """;
        return Query(sql, r => new SparePartsTxnRow(
            (int)r["SparePartsTxnID"], (string)r["SparePartNo"], r["PartNo"] as string, r["PartName"] as string,
            (string)r["MoveType"], (int)r["Qty"], (int)r["BalanceBefore"], (int)r["BalanceAfter"],
            r["RefType"] as string, r["RefID"] as string,
            (DateTime)r["TxnAt"], r["Note"] as string, r["ActorID"] as string,
            r["SparePartItemID"] is long itemId ? itemId : null,
            r["SerialNo"] as string, r["VendorName"] as string,
            r["LocationID"] as string, r["ExtraLocation"] as string),
            ("@N", topN), ("@P", (object?)sparePartNo ?? DBNull.Value));
    }

    /// <summary>
    /// 재고 증감 — 마스터 OnHandQty 갱신 + 이력 1행을 한 트랜잭션으로. 현재고의 정본은 마스터이고 이력은 전/후 스냅샷이다.
    ///   IN  : qty 만큼 증가(qty > 0)   OUT : qty 만큼 감소(qty > 0, 재고 부족이면 거부)   ADJ : qty 부호대로 보정(결과가 음수면 거부)
    /// </summary>
    public StockMoveResult AdjustSparePartStock(string sparePartNo, string moveType, int qty,
        string? refType, string? refId, string? note, string actor)
    {
        moveType = moveType?.Trim().ToUpperInvariant() ?? "";
        if (moveType is not (SpareMoveTypes.In or SpareMoveTypes.Out or SpareMoveTypes.Adjust))
            throw new ArgumentException($"Unknown move type '{moveType}'.");
        if (moveType != SpareMoveTypes.Adjust && qty <= 0)
            throw new ArgumentException("Qty must be positive for IN/OUT.");

        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            int before;
            using (var cmd = new SqlCommand("SELECT OnHandQty FROM dbo.MD_SparePart WITH (UPDLOCK, HOLDLOCK) WHERE SparePartNo = @SP", conn, tx))
            {
                cmd.Parameters.Add("@SP", SqlDbType.VarChar, 16).Value = sparePartNo;
                var o = cmd.ExecuteScalar();
                if (o is null) throw new InvalidOperationException($"Spare part {sparePartNo} not found.");
                before = Convert.ToInt32(o);
            }
            var delta = moveType switch { SpareMoveTypes.In => qty, SpareMoveTypes.Out => -qty, _ => qty };
            if (refType == "PDA_ADJUST")
            {
                using var received = new SqlCommand("""
                    SELECT COUNT(*) FROM dbo.MNT_SparePartsTxn
                    WHERE SparePartNo=@SP AND MoveType='IN' AND RefType='PDA';
                    """, conn, tx);
                received.Parameters.Add("@SP", SqlDbType.VarChar, 16).Value = sparePartNo;
                if (Convert.ToInt32(received.ExecuteScalar()) == 0)
                    throw new InvalidOperationException("This spare part has not been received yet. Receive it before adjustment.");
            }
            var after = before + delta;
            if (after < 0) throw new InvalidOperationException($"Insufficient stock for {sparePartNo}: on hand {before}, requested {-delta}.");

            using (var cmd = new SqlCommand("UPDATE dbo.MD_SparePart SET OnHandQty = @A, ModifiedBy = @By, ModifiedTS = SYSDATETIME() WHERE SparePartNo = @SP", conn, tx))
            {
                cmd.Parameters.Add("@SP", SqlDbType.VarChar,   16).Value = sparePartNo;
                cmd.Parameters.Add("@A",  SqlDbType.Int).Value           = after;
                cmd.Parameters.Add("@By", SqlDbType.NVarChar, 450).Value = actor;
                cmd.ExecuteNonQuery();
            }
            int txnId;
            using (var cmd = new SqlCommand("""
                INSERT INTO dbo.MNT_SparePartsTxn (SparePartNo, MoveType, Qty, BalanceBefore, BalanceAfter, RefType, RefID, Note, TxnAt, ActorID, CreatedBy, CreatedTS)
                VALUES (@SP, @MT, @Q, @B, @A, @RT, @RID, @Note, SYSDATETIME(), @Actor, @By, SYSDATETIME());
                SELECT CAST(SCOPE_IDENTITY() AS int);
                """, conn, tx))
            {
                cmd.Parameters.Add("@SP",    SqlDbType.VarChar,   16).Value = sparePartNo;
                cmd.Parameters.Add("@MT",    SqlDbType.VarChar,   10).Value = moveType;
                cmd.Parameters.Add("@Q",     SqlDbType.Int).Value           = Math.Abs(qty);
                cmd.Parameters.Add("@B",     SqlDbType.Int).Value           = before;
                cmd.Parameters.Add("@A",     SqlDbType.Int).Value           = after;
                cmd.Parameters.Add("@RT",    SqlDbType.VarChar,   15).Value = (object?)refType ?? DBNull.Value;
                cmd.Parameters.Add("@RID",   SqlDbType.VarChar,   24).Value = (object?)refId ?? DBNull.Value;
                cmd.Parameters.Add("@Note",  SqlDbType.NVarChar, 500).Value = (object?)note ?? DBNull.Value;
                cmd.Parameters.Add("@Actor", SqlDbType.NVarChar, 450).Value = actor;
                cmd.Parameters.Add("@By",    SqlDbType.VarChar,   50).Value = actor.Length > 50 ? actor[..50] : actor;
                txnId = Convert.ToInt32(cmd.ExecuteScalar());
            }
            tx.Commit();
            return new StockMoveResult(txnId, before, after);
        }
        catch { tx.Rollback(); throw; }
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
                 WHERE p.OnHandQty <= ISNULL(p.ReorderPoint,0)
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
