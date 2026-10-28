using System.Data;
using AMES.Contracts.Dto;
using AMES.Contracts.Enums;
using AMES.Data.Connection;
using AMES.Data.Services;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// REWORK 스테이션 — PR_DefectDetail(Disposition NULL) 대기열과 판정.
/// 대기열은 전 라인 공용이다. 판정은 PR_DefectDetail 행을 잠그고 Disposition 을 재검증해 두 터미널이
/// 같은 LOT 을 동시에 처리하지 못하게 한다(2차 방어선은 UX_PR_DefectDetail_OpenLot).
/// </summary>
public sealed class ReworkRepository
{
    private readonly AmesConnectionFactory _factory;
    public ReworkRepository(AmesConnectionFactory f) => _factory = f;

    const string SelectView = """
        SELECT d.DefectID, d.LotID, l.LotCode, l.ItemNo, COALESCE(i.ItemName, N'') AS ItemName,
               d.ProcessCode, l.LineID, d.DefectCode, c.DefectName, c.DefectNameEn, c.DefaultCauseCode,
               d.PriorStatus, d.ReasonNote, d.DetectedAt, d.CreatedBy, d.WoID, w.WoNumber,
               d.Disposition, d.CauseCode, d.CorrectiveAction, d.DispositionBy, d.DispositionAt
        FROM   dbo.PR_DefectDetail d
        JOIN   dbo.tbl_Lot          l ON l.LotID = d.LotID
        LEFT   JOIN dbo.MD_Item       i ON i.ItemNo = l.ItemNo
        LEFT   JOIN dbo.MD_DefectCode c ON c.DefectCode = d.DefectCode
        LEFT   JOIN dbo.PP_WorkOrder  w ON w.WoID = d.WoID
        """;

    static ReworkItemDto MapToDto(SqlDataReader r) => new()
    {
        DefectId         = (int)r["DefectID"],
        LotId            = (int)r["LotID"],
        LotCode          = r["LotCode"] as string ?? string.Empty,
        ItemNo           = r["ItemNo"]  as string ?? string.Empty,
        ItemName         = (string)r["ItemName"],
        ProcessCode      = r["ProcessCode"] as string ?? "INJ",
        LineId           = r["LineID"] as string ?? string.Empty,
        DefectCode       = r["DefectCode"] as string ?? string.Empty,
        DefectName       = r["DefectName"]       as string,
        DefectNameEn     = r["DefectNameEn"]     as string,
        DefaultCauseCode = r["DefaultCauseCode"] as string,
        PriorStatus      = r["PriorStatus"]      as string,
        ReasonNote       = r["ReasonNote"]       as string,
        DetectedAt       = r["DetectedAt"] as DateTime? ?? default,
        RegisteredBy     = r["CreatedBy"]        as string,
        WoId             = r["WoID"]             as int?,
        WoNumber         = r["WoNumber"]         as string,
        Disposition      = r["Disposition"]      as string,
        CauseCode        = r["CauseCode"]        as string,
        CorrectiveAction = r["CorrectiveAction"] as string,
        DispositionBy    = r["DispositionBy"]    as string,
        DispositionAt    = r["DispositionAt"]    as DateTime?,
    };

    /// <summary>재작업 대기 전부(전 라인), 최신 등록순.</summary>
    public List<ReworkItemDto> ListPending()
    {
        var sql = "dbo.SP_POP_RWK_ListPending";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        using var rdr  = cmd.ExecuteReader();
        var list = new List<ReworkItemDto>();
        while (rdr.Read()) list.Add(MapToDto(rdr));
        return list;
    }

    public ReworkItemDto? FindPendingByLotCode(string lotCode)
    {
        var sql = "dbo.SP_POP_RWK_FindPendingByLotCode";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@Code", SqlDbType.VarChar, 40).Value = lotCode;
        using var rdr = cmd.ExecuteReader();
        return rdr.Read() ? MapToDto(rdr) : null;
    }

