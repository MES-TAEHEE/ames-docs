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
    /// <summary>설비 카드 1행. Status 는 자동 판정값(EquipStatusRules), ManualStatus 는 MNT-001 에서 손으로 넣은 값(가동·유휴).
    /// NextPmDate·OpenWo* 는 PM 일정·작업지시에서 조회 시 계산한다(10-16 — 전에는 카드에 손으로 넣은 값이었다).</summary>
    public sealed record EquipCardRow(string EquipId, string? EquipName, string? LineId,
        string? EquipType, string? MakerModel, DateTime? InstallDate, string? Status,
        decimal? TodayOee, decimal? RuntimeHours, long? CycleCount,
        DateTime? NextPmDate, string? MountedMoldId, int? OpenWoId, DateTime? PlcConnTs,
        int? EquipStatusId = null,    // null = MD_Equipment 만 있고 설비 카드(MNT_EquipmentStatus) 미등록
        string? ManualStatus = null,
        string? StopFailureNumber = null,   // DOWN 근거 — 설비를 멈추는 열린 고장(가장 먼저 신고된 것)
        string? PmWoNumber = null,          // PM 근거 — 착수된 PM 작업지시
        string? OpenWoNumber = null,        // 열린 작업지시 중 대표(진행 중 우선, 그다음 먼저 발행된 것)
        int OpenWoCount = 0);

    public sealed record FailureRow(int FailureId, string? FailureNumber, string? EquipId,
        string? FailureType, string? Symptom, string? Severity, string? Source,
        string? Status, DateTime? ReportedAt, DateTime? ResolvedAt, int? WorkOrderId,
        string? EquipName = null, string? LineId = null, string? ReportedBy = null,
        string? AndonRefId = null, int? DowntimeId = null,
        string? WoNumber = null, string? WoStatus = null,    // 연결된 정비 작업지시(있을 때)
        string? DowntimeLineId = null, DateTime? DowntimeStart = null, DateTime? DowntimeEnd = null,
        bool DowntimeOwned = false);   // 연결된 라인 비가동(10-16) — Owned = 이 고장이 연 행(PP_LineDowntimeLog.FailureID)

    /// <summary>고장 「라인 정지」 처리 결과 — 연결된 비가동과, 만들지 못했을 때 그 사유(FailureDowntimeRules).</summary>
    public sealed record FailureDowntimeResult(int? DowntimeId, bool Created, FailureDowntimeRules.Issue Issue)
    {
        public static readonly FailureDowntimeResult None = new(null, false, FailureDowntimeRules.Issue.None);
    }

    public sealed record FailureActionRow(int FailureActionId, int FailureId, string? ActionType, string? Description,
        string? TechnicianId, DateTime? ActionAt, string? CreatedBy)
    {
        public FailureTimeline.Action ToAction() =>
            new(ActionType, ActionAt, string.IsNullOrEmpty(TechnicianId) ? CreatedBy : TechnicianId, Description);
    }

    public sealed record OeeRow(int OeeLogId, string? OeeRecordNumber, string? EquipId, string? LineId,
        string? AggLevel, DateTime? AggDate, string? ShiftCode,
        int? PlannedTimeMin, int? DowntimeMin,
        decimal? Availability, decimal? Performance, decimal? Quality, decimal? Oee,
        decimal? GoodQty, decimal? TotalQty, string? EquipName = null);

    public sealed record MoldRow(string MoldId, string? MoldName, int? RatedShots, int? CurrentShots,
        int? CavityCount, int? Tonnage, string? StorageLoc, DateTime? LastMaintDate, string? Status,
        int? LifetimeShots, string? MountedEquipId, string? ThresholdLevel, int? RefurbishCount,
        long? CumulativeShots = null);

    public sealed record PmRow(int PmScheduleId, string? PmPlanNumber, string? EquipId, string? PmType,
        string? CycleBasis, int? CycleValue, DateTime? LastPmDate, DateTime? NextDueDate,
        string? ChecklistId, string? AssignedTechId, string? Status, int? ActiveWoId, int DaysToDue,
        string? PmClass = null);   // 공통코드 PM_CLASS (EQUIP/MAINT)

    public sealed record DowntimeRow(int DowntimeId, string? LineId, DateTime? StartTs, DateTime? EndTs,
        int? DurationMin, string? ReasonCode, string? CauseCode, string? Comment, int? WoId,
        string? LineName = null, string? LineNameEn = null, string? LoggedBy = null, int? AndonId = null,
        string? CreatedBy = null, DateTime? CreatedTs = null, string? ModifiedBy = null, DateTime? ModifiedTs = null);

    public sealed record MwoRow(int WorkOrderId, string? WoNumber, string? WoType, string? EquipId,
        string? Priority, string? SourceType, string? AssignedTechId, string? Status,
        DateTime? IssuedAt, DateTime? StartedAt, DateTime? CompletedAt, int? LaborMinutes,
        int TaskCount, int TaskDone,
        string? SourceRefId = null, string? ActionDesc = null, string? ChecklistId = null,
        string? PartsUsedJson = null, string? ResultJson = null, DateTime? ClosedAt = null,
        string? EquipName = null,    // ResultJson = 완료 결과(MNT_WorkOrder.ChecklistResultsJSON)
        string? PmClass = null, DateTime? PmDueDate = null,   // PM 작업지시의 일정 분류(EQUIP/MAINT)·예정일(SourceRefID = PMScheduleID)
        bool Planned = false,        // 「예정」 — 착수 전이고 활성 PM 의 예정일이 N일보다 많이 남음(PmRules)
        int TaskFail = 0,            // 불합격 점검 항목 수 — 미점검 = TaskCount − TaskDone
        bool PmNeedsNextDue = false);   // 활성 PM 작업지시인데 기간 주기가 아니라 완료 때 다음 예정일을 넣어야 함(PmRules.NeedsManualNextDue)

    /// <summary>완료 처리 결과 — 어느 원천이 역방향으로 갱신됐는지 화면 메시지용.</summary>
    /// <paramref name="FollowUpWoNumber"/> = 부분·후속 완료로 고장을 넘겨받은 새 작업지시(있으면 고장은 열린 채다).
    public sealed record WoCompleteOutcome(int WorkOrderId, string WoNumber, string? FailureNumber,
        string? PmPlanNumber, DateTime? PmNextDue, string? NextWoNumber, string? FollowUpWoNumber = null);

    /// <summary>종료 처리 결과 — 같이 닫힌 고장·건너뛴 PM 회차·발행된 다음 PM 작업지시.</summary>
    public sealed record WoCloseOutcome(int WorkOrderId, string WoNumber, string? FailureNumber,
        string? PmPlanNumber, DateTime? PmNextDue, string? NextWoNumber);

    /// <summary>MNT_WorkOrder.ChecklistResultsJSON 에 남기는 완료 결과(종료면 Result = CLOSED, Action = 종료 사유). 전용 컬럼이 없어 JSON 으로 둔다.</summary>
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
        decimal AvgOeeToday, int DowntimeMin24h, DateTime? OeeDate = null,
        int PmDueEquip = 0, int PmDueMaint = 0);   // PmDueIn7d 의 분류별(설비 PM·보전 PM) 건수

    // ── MNT-001 Equipment Card ──────────────────────────────────────────
    public List<EquipCardRow> ListEquipment(string? lineId = null)
    {
        // 상태·다음 PM·열린 작업지시는 고장·작업지시·PM 일정에서 계산한다(EquipStatusRules — SQL 의 고장 조건과 같은 규칙).
        // 열린 작업지시에서 「예정」 PM 작업지시(PmRules — 예정일이 N일보다 많이 남은 착수 전 PM)는 뺀다.
        const string sql = $"""
            SELECT  e.EquipID, e.EquipName, e.LineID, e.EquipType, e.MakerModel, e.InstallDate,
                    COALESCE(es.Status, e.Status) AS ManualStatus,
                    es.TodayOEE, es.RuntimeHours, es.CycleCount,
                    np.NextDue, es.MountedMoldID, es.PLCConnTS, es.EquipStatusID,
                    sf.FailureNumber AS StopFailureNumber, pw.WoNumber AS PmWoNumber,
                    ow.WorkOrderID AS OpenWoID, ow.WoNumber AS OpenWoNumber, ISNULL(oc.Cnt, 0) AS OpenWoCount
            FROM    dbo.MD_Equipment e
            LEFT JOIN dbo.MNT_EquipmentStatus es ON es.EquipID = e.EquipID
            OUTER APPLY (SELECT TOP 1 f.FailureNumber FROM dbo.MNT_FailureRegister f
                         WHERE  f.EquipID = e.EquipID AND {EquipStatusRules.StopFailureSql}
                         ORDER  BY f.ReportedAt, f.FailureID) sf
            OUTER APPLY (SELECT TOP 1 w.WoNumber FROM dbo.MNT_WorkOrder w
                         WHERE  w.EquipID = e.EquipID AND {EquipStatusRules.PmInProgressSql}
                         ORDER  BY w.StartedAt, w.WorkOrderID) pw
            OUTER APPLY (SELECT MIN(p.NextDueDate) AS NextDue FROM dbo.MNT_PMSchedule p
                         WHERE  p.EquipID = e.EquipID AND ISNULL(p.Status, '') <> 'CANCELLED') np
            OUTER APPLY (SELECT TOP 1 w.WorkOrderID, w.WoNumber FROM dbo.MNT_WorkOrder w
                         WHERE  w.EquipID = e.EquipID AND w.Status IN ('ISSUED', 'ASSIGNED', 'IN_PROGRESS') AND NOT {PmRules.PlannedWoSql}
                         ORDER  BY CASE WHEN w.Status = 'IN_PROGRESS' THEN 0 ELSE 1 END, w.IssuedAt, w.WorkOrderID) ow
            OUTER APPLY (SELECT COUNT(*) AS Cnt FROM dbo.MNT_WorkOrder w
                         WHERE  w.EquipID = e.EquipID AND w.Status IN ('ISSUED', 'ASSIGNED', 'IN_PROGRESS') AND NOT {PmRules.PlannedWoSql}) oc
            WHERE   ISNULL(e.ActiveFlag,1) = 1
              AND  (@L IS NULL OR e.LineID = @L)
            ORDER BY e.LineID, e.EquipID;
            """;
        return Query(sql, r =>
        {
            var stop = r["StopFailureNumber"] as string;
            var pmWo = r["PmWoNumber"] as string;
            var manual = r["ManualStatus"] as string;
            return new EquipCardRow(
                (string)r["EquipID"], r["EquipName"] as string, r["LineID"] as string,
                r["EquipType"] as string, r["MakerModel"] as string, r["InstallDate"] as DateTime?,
                EquipStatusRules.Effective(stop is not null, pmWo is not null, manual),
                r["TodayOEE"] as decimal?, r["RuntimeHours"] as decimal?,
                r["CycleCount"] as long?, r["NextDue"] as DateTime?,
                r["MountedMoldID"] as string, r["OpenWoID"] as int?, r["PLCConnTS"] as DateTime?,
                r["EquipStatusID"] as int?,
                manual is null ? null : EquipStatusRules.NormalizeManual(manual),
                stop, pmWo, r["OpenWoNumber"] as string, r["OpenWoCount"] as int? ?? 0);
        },
            ("@L", "MD_Equipment.LineID", (object?)lineId ?? DBNull.Value));
    }

    // ── MNT-001 설비 카드 등록·수정·삭제 — MNT_EquipmentStatus (설비 1대당 1행, MD_Equipment 기준) ──
    public bool EquipStatusExists(string equipId)
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand("SELECT 1 FROM dbo.MNT_EquipmentStatus WHERE EquipID = @E", conn);
        cmd.Parameters.Add("@E", SqlDbType.VarChar, 20).Value = equipId;
        return cmd.ExecuteScalar() is not null;
    }

    /// <summary>설비 카드 등록. LineID 는 MD_Equipment 의 라인을 그대로 옮긴다. 같은 설비가 이미 있으면 예외.
    /// 상태는 손으로 정하는 가동·유휴만 저장한다(중지·예방보전은 고장·PM 작업지시로 자동 — EquipStatusRules). 다음 PM 일은 PM 일정에서 계산하므로 받지 않는다.</summary>
    public int InsertEquipStatus(string equipId, string? status, decimal? runtimeHours, long? cycleCount,
        string? mountedMoldId, string actor)
    {
        const string sql = """
            IF EXISTS (SELECT 1 FROM dbo.MNT_EquipmentStatus WHERE EquipID = @E)
                THROW 51000, 'Equipment card already registered', 1;
            INSERT INTO dbo.MNT_EquipmentStatus
                (EquipID, LineID, Status, RuntimeHours, CycleCount, MountedMoldID, CreatedBy, CreatedTS)
            SELECT @E, e.LineID, @St, @Run, @Cyc, @Mold, @By, SYSDATETIME()
            FROM   dbo.MD_Equipment e WHERE e.EquipID = @E;
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@E",    SqlDbType.VarChar, 20).Value = equipId;
        cmd.Parameters.Add("@St",   SqlDbType.VarChar, 10).Value = EquipStatusRules.NormalizeManual(status);
        cmd.Parameters.Add("@Run",  SqlDbType.Decimal).Value      = (object?)runtimeHours ?? DBNull.Value;
        cmd.Parameters["@Run"].Precision = 10; cmd.Parameters["@Run"].Scale = 1;
        cmd.Parameters.Add("@Cyc",  SqlDbType.BigInt).Value       = (object?)cycleCount ?? DBNull.Value;
        cmd.Parameters.Add("@Mold", SqlDbType.VarChar, 20).Value = (object?)mountedMoldId ?? DBNull.Value;
        cmd.Parameters.Add("@By",   SqlDbType.VarChar, 20).Value = actor;
        var id = cmd.ExecuteScalar();
        if (id is null || id is DBNull) throw new InvalidOperationException($"MD_Equipment '{equipId}' not found");
        return Convert.ToInt32(id);
    }

    /// <summary>설비 카드 수정. EquipID 는 바꾸지 않는다(설비 1대당 1행). OEE·PLC 연결은 시스템 값, 다음 PM 일·열린 작업지시는 계산값이라 손대지 않는다.
    /// 상태는 가동·유휴만 저장한다(EquipStatusRules.NormalizeManual).</summary>
    public void UpdateEquipStatus(int equipStatusId, string? status, decimal? runtimeHours, long? cycleCount,
        string? mountedMoldId, string actor)
    {
        const string sql = """
            UPDATE dbo.MNT_EquipmentStatus
            SET    Status = @St, RuntimeHours = @Run, CycleCount = @Cyc, MountedMoldID = @Mold,
                   ModifiedBy = @By, ModifiedTS = SYSDATETIME()
            WHERE  EquipStatusID = @Id;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Id",   SqlDbType.Int).Value          = equipStatusId;
        cmd.Parameters.Add("@St",   SqlDbType.VarChar, 10).Value = EquipStatusRules.NormalizeManual(status);
        cmd.Parameters.Add("@Run",  SqlDbType.Decimal).Value      = (object?)runtimeHours ?? DBNull.Value;
        cmd.Parameters["@Run"].Precision = 10; cmd.Parameters["@Run"].Scale = 1;
        cmd.Parameters.Add("@Cyc",  SqlDbType.BigInt).Value       = (object?)cycleCount ?? DBNull.Value;
        cmd.Parameters.Add("@Mold", SqlDbType.VarChar, 20).Value = (object?)mountedMoldId ?? DBNull.Value;
        cmd.Parameters.Add("@By",   SqlDbType.VarChar,   20).Value = actor;
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
               w.WoNumber, w.Status AS WoStatus,
               d.LineID AS DtLineID, d.StartTS AS DtStart, d.EndTS AS DtEnd,
               CAST(CASE WHEN d.FailureID = f.FailureID THEN 1 ELSE 0 END AS bit) AS DtOwned
        FROM   dbo.MNT_FailureRegister f
        LEFT JOIN dbo.MD_Equipment       e ON e.EquipID      = f.EquipID
        LEFT JOIN dbo.MNT_WorkOrder      w ON w.WorkOrderID  = f.WorkOrderID
        LEFT JOIN dbo.PP_LineDowntimeLog d ON d.DowntimeID   = f.DowntimeID
        """;

    private static FailureRow MapFailure(IDataReader r) => new(
        (int)r["FailureID"], r["FailureNumber"] as string, r["EquipID"] as string,
        r["FailureType"] as string, r["Symptom"] as string, r["Severity"] as string,
        r["Source"] as string, r["Status"] as string,
        r["ReportedAt"] as DateTime?, r["ResolvedAt"] as DateTime?, r["WorkOrderID"] as int?,
        r["EquipName"] as string, r["LineID"] as string, r["ReportedBy"] as string,
        r["AndonRefID"] as string, r["DowntimeID"] as int?,
        r["WoNumber"] as string, r["WoStatus"] as string,
        r["DtLineID"] as string, r["DtStart"] as DateTime?, r["DtEnd"] as DateTime?,
        r["DtOwned"] is true);

    public List<FailureRow> ListFailures(int topN = 100, string? statusFilter = null)
    {
        const string sql = $"""
            {FailureSelect}
            WHERE  (@S IS NULL OR f.Status = @S)
            ORDER  BY f.ReportedAt DESC, f.FailureID DESC
            OFFSET 0 ROWS FETCH NEXT @N ROWS ONLY;
            """;
        return Query(sql, MapFailure, ("@N", null, topN), ("@S", "MNT_FailureRegister.Status", (object?)statusFilter ?? DBNull.Value));
    }

    /// <summary>발생일 기준 기간 조회 (to 는 당일 포함). MNT-002 화면용 — 필터는 화면에서 건다.</summary>
    public List<FailureRow> ListFailuresRange(DateTime from, DateTime to)
    {
        const string sql = $"""
            {FailureSelect}
            WHERE  f.ReportedAt >= @F AND f.ReportedAt < @T
            ORDER  BY f.ReportedAt DESC, f.FailureID DESC;
            """;
        return Query(sql, MapFailure, ("@F", "MNT_FailureRegister.ReportedAt", from.Date), ("@T", "MNT_FailureRegister.ReportedAt", to.Date.AddDays(1)));
    }

    public (DateTime? Min, DateTime? Max) FailureDateExtent()
    {
        var rows = Query("SELECT MIN(ReportedAt) AS Mn, MAX(ReportedAt) AS Mx FROM dbo.MNT_FailureRegister;",
            r => (r["Mn"] as DateTime?, r["Mx"] as DateTime?));
        return rows.Count > 0 ? rows[0] : (null, null);
    }

    public FailureRow? FindFailure(int failureId)
    {
        const string sql = $"""
            {FailureSelect}
            WHERE  f.FailureID = @Id;
            """;
        return Query(sql, MapFailure, ("@Id", "MNT_FailureRegister.FailureID", failureId)).FirstOrDefault();
    }

    /// <summary>
    /// 작업지시의 고장 — 지금 연결된 고장(MNT_FailureRegister.WorkOrderID) + 이 작업지시로 조치 이력을 남긴 고장
    /// (부분·후속 완료로 후속 작업지시에 넘어간 고장, MNT_FailureAction.WorkOrderID — 10-14). 발생순.
    /// 넘어간 고장은 FailureRow.WorkOrderId·WoNumber 가 지금 담당하는 후속 작업지시다.
    /// </summary>
    public List<FailureRow> ListFailuresForWo(int workOrderId)
    {
        const string sql = $"""
            {FailureSelect}
            WHERE  f.WorkOrderID = @Wo
               OR  EXISTS (SELECT 1 FROM dbo.MNT_FailureAction a WHERE a.FailureID = f.FailureID AND a.WorkOrderID = @Wo)
            ORDER  BY f.ReportedAt, f.FailureID;
            """;
        return Query(sql, MapFailure, ("@Wo", "MNT_FailureRegister.WorkOrderID", workOrderId));
    }

    /// <summary>
    /// 고장 조치 이력(MNT_FailureAction) — 안돈 보전 도착·ACK 와 웹 수리 완료(REPAIRED). 고장별·시각 순.
    /// Who = 조치자(안돈은 배지 사번, 수리 완료는 담당 사용자 ID) → 없으면 기록한 행위자.
    /// </summary>
    public List<FailureActionRow> ListFailureActions(IEnumerable<int> failureIds)
    {
        var ids = failureIds.Distinct().ToList();
        if (ids.Count == 0) return new();
        const string sql = """
            SELECT a.FailureActionID, a.FailureID, a.ActionType, a.Description, a.TechnicianID, a.ActionAt, a.CreatedBy
            FROM   dbo.MNT_FailureAction a
            WHERE  a.FailureID IN (SELECT TRY_CAST(value AS INT) FROM STRING_SPLIT(@Ids, ','))
            ORDER  BY a.FailureID, a.ActionAt, a.FailureActionID;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Ids", SqlDbType.VarChar, -1).Value = string.Join(",", ids);
        using var rdr  = cmd.ExecuteReader();
        var list = new List<FailureActionRow>();
        while (rdr.Read())
            list.Add(new((int)rdr["FailureActionID"], (int)rdr["FailureID"], rdr["ActionType"] as string,
                rdr["Description"] as string, rdr["TechnicianID"] as string, rdr["ActionAt"] as DateTime?,
                rdr["CreatedBy"] as string));
        return list;
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
        return Query(sql, MapOee, ("@D", null, daysBack), ("@E", "MNT_OEELog.EquipID", (object?)equipId ?? DBNull.Value));
    }

    /// <summary>집계일 기준 기간 조회 (양끝 포함). MNT-003 화면용 — 라인·설비·교대 필터는 화면에서 건다.</summary>
    public List<OeeRow> ListOeeRange(DateTime from, DateTime to)
    {
        const string sql = $"""
            {OeeSelect}
            WHERE   o.AggDate >= @F AND o.AggDate <= @T
            ORDER BY o.AggDate DESC, o.EquipID, o.ShiftCode;
            """;
        return Query(sql, MapOee, ("@F", "MNT_OEELog.AggDate", from.Date), ("@T", "MNT_OEELog.AggDate", to.Date));
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
            r["CumulativeShots"] as long?));
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
            ("@N", null, topN), ("@M", "PR_MoldChange.OldMoldID", (object?)moldId ?? DBNull.Value));
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
            ("@A", null, daysAhead), ("@B", null, daysBack), ("@C", "MNT_PMSchedule.PMClass", (object?)pmClass ?? DBNull.Value));
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
        r["LineName"] as string, r["LineNameEn"] as string, r["LoggedBy"] as string, r["AndonID"] as int?,
        r["CreatedBy"] as string, r["CreatedTS"] as DateTime?, r["ModifiedBy"] as string, r["ModifiedTS"] as DateTime?);

    public List<DowntimeRow> ListDowntime(int daysBack = 7)
    {
        const string sql = $"""
            {DowntimeSelect}
            WHERE   d.StartTS >= DATEADD(DAY, -@D, SYSDATETIME())
            ORDER BY d.StartTS DESC, d.DowntimeID DESC;
            """;
        return Query(sql, MapDowntime, ("@D", null, daysBack));
    }

    /// <summary>시작 시각 기준 기간 조회 (to 는 당일 포함). MNT-006 화면용 — 라인·상태·사유 필터는 화면에서 건다.</summary>
    public List<DowntimeRow> ListDowntimeRange(DateTime from, DateTime to)
    {
        const string sql = $"""
            {DowntimeSelect}
            WHERE   d.StartTS >= @F AND d.StartTS < @T
            ORDER BY d.StartTS DESC, d.DowntimeID DESC;
            """;
        return Query(sql, MapDowntime, ("@F", "PP_LineDowntimeLog.StartTS", from.Date), ("@T", "PP_LineDowntimeLog.StartTS", to.Date.AddDays(1)));
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
            ("@F", "MNT_PMSchedule.NextDueDate", from.Date), ("@T", "MNT_PMSchedule.NextDueDate", to.Date), ("@C", "MNT_PMSchedule.PMClass", (object?)pmClass ?? DBNull.Value));
    }

    /// <summary>예정일(NextDueDate)이 기간 안에 드는 PM(취소 제외) — MNT-009 "예정 예방보전" 패널용. DaysToDue 는 다른 PM 조회와 같이 오늘(서버 날짜) 기준.</summary>
    public List<PmRow> ListPmDueRange(DateTime from, DateTime to)
    {
        const string sql = """
            SELECT  PMScheduleID, PMPlanNumber, EquipID, PMClass, PMType, CycleBasis, CycleValue,
                    LastPMDate, NextDueDate, ChecklistID, AssignedTechID, Status, ActiveWoID,
                    DATEDIFF(DAY, CAST(SYSDATETIME() AS DATE), NextDueDate) AS DaysToDue
            FROM    dbo.MNT_PMSchedule
            WHERE   NextDueDate BETWEEN @F AND @T AND ISNULL(Status, '') <> 'CANCELLED'
            ORDER BY NextDueDate, EquipID;
            """;
        return Query(sql, r => new PmRow(
            (int)r["PMScheduleID"], r["PMPlanNumber"] as string, r["EquipID"] as string,
            r["PMType"] as string, r["CycleBasis"] as string, r["CycleValue"] as int?,
            r["LastPMDate"] as DateTime?, r["NextDueDate"] as DateTime?,
            r["ChecklistID"] as string, r["AssignedTechID"] as string,
            r["Status"] as string, r["ActiveWoID"] as int?,
            r["DaysToDue"] as int? ?? 0, r["PMClass"] as string),
            ("@F", "MNT_PMSchedule.NextDueDate", from.Date), ("@T", "MNT_PMSchedule.NextDueDate", to.Date));
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
        return Query(sql, MapPmExec, ("@F", "MNT_PMExecution.CompletedAt", from.Date), ("@T", "MNT_PMExecution.CompletedAt", to.Date.AddDays(1)), ("@C", "MNT_PMExecution.PMClass", (object?)pmClass ?? DBNull.Value));
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
        return Query(sql, MapPmExec, ("@Id", "MNT_PMExecution.PMScheduleID", pmScheduleId), ("@N", null, top));
    }

    // ── 번호 채번 — 고장(FAIL-yyMM-nnn)·PM 계획(PM-yyMM-nnn)·보전 WO(MWO-yyMM-nnn) ──
    // 저장 트랜잭션 안에서 그 달 범위를 UPDLOCK·HOLDLOCK 으로 잡고 정한다(10-13) — 전에는 화면이 저장 전에 따로 MAX+1 을 읽어
    // 두 사람이 동시에 저장하면 같은 번호가 들어갔다. POP 안돈의 고장번호(AndonRepository)도 같은 범위 잠금을 쓴다.
    // 범위 잠금이 그 달 번호만 잡도록 세 컬럼에 인덱스가 있다(dist/migrate_mnt_number_index.sql).

    /// <summary>직접 입력한 번호가 이미 있을 때. 화면은 Number 로 안내 문구를 만든다.</summary>
    public sealed class DuplicateNumberException(string number) : InvalidOperationException($"{number} already exists.")
    {
        public string Number { get; } = number;
    }

    private sealed record NumberSpec(string Table, string Column, string KeyColumn, int Length, string Prefix);

    private static readonly NumberSpec FailureNo = new("MNT_FailureRegister", "FailureNumber", "FailureID",    24, "FAIL");
    private static readonly NumberSpec PmPlanNo  = new("MNT_PMSchedule",      "PMPlanNumber",  "PMScheduleID", 30, "PM");
    private static readonly NumberSpec MwoNo     = new("MNT_WorkOrder",       "WoNumber",      "WorkOrderID",  28, "MWO");

    /// <summary>비어 있으면 그 달 최대 순번 + 1 로 채번하고, 입력값이면 같은 번호가 다른 행에 없는지 본다(둘 다 범위 잠금).</summary>
    private static string ClaimNumber(SqlConnection conn, SqlTransaction tx, NumberSpec spec, string? requested, int? excludeId)
    {
        var no = requested?.Trim() ?? "";
        if (no.Length == 0)
        {
            var prefix = $"{spec.Prefix}-{DbClock.Today:yyMM}-";
            using var cmd = new SqlCommand($"""
                SELECT ISNULL(MAX(TRY_CAST(SUBSTRING({spec.Column}, LEN(@P) + 1, 10) AS int)), 0)
                FROM   dbo.{spec.Table} WITH (UPDLOCK, HOLDLOCK) WHERE {spec.Column} LIKE @P + '%'
                """, conn, tx);
            cmd.Parameters.Add("@P", SqlDbType.VarChar, spec.Length).Value = prefix;
            return $"{prefix}{Convert.ToInt32(cmd.ExecuteScalar()) + 1:D3}";
        }
        using (var cmd = new SqlCommand($"""
            SELECT 1 FROM dbo.{spec.Table} WITH (UPDLOCK, HOLDLOCK)
            WHERE  {spec.Column} = @N AND (@X IS NULL OR {spec.KeyColumn} <> @X)
            """, conn, tx))
        {
            cmd.Parameters.Add("@N", SqlDbType.VarChar, spec.Length).Value = no;
            cmd.Parameters.Add("@X", SqlDbType.Int).Value = (object?)excludeId ?? DBNull.Value;
            if (cmd.ExecuteScalar() is not null) throw new DuplicateNumberException(no);
        }
        return no;
    }

    // ── MNT-002 고장 등록·수정·삭제 — 등록 시 정비 작업지시(WoType='CM') 를 같은 트랜잭션으로 발행 ──

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
    /// 상태는 항상 OPEN·해결일시 없음으로 시작한다(10-10 사용자 결정) — 해결은 ResolveFailure(MNT-002 수리 완료)·작업지시 완료(MNT-007)·안돈 종료로만.
    /// 고장번호가 비어 있으면 채번한다(입력값이 겹치면 DuplicateNumberException).
    /// 「라인 정지」(CRITICAL 은 항상)면 같은 트랜잭션에서 라인 비가동을 열거나 기존 열린 비가동에 연결한다(FailureDowntimeRules, 10-16).
    /// </summary>
    public (int FailureId, string FailureNumber, string WoNumber, FailureDowntimeResult Downtime) InsertFailure(string? failNo, string equipId, string failureType, string symptom, string severity,
        string? source, DateTime reportedAt, string? reportedBy, string actor, bool lineStop = false)
    {
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            failNo = ClaimNumber(conn, tx, FailureNo, failNo, null);
            int failId;
            using (var cmd = new SqlCommand("""
                INSERT INTO dbo.MNT_FailureRegister
                    (FailureNumber, EquipID, FailureType, Symptom, Severity, Source, Status, ReportedBy, ReportedAt, ResolvedAt, CreatedBy, CreatedTS)
                VALUES (@No, @Eq, @Type, @Sym, @Sev, @Src, 'OPEN', @By, @Rep, NULL, @Actor, SYSDATETIME());
                SELECT CAST(SCOPE_IDENTITY() AS int);
                """, conn, tx))
            {
                cmd.Parameters.Add("@No",    SqlDbType.VarChar,   24).Value = failNo;
                cmd.Parameters.Add("@Eq",    SqlDbType.VarChar,   20).Value = equipId;
                cmd.Parameters.Add("@Type",  SqlDbType.VarChar,   15).Value = failureType;
                cmd.Parameters.Add("@Sym",   SqlDbType.NVarChar, 500).Value = symptom;
                cmd.Parameters.Add("@Sev",   SqlDbType.VarChar,   10).Value = severity;
                cmd.Parameters.Add("@Src",   SqlDbType.VarChar,   15).Value = (object?)source ?? DBNull.Value;
                cmd.Parameters.Add("@By",    SqlDbType.VarChar,   20).Value = (object?)reportedBy ?? DBNull.Value;
                cmd.Parameters.Add("@Rep",   SqlDbType.DateTime2).Value      = reportedAt;
                cmd.Parameters.Add("@Actor", SqlDbType.VarChar,   20).Value = actor;
                failId = Convert.ToInt32(cmd.ExecuteScalar());
            }

            var woNumber = NextMwoNumber(conn, tx);
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
                cmd.Parameters.Add("@By",   SqlDbType.VarChar,    20).Value = actor;
                woId = Convert.ToInt32(cmd.ExecuteScalar());
            }

            using (var cmd = new SqlCommand("UPDATE dbo.MNT_FailureRegister SET WorkOrderID = @Wo WHERE FailureID = @Id", conn, tx))
            {
                cmd.Parameters.Add("@Wo", SqlDbType.Int).Value = woId;
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = failId;
                cmd.ExecuteNonQuery();
            }

            var downtime = FailureDowntimeRules.Wanted(lineStop, severity)
                ? LinkFailureDowntime(conn, tx, failId, failNo, equipId, symptom, reportedAt, actor)
                : FailureDowntimeResult.None;

            tx.Commit();
            return (failId, failNo, woNumber, downtime);
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>
    /// 작업지시가 없는 열린 고장(안돈 고장·옛 데이터)에 CM 작업지시를 발행해 연결한다(MNT-002, 10-16 사용자 결정).
    /// 출처는 안돈 고장이면 ANDON, 그 밖은 FAILURE · SourceRefID = 고장번호 · 우선순위는 심각도에서 · 담당자를 고르면 ASSIGNED.
    /// 고장 행을 잠그고 아직 열려 있고 작업지시가 없을 때만 — 아니면 InvalidOperationException. 새 작업지시 번호를 돌려준다.
    /// 안돈 고장은 안돈이 끝나면 고장만 닫히고 이 작업지시는 남는다(POP 쪽 처리는 확인사항 심재현 요청) — MNT-007 에서 완료·종료한다.
    /// </summary>
    public string IssueFailureWorkOrder(int failureId, string? techId, string actor)
    {
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            string failNo; string? equipId, failureType, symptom, severity, source, status; int? woId;
            using (var cmd = new SqlCommand("""
                SELECT FailureNumber, EquipID, FailureType, Symptom, Severity, Source, Status, WorkOrderID
                FROM   dbo.MNT_FailureRegister WITH (UPDLOCK, ROWLOCK) WHERE FailureID = @Id;
                """, conn, tx))
            {
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = failureId;
                using var r = cmd.ExecuteReader();
                if (!r.Read()) throw new InvalidOperationException($"Failure #{failureId} not found.");
                failNo = r["FailureNumber"] as string ?? $"F#{failureId}";
                equipId = r["EquipID"] as string; failureType = r["FailureType"] as string; symptom = r["Symptom"] as string;
                severity = r["Severity"] as string; source = r["Source"] as string; status = r["Status"] as string;
                woId = r["WorkOrderID"] as int?;
            }
            if (FailureTimeline.IsResolved(status)) throw new InvalidOperationException($"{failNo} is already resolved or closed.");
            if (woId is not null) throw new InvalidOperationException($"{failNo} already has a work order.");

            var tech = string.IsNullOrWhiteSpace(techId) ? null : techId;
            var woNumber = NextMwoNumber(conn, tx);
            int newId;
            using (var cmd = new SqlCommand("""
                INSERT INTO dbo.MNT_WorkOrder
                    (WoNumber, WoType, EquipID, Priority, SourceType, SourceRefID, AssignedTechID,
                     ActionDesc, Status, IssuedAt, CreatedBy, CreatedTS)
                VALUES (@Wo, @Type, @Eq, @Pri, @Src, @Ref, @Tech, @Desc, @St, SYSDATETIME(), @By, SYSDATETIME());
                SELECT CAST(SCOPE_IDENTITY() AS int);
                """, conn, tx))
            {
                cmd.Parameters.Add("@Wo",   SqlDbType.VarChar,    28).Value = woNumber;
                cmd.Parameters.Add("@Type", SqlDbType.VarChar,    15).Value = MwoTypeCodes.Cm;
                cmd.Parameters.Add("@Eq",   SqlDbType.VarChar,    20).Value = (object?)equipId ?? DBNull.Value;
                cmd.Parameters.Add("@Pri",  SqlDbType.VarChar,    10).Value = WoPriorityFor(severity ?? "");
                cmd.Parameters.Add("@Src",  SqlDbType.VarChar,    15).Value = string.Equals(source, "ANDON", StringComparison.OrdinalIgnoreCase) ? "ANDON" : "FAILURE";
                cmd.Parameters.Add("@Ref",  SqlDbType.VarChar,    24).Value = failNo;
                cmd.Parameters.Add("@Tech", SqlDbType.NVarChar,  450).Value = (object?)tech ?? DBNull.Value;
                cmd.Parameters.Add("@Desc", SqlDbType.NVarChar, 1000).Value = WoDescFor(failNo, failureType, symptom ?? "");
                cmd.Parameters.Add("@St",   SqlDbType.VarChar,    15).Value = MwoStatusRules.Initial(tech);
                cmd.Parameters.Add("@By",   SqlDbType.VarChar,    20).Value = actor;
                newId = Convert.ToInt32(cmd.ExecuteScalar());
            }
            using (var cmd = new SqlCommand("UPDATE dbo.MNT_FailureRegister SET WorkOrderID = @Wo, ModifiedBy = @By, ModifiedTS = SYSDATETIME() WHERE FailureID = @Id", conn, tx))
            {
                cmd.Parameters.Add("@Wo", SqlDbType.Int).Value          = newId;
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value          = failureId;
                cmd.Parameters.Add("@By", SqlDbType.VarChar, 20).Value = actor;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
            return woNumber;
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>고장 수정(내용만). 연결된 작업지시가 착수 전(발행·배정)이면 설비·우선순위·설명을 같이 맞춘다.
    /// 상태·해결일시는 바꾸지 않는다 — 수정 창에서 바꾸면 작업지시가 발행 상태로 남아 닫을 수 없었다(10-10).
    /// 저장한 고장번호를 돌려준다(비우면 새로 채번, 다른 고장과 겹치면 DuplicateNumberException).
    /// <paramref name="lineStop"/> 이 있으면 열린 웹 고장의 라인 비가동을 맞춘다(SyncFailureDowntime — null 이면 건드리지 않음).</summary>
    public (string FailureNumber, FailureDowntimeResult Downtime) UpdateFailure(int id, string? failNo, string equipId, string failureType, string symptom, string severity,
        string? source, DateTime reportedAt, string? reportedBy, string actor, bool? lineStop = null)
    {
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            failNo = ClaimNumber(conn, tx, FailureNo, failNo, id);
            using (var cmd = new SqlCommand("""
                UPDATE dbo.MNT_FailureRegister
                SET    FailureNumber = @No, EquipID = @Eq, FailureType = @Type, Symptom = @Sym, Severity = @Sev, Source = @Src,
                       ReportedBy = @By, ReportedAt = @Rep,
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
                cmd.Parameters.Add("@By",    SqlDbType.VarChar,   20).Value = (object?)reportedBy ?? DBNull.Value;
                cmd.Parameters.Add("@Rep",   SqlDbType.DateTime2).Value      = reportedAt;
                cmd.Parameters.Add("@Actor", SqlDbType.VarChar,   20).Value = actor;
                cmd.ExecuteNonQuery();
            }

            // 진행 중 고장의 후속 작업지시(부분·후속 완료로 발행) 설명은 남은 작업 내용이라 덮어쓰지 않는다
            using (var cmd = new SqlCommand("""
                UPDATE w
                SET    w.EquipID = @Eq, w.Priority = @Pri, w.SourceRefID = @Ref,
                       w.ActionDesc = CASE WHEN ISNULL(f.Status, 'OPEN') IN ('OPEN', 'REGISTERED') THEN @Desc ELSE w.ActionDesc END,
                       w.ModifiedBy = @By, w.ModifiedTS = SYSDATETIME()
                FROM   dbo.MNT_WorkOrder w
                JOIN   dbo.MNT_FailureRegister f ON f.WorkOrderID = w.WorkOrderID
                WHERE  f.FailureID = @Id AND w.Status IN ('ISSUED', 'ASSIGNED');
                """, conn, tx))
            {
                cmd.Parameters.Add("@Id",   SqlDbType.Int).Value             = id;
                cmd.Parameters.Add("@Eq",   SqlDbType.VarChar,    20).Value = equipId;
                cmd.Parameters.Add("@Pri",  SqlDbType.VarChar,    10).Value = WoPriorityFor(severity);
                cmd.Parameters.Add("@Ref",  SqlDbType.VarChar,    24).Value = failNo;
                cmd.Parameters.Add("@Desc", SqlDbType.NVarChar, 1000).Value = WoDescFor(failNo, failureType, symptom);
                cmd.Parameters.Add("@By",   SqlDbType.VarChar,    20).Value = actor;
                cmd.ExecuteNonQuery();
            }

            var downtime = lineStop is bool want
                ? SyncFailureDowntime(conn, tx, id, failNo, equipId, symptom, severity, reportedAt, want, actor)
                : FailureDowntimeResult.None;

            tx.Commit();
            return (failNo, downtime);
        }
        catch { tx.Rollback(); throw; }
    }

    // ── 고장 ↔ 라인 비가동(10-16, FailureDowntimeRules) ─────────────────
    // 연결 = MNT_FailureRegister.DowntimeID, 소유 = PP_LineDowntimeLog.FailureID(그 고장이 연 행). 안돈 행은 안돈이 열고 닫는다.

    private const int DowntimeCommentMax = 500;   // PpRepository.DowntimeCommentMax 와 같게(PP-DTL 입력 한도)

    /// <summary>같은 라인의 열린 비가동에 연결하거나 새로 연다. 라인이 없거나 시작이 미래이거나 끝난 비가동과 겹치면 만들지 않는다.</summary>
    private static FailureDowntimeResult LinkFailureDowntime(SqlConnection conn, SqlTransaction tx, int failId, string failNo,
        string equipId, string symptom, DateTime start, string actor)
    {
        string? lineId;
        using (var cmd = new SqlCommand("SELECT LineID FROM dbo.MD_Equipment WHERE EquipID = @E", conn, tx))
        {
            cmd.Parameters.Add("@E", SqlDbType.VarChar, 20).Value = equipId;
            lineId = cmd.ExecuteScalar() as string;
        }
        if (string.IsNullOrWhiteSpace(lineId)) return new(null, false, FailureDowntimeRules.Issue.NoLine);

        var (now, rows) = ReadLineDowntimes(conn, tx, lineId, start, exceptId: null);
        var d = FailureDowntimeRules.Decide(start, now, rows);
        if (d.Plan == FailureDowntimeRules.Plan.Skip) return new(null, false, d.Issue);

        int downtimeId;
        if (d.Plan == FailureDowntimeRules.Plan.Link) downtimeId = d.DowntimeId!.Value;
        else
        {
            var comment = $"{failNo}: {symptom.Trim()}";
            if (comment.Length > DowntimeCommentMax) comment = comment[..DowntimeCommentMax];
            using var cmd = new SqlCommand("""
                INSERT INTO dbo.PP_LineDowntimeLog
                       (LineID, StartTS, EndTS, DurationMin, ReasonCode, CauseCode, Comment, LoggedBy, FailureID, CreatedBy, CreatedTS)
                OUTPUT INSERTED.DowntimeID
                VALUES (@L, @S, NULL, NULL, @R, NULL, @C, @By, @F, @By, SYSDATETIME());
                """, conn, tx);
            cmd.Parameters.Add("@L",  SqlDbType.VarChar,   20).Value = lineId;
            cmd.Parameters.Add("@S",  SqlDbType.DateTime2).Value      = start;
            cmd.Parameters.Add("@R",  SqlDbType.VarChar,   20).Value = FailureDowntimeRules.ReasonCode;
            cmd.Parameters.Add("@C",  SqlDbType.NVarChar,  500).Value = comment;
            cmd.Parameters.Add("@By", SqlDbType.VarChar,   20).Value = actor;
            cmd.Parameters.Add("@F",  SqlDbType.Int).Value            = failId;
            downtimeId = (int)cmd.ExecuteScalar()!;
        }

        using (var cmd = new SqlCommand("UPDATE dbo.MNT_FailureRegister SET DowntimeID = @D WHERE FailureID = @F", conn, tx))
        {
            cmd.Parameters.Add("@D", SqlDbType.Int).Value = downtimeId;
            cmd.Parameters.Add("@F", SqlDbType.Int).Value = failId;
            cmd.ExecuteNonQuery();
        }
        return new(downtimeId, d.Plan == FailureDowntimeRules.Plan.Create, FailureDowntimeRules.Issue.None);
    }

    /// <summary>DB 시각과, 같은 라인에서 <paramref name="from"/> 이후에 끝나는(또는 열린) 비가동 — 범위를 잡아 동시 등록이 같은 구간을 두 번 열지 않게 한다.</summary>
    private static (DateTime Now, List<FailureDowntimeRules.Existing> Rows) ReadLineDowntimes(SqlConnection conn, SqlTransaction tx,
        string lineId, DateTime from, int? exceptId)
    {
        var rows = new List<FailureDowntimeRules.Existing>();
        using (var cmd = new SqlCommand("""
            SELECT DowntimeID, StartTS, EndTS FROM dbo.PP_LineDowntimeLog WITH (UPDLOCK, HOLDLOCK)
            WHERE  LineID = @L AND ISNULL(EndTS, '9999-12-31') > @S AND (@X IS NULL OR DowntimeID <> @X);
            """, conn, tx))
        {
            cmd.Parameters.Add("@L", SqlDbType.VarChar, 20).Value = lineId;
            cmd.Parameters.Add("@S", SqlDbType.DateTime2).Value    = from;
            cmd.Parameters.Add("@X", SqlDbType.Int).Value          = (object?)exceptId ?? DBNull.Value;
            using var r = cmd.ExecuteReader();
            while (r.Read()) rows.Add(new((int)r["DowntimeID"], (DateTime)r["StartTS"], r["EndTS"] as DateTime?));
        }
        using var now = new SqlCommand("SELECT SYSDATETIME()", conn, tx);
        return ((DateTime)now.ExecuteScalar()!, rows);
    }

    /// <summary>
    /// 고장 수정 때 「라인 정지」를 맞춘다 — 열린 웹 고장만(안돈 고장·해결·종료된 고장은 그대로).
    /// 켜짐: 연결이 없으면 연결·생성, 이 고장이 연 열린 행은 발생 시각·라인을 따라간다(겹치면 그대로 두고 Overlap).
    /// 꺼짐(CRITICAL 이 아닐 때): 이 고장이 연 열린 행은 지우고(다른 열린 고장이 연결돼 있으면 넘김) 연결을 푼다.
    /// </summary>
    private static FailureDowntimeResult SyncFailureDowntime(SqlConnection conn, SqlTransaction tx, int failId, string failNo,
        string equipId, string symptom, string severity, DateTime reportedAt, bool lineStop, string actor)
    {
        string? status, andonRef; int? downtimeId; bool owned = false, open = false; DateTime? dtStart = null; string? dtLine = null;
        using (var cmd = new SqlCommand("""
            SELECT f.Status, f.AndonRefID, f.DowntimeID, d.FailureID AS Owner, d.StartTS, d.EndTS, d.LineID
            FROM   dbo.MNT_FailureRegister f
            LEFT JOIN dbo.PP_LineDowntimeLog d WITH (UPDLOCK) ON d.DowntimeID = f.DowntimeID
            WHERE  f.FailureID = @F;
            """, conn, tx))
        {
            cmd.Parameters.Add("@F", SqlDbType.Int).Value = failId;
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return FailureDowntimeResult.None;
            status = r["Status"] as string; andonRef = r["AndonRefID"] as string; downtimeId = r["DowntimeID"] as int?;
            if (downtimeId is not null && r["StartTS"] is DateTime s)
            {
                owned = r["Owner"] as int? == failId; open = r["EndTS"] is DBNull; dtStart = s; dtLine = r["LineID"] as string;
            }
        }
        if (andonRef is not null || status is "RESOLVED" or "CLOSED")
            return new(downtimeId, false, FailureDowntimeRules.Issue.None);

        if (!FailureDowntimeRules.Wanted(lineStop, severity))
        {
            if (downtimeId is not null)
            {
                if (owned && open) ReleaseFailureDowntime(conn, tx, failId, endAt: null, actor);
                using var cmd = new SqlCommand("UPDATE dbo.MNT_FailureRegister SET DowntimeID = NULL WHERE FailureID = @F", conn, tx);
                cmd.Parameters.Add("@F", SqlDbType.Int).Value = failId;
                cmd.ExecuteNonQuery();
            }
            return FailureDowntimeResult.None;
        }

        // 연결이 있지만 행이 사라졌으면(PP-DTL 에서 지움 등) 새로 연결한다
        if (downtimeId is null || dtStart is null)
            return LinkFailureDowntime(conn, tx, failId, failNo, equipId, symptom, reportedAt, actor);
        if (!(owned && open)) return new(downtimeId, false, FailureDowntimeRules.Issue.None);

        string? lineId;
        using (var cmd = new SqlCommand("SELECT LineID FROM dbo.MD_Equipment WHERE EquipID = @E", conn, tx))
        {
            cmd.Parameters.Add("@E", SqlDbType.VarChar, 20).Value = equipId;
            lineId = cmd.ExecuteScalar() as string;
        }
        if (string.IsNullOrWhiteSpace(lineId)) return new(downtimeId, false, FailureDowntimeRules.Issue.NoLine);
        if (lineId == dtLine && reportedAt == dtStart) return new(downtimeId, false, FailureDowntimeRules.Issue.None);

        var (now, rows) = ReadLineDowntimes(conn, tx, lineId, reportedAt, exceptId: downtimeId);
        if (reportedAt > now) return new(downtimeId, false, FailureDowntimeRules.Issue.StartInFuture);
        if (rows.Count > 0) return new(downtimeId, false, FailureDowntimeRules.Issue.Overlap);
        using (var cmd = new SqlCommand("""
            UPDATE dbo.PP_LineDowntimeLog SET LineID = @L, StartTS = @S, ModifiedBy = @By, ModifiedTS = SYSDATETIME()
            WHERE  DowntimeID = @D AND FailureID = @F AND EndTS IS NULL;
            """, conn, tx))
        {
            cmd.Parameters.Add("@L",  SqlDbType.VarChar, 20).Value = lineId;
            cmd.Parameters.Add("@S",  SqlDbType.DateTime2).Value    = reportedAt;
            cmd.Parameters.Add("@By", SqlDbType.VarChar, 20).Value = actor;
            cmd.Parameters.Add("@D",  SqlDbType.Int).Value          = downtimeId;
            cmd.Parameters.Add("@F",  SqlDbType.Int).Value          = failId;
            cmd.ExecuteNonQuery();
        }
        return new(downtimeId, false, FailureDowntimeRules.Issue.None);
    }

    /// <summary>
    /// 고장이 해결·종료·삭제될 때 그 고장이 연 열린 비가동을 정리한다. 같은 행에 연결된 다른 열린 고장이 있으면
    /// 닫지 않고 소유를 넘긴다(라인은 아직 서 있다). 없으면 <paramref name="endAt"/> 으로 닫고, null 이면 행을 지운다(고장 삭제).
    /// </summary>
    private static void ReleaseFailureDowntime(SqlConnection conn, SqlTransaction tx, int failId, DateTime? endAt, string actor)
    {
        int downtimeId; DateTime start;
        using (var cmd = new SqlCommand("""
            SELECT DowntimeID, StartTS FROM dbo.PP_LineDowntimeLog WITH (UPDLOCK, ROWLOCK)
            WHERE  FailureID = @F AND EndTS IS NULL;
            """, conn, tx))
        {
            cmd.Parameters.Add("@F", SqlDbType.Int).Value = failId;
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return;
            downtimeId = (int)r["DowntimeID"]; start = (DateTime)r["StartTS"];
        }

        int? heir; bool othersLinked;
        using (var cmd = new SqlCommand("""
            SELECT MIN(CASE WHEN ISNULL(Status, 'OPEN') NOT IN ('RESOLVED', 'CLOSED') THEN FailureID END), COUNT(*)
            FROM   dbo.MNT_FailureRegister
            WHERE  DowntimeID = @D AND FailureID <> @F;
            """, conn, tx))
        {
            cmd.Parameters.Add("@D", SqlDbType.Int).Value = downtimeId;
            cmd.Parameters.Add("@F", SqlDbType.Int).Value = failId;
            using var r = cmd.ExecuteReader();
            r.Read();
            heir = r[0] as int?; othersLinked = (int)r[1] > 0;
        }

        DateTime now;
        using (var cmd = new SqlCommand("SELECT SYSDATETIME()", conn, tx)) now = (DateTime)cmd.ExecuteScalar()!;
        // 지우는 고장의 행이라도 다른(끝난) 고장이 이 행을 가리키면 그 기록을 남기려고 지금 시각으로 닫는다
        var end = endAt is { } at ? FailureDowntimeRules.EndAt(start, at, now)
                : othersLinked ? FailureDowntimeRules.EndAt(start, now, now) : (DateTime?)null;

        using (var cmd = new SqlCommand(heir is not null
            ? "UPDATE dbo.PP_LineDowntimeLog SET FailureID = @H, ModifiedBy = @By, ModifiedTS = SYSDATETIME() WHERE DowntimeID = @D;"
            : end is null
                ? "DELETE FROM dbo.PP_LineDowntimeLog WHERE DowntimeID = @D;"
                : """
                  UPDATE dbo.PP_LineDowntimeLog
                  SET    EndTS = @E, DurationMin = DATEDIFF(minute, StartTS, @E), ModifiedBy = @By, ModifiedTS = SYSDATETIME()
                  WHERE  DowntimeID = @D;
                  """, conn, tx))
        {
            cmd.Parameters.Add("@D",  SqlDbType.Int).Value          = downtimeId;
            cmd.Parameters.Add("@H",  SqlDbType.Int).Value          = (object?)heir ?? DBNull.Value;
            cmd.Parameters.Add("@E",  SqlDbType.DateTime2).Value    = (object?)end ?? DBNull.Value;
            cmd.Parameters.Add("@By", SqlDbType.VarChar, 20).Value = actor;
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>삭제 규칙(FailureDeleteRules)에 걸려 지울 수 없을 때. 화면은 Reason 으로 안내 문구를 고른다.</summary>
    public sealed class FailureNotDeletableException(string failNo, FailureDeleteRules.Block reason)
        : InvalidOperationException($"{failNo} cannot be deleted ({reason}).")
    {
        public string FailureNumber { get; } = failNo;
        public FailureDeleteRules.Block Reason { get; } = reason;
    }

    /// <summary>
    /// 고장 삭제 — 열린 웹 등록 고장이고 연결 작업지시가 착수 전일 때만(FailureDeleteRules, 10-13). 조치 이력·작업지시·고장을
    /// 한 트랜잭션으로 지운다(전에는 조치 이력이 고아로 남고 진행·완료 WO 는 없는 고장번호를 가리킨 채 남았다).
    /// 지운 고장번호와 작업지시 번호를 돌려준다. 이미 없으면 null.
    /// </summary>
    public (string FailureNumber, string? WoNumber)? DeleteFailure(int id, string? actor = null)
    {
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            // 잠금 순서 WO → 고장(완료 경로와 같게) — 연결 WO 를 먼저 잡고 고장을 잡은 뒤 연결이 그대로인지 다시 본다.
            var woId0 = ReadIntOrNull(conn, tx, "SELECT WorkOrderID FROM dbo.MNT_FailureRegister WHERE FailureID = @Id", id);
            string? woStatus = null, woNumber = null;
            if (woId0 is int lw)
                using (var cmd = new SqlCommand("SELECT Status, WoNumber FROM dbo.MNT_WorkOrder WITH (UPDLOCK, ROWLOCK) WHERE WorkOrderID = @Id", conn, tx))
                {
                    cmd.Parameters.Add("@Id", SqlDbType.Int).Value = lw;
                    using var r = cmd.ExecuteReader();
                    if (r.Read()) { woStatus = r["Status"] as string; woNumber = r["WoNumber"] as string; }
                }

            string failNo; string? status, source; int? woId;
            using (var cmd = new SqlCommand("""
                SELECT FailureNumber, Status, Source, WorkOrderID
                FROM   dbo.MNT_FailureRegister WITH (UPDLOCK, ROWLOCK)
                WHERE  FailureID = @Id;
                """, conn, tx))
            {
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                using var r = cmd.ExecuteReader();
                if (!r.Read()) return null;   // 커밋 없이 끝나면 dispose 가 되돌린다
                failNo = r["FailureNumber"] as string ?? $"F#{id}";
                status = r["Status"] as string;
                source = r["Source"] as string;
                woId   = r["WorkOrderID"] as int?;
            }
            if (woId != woId0) throw new InvalidOperationException($"{failNo} was changed by another user. Please retry.");
            var block = FailureDeleteRules.Check(status, source, woId, woStatus);
            if (block != FailureDeleteRules.Block.None) throw new FailureNotDeletableException(failNo, block);
            ReleaseFailureDowntime(conn, tx, id, endAt: null, actor ?? "MNT-002");   // 이 고장이 연 비가동도 지운다(다른 고장이 연결돼 있으면 넘기거나 닫음)

            using (var cmd = new SqlCommand("""
                DELETE FROM dbo.MNT_FailureAction   WHERE FailureID = @Id;
                DELETE FROM dbo.MNT_WorkOrderTask   WHERE WorkOrderID = @Wo;
                DELETE FROM dbo.MNT_WorkOrder       WHERE WorkOrderID = @Wo;
                DELETE FROM dbo.MNT_FailureRegister WHERE FailureID = @Id;
                """, conn, tx))
            {
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                cmd.Parameters.Add("@Wo", SqlDbType.Int).Value = (object?)woId ?? DBNull.Value;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
            return (failNo, woNumber);
        }
        catch { tx.Rollback(); throw; }
    }

    // ── MNT-005 설비 PM / MNT-010 보전 PM — 등록·수정·삭제 (테이블 공용, PMClass 로 구분) ──

    /// <summary>
    /// PM 등록 = MNT_PMSchedule 1행 + 정비 작업지시(MNT_WorkOrder, WoType='PM') 1행을 한 트랜잭션으로 만든다.
    /// 작업지시는 담당자가 있으면 ASSIGNED, 없으면 ISSUED 로 발행되고 PM 행의 ActiveWoID 가 이를 가리킨다. 계획번호가 비어 있으면 채번한다(입력값이 겹치면 DuplicateNumberException).
    /// 상태는 항상 사용(OK)으로 저장한다 — <paramref name="status"/> 는 옛 호출 호환용으로 읽지 않는다(PmRules, 10-16).
    /// </summary>
    public (int PmScheduleId, string PlanNumber, string WoNumber) InsertPm(string pmClass, string? planNo, string equipId, string pmType, string? cycleBasis, int? cycleValue,
        DateTime? lastPm, DateTime nextDue, string? checklistId, string? techId, string? status, string actor)
    {
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            EnsureTemplateFitsEquipment(conn, tx, checklistId, equipId);
            planNo = ClaimNumber(conn, tx, PmPlanNo, planNo, null);
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
                cmd.Parameters.Add("@St",    SqlDbType.VarChar,   10).Value = PmRules.Ok;   // 새 일정은 사용 — 상태는 예정일로 계산(PmRules)
                cmd.Parameters.Add("@By",    SqlDbType.VarChar,   20).Value = actor;
                pmId = Convert.ToInt32(cmd.ExecuteScalar());
            }

            var (_, woNumber) = IssuePmWo(conn, tx, pmId, planNo, equipId, pmType, techId, checklistId, nextDue, actor);

            tx.Commit();
            return (pmId, planNo, woNumber);
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>
    /// PM 작업지시 발행(등록·다음 회차·수정에서 재발행 공통) — 담당자가 있으면 ASSIGNED, 없으면 ISSUED.
    /// 출처 PM·SourceRefID = PMScheduleID, 일정의 ActiveWoID 를 잇고 템플릿 단계를 점검 항목으로 복사한다.
    /// </summary>
    private static (int WoId, string WoNumber) IssuePmWo(SqlConnection conn, SqlTransaction tx, int pmId, string? planNo, string? equipId,
        string? pmType, string? techId, string? checklistId, DateTime due, string actor)
    {
        var woNumber = NextMwoNumber(conn, tx);
        int woId;
        using (var cmd = new SqlCommand("""
            INSERT INTO dbo.MNT_WorkOrder
                (WoNumber, WoType, EquipID, Priority, SourceType, SourceRefID, AssignedTechID, ChecklistID,
                 ActionDesc, Status, IssuedAt, CreatedBy, CreatedTS)
            VALUES (@Wo, @Type, @Eq, 'MED', 'PM', @Ref, @Tech, @Chk, @Desc, @St, SYSDATETIME(), @By, SYSDATETIME());
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, conn, tx))
        {
            cmd.Parameters.Add("@St",   SqlDbType.VarChar,    15).Value = MwoStatusRules.Initial(techId);
            cmd.Parameters.Add("@Wo",   SqlDbType.VarChar,    28).Value = woNumber;
            cmd.Parameters.Add("@Type", SqlDbType.VarChar,    15).Value = MwoTypeCodes.Pm;
            cmd.Parameters.Add("@Eq",   SqlDbType.VarChar,    20).Value = (object?)equipId ?? DBNull.Value;
            cmd.Parameters.Add("@Ref",  SqlDbType.VarChar,    24).Value = pmId.ToString();
            cmd.Parameters.Add("@Tech", SqlDbType.NVarChar,  450).Value = (object?)techId ?? DBNull.Value;
            cmd.Parameters.Add("@Chk",  SqlDbType.VarChar,    20).Value = (object?)checklistId ?? DBNull.Value;
            cmd.Parameters.Add("@Desc", SqlDbType.NVarChar, 1000).Value = $"{pmType} PM — {planNo} ({due:yyyy-MM-dd})";
            cmd.Parameters.Add("@By",   SqlDbType.VarChar,    20).Value = actor;
            woId = Convert.ToInt32(cmd.ExecuteScalar());
        }
        using (var cmd = new SqlCommand("UPDATE dbo.MNT_PMSchedule SET ActiveWoID = @Wo WHERE PMScheduleID = @Id", conn, tx))
        {
            cmd.Parameters.Add("@Wo", SqlDbType.Int).Value = woId;
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = pmId;
            cmd.ExecuteNonQuery();
        }
        CopyTemplateSteps(conn, tx, woId, checklistId, actor);
        return (woId, woNumber);
    }

    /// <summary>작업지시 번호 자동 채번: MWO-yyMM-nnn (mnt-seed 와 같은 양식, 범위 잠금).</summary>
    private static string NextMwoNumber(SqlConnection conn, SqlTransaction tx) => ClaimNumber(conn, tx, MwoNo, null, null);

    /// <summary>
    /// PMClass 는 화면이 정하므로 바꾸지 않는다(설비 PM 화면에서 보전 PM 으로 옮길 수 없음).
    /// 연결된 작업지시가 아직 착수 전(발행·배정)이면 설비·담당자·템플릿·설명을 같이 맞추고, 담당자 유무로 발행·배정 상태도 맞춘다.
    /// 상태는 사용(OK)·취소(CANCELLED)만 저장한다(PmRules, 10-16):
    ///   · 취소 — 착수 전 활성 작업지시(점검 항목 포함)를 지우고 활성 작업지시를 비운다(진행 중이면 남긴다).
    ///   · 사용 — 열린 활성 작업지시가 없으면(사이클 PM 완료·종료로 비었거나 옛 데이터) 이 예정일로 새로 발행한다.
    /// 저장한 계획번호를 돌려준다(비우면 새로 채번, 다른 일정과 겹치면 DuplicateNumberException).
    /// </summary>
    public string UpdatePm(int id, string? planNo, string equipId, string pmType, string? cycleBasis, int? cycleValue,
        DateTime? lastPm, DateTime nextDue, string? checklistId, string? techId, string? status, string actor)
    {
        var cancelled = PmRules.IsCancelled(status);
        status = PmRules.Stored(cancelled);
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            EnsureTemplateFitsEquipment(conn, tx, checklistId, equipId);
            planNo = ClaimNumber(conn, tx, PmPlanNo, planNo, id);
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
                cmd.Parameters.Add("@By",    SqlDbType.VarChar,   20).Value = actor;
                cmd.ExecuteNonQuery();
            }

            // 착수 전 작업지시의 템플릿이 바뀌었거나 점검 항목이 아직 없으면(기능 도입 전 발행분) 점검 항목을 새 템플릿으로 다시 만든다
            int? recopyWo = null;
            using (var cmd = new SqlCommand("""
                DECLARE @chg TABLE (WorkOrderID int, OldChk varchar(20), NewChk varchar(20));
                UPDATE w
                SET    w.EquipID = @Eq, w.AssignedTechID = @Tech, w.ChecklistID = @Chk,
                       w.ActionDesc = @Desc, w.Status = CASE WHEN @Tech IS NULL THEN 'ISSUED' ELSE 'ASSIGNED' END,
                       w.ModifiedBy = @By, w.ModifiedTS = SYSDATETIME()
                OUTPUT inserted.WorkOrderID, deleted.ChecklistID, inserted.ChecklistID INTO @chg
                FROM   dbo.MNT_WorkOrder w
                JOIN   dbo.MNT_PMSchedule p ON p.ActiveWoID = w.WorkOrderID
                WHERE  p.PMScheduleID = @Id AND w.Status IN ('ISSUED', 'ASSIGNED');
                SELECT c.WorkOrderID FROM @chg c
                WHERE  ISNULL(c.OldChk, '') <> ISNULL(c.NewChk, '')
                   OR  NOT EXISTS (SELECT 1 FROM dbo.MNT_WorkOrderTask t WHERE t.WorkOrderID = c.WorkOrderID);
                """, conn, tx))
            {
                cmd.Parameters.Add("@Id",   SqlDbType.Int).Value             = id;
                cmd.Parameters.Add("@Eq",   SqlDbType.VarChar,    20).Value = equipId;
                cmd.Parameters.Add("@Tech", SqlDbType.NVarChar,  450).Value = (object?)techId ?? DBNull.Value;
                cmd.Parameters.Add("@Chk",  SqlDbType.VarChar,    20).Value = (object?)checklistId ?? DBNull.Value;
                cmd.Parameters.Add("@Desc", SqlDbType.NVarChar, 1000).Value = $"{pmType} PM — {planNo} ({nextDue:yyyy-MM-dd})";
                cmd.Parameters.Add("@By",   SqlDbType.VarChar,    20).Value = actor;
                recopyWo = cmd.ExecuteScalar() as int?;
            }
            if (recopyWo is int rw)
            {
                using (var cmd = new SqlCommand("DELETE FROM dbo.MNT_WorkOrderTask WHERE WorkOrderID = @Wo", conn, tx))
                {
                    cmd.Parameters.Add("@Wo", SqlDbType.Int).Value = rw;
                    cmd.ExecuteNonQuery();
                }
                CopyTemplateSteps(conn, tx, rw, checklistId, actor);
            }

            if (cancelled)
            {
                using var cmd = new SqlCommand("""
                    DELETE t FROM dbo.MNT_WorkOrderTask t
                    JOIN   dbo.MNT_PMSchedule p ON p.ActiveWoID = t.WorkOrderID
                    JOIN   dbo.MNT_WorkOrder w  ON w.WorkOrderID = t.WorkOrderID
                    WHERE  p.PMScheduleID = @Id AND w.Status IN ('ISSUED', 'ASSIGNED');
                    DELETE w FROM dbo.MNT_WorkOrder w
                    JOIN   dbo.MNT_PMSchedule p ON p.ActiveWoID = w.WorkOrderID
                    WHERE  p.PMScheduleID = @Id AND w.Status IN ('ISSUED', 'ASSIGNED');
                    UPDATE p SET ActiveWoID = NULL
                    FROM   dbo.MNT_PMSchedule p
                    WHERE  p.PMScheduleID = @Id
                      AND  NOT EXISTS (SELECT 1 FROM dbo.MNT_WorkOrder w WHERE w.WorkOrderID = p.ActiveWoID AND w.Status = 'IN_PROGRESS');
                    """, conn, tx);
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                cmd.ExecuteNonQuery();
            }
            else
            {
                bool needsWo;
                using (var cmd = new SqlCommand("""
                    SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.MNT_PMSchedule p JOIN dbo.MNT_WorkOrder w ON w.WorkOrderID = p.ActiveWoID
                                             WHERE p.PMScheduleID = @Id AND w.Status IN ('ISSUED', 'ASSIGNED', 'IN_PROGRESS'))
                                THEN 0 ELSE 1 END;
                    """, conn, tx))
                {
                    cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                    needsWo = Convert.ToInt32(cmd.ExecuteScalar()) == 1;
                }
                if (needsWo) IssuePmWo(conn, tx, id, planNo, equipId, pmType, techId, checklistId, nextDue, actor);
            }

            tx.Commit();
            return planNo;
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>PM 삭제. 연결된 작업지시가 착수 전(발행·배정)이면 함께 지우고, 이미 진행·완료·종료된 WO 는 남긴다.</summary>
    public void DeletePm(int id)
    {
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            using (var cmd = new SqlCommand("""
                DELETE t
                FROM   dbo.MNT_WorkOrderTask t
                JOIN   dbo.MNT_WorkOrder w ON w.WorkOrderID = t.WorkOrderID
                JOIN   dbo.MNT_PMSchedule p ON p.ActiveWoID = w.WorkOrderID
                WHERE  p.PMScheduleID = @Id AND w.Status IN ('ISSUED', 'ASSIGNED');
                DELETE w
                FROM   dbo.MNT_WorkOrder w
                JOIN   dbo.MNT_PMSchedule p ON p.ActiveWoID = w.WorkOrderID
                WHERE  p.PMScheduleID = @Id AND w.Status IN ('ISSUED', 'ASSIGNED');
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

    // PM 작업지시의 일정 = SourceType PM + SourceRefID(PMScheduleID) — 완료된 작업지시도 분류가 남게 ActiveWoID 대신 출처로 잇는다
    private const string MwoSelect = $"""
        SELECT w.WorkOrderID, w.WoNumber, w.WoType, w.EquipID, w.Priority, w.SourceType, w.SourceRefID,
               w.AssignedTechID, w.ChecklistID, w.ActionDesc, w.PartsUsedJSON, w.ChecklistResultsJSON,
               w.Status, w.IssuedAt, w.StartedAt, w.CompletedAt, w.ClosedAt, w.LaborMinutes,
               e.EquipName,
               ISNULL(tk.TaskCount, 0)  AS TaskCount,
               ISNULL(tk.TaskDone , 0)  AS TaskDone,
               ISNULL(tk.TaskFail , 0)  AS TaskFail,
               pm.PMClass, CASE WHEN pm.ActiveWoID = w.WorkOrderID THEN pm.NextDueDate END AS PmDueDate,
               CAST(CASE WHEN {PmRules.PlannedWoSql} THEN 1 ELSE 0 END AS bit) AS Planned,
               CAST(CASE WHEN pm.ActiveWoID = w.WorkOrderID AND NOT (ISNULL(pm.CycleBasis, '') = 'TIME' AND ISNULL(pm.CycleValue, 0) > 0) THEN 1 ELSE 0 END AS bit) AS PmNeedsNextDue
        FROM   dbo.MNT_WorkOrder w
        LEFT JOIN dbo.MD_Equipment e ON e.EquipID = w.EquipID
        LEFT JOIN dbo.MNT_PMSchedule pm ON w.SourceType = 'PM' AND pm.PMScheduleID = TRY_CAST(w.SourceRefID AS int)
        LEFT JOIN (
            SELECT WorkOrderID,
                   COUNT(*)                                                       AS TaskCount,
                   SUM(CASE WHEN Result IN ('PASS','FAIL','NA') THEN 1 ELSE 0 END) AS TaskDone,   -- 점검을 마친 항목(MNT_RESULT, 미점검 제외)
                   SUM(CASE WHEN Result = 'FAIL' THEN 1 ELSE 0 END)                AS TaskFail
            FROM   dbo.MNT_WorkOrderTask
            GROUP  BY WorkOrderID
        ) tk ON tk.WorkOrderID = w.WorkOrderID
        """;

    private static MwoRow MapMwo(IDataReader r) => new(
        (int)r["WorkOrderID"], r["WoNumber"] as string, r["WoType"] as string,
        r["EquipID"] as string, r["Priority"] as string, r["SourceType"] as string,
        r["AssignedTechID"] as string, r["Status"] as string,
        r["IssuedAt"] as DateTime?, r["StartedAt"] as DateTime?, r["CompletedAt"] as DateTime?,
        r["LaborMinutes"] as int?,
        r["TaskCount"] as int? ?? 0, r["TaskDone"] as int? ?? 0,
        r["SourceRefID"] as string, r["ActionDesc"] as string, r["ChecklistID"] as string,
        r["PartsUsedJSON"] as string, r["ChecklistResultsJSON"] as string, r["ClosedAt"] as DateTime?,
        r["EquipName"] as string,
        r["PMClass"] as string, r["PmDueDate"] as DateTime?, r["Planned"] is true, r["TaskFail"] as int? ?? 0, r["PmNeedsNextDue"] is true);

    public List<MwoRow> ListWorkOrders(int topN = 100, string? statusFilter = null)
    {
        const string sql = $"""
            {MwoSelect}
            WHERE  (@S IS NULL OR w.Status = @S)
            ORDER  BY w.IssuedAt DESC, w.WorkOrderID DESC
            OFFSET 0 ROWS FETCH NEXT @N ROWS ONLY;
            """;
        return Query(sql, MapMwo, ("@N", null, topN), ("@S", "MNT_WorkOrder.Status", (object?)statusFilter ?? DBNull.Value));
    }

    /// <summary>작업지시 1건 — 목록(최근 N건) 밖의 작업지시를 다른 화면에서 열 때(MNT-005·010 「PM 완료」 → MNT-007).</summary>
    public MwoRow? FindWorkOrder(int workOrderId)
    {
        const string sql = $"""
            {MwoSelect}
            WHERE  w.WorkOrderID = @Id;
            """;
        return Query(sql, MapMwo, ("@Id", "MNT_WorkOrder.WorkOrderID", workOrderId)).FirstOrDefault();
    }

    /// <summary>PM 「도래」·「예정」을 가르는 N일 — SYS_Config MNT_PM_LEAD_DAYS(PmRules.ParseLeadDays).</summary>
    public int ReadPmLeadDays()
    {
        var v = Query("SELECT TOP 1 ConfigValue FROM dbo.SYS_Config WHERE ConfigKey = @K ORDER BY ConfigID",
            r => r["ConfigValue"] as string, ("@K", "SYS_Config.ConfigKey", PmRules.LeadDaysKey)).FirstOrDefault();
        return PmRules.ParseLeadDays(v);
    }

    // ── 작업지시 점검 항목 (MNT_WorkOrderTask) — PM 템플릿 단계를 발행 때 복사, 착수 뒤 결과 기록(10-15) ──────

    /// <summary>점검 항목 1행. 템플릿 단계의 발행 시점 스냅샷이라 나중에 템플릿을 고쳐도 바뀌지 않는다. PartNo·PartName 은 표시용 조인.</summary>
    public sealed record WoTaskRow(int WorkOrderTaskId, int WorkOrderId, string? PmStepId, int? TaskSeq, string? TaskName,
        string? AcceptanceCriteria, string? RequiredSparePartNo, int? RequiredQty, int? StepDurationMin,
        string? Result, string? Note, string? CompletedBy, DateTime? CompletedAt, string? PartNo, string? PartName);

    public List<WoTaskRow> ListWorkOrderTasks(int workOrderId) => Query("""
        SELECT t.WorkOrderTaskID, t.WorkOrderID, t.PMStepID, t.TaskSeq, t.TaskName, t.AcceptanceCriteria,
               t.RequiredSparePartNo, t.RequiredQty, t.StepDurationMin, t.Result, t.Note, t.CompletedBy, t.CompletedAt,
               p.PartNo, p.PartName
        FROM   dbo.MNT_WorkOrderTask t
        LEFT JOIN dbo.MD_SparePart p ON p.SparePartNo = t.RequiredSparePartNo
        WHERE  t.WorkOrderID = @W
        ORDER  BY ISNULL(t.TaskSeq, 9999), t.WorkOrderTaskID;
        """, r => new WoTaskRow((int)r["WorkOrderTaskID"], (int)r["WorkOrderID"], r["PMStepID"] as string, r["TaskSeq"] as int?,
            r["TaskName"] as string, r["AcceptanceCriteria"] as string, r["RequiredSparePartNo"] as string, r["RequiredQty"] as int?,
            r["StepDurationMin"] as int?, r["Result"] as string, r["Note"] as string, r["CompletedBy"] as string,
            r["CompletedAt"] as DateTime?, r["PartNo"] as string, r["PartName"] as string),
        ("@W", "MNT_WorkOrderTask.WorkOrderID", workOrderId));

    /// <summary>
    /// PM 일정에 지정할 수 있는 점검 템플릿 = 설비와 설비 유형(공통코드 EQUIP_TYPE)이 같은 템플릿(10-16 사용자 결정).
    /// 둘 중 하나라도 유형이 비어 있으면 맞지 않는 것으로 본다. 화면 콤보와 PM 저장(InsertPm·UpdatePm)이 같은 규칙을 쓴다.
    /// </summary>
    public static bool TemplateMatchesEquipment(string? templateEquipType, string? equipType) =>
        !string.IsNullOrWhiteSpace(templateEquipType) && !string.IsNullOrWhiteSpace(equipType)
        && string.Equals(templateEquipType.Trim(), equipType.Trim(), StringComparison.OrdinalIgnoreCase);

    public sealed class TemplateTypeMismatchException(string templateId, string equipId)
        : InvalidOperationException($"PM template {templateId} is not for the equipment type of {equipId}.")
    {
        public string TemplateId { get; } = templateId;
        public string EquipId    { get; } = equipId;
    }

    /// <summary>미점검·불합격 점검 항목이 남은 작업지시를 정상 완료하려 함(MwoChecklistRules, 10-16).</summary>
    public sealed class ChecklistIncompleteException(string woNumber, int pending, int failed)
        : InvalidOperationException($"{woNumber}: {pending} unchecked and {failed} failed check item(s) — complete it as partial/follow-up or close it.")
    {
        public string WoNumber { get; } = woNumber;
        public int    Pending  { get; } = pending;
        public int    Failed   { get; } = failed;
    }

    /// <summary>기간 주기가 아닌 PM 을 완료하면서 다음 예정일을 넣지 않았거나 완료일 이전으로 넣음(PmRules, 10-16).</summary>
    public sealed class PmNextDueRequiredException(string planNo, bool invalid)
        : InvalidOperationException(invalid ? $"{planNo}: the next due date must be after the completion date." : $"{planNo}: enter the next due date — this PM has no time-based cycle.")
    {
        public string PlanNo  { get; } = planNo;
        public bool   Invalid { get; } = invalid;
    }

    private static (int Pending, int Failed) ReadTaskCounts(SqlConnection conn, SqlTransaction tx, int woId)
    {
        using var cmd = new SqlCommand("""
            SELECT ISNULL(SUM(CASE WHEN ISNULL(Result, 'PENDING') NOT IN ('PASS', 'FAIL', 'NA') THEN 1 ELSE 0 END), 0),
                   ISNULL(SUM(CASE WHEN Result = 'FAIL' THEN 1 ELSE 0 END), 0)
            FROM   dbo.MNT_WorkOrderTask WHERE WorkOrderID = @Id;
            """, conn, tx);
        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = woId;
        using var r = cmd.ExecuteReader();
        r.Read();
        return (r.GetInt32(0), r.GetInt32(1));
    }

    private static void EnsureTemplateFitsEquipment(SqlConnection conn, SqlTransaction tx, string? templateId, string equipId)
    {
        if (string.IsNullOrWhiteSpace(templateId)) return;
        using var cmd = new SqlCommand("""
            SELECT t.EquipType, e.EquipType
            FROM   (SELECT 1 AS k) x
            LEFT JOIN dbo.MD_PmTemplate t ON t.PMTemplateID = @T
            LEFT JOIN dbo.MD_Equipment  e ON e.EquipID = @E;
            """, conn, tx);
        cmd.Parameters.Add("@T", SqlDbType.VarChar, 20).Value = templateId;
        cmd.Parameters.Add("@E", SqlDbType.VarChar, 20).Value = equipId;
        using var r = cmd.ExecuteReader();
        r.Read();
        if (!TemplateMatchesEquipment(r.IsDBNull(0) ? null : r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1)))
            throw new TemplateTypeMismatchException(templateId, equipId);
    }

    /// <summary>PM 템플릿 단계 → 작업지시 점검 항목(미점검). 템플릿이 없거나 단계가 없으면 아무것도 넣지 않는다. 넣은 건수를 돌려준다.</summary>
    private static int CopyTemplateSteps(SqlConnection conn, SqlTransaction tx, int woId, string? templateId, string actor)
    {
        if (string.IsNullOrWhiteSpace(templateId)) return 0;
        using var cmd = new SqlCommand("""
            INSERT INTO dbo.MNT_WorkOrderTask
                (WorkOrderID, PMStepID, TaskSeq, TaskName, AcceptanceCriteria, RequiredSparePartNo, RequiredQty, StepDurationMin,
                 TaskType, Result, CreatedBy, CreatedTS)
            SELECT @Wo, s.PMStepID, s.StepSeq, s.StepDescription, s.AcceptanceCriteria, s.RequiredSparePartNo, s.RequiredQty, s.StepDurationMin,
                   'CHECK', 'PENDING', @By, SYSDATETIME()
            FROM   dbo.MD_PmTemplateStep s
            WHERE  s.PMTemplateID = @T
            ORDER  BY ISNULL(s.StepSeq, 9999), s.PMStepID;
            """, conn, tx);
        cmd.Parameters.Add("@Wo", SqlDbType.Int).Value          = woId;
        cmd.Parameters.Add("@T",  SqlDbType.VarChar, 20).Value = templateId;
        cmd.Parameters.Add("@By", SqlDbType.VarChar, 20).Value = actor;
        return cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 점검 항목 결과 기록(MNT-007) — 작업지시가 진행 중(IN_PROGRESS)일 때만. 결과는 공통코드 MNT_RESULT 값.
    /// 미점검(PENDING)으로 되돌리면 점검자·시각을 지운다. 메모는 결과와 함께 덮어쓴다(500자).
    /// </summary>
    public void SetWorkOrderTaskResult(int taskId, string result, string? note, string actor)
    {
        if (result is not (MntResultCodes.Pass or MntResultCodes.Fail or MntResultCodes.Na or MntResultCodes.Pending))
            throw new ArgumentException($"Unknown check result '{result}'.");
        var n = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (n is { Length: > 500 }) throw new ArgumentException("Note is longer than 500 characters.");

        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            // 잠금 순서는 다른 작업지시 경로와 같게 WO 먼저 — 완료·종료와 겹치면 그쪽이 끝난 뒤 상태를 보고 거부된다
            string? status;
            using (var cmd = new SqlCommand("""
                SELECT w.Status FROM dbo.MNT_WorkOrder w WITH (UPDLOCK, ROWLOCK)
                WHERE  w.WorkOrderID = (SELECT WorkOrderID FROM dbo.MNT_WorkOrderTask WHERE WorkOrderTaskID = @Id);
                """, conn, tx))
            {
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = taskId;
                status = cmd.ExecuteScalar() as string;
            }
            if (status != MwoStatusCodes.InProgress)
                throw new InvalidOperationException("Check results can be recorded only while the work order is in progress.");

            using (var cmd = new SqlCommand("""
                UPDATE dbo.MNT_WorkOrderTask
                SET    Result = @R, Note = @N,
                       CompletedBy = CASE WHEN @R = 'PENDING' THEN NULL ELSE @By END,
                       CompletedAt = CASE WHEN @R = 'PENDING' THEN NULL ELSE SYSDATETIME() END,
                       ModifiedBy = @By, ModifiedTS = SYSDATETIME()
                WHERE  WorkOrderTaskID = @Id;
                """, conn, tx))
            {
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value           = taskId;
                cmd.Parameters.Add("@R",  SqlDbType.VarChar,  10).Value = result;
                cmd.Parameters.Add("@N",  SqlDbType.NVarChar, 500).Value = (object?)n ?? DBNull.Value;
                cmd.Parameters.Add("@By", SqlDbType.VarChar,  20).Value = actor;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    // ── MNT-007 발행 · 배정 · 착수 · 완료 · 종료 (+ MNT-002 수리 완료 · MNT-005/010 PM 완료) ──────
    // 완료의 역방향 반영은 CompleteWoCore 한 곳에만 있다 — 세 화면이 모두 여기를 지나야 고장·PM 이 같은 규칙으로 닫힌다.

    public const int WoDescMax = 1000;   // MNT_WorkOrder.ActionDesc nvarchar(1000)
    public const int CloseReasonMax = 500;   // MNT_FailureAction.Description·MNT_PMExecution.ResultNote 와 같은 길이

    /// <summary>
    /// 수동 발행(MNT-007, 10-15) — 고장·PM 과 무관한 작업지시(SourceType MANUAL). PdM 은 이 경로로만 생긴다.
    /// 담당자를 고르면 ASSIGNED, 아니면 ISSUED(MwoStatusRules.Initial). 번호는 같은 트랜잭션에서 채번한다.
    /// </summary>
    public (int WorkOrderId, string WoNumber, string Status) IssueWorkOrder(string woType, string equipId, string priority,
        string description, string? techId, string actor)
    {
        if (string.IsNullOrWhiteSpace(woType) || string.IsNullOrWhiteSpace(equipId) || string.IsNullOrWhiteSpace(priority) || string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Type, equipment, priority and description are required.");
        var desc   = description.Trim();
        if (desc.Length > WoDescMax) throw new ArgumentException($"Description is longer than {WoDescMax} characters.");
        var tech   = string.IsNullOrWhiteSpace(techId) ? null : techId;
        var status = MwoStatusRules.Initial(tech);

        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            var woNumber = NextMwoNumber(conn, tx);
            int woId;
            using (var cmd = new SqlCommand("""
                INSERT INTO dbo.MNT_WorkOrder
                    (WoNumber, WoType, EquipID, Priority, SourceType, SourceRefID, AssignedTechID,
                     ActionDesc, Status, IssuedAt, CreatedBy, CreatedTS)
                VALUES (@Wo, @Type, @Eq, @Pri, 'MANUAL', NULL, @Tech, @Desc, @St, SYSDATETIME(), @By, SYSDATETIME());
                SELECT CAST(SCOPE_IDENTITY() AS int);
                """, conn, tx))
            {
                cmd.Parameters.Add("@Wo",   SqlDbType.VarChar,    28).Value = woNumber;
                cmd.Parameters.Add("@Type", SqlDbType.VarChar,    15).Value = woType;
                cmd.Parameters.Add("@Eq",   SqlDbType.VarChar,    20).Value = equipId;
                cmd.Parameters.Add("@Pri",  SqlDbType.VarChar,    10).Value = priority;
                cmd.Parameters.Add("@Tech", SqlDbType.NVarChar,  450).Value = (object?)tech ?? DBNull.Value;
                cmd.Parameters.Add("@Desc", SqlDbType.NVarChar, 1000).Value = desc;
                cmd.Parameters.Add("@St",   SqlDbType.VarChar,    15).Value = status;
                cmd.Parameters.Add("@By",   SqlDbType.VarChar,    20).Value = actor;
                woId = Convert.ToInt32(cmd.ExecuteScalar());
            }
            tx.Commit();
            return (woId, woNumber, status);
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>담당자 배정(MwoStatusRules.AfterAssign) — 착수 전이면 ASSIGNED, 진행 중이면 담당자만 바꾼다. PM 발행 WO 면 PM 일정의 담당자도 같이 바꾼다. 완료·종료된 WO 는 거부.</summary>
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
                       Status = CASE WHEN Status = 'IN_PROGRESS' THEN 'IN_PROGRESS' ELSE 'ASSIGNED' END,
                       ModifiedBy = @By, ModifiedTS = SYSDATETIME()
                WHERE  WorkOrderID = @Id AND ISNULL(Status, '') NOT IN ('COMPLETED', 'CLOSED');
                """, conn, tx))
            {
                cmd.Parameters.Add("@Id",   SqlDbType.Int).Value            = woId;
                cmd.Parameters.Add("@Tech", SqlDbType.NVarChar, 450).Value = techId;
                cmd.Parameters.Add("@By",   SqlDbType.VarChar,   20).Value = actor;
                n = cmd.ExecuteNonQuery();
            }
            if (n == 0) throw new InvalidOperationException($"Work order #{woId} is already completed/closed or does not exist.");

            using (var cmd = new SqlCommand("""
                UPDATE dbo.MNT_PMSchedule SET AssignedTechID = @Tech, ModifiedBy = @By, ModifiedTS = SYSDATETIME()
                WHERE  ActiveWoID = @Id;
                """, conn, tx))
            {
                cmd.Parameters.Add("@Id",   SqlDbType.Int).Value            = woId;
                cmd.Parameters.Add("@Tech", SqlDbType.NVarChar, 450).Value = techId;
                cmd.Parameters.Add("@By",   SqlDbType.VarChar,   20).Value = actor;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>착수. 발행·배정 → IN_PROGRESS, StartedAt 기록. 연결된 고장은 IN_PROGRESS 로 따라간다.</summary>
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
                WHERE  WorkOrderID = @Id AND ISNULL(Status, 'ISSUED') IN ('ISSUED', 'ASSIGNED');
                """, conn, tx))
            {
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value            = woId;
                cmd.Parameters.Add("@By", SqlDbType.VarChar,   20).Value = actor;
                n = cmd.ExecuteNonQuery();
            }
            if (n == 0) throw new InvalidOperationException($"Work order #{woId} is not in ISSUED/ASSIGNED state.");

            using (var cmd = new SqlCommand("""
                UPDATE dbo.MNT_FailureRegister SET Status = 'IN_PROGRESS', ModifiedBy = @By, ModifiedTS = SYSDATETIME()
                WHERE  WorkOrderID = @Id AND ISNULL(Status, 'OPEN') IN ('OPEN', 'REGISTERED');
                """, conn, tx))
            {
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value            = woId;
                cmd.Parameters.Add("@By", SqlDbType.VarChar,   20).Value = actor;
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
    ///     기간(TIME) 주기면 NextDueDate = 완료일 + 주기, 그 밖은 <paramref name="pmNextDue"/>(사람이 넣은 다음 예정일)로 미루고
    ///     다음 작업지시를 발행(ActiveWoID 갱신)
    ///   · 점검 항목: 미점검·불합격이 있으면 정상 완료 거부(ChecklistIncompleteException), 불합격을 부분·후속으로 완료하면 CM 후속 작업지시(10-16)
    /// 결과·조치·근본원인은 MNT_WorkOrder 에 전용 컬럼이 없어 ChecklistResultsJSON 에 JSON(WoResult)으로 남긴다.
    /// 사용 부품은 웹에서 입력·차감하지 않는다(10-15 사용자 결정 (b)) — PDA 예비품 출고(SP-07)가 이 작업지시를 골라 출고한 이력을
    /// 완료 시점에 PartsUsedJSON 으로 스냅샷한다(ReadWoPartReleases, 재고는 PDA 출고 때 이미 줄어 있다).
    /// </summary>
    public WoCompleteOutcome CompleteWorkOrder(int woId, string result, string actionTaken, string? rootCause,
        int? laborMinutes, DateTime completedAt, string? techId, string actor, DateTime? pmNextDue = null)
    {
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            var o = CompleteWoCore(conn, tx, woId, result, actionTaken, rootCause, laborMinutes, completedAt, techId, actor, pmNextDue);
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
            // 잠금 순서 WO → 고장(CompleteWoCore 와 같게) — 연결 WO 를 먼저 잡고 고장을 잡은 뒤 연결이 그대로인지 다시 본다.
            var woId0 = ReadIntOrNull(conn, tx, "SELECT WorkOrderID FROM dbo.MNT_FailureRegister WHERE FailureID = @Id", failureId);
            var woStatus = woId0 is int lw ? LockWoStatus(conn, tx, lw) : null;

            string failNo; string? status; int? woId;
            using (var cmd = new SqlCommand("""
                SELECT FailureNumber, Status, WorkOrderID
                FROM   dbo.MNT_FailureRegister WITH (UPDLOCK, ROWLOCK)
                WHERE  FailureID = @Id;
                """, conn, tx))
            {
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = failureId;
                using var r = cmd.ExecuteReader();
                if (!r.Read()) throw new InvalidOperationException($"Failure #{failureId} not found.");
                failNo   = r["FailureNumber"] as string ?? $"F#{failureId}";
                status   = r["Status"] as string;
                woId     = r["WorkOrderID"] as int?;
            }
            if (woId != woId0) throw new InvalidOperationException($"{failNo} was changed by another user. Please retry.");
            if (status is "RESOLVED" or "CLOSED")
                throw new InvalidOperationException($"{failNo} is already resolved.");

            if (woId is int w && woStatus is not (MwoStatusCodes.Completed or MwoStatusCodes.Closed))
            {
                var o = CompleteWoCore(conn, tx, w, MwoResultCodes.Ok, actionTaken, rootCause, laborMinutes, resolvedAt, null, actor);
                tx.Commit();
                return (failNo, o.WoNumber);
            }

            if (ResolveFailureRows(conn, tx, new[] { failureId }, rootCause, actionTaken, resolvedAt, null, actor, woId) == 0)
                throw new InvalidOperationException($"{failNo} is already resolved.");
            tx.Commit();
            return (failNo, null);
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>
    /// MNT-005/010 PM 완료. 활성 작업지시가 열려 있으면 그 완료로 처리하고(작업지시 화면과 같은 경로),
    /// 없거나 이미 완료됐으면(구 시연 데이터) 실행 이력·예정일만 갱신하고 기간 주기면 다음 작업지시를 발행한다.
    /// </summary>
    public WoCompleteOutcome CompletePm(int pmId, string result, string? note, int? laborMinutes, DateTime completedAt, string? techId, string actor,
        DateTime? nextDue = null)
    {
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            // 잠금 순서 WO → PM(CompleteWoCore 와 같게) — 활성 WO 를 먼저 잡고 PM 을 잡은 뒤 활성 WO 가 그대로인지 다시 본다.
            var activeWo0 = ReadIntOrNull(conn, tx, "SELECT ActiveWoID FROM dbo.MNT_PMSchedule WHERE PMScheduleID = @Id", pmId);
            var woStatus = activeWo0 is int lw ? LockWoStatus(conn, tx, lw) : null;

            PmCore? pm; int? activeWo;
            using (var cmd = new SqlCommand($"""
                SELECT {PmCoreCols}
                FROM   dbo.MNT_PMSchedule p WITH (UPDLOCK, ROWLOCK)
                WHERE  p.PMScheduleID = @Id;
                """, conn, tx))
            {
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = pmId;
                using var r = cmd.ExecuteReader();
                if (!r.Read()) throw new InvalidOperationException($"PM schedule #{pmId} not found.");
                pm       = MapPmCore(r);
                activeWo = r["ActiveWoID"] as int?;
            }
            if (activeWo != activeWo0) throw new InvalidOperationException($"{pm.PlanNo ?? $"PM#{pmId}"} was changed by another user. Please retry.");

            WoCompleteOutcome o;
            if (activeWo is int w && woStatus is not (MwoStatusCodes.Completed or MwoStatusCodes.Closed))
                o = CompleteWoCore(conn, tx, w, result, note ?? "", null, laborMinutes, completedAt, techId, actor, nextDue);
            else
            {
                var (planNo, next, nextWo) = AdvancePm(conn, tx, pm, null, result, note, laborMinutes, completedAt, techId, actor, manualNextDue: nextDue);
                o = new WoCompleteOutcome(0, "", null, planNo, next, nextWo);
            }
            tx.Commit();
            return o;
        }
        catch { tx.Rollback(); throw; }
    }

    private static int? ReadIntOrNull(SqlConnection conn, SqlTransaction tx, string sql, int id)
    {
        using var cmd = new SqlCommand(sql, conn, tx);
        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
        return cmd.ExecuteScalar() is int v ? v : null;
    }

    private static string? LockWoStatus(SqlConnection conn, SqlTransaction tx, int woId)
    {
        using var cmd = new SqlCommand("SELECT Status FROM dbo.MNT_WorkOrder WITH (UPDLOCK, ROWLOCK) WHERE WorkOrderID = @Id", conn, tx);
        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = woId;
        return cmd.ExecuteScalar() as string;
    }

    private WoCompleteOutcome CompleteWoCore(SqlConnection conn, SqlTransaction tx, int woId, string result, string actionTaken,
        string? rootCause, int? laborMinutes, DateTime completedAt, string? techId, string actor, DateTime? pmNextDue = null)
    {
        // 잠금 순서는 WO → 고장 → PM (배정·착수·완료·수리 완료·PM 완료 공통) — 동시 완료의 두 번째는 여기서 기다렸다가 완료 상태를 보고 거부된다.
        string woNumber; string? status;
        using (var cmd = new SqlCommand("SELECT WoNumber, Status FROM dbo.MNT_WorkOrder WITH (UPDLOCK, ROWLOCK) WHERE WorkOrderID = @Id", conn, tx))
        {
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = woId;
            using var r = cmd.ExecuteReader();
            if (!r.Read()) throw new InvalidOperationException($"Work order #{woId} not found.");
            woNumber = r["WoNumber"] as string ?? $"MWO#{woId}";
            status   = r["Status"] as string;
        }
        if (status is MwoStatusCodes.Completed or MwoStatusCodes.Closed)
            throw new InvalidOperationException($"{woNumber} is already completed.");

        // 미점검·불합격 점검 항목이 있으면 정상 완료는 안 된다(MwoChecklistRules — MNT-002 수리 완료도 이 경로)
        var (pending, failed) = ReadTaskCounts(conn, tx, woId);
        if (!MwoChecklistRules.Allows(result, pending, failed)) throw new ChecklistIncompleteException(woNumber, pending, failed);

        var resultJson = System.Text.Json.JsonSerializer.Serialize(new WoResult(result, actionTaken, rootCause, actor, completedAt), JsonReadable);

        // PDA 로 이 작업지시에 출고된 부품(취소분 제외) — 없으면 기존 값(옛 자유 입력)을 그대로 둔다
        var partsJson = MwoPartsUsed.Serialize(MwoPartsUsed.Merge(ReadWoPartReleases(conn, tx, woNumber).Select(r => r.ToLine())));

        using (var cmd = new SqlCommand("""
            UPDATE dbo.MNT_WorkOrder
            SET    Status = 'COMPLETED', CompletedAt = @At,
                   StartedAt = ISNULL(StartedAt, DATEADD(MINUTE, -ISNULL(@Labor, 0), @At)),
                   LaborMinutes = @Labor, PartsUsedJSON = ISNULL(@Parts, PartsUsedJSON), ChecklistResultsJSON = @Res,
                   AssignedTechID = ISNULL(@Tech, AssignedTechID),
                   ModifiedBy = @By, ModifiedTS = SYSDATETIME()
            WHERE  WorkOrderID = @Id AND ISNULL(Status, '') NOT IN ('COMPLETED', 'CLOSED');
            """, conn, tx))
        {
            cmd.Parameters.Add("@Id",    SqlDbType.Int).Value            = woId;
            cmd.Parameters.Add("@At",    SqlDbType.DateTime2).Value      = completedAt;
            cmd.Parameters.Add("@Labor", SqlDbType.Int).Value            = (object?)laborMinutes ?? DBNull.Value;
            cmd.Parameters.Add("@Parts", SqlDbType.NVarChar, -1).Value   = (object?)partsJson ?? DBNull.Value;
            cmd.Parameters.Add("@Res",   SqlDbType.NVarChar, -1).Value   = resultJson;
            cmd.Parameters.Add("@Tech",  SqlDbType.NVarChar, 450).Value = (object?)techId ?? DBNull.Value;
            cmd.Parameters.Add("@By",    SqlDbType.VarChar,   20).Value = actor;
            if (cmd.ExecuteNonQuery() == 0) throw new InvalidOperationException($"{woNumber} is already completed.");
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
        // 부분·후속 완료는 고장을 닫지 않고 후속 작업지시로 넘긴다(10-14 사용자 결정) — 정상 완료만 해결
        string? followUpWo = null;
        if (failures.Count > 0)
        {
            if (result is MwoResultCodes.Partial or MwoResultCodes.FollowUp)
                followUpWo = CarryFailuresToFollowUp(conn, tx, woId, woNumber, failures.Select(f => f.Id), result, rootCause, actionTaken, completedAt, techId, actor);
            else
                ResolveFailureRows(conn, tx, failures.Select(f => f.Id), rootCause, actionTaken, completedAt, techId, actor, woId);
        }
        // 고장이 없는 작업지시(PM 등)의 불합격 점검 항목은 CM 후속 작업지시로 넘긴다(10-16)
        if (MwoChecklistRules.IssuesFollowUp(result, failed, failures.Count > 0))
            followUpWo = IssueChecklistFollowUp(conn, tx, woId, woNumber, result, actor);

        var pm = FindPmForWo(conn, tx, woId);
        string? pmNo = null; DateTime? nextDue = null; string? nextWo = null;
        if (pm is not null)
            (pmNo, nextDue, nextWo) = AdvancePm(conn, tx, pm, woId, result, actionTaken, laborMinutes, completedAt, techId, actor, manualNextDue: pmNextDue);

        return new WoCompleteOutcome(woId, woNumber, failures.Count > 0 ? string.Join(", ", failures.Select(f => f.No)) : null, pmNo, nextDue, nextWo, followUpWo);
    }

    /// <summary>PDA 예비품 출고(SP-07)가 이 작업지시로 출고한 부품 1행 — 시리얼 1개 = 1행. 취소된 출고(ReversalOfTxnID 로 되돌린 행)는 빠진다.</summary>
    public sealed record PartReleaseRow(int TxnId, string SparePartNo, string? PartNo, string? PartName, string? SerialNo,
        int Qty, DateTime? TxnAt, string? ActorId)
    {
        public MwoPartsUsed.Line ToLine() => new(SparePartNo, Qty, PartNo, PartName);
    }

    // 출고 이력의 작업지시 연결 = RefType 'MWO' + RefID 작업지시 번호(PDA 출고 프로시저가 남긴다 — 확인사항 안영훈 요청)
    private const string WoPartReleaseSql = """
        SELECT  t.SparePartsTxnID, t.SparePartNo, p.PartNo, p.PartName, i.SerialNo, t.Qty, t.TxnAt, t.ActorID
        FROM    dbo.MNT_SparePartsTxn t
        LEFT JOIN dbo.MD_SparePart p      ON p.SparePartNo = t.SparePartNo
        LEFT JOIN dbo.MNT_SparePartItem i ON i.SparePartItemID = t.SparePartItemID
        WHERE   t.RefType = 'MWO' AND t.RefID = @Wo AND t.MoveType = 'OUT'
          AND   NOT EXISTS (SELECT 1 FROM dbo.MNT_SparePartsTxn r WHERE r.ReversalOfTxnID = t.SparePartsTxnID)
        ORDER BY t.TxnAt, t.SparePartsTxnID;
        """;

    private static PartReleaseRow MapPartRelease(IDataReader r) => new(
        (int)r["SparePartsTxnID"], (string)r["SparePartNo"], r["PartNo"] as string, r["PartName"] as string, r["SerialNo"] as string,
        r["Qty"] as int? ?? 0, r["TxnAt"] as DateTime?, r["ActorID"] as string);

    public List<PartReleaseRow> ListWoPartReleases(string woNumber) =>
        Query(WoPartReleaseSql, MapPartRelease, ("@Wo", "MNT_SparePartsTxn.RefID", woNumber));

    private static List<PartReleaseRow> ReadWoPartReleases(SqlConnection conn, SqlTransaction tx, string woNumber)
    {
        using var cmd = new SqlCommand(WoPartReleaseSql, conn, tx);
        cmd.Parameters.Add("@Wo", SqlDbType.VarChar, 24).Value = woNumber;
        using var r = cmd.ExecuteReader();
        var list = new List<PartReleaseRow>();
        while (r.Read()) list.Add(MapPartRelease(r));
        return list;
    }

    // PM 역방향 — 이 WO 를 활성 WO 로 가진 일정(구 데이터는 SourceRefID 로 보조 매칭)
    // 고장·PM 조회는 인덱스 없는 스캔이라 UPDLOCK 을 걸지 않는다(다른 WO 를 동시에 완료하는 트랜잭션끼리 교착) — 이중 반영은 WO 행 잠금이 막는다.
    private static PmCore? FindPmForWo(SqlConnection conn, SqlTransaction tx, int woId)
    {
        using var cmd = new SqlCommand($"""
            SELECT {PmCoreCols}
            FROM   dbo.MNT_PMSchedule p
            WHERE  p.ActiveWoID = @Id
               OR (p.ActiveWoID IS NULL AND EXISTS (
                      SELECT 1 FROM dbo.MNT_WorkOrder w
                      WHERE  w.WorkOrderID = @Id AND w.SourceType = 'PM' AND TRY_CAST(w.SourceRefID AS int) = p.PMScheduleID))
            ORDER  BY CASE WHEN p.ActiveWoID = @Id THEN 0 ELSE 1 END;
            """, conn, tx);
        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = woId;
        using var r = cmd.ExecuteReader();
        return r.Read() ? MapPmCore(r) : null;
    }

    /// <summary>
    /// 작업지시 종료(10-15 사용자 결정) — 정상 완료(COMPLETED)와 달리 작업 없이 닫는다(오등록·중복·불필요). 끝나기 전 어느 상태에서든 가능, 사유 필수.
    ///   · WO: Status CLOSED · ClosedAt · 결과 JSON(Result CLOSED, Action = 사유). 완료 시각·소요 시간은 남기지 않는다.
    ///   · 연결된 열린 고장: Status CLOSED(해결 시각 없음) + 조치 이력 CLOSED(사유, 이 작업지시) 1행.
    ///   · PM: 이번 회차 건너뜀 — 실행 이력 Result CLOSED(사유), 마지막 실시일은 그대로, 기간 주기면 다음 예정일 = 이번 예정일 + 주기로 넘기고 다음 작업지시 발행.
    /// 잠금 순서는 완료와 같은 WO → 고장 → PM.
    /// </summary>
    public WoCloseOutcome CloseWorkOrder(int woId, string reason, string actor)
    {
        var note = reason?.Trim() ?? "";
        if (note.Length == 0) throw new ArgumentException("Close reason is required.");
        if (note.Length > CloseReasonMax) throw new ArgumentException($"Close reason is longer than {CloseReasonMax} characters.");

        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            string woNumber; string? status;
            using (var cmd = new SqlCommand("SELECT WoNumber, Status FROM dbo.MNT_WorkOrder WITH (UPDLOCK, ROWLOCK) WHERE WorkOrderID = @Id", conn, tx))
            {
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = woId;
                using var r = cmd.ExecuteReader();
                if (!r.Read()) throw new InvalidOperationException($"Work order #{woId} not found.");
                woNumber = r["WoNumber"] as string ?? $"MWO#{woId}";
                status   = r["Status"] as string;
            }
            if (!MwoStatusRules.CanClose(status)) throw new InvalidOperationException($"{woNumber} is already completed or closed.");

            DateTime at;
            using (var cmd = new SqlCommand("SELECT SYSDATETIME()", conn, tx)) at = (DateTime)cmd.ExecuteScalar()!;
            var resultJson = System.Text.Json.JsonSerializer.Serialize(new WoResult(MwoStatusCodes.Closed, note, null, actor, at), JsonReadable);
            // 종료 전에 PDA 로 이미 출고된 부품도 기록으로 남긴다(재고는 PDA 출고 때 줄었다)
            var partsJson = MwoPartsUsed.Serialize(MwoPartsUsed.Merge(ReadWoPartReleases(conn, tx, woNumber).Select(r => r.ToLine())));

            using (var cmd = new SqlCommand("""
                UPDATE dbo.MNT_WorkOrder
                SET    Status = 'CLOSED', ClosedAt = @At, ChecklistResultsJSON = @Res, PartsUsedJSON = ISNULL(@Parts, PartsUsedJSON),
                       ModifiedBy = @By, ModifiedTS = SYSDATETIME()
                WHERE  WorkOrderID = @Id AND ISNULL(Status, '') NOT IN ('COMPLETED', 'CLOSED');
                """, conn, tx))
            {
                cmd.Parameters.Add("@Id",  SqlDbType.Int).Value          = woId;
                cmd.Parameters.Add("@At",  SqlDbType.DateTime2).Value    = at;
                cmd.Parameters.Add("@Res", SqlDbType.NVarChar, -1).Value = resultJson;
                cmd.Parameters.Add("@Parts", SqlDbType.NVarChar, -1).Value = (object?)partsJson ?? DBNull.Value;
                cmd.Parameters.Add("@By",  SqlDbType.VarChar,  20).Value = actor;
                if (cmd.ExecuteNonQuery() == 0) throw new InvalidOperationException($"{woNumber} is already completed or closed.");
            }

            var failures = new List<(int Id, string No)>();
            using (var cmd = new SqlCommand("""
                DECLARE @closed TABLE (FailureID int, FailureNumber varchar(24));
                UPDATE dbo.MNT_FailureRegister
                SET    Status = 'CLOSED', ModifiedBy = @By, ModifiedTS = SYSDATETIME()
                OUTPUT inserted.FailureID, inserted.FailureNumber INTO @closed
                WHERE  WorkOrderID = @Id AND ISNULL(Status, 'OPEN') NOT IN ('RESOLVED', 'CLOSED');
                INSERT INTO dbo.MNT_FailureAction (FailureID, ActionType, Description, TechnicianID, ActionAt, WorkOrderID, CreatedBy, CreatedTS)
                SELECT FailureID, 'CLOSED', @Note, NULL, @At, @Id, @By, SYSDATETIME() FROM @closed;
                SELECT FailureID, ISNULL(FailureNumber, CONCAT('F#', FailureID)) FROM @closed ORDER BY FailureID;
                """, conn, tx))
            {
                cmd.Parameters.Add("@Id",   SqlDbType.Int).Value            = woId;
                cmd.Parameters.Add("@Note", SqlDbType.NVarChar, 500).Value = note;
                cmd.Parameters.Add("@At",   SqlDbType.DateTime2).Value      = at;
                cmd.Parameters.Add("@By",   SqlDbType.VarChar,   20).Value = actor;
                using var r = cmd.ExecuteReader();
                while (r.Read()) failures.Add((r.GetInt32(0), r.GetString(1)));
            }
            // 수리 없이 닫아도 라인이 서 있던 시간은 남긴다 — 종료 시각으로 닫는다(오등록이면 PP-DTL 에서 지운다)
            foreach (var (fid, _) in failures) ReleaseFailureDowntime(conn, tx, fid, at, actor);

            string? pmNo = null; DateTime? nextDue = null; string? nextWo = null;
            if (FindPmForWo(conn, tx, woId) is { } pm)
                (pmNo, nextDue, nextWo) = AdvancePm(conn, tx, pm, woId, MwoStatusCodes.Closed, note, null, at, null, actor, skipped: true);

            tx.Commit();
            return new WoCloseOutcome(woId, woNumber, failures.Count > 0 ? string.Join(", ", failures.Select(f => f.No)) : null, pmNo, nextDue, nextWo);
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>
    /// 부분 완료(PARTIAL)·후속 필요(FOLLOWUP) — 고장은 열린 채(IN_PROGRESS) 두고, 완료한 작업지시의 유형·설비·우선순위로
    /// 후속 작업지시(ISSUED, 미배정)를 발행해 고장의 WorkOrderID 를 옮긴다. 조치 이력에는 결과 코드를 유형으로 1행 남긴다
    /// (REPAIRED 가 아니므로 해결 시각·수리 시간은 최종 정상 완료 때 정해진다). 새 작업지시 번호를 돌려준다.
    /// </summary>
    private string CarryFailuresToFollowUp(SqlConnection conn, SqlTransaction tx, int woId, string woNumber, IEnumerable<int> failureIds,
        string result, string? rootCause, string actionTaken, DateTime at, string? techId, string actor)
    {
        string? woType, equipId, priority, sourceRef;
        using (var cmd = new SqlCommand("SELECT WoType, EquipID, Priority, SourceRefID FROM dbo.MNT_WorkOrder WHERE WorkOrderID = @Id", conn, tx))
        {
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = woId;
            using var r = cmd.ExecuteReader();
            r.Read();
            woType = r["WoType"] as string; equipId = r["EquipID"] as string; priority = r["Priority"] as string; sourceRef = r["SourceRefID"] as string;
        }

        var note = string.IsNullOrWhiteSpace(rootCause) ? actionTaken.Trim() : $"{rootCause.Trim()} → {actionTaken.Trim()}";
        var desc = $"{result} ← {woNumber}: {note}";
        if (desc.Length > 1000) desc = desc[..1000];

        var newNo = NextMwoNumber(conn, tx);
        int newId;
        using (var cmd = new SqlCommand("""
            INSERT INTO dbo.MNT_WorkOrder
                (WoNumber, WoType, EquipID, Priority, SourceType, SourceRefID, AssignedTechID,
                 ActionDesc, Status, IssuedAt, CreatedBy, CreatedTS)
            VALUES (@Wo, @Type, @Eq, @Pri, 'FAILURE', @Ref, NULL, @Desc, 'ISSUED', SYSDATETIME(), @By, SYSDATETIME());
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, conn, tx))
        {
            cmd.Parameters.Add("@Wo",   SqlDbType.VarChar,    28).Value = newNo;
            cmd.Parameters.Add("@Type", SqlDbType.VarChar,    15).Value = woType ?? MwoTypeCodes.Cm;
            cmd.Parameters.Add("@Eq",   SqlDbType.VarChar,    20).Value = (object?)equipId ?? DBNull.Value;
            cmd.Parameters.Add("@Pri",  SqlDbType.VarChar,    10).Value = (object?)priority ?? DBNull.Value;
            cmd.Parameters.Add("@Ref",  SqlDbType.VarChar,    24).Value = (object?)sourceRef ?? DBNull.Value;
            cmd.Parameters.Add("@Desc", SqlDbType.NVarChar, 1000).Value = desc;
            cmd.Parameters.Add("@By",   SqlDbType.VarChar,    20).Value = actor;
            newId = Convert.ToInt32(cmd.ExecuteScalar());
        }

        if (note.Length > 500) note = note[..500];   // MNT_FailureAction.Description nvarchar(500) — ResolveFailureRows 와 같게
        foreach (var id in failureIds)
        {
            using var cmd = new SqlCommand("""
                UPDATE dbo.MNT_FailureRegister
                SET    Status = 'IN_PROGRESS', WorkOrderID = @New, ModifiedBy = @By, ModifiedTS = SYSDATETIME()
                WHERE  FailureID = @Id AND ISNULL(Status, 'OPEN') NOT IN ('RESOLVED', 'CLOSED');
                IF @@ROWCOUNT > 0
                    INSERT INTO dbo.MNT_FailureAction (FailureID, ActionType, Description, TechnicianID, ActionAt, WorkOrderID, CreatedBy, CreatedTS)
                    VALUES (@Id, @Type, @Desc, @Tech, @At, @Wo, @By, SYSDATETIME());
                """, conn, tx);
            cmd.Parameters.Add("@Id",   SqlDbType.Int).Value             = id;
            cmd.Parameters.Add("@New",  SqlDbType.Int).Value             = newId;
            cmd.Parameters.Add("@Wo",   SqlDbType.Int).Value             = woId;   // 원래 작업지시 — 고장이 넘어가도 그 상세에 남게
            cmd.Parameters.Add("@Type", SqlDbType.VarChar,    20).Value = result;
            cmd.Parameters.Add("@Desc", SqlDbType.NVarChar,  500).Value = note;
            cmd.Parameters.Add("@Tech", SqlDbType.NVarChar,  450).Value = (object?)techId ?? DBNull.Value;
            cmd.Parameters.Add("@At",   SqlDbType.DateTime2).Value       = at;
            cmd.Parameters.Add("@By",   SqlDbType.VarChar,    20).Value = actor;
            cmd.ExecuteNonQuery();
        }
        return newNo;
    }

    public const string CheckFollowUpSource = "CHECK";   // MNT_WorkOrder.SourceType — 점검 불합격 후속(SourceRefID = 원래 작업지시 번호)

    /// <summary>
    /// 불합격 점검 항목 → CM 후속 작업지시(ISSUED, 미배정, 원래 작업지시의 설비·우선순위). 설명 = 결과 ← 원래 번호: 불합격 항목(메모).
    /// 출처는 PM 이 아니라 CHECK 다 — PM 출처로 두면 FindPmForWo 의 옛 데이터 보조 매칭이 그 PM 회차를 다시 넘긴다.
    /// </summary>
    private string IssueChecklistFollowUp(SqlConnection conn, SqlTransaction tx, int woId, string woNumber, string result, string actor)
    {
        string? equipId, priority;
        using (var cmd = new SqlCommand("SELECT EquipID, Priority FROM dbo.MNT_WorkOrder WHERE WorkOrderID = @Id", conn, tx))
        {
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = woId;
            using var r = cmd.ExecuteReader();
            r.Read();
            equipId = r["EquipID"] as string; priority = r["Priority"] as string;
        }
        var items = new List<string>();
        using (var cmd = new SqlCommand("""
            SELECT TaskSeq, TaskName, Note FROM dbo.MNT_WorkOrderTask
            WHERE  WorkOrderID = @Id AND Result = 'FAIL'
            ORDER  BY ISNULL(TaskSeq, 9999), WorkOrderTaskID;
            """, conn, tx))
        {
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = woId;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var name = r["TaskName"] as string ?? "—";
                var note = r["Note"] as string;
                items.Add($"{(r["TaskSeq"] is int seq ? $"{seq}. " : "")}{name}{(string.IsNullOrWhiteSpace(note) ? "" : $" ({note.Trim()})")}");
            }
        }
        var desc = $"{result} ← {woNumber}: FAIL {string.Join("; ", items)}";
        if (desc.Length > WoDescMax) desc = desc[..WoDescMax];

        var newNo = NextMwoNumber(conn, tx);
        using var ins = new SqlCommand("""
            INSERT INTO dbo.MNT_WorkOrder
                (WoNumber, WoType, EquipID, Priority, SourceType, SourceRefID, AssignedTechID,
                 ActionDesc, Status, IssuedAt, CreatedBy, CreatedTS)
            VALUES (@Wo, @Type, @Eq, @Pri, @Src, @Ref, NULL, @Desc, 'ISSUED', SYSDATETIME(), @By, SYSDATETIME());
            """, conn, tx);
        ins.Parameters.Add("@Wo",   SqlDbType.VarChar,    28).Value = newNo;
        ins.Parameters.Add("@Type", SqlDbType.VarChar,    15).Value = MwoTypeCodes.Cm;
        ins.Parameters.Add("@Eq",   SqlDbType.VarChar,    20).Value = (object?)equipId ?? DBNull.Value;
        ins.Parameters.Add("@Pri",  SqlDbType.VarChar,    10).Value = (object?)priority ?? "MED";
        ins.Parameters.Add("@Src",  SqlDbType.VarChar,    15).Value = CheckFollowUpSource;
        ins.Parameters.Add("@Ref",  SqlDbType.VarChar,    24).Value = woNumber;
        ins.Parameters.Add("@Desc", SqlDbType.NVarChar, 1000).Value = desc;
        ins.Parameters.Add("@By",   SqlDbType.VarChar,    20).Value = actor;
        ins.ExecuteNonQuery();
        return newNo;
    }

    /// <summary>고장 RESOLVED + 조치 이력(MNT_FailureAction 'REPAIRED') — 안돈의 ARRIVED/ACK 와 같은 테이블에 쌓인다.
    /// 이미 해결·마감된 고장은 건너뛰며(조치 행도 남기지 않음) 실제로 닫은 건수를 돌려준다.</summary>
    private static int ResolveFailureRows(SqlConnection conn, SqlTransaction tx, IEnumerable<int> failureIds,
        string? rootCause, string actionTaken, DateTime resolvedAt, string? techId, string actor, int? woId)
    {
        var desc = string.IsNullOrWhiteSpace(rootCause) ? actionTaken.Trim() : $"{rootCause.Trim()} → {actionTaken.Trim()}";
        if (desc.Length > 500) desc = desc[..500];
        var resolved = 0;
        foreach (var id in failureIds)
        {
            using var cmd = new SqlCommand("""
                UPDATE dbo.MNT_FailureRegister
                SET    Status = 'RESOLVED', ResolvedAt = @At, ModifiedBy = @By, ModifiedTS = SYSDATETIME()
                WHERE  FailureID = @Id AND ISNULL(Status, 'OPEN') NOT IN ('RESOLVED', 'CLOSED');
                IF @@ROWCOUNT = 0 BEGIN SELECT 0; RETURN; END;
                INSERT INTO dbo.MNT_FailureAction (FailureID, ActionType, Description, TechnicianID, ActionAt, WorkOrderID, CreatedBy, CreatedTS)
                VALUES (@Id, 'REPAIRED', @Desc, @Tech, @At, @Wo, @By, SYSDATETIME());
                SELECT 1;
                """, conn, tx);
            cmd.Parameters.Add("@Id",   SqlDbType.Int).Value            = id;
            cmd.Parameters.Add("@Wo",   SqlDbType.Int).Value            = (object?)woId ?? DBNull.Value;
            cmd.Parameters.Add("@At",   SqlDbType.DateTime2).Value      = resolvedAt;
            cmd.Parameters.Add("@Desc", SqlDbType.NVarChar, 500).Value = desc;
            cmd.Parameters.Add("@Tech", SqlDbType.NVarChar, 450).Value = (object?)techId ?? DBNull.Value;
            cmd.Parameters.Add("@By",   SqlDbType.VarChar,   20).Value = actor;
            if (Convert.ToInt32(cmd.ExecuteScalar()) == 0) continue;
            resolved++;
            ReleaseFailureDowntime(conn, tx, id, resolvedAt, actor);   // 라인 정지로 연 비가동은 해결 시각에 닫는다
        }
        return resolved;
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
    /// PM 실행 이력 + 일정 전진. 기간(TIME) 주기면 다음 예정일 = 완료일 + 주기, 그 밖(사이클·주기 없음)은 사람이 넣은
    /// <paramref name="manualNextDue"/>(완료일 뒤, 없으면 PmNextDueRequiredException — 10-16 사용자 결정, 샷 누적 연동 전까지)로 두고
    /// 다음 작업지시(담당자가 있으면 ASSIGNED, 없으면 ISSUED)를 발행해 ActiveWoID 를 잇는다. 일정 상태는 사용(OK)으로 맞춘다(취소는 그대로).
    /// 이력 행에는 분류·계획번호·설비·유형·이행한 예정일을 스냅샷으로 남긴다 — 일정이 지워지거나 바뀌어도 달력의 완료 칩은 그대로다.
    /// <paramref name="skipped"/> = 작업지시 종료로 이번 회차를 건너뜀(10-15): 마지막 실시일은 그대로, 다음 예정일 = 이번 예정일 + 주기
    /// (예정일이 없으면 종료일 + 주기), 기간 주기가 아니면 활성 작업지시만 비운다(MNT-005·010 수정에서 예정일을 넣으면 다시 발행된다).
    /// </summary>
    private (string? PlanNo, DateTime? NextDue, string? NextWoNumber) AdvancePm(SqlConnection conn, SqlTransaction tx, PmCore pm, int? woId,
        string result, string? note, int? laborMinutes, DateTime completedAt, string? techId, string actor, bool skipped = false,
        DateTime? manualNextDue = null)
    {
        var tech = techId ?? pm.TechId;
        var resultNote = note is { Length: > 500 } ? note[..500] : note;
        var planLabel = pm.PlanNo ?? $"PM#{pm.Id}";

        DateTime? next = PmRules.AutoNextDue(pm.CycleBasis, pm.CycleValue, skipped ? (pm.NextDue ?? completedAt) : completedAt);
        if (next is null && !skipped)
        {
            if (manualNextDue is null) throw new PmNextDueRequiredException(planLabel, false);
            if (!PmRules.ValidManualNextDue(manualNextDue, completedAt)) throw new PmNextDueRequiredException(planLabel, true);
            next = manualNextDue.Value.Date;
        }

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
            cmd.Parameters.Add("@By",   SqlDbType.VarChar,   20).Value = actor;
            cmd.ExecuteNonQuery();
        }

        // 상태는 사용·취소만(PmRules) — 옛 값 DUE·OVERDUE·DONE 은 사용으로 맞추고 취소는 그대로 둔다
        using (var cmd = new SqlCommand("""
            UPDATE dbo.MNT_PMSchedule
            SET    LastPMDate = ISNULL(@Last, LastPMDate), NextDueDate = ISNULL(@Next, NextDueDate),
                   Status = CASE WHEN Status = 'CANCELLED' THEN Status ELSE 'OK' END, ActiveWoID = NULL,
                   AssignedTechID = ISNULL(@Tech, AssignedTechID), ModifiedBy = @By, ModifiedTS = SYSDATETIME()
            WHERE  PMScheduleID = @Pm;
            SELECT Status FROM dbo.MNT_PMSchedule WHERE PMScheduleID = @Pm;
            """, conn, tx))
        {
            cmd.Parameters.Add("@Pm",   SqlDbType.Int).Value            = pm.Id;
            cmd.Parameters.Add("@Last", SqlDbType.Date).Value           = skipped ? DBNull.Value : completedAt.Date;
            cmd.Parameters.Add("@Next", SqlDbType.Date).Value           = (object?)next ?? DBNull.Value;
            cmd.Parameters.Add("@Tech", SqlDbType.NVarChar, 450).Value = (object?)tech ?? DBNull.Value;
            cmd.Parameters.Add("@By",   SqlDbType.VarChar,   20).Value = actor;
            if (PmRules.IsCancelled(cmd.ExecuteScalar() as string)) return (pm.PlanNo, next, null);   // 취소된 일정은 다음 회차를 발행하지 않는다
        }

        if (next is not { } nd) return (pm.PlanNo, null, null);

        var (_, nextWoNumber) = IssuePmWo(conn, tx, pm.Id, pm.PlanNo, pm.EquipId, pm.PmType, tech, pm.ChecklistId, nd, actor);
        return (pm.PlanNo, nd, nextWoNumber);
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
            ("@N", null, topN), ("@P", "MNT_SparePartsTxn.SparePartNo", (object?)sparePartNo ?? DBNull.Value));
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
                cmd.Parameters.Add("@By", SqlDbType.VarChar,   20).Value = actor;
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
                cmd.Parameters.Add("@By",    SqlDbType.VarChar,   20).Value = actor;
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
        // 열린 작업지시에서 「예정」 PM 작업지시는 빼고, PM 7일 내는 취소된 일정을 빼고 분류별로도 센다(PmRules)
        const string sql = $"""
            DECLARE @today DATE = CAST(SYSDATETIME() AS DATE);

            SELECT
              (SELECT COUNT(*) FROM dbo.MNT_FailureRegister WHERE Status IN ('OPEN','REGISTERED','IN_PROGRESS'))         AS OpenFailures,
              (SELECT COUNT(*) FROM dbo.MNT_WorkOrder w
                 WHERE w.Status IN ('ISSUED','ASSIGNED','IN_PROGRESS') AND NOT {PmRules.PlannedWoSql})                  AS OpenWos,
              pm.Total AS PmDueIn7d, pm.Equip AS PmDueEquip, pm.Maint AS PmDueMaint,
              (SELECT COUNT(*) FROM dbo.MD_SparePart p
                 WHERE p.OnHandQty <= ISNULL(p.ReorderPoint,0)
                   AND ISNULL(p.ActiveFlag,1)=1)                                                                          AS LowStockParts,
              -- 오늘 집계가 없으면(야간·휴일·집계 지연) 가장 최근 집계일 평균을 쓰고 그 날짜를 함께 돌려준다
              ISNULL((SELECT AVG(OEE) FROM dbo.MNT_OEELog WHERE AggDate = o.LastDate), 0)                                AS AvgOeeToday,
              o.LastDate                                                                                                  AS OeeDate,
              ISNULL((SELECT SUM(DurationMin) FROM dbo.PP_LineDowntimeLog
                       WHERE StartTS >= DATEADD(HOUR, -24, SYSDATETIME())), 0)                                           AS DowntimeMin24h
            FROM (SELECT MAX(AggDate) AS LastDate FROM dbo.MNT_OEELog WHERE AggDate <= @today) o
            CROSS JOIN (SELECT COUNT(*) AS Total,
                               SUM(CASE WHEN p.PMClass = 'MAINT' THEN 0 ELSE 1 END) AS Equip,
                               SUM(CASE WHEN p.PMClass = 'MAINT' THEN 1 ELSE 0 END) AS Maint
                        FROM   dbo.MNT_PMSchedule p
                        WHERE  p.NextDueDate BETWEEN @today AND DATEADD(DAY, 7, @today) AND ISNULL(p.Status, '') <> 'CANCELLED') pm;
            """;
        // 설비 대수·가동/중지/유휴는 MNT-001 과 같은 자동 판정(EquipStatusRules)으로 센다 — 예방보전(PM) 중인 설비는 셋 어디에도 넣지 않는다
        var equips = ListEquipment();
        int Count(string s) => equips.Count(e => e.Status == s);
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        using var rdr  = cmd.ExecuteReader();
        if (!rdr.Read())
            return new DashboardKpi(equips.Count, Count(EquipStatusRules.Run), Count(EquipStatusRules.Down), Count(EquipStatusRules.Idle), 0, 0, 0, 0, 0m, 0);
        return new DashboardKpi(
            equips.Count, Count(EquipStatusRules.Run), Count(EquipStatusRules.Down), Count(EquipStatusRules.Idle),
            (int)rdr["OpenFailures"], (int)rdr["OpenWos"], (int)rdr["PmDueIn7d"], (int)rdr["LowStockParts"],
            rdr["AvgOeeToday"] as decimal? ?? 0m,
            rdr["DowntimeMin24h"] as int? ?? 0,
            rdr["OeeDate"] as DateTime?,
            rdr["PmDueEquip"] as int? ?? 0, rdr["PmDueMaint"] as int? ?? 0);
    }

    // ── helpers ──────────────────────────────────────────────────────────
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