    /// <summary>오늘 판정된 행 최신순 (DispositionAt 기준).</summary>
    public List<ReworkItemDto> ListTodayDecided(int top = 100)
    {
        var sql = "dbo.SP_POP_RWK_ListTodayDecided";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@Top", SqlDbType.Int).Value = top;
        using var rdr = cmd.ExecuteReader();
        var list = new List<ReworkItemDto>();
        while (rdr.Read()) list.Add(MapToDto(rdr));
        return list;
    }

    public ReworkStatsDto GetTodayStats()
    {
        const string sql = "dbo.SP_POP_RWK_GetTodayStats";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        using var rdr  = cmd.ExecuteReader();
        if (!rdr.Read()) return new ReworkStatsDto { Pending = 0, TodayReworked = 0, TodayScrapped = 0 };
        return new ReworkStatsDto
        {
            Pending       = Convert.ToInt32(rdr["Pending"]),
            TodayReworked = Convert.ToInt32(rdr["TodayReworked"]),
            TodayScrapped = Convert.ToInt32(rdr["TodayScrapped"]),
        };
    }

    // 판정 대상 행 잠금 + 재검증. 반환 null 이면 호출자가 outcome 을 이미 정했다.
    private static (int LotId, int? WoId, string ProcessCode, string ItemNo, string LineId, string? PriorStatus)? LockOpenRow(
        SqlConnection conn, SqlTransaction tx, int defectId, out ReworkOutcome outcome)
    {
        using var cmd = new SqlCommand("dbo.SP_POP_RWK_LockOpenRow", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@D", SqlDbType.Int).Value = defectId;
        using var rdr = cmd.ExecuteReader();
        if (!rdr.Read()) { outcome = ReworkOutcome.NotFound; return null; }
        if (!LotDefectRules.CanDecide(rdr["Disposition"] as string)) { outcome = ReworkOutcome.AlreadyDecided; return null; }
        outcome = ReworkOutcome.Done;
        return ((int)rdr["LotID"], rdr["WoID"] as int?, rdr["ProcessCode"] as string ?? "INJ",
                rdr["ItemNo"] as string ?? string.Empty, rdr["LineID"] as string ?? string.Empty,
                rdr["PriorStatus"] as string);
    }

    private static void CloseRow(SqlConnection conn, SqlTransaction tx, int defectId, string disposition,
                                 string causeCode, string? note, string operatorId, string employeeNo, DateTime? at)
    {
        using var cmd = new SqlCommand("dbo.SP_POP_RWK_CloseRow", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@D",     SqlDbType.Int          ).Value = defectId;
        cmd.Parameters.Add("@Disp",  SqlDbType.VarChar,  20 ).Value = disposition;
        cmd.Parameters.Add("@Cause", SqlDbType.VarChar,  16 ).Value = causeCode;
        cmd.Parameters.Add("@Note",  SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(note) ? DBNull.Value : note.Trim();
        cmd.Parameters.Add("@By",    SqlDbType.VarChar,  20 ).Value = employeeNo;
        cmd.Parameters.Add("@Op",    SqlDbType.VarChar,   20).Value = operatorId;
        AddAt(cmd, at);
        cmd.ExecuteNonQuery();
    }

    // 오프라인 POP 복구 때만 넘긴다 — 없으면 붙이지 않아 @At 이 없는 구 프로시저에서도 돈다.
    private static void AddAt(SqlCommand cmd, DateTime? at)
    {
        if (at is { } v) cmd.Parameters.Add("@At", SqlDbType.DateTime2).Value = v;
    }

    /// <summary>
    /// 수리 → 양품: 원래 WO 에 실적 +1(ProcessCode RWK, REWORK 라인) + 단계 +1, LOT CONFIRMED, 행 REWORKED.
    /// WO 는 행의 WoID(확정 후 LOT) 를 먼저, 없으면 원래 라인에서 품번의 열린 단계로 해석한다.
    /// 둘 다 없으면 WO 없이 확정한다(WoID NULL, 단계 반영 없음) — WO 없는 생산분의 수리(2026-10-01).
    /// </summary>
    public ReworkOutcome Rework(int defectId, string causeCode, string? note,
                                string reworkLineId, string operatorId, int? sessionId, string employeeNo)
        => Rework(defectId, causeCode, note, reworkLineId, operatorId, sessionId, employeeNo, out _);

    /// <param name="backflush">확정된 적 없는 LOT(RAW → DEFECT → 수리)만 실제로 빠진다 — 이미 확정됐던 LOT 은 Apply 가 멱등으로 건너뛴다.</param>
    public ReworkOutcome Rework(int defectId, string causeCode, string? note,
                                string reworkLineId, string operatorId, int? sessionId, string employeeNo,
                                out BackflushResult backflush, DateTime? occurredAt = null)
    {
        backflush = BackflushResult.None;
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            var row = LockOpenRow(conn, tx, defectId, out var outcome);
            if (row is null) { tx.Rollback(); return outcome; }
            var (lotId, rowWoId, processCode, itemNo, lineId, priorStatus) = row.Value;

            int? woId = null, stepId = null;
            if (rowWoId is int w && WorkOrderRepository.FindStepId(conn, tx, w, lineId) is int s) { woId = w; stepId = s; }
            else if (LotDefectWriter.ResolveOpenWo(conn, tx, lineId, itemNo) is { } open) { woId = open.WoId; stepId = open.StepId; }

            var (now, prodDate, shiftCode) = ProdCalendar.ResolveNow(conn, tx, occurredAt);
            using (var cmd = new SqlCommand("dbo.SP_POP_RWK_Rework_InsProductionResult", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
            {
                var entryNo = $"E{now:yyMMddHHmmssfff}-{reworkLineId}";
                if (entryNo.Length > 28) entryNo = entryNo[..28];
                cmd.Parameters.Add("@EntryNo",  SqlDbType.VarChar, 28  ).Value = entryNo;
                cmd.Parameters.Add("@WoID",     SqlDbType.Int          ).Value = (object?)woId ?? DBNull.Value;
                cmd.Parameters.Add("@LotID",    SqlDbType.Int          ).Value = lotId;
                cmd.Parameters.Add("@LineID",   SqlDbType.VarChar, 20  ).Value = reworkLineId;
                cmd.Parameters.Add("@Op",       SqlDbType.NVarChar, 450).Value = operatorId;
                cmd.Parameters.Add("@Sess",     SqlDbType.Int          ).Value = (object?)sessionId ?? DBNull.Value;
                cmd.Parameters.Add("@Now",      SqlDbType.DateTime2    ).Value = now;
                cmd.Parameters.Add("@ProdDate", SqlDbType.Date         ).Value = prodDate;
                cmd.Parameters.Add("@Shift",    SqlDbType.VarChar, 10  ).Value = (object?)shiftCode ?? DBNull.Value;
                cmd.Parameters.Add("@By",       SqlDbType.VarChar,   20).Value = employeeNo;
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new SqlCommand("dbo.SP_POP_RWK_Rework_UpdLotConfirm", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
            {
                cmd.Parameters.Add("@ProcessCode", SqlDbType.VarChar, 10).Value = processCode;
                cmd.Parameters.Add("@Lot",  SqlDbType.Int          ).Value = lotId;
                cmd.Parameters.Add("@WoID", SqlDbType.Int          ).Value = (object?)woId ?? DBNull.Value;
                cmd.Parameters.Add("@Op",   SqlDbType.VarChar,   20).Value = operatorId;
                cmd.Parameters.Add("@Sess", SqlDbType.Int          ).Value = (object?)sessionId ?? DBNull.Value;
                AddAt(cmd, occurredAt);
                cmd.ExecuteNonQuery();
            }

            if (stepId is int step) WorkOrderRepository.BumpStepCompleted(conn, tx, step, 1m, operatorId);
            // 확정된 적 없는 LOT(등록 전 상태 RAW·NG_BLOCKED)만 — 전에 CONFIRMED 였던 LOT 은 그때 자재가 들어갔다(배포 전 확정이라
            // 차감 기록이 없어도 다시 빼지 않는다). 자재는 LOT 의 원래 라인(lineId)에서 썼다 — 재작업 라인이 아니다
            if (LotDefectRules.WasNeverConfirmed(priorStatus))
                backflush = BackflushRepository.Apply(conn, tx, lotId, ReadLotCode(conn, tx, lotId), itemNo, lineId, operatorId, employeeNo,
                                                      BackflushRules.IncludesInjection(processCode));
            CloseRow(conn, tx, defectId, LotDefectRules.DispositionReworked, causeCode, note, operatorId, employeeNo, occurredAt);

            tx.Commit();
            return ReworkOutcome.Done;
        }
        catch { tx.Rollback(); throw; }
    }

    static string ReadLotCode(SqlConnection conn, SqlTransaction tx, int lotId)
    {
        using var cmd = new SqlCommand("dbo.SP_POP_RWK_ReadLotCode", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure };
        ColumnParams.AddFilter(cmd, "@L", "tbl_Lot.LotID", lotId);
        return cmd.ExecuteScalar() as string ?? lotId.ToString();
    }

    /// <summary>폐기: LOT SCRAPPED + tbl_Lot SCRAP, 행 SCRAPPED. 실적은 건드리지 않는다(등록 시 이미 역분개됐거나 애초에 없다).</summary>
    public ReworkOutcome Scrap(int defectId, string causeCode, string? note, string operatorId, string employeeNo)
        => Scrap(defectId, causeCode, note, operatorId, employeeNo, out _);

    /// <param name="unload">
    /// 그 LOT 의 재고 행이 아직 남아 있었으면(불량 등록이 재고를 빼기 전에 등록된 LOT) 같은 트랜잭션에서 지운 결과 —
    /// 안전망이라 보통은 <see cref="PalletStockRepository.PalletUnloadResult.None"/> 이다. 위치가 있던 행이면 화면이 경고한다.
    /// </param>
    public ReworkOutcome Scrap(int defectId, string causeCode, string? note, string operatorId, string employeeNo,
                               out PalletStockRepository.PalletUnloadResult unload, DateTime? occurredAt = null)
    {
        unload = PalletStockRepository.PalletUnloadResult.None;
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            var row = LockOpenRow(conn, tx, defectId, out var outcome);
            if (row is null) { tx.Rollback(); return outcome; }
            var (lotId, _, processCode, itemNo, _, _) = row.Value;

            using (var cmd = new SqlCommand("dbo.SP_POP_RWK_Scrap", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
            {
                cmd.Parameters.Add("@ProcessCode", SqlDbType.VarChar, 10).Value = processCode;
                cmd.Parameters.Add("@Lot", SqlDbType.Int          ).Value = lotId;
                cmd.Parameters.Add("@Op",  SqlDbType.VarChar,   20).Value = operatorId;
                AddAt(cmd, occurredAt);
                cmd.ExecuteNonQuery();
            }

            CloseRow(conn, tx, defectId, LotDefectRules.DispositionScrapped, causeCode, note, operatorId, employeeNo, occurredAt);
            unload = PalletStockRepository.Unload(conn, tx, lotId, ReadLotCode(conn, tx, lotId), itemNo,
                                                  PalletStockRepository.ScrapSourceType, $"SCRAP {causeCode}", operatorId, employeeNo);
            tx.Commit();
            return ReworkOutcome.Done;
        }
        catch { tx.Rollback(); throw; }
    }
}
