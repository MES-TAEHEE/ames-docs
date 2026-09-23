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
        var sql = SelectView + """

            WHERE  d.Disposition IS NULL AND d.LotID IS NOT NULL
            ORDER  BY d.DetectedAt DESC, d.DefectID DESC;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        using var rdr  = cmd.ExecuteReader();
        var list = new List<ReworkItemDto>();
        while (rdr.Read()) list.Add(MapToDto(rdr));
        return list;
    }

    public ReworkItemDto? FindPendingByLotCode(string lotCode)
    {
        var sql = SelectView + """

            WHERE  d.Disposition IS NULL AND l.LotCode = @Code;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Code", SqlDbType.VarChar, 40).Value = lotCode;
        using var rdr = cmd.ExecuteReader();
        return rdr.Read() ? MapToDto(rdr) : null;
    }

    /// <summary>오늘 판정된 행 최신순 (DispositionAt 기준).</summary>
    public List<ReworkItemDto> ListTodayDecided(int top = 100)
    {
        var sql = SelectView + """

            WHERE  d.Disposition IN ('REWORKED','SCRAPPED')
              AND  d.DispositionAt >= CAST(SYSDATETIME() AS date)
              AND  d.DispositionAt <  DATEADD(day, 1, CAST(SYSDATETIME() AS date))
            ORDER  BY d.DispositionAt DESC, d.DefectID DESC
            OFFSET 0 ROWS FETCH NEXT @Top ROWS ONLY;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Top", SqlDbType.Int).Value = top;
        using var rdr = cmd.ExecuteReader();
        var list = new List<ReworkItemDto>();
        while (rdr.Read()) list.Add(MapToDto(rdr));
        return list;
    }

    public ReworkStatsDto GetTodayStats()
    {
        const string sql = """
            DECLARE @Today date = CAST(SYSDATETIME() AS date);
            SELECT
              (SELECT COUNT(*) FROM dbo.PR_DefectDetail WHERE Disposition IS NULL AND LotID IS NOT NULL) AS Pending,
              (SELECT COUNT(*) FROM dbo.PR_DefectDetail WHERE Disposition = 'REWORKED'
                 AND DispositionAt >= @Today AND DispositionAt < DATEADD(day,1,@Today)) AS TodayReworked,
              (SELECT COUNT(*) FROM dbo.PR_DefectDetail WHERE Disposition = 'SCRAPPED'
                 AND DispositionAt >= @Today AND DispositionAt < DATEADD(day,1,@Today)) AS TodayScrapped;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
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
    private static (int LotId, int? WoId, string ProcessCode, string ItemNo, string LineId)? LockOpenRow(
        SqlConnection conn, SqlTransaction tx, int defectId, out ReworkOutcome outcome)
    {
        using var cmd = new SqlCommand("""
            SELECT d.LotID, d.WoID, d.ProcessCode, d.Disposition, l.ItemNo, l.LineID
            FROM   dbo.PR_DefectDetail d WITH (UPDLOCK, ROWLOCK)
            JOIN   dbo.tbl_Lot         l WITH (UPDLOCK, ROWLOCK) ON l.LotID = d.LotID
            WHERE  d.DefectID = @D;
            """, conn, tx);
        cmd.Parameters.Add("@D", SqlDbType.Int).Value = defectId;
        using var rdr = cmd.ExecuteReader();
        if (!rdr.Read()) { outcome = ReworkOutcome.NotFound; return null; }
        if (!LotDefectRules.CanDecide(rdr["Disposition"] as string)) { outcome = ReworkOutcome.AlreadyDecided; return null; }
        outcome = ReworkOutcome.Done;
        return ((int)rdr["LotID"], rdr["WoID"] as int?, rdr["ProcessCode"] as string ?? "INJ",
                rdr["ItemNo"] as string ?? string.Empty, rdr["LineID"] as string ?? string.Empty);
    }

    private static void CloseRow(SqlConnection conn, SqlTransaction tx, int defectId, string disposition,
                                 string causeCode, string? note, string operatorId, string employeeNo)
    {
        using var cmd = new SqlCommand("""
            UPDATE dbo.PR_DefectDetail
            SET    Disposition = @Disp, CauseCode = @Cause, CorrectiveAction = @Note,
                   DispositionBy = @By, DispositionAt = SYSDATETIME(),
                   ModifiedBy = @Op, ModifiedTS = SYSDATETIME()
            WHERE  DefectID = @D;
            """, conn, tx);
        cmd.Parameters.Add("@D",     SqlDbType.Int          ).Value = defectId;
        cmd.Parameters.Add("@Disp",  SqlDbType.VarChar,  20 ).Value = disposition;
        cmd.Parameters.Add("@Cause", SqlDbType.VarChar,  16 ).Value = causeCode;
        cmd.Parameters.Add("@Note",  SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(note) ? DBNull.Value : note.Trim();
        cmd.Parameters.Add("@By",    SqlDbType.VarChar,  50 ).Value = employeeNo;
        cmd.Parameters.Add("@Op",    SqlDbType.NVarChar,  20).Value = operatorId;
        cmd.ExecuteNonQuery();
    }

    // PR_InjLot / PR_ImgLot 은 컬럼 이름이 같아 테이블명만 바꾼다. 다른 공정코드는 없다.
    private static string LotTable(string processCode) => processCode switch
    {
        "INJ" => "dbo.PR_InjLot",
        "IMG" => "dbo.PR_ImgLot",
        _     => throw new InvalidOperationException($"No lot table for process '{processCode}'."),
    };

    /// <summary>
    /// 수리 → 양품: 원래 WO 에 실적 +1(ProcessCode RWK, REWORK 라인) + 단계 +1, LOT CONFIRMED, 행 REWORKED.
    /// WO 는 행의 WoID(확정 후 LOT) 를 먼저, 없으면 원래 라인에서 품번의 열린 단계로 해석한다. 둘 다 없으면 NoWo — 폐기는 가능.
    /// </summary>
    public ReworkOutcome Rework(int defectId, string causeCode, string? note,
                                string reworkLineId, string operatorId, int? sessionId, string employeeNo)
    {
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            var row = LockOpenRow(conn, tx, defectId, out var outcome);
            if (row is null) { tx.Rollback(); return outcome; }
            var (lotId, rowWoId, processCode, itemNo, lineId) = row.Value;

            int woId, stepId;
            if (rowWoId is int w && WorkOrderRepository.FindStepId(conn, tx, w, lineId) is int s) { woId = w; stepId = s; }
            else if (LotDefectWriter.ResolveOpenWo(conn, tx, lineId, itemNo) is { } open) { woId = open.WoId; stepId = open.StepId; }
            else { tx.Rollback(); return ReworkOutcome.NoWo; }

            var (now, prodDate, shiftCode) = ProdCalendar.ResolveNow(conn, tx);
            using (var cmd = new SqlCommand("""
                INSERT INTO dbo.PR_ProductionResult
                    (EntryNo, WoID, LotID, LineID, ProcessCode, GoodQty, CycleSec,
                     OperatorID, SessionID, DefectFlag, EntryAt, ProdDate, ShiftCode, CreatedBy, CreatedTS)
                VALUES
                    (@EntryNo, @WoID, @LotID, @LineID, 'RWK', 1, 0,
                     @Op, @Sess, 0, @Now, @ProdDate, @Shift, @By, SYSDATETIME());
                """, conn, tx))
            {
                var entryNo = $"E{now:yyMMddHHmmssfff}-{reworkLineId}";
                if (entryNo.Length > 28) entryNo = entryNo[..28];
                cmd.Parameters.Add("@EntryNo",  SqlDbType.VarChar, 28  ).Value = entryNo;
                cmd.Parameters.Add("@WoID",     SqlDbType.Int          ).Value = woId;
                cmd.Parameters.Add("@LotID",    SqlDbType.Int          ).Value = lotId;
                cmd.Parameters.Add("@LineID",   SqlDbType.VarChar, 20  ).Value = reworkLineId;
                cmd.Parameters.Add("@Op",       SqlDbType.NVarChar, 450).Value = operatorId;
                cmd.Parameters.Add("@Sess",     SqlDbType.Int          ).Value = (object?)sessionId ?? DBNull.Value;
                cmd.Parameters.Add("@Now",      SqlDbType.DateTime2    ).Value = now;
                cmd.Parameters.Add("@ProdDate", SqlDbType.Date         ).Value = prodDate;
                cmd.Parameters.Add("@Shift",    SqlDbType.VarChar, 10  ).Value = (object?)shiftCode ?? DBNull.Value;
                cmd.Parameters.Add("@By",       SqlDbType.VarChar, 50  ).Value = employeeNo;
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new SqlCommand($"""
                UPDATE {LotTable(processCode)}
                SET    ConfirmStatus = 'CONFIRMED', ConfirmedAt = SYSDATETIME(),
                       ConfirmedBy = @Op, ConfirmedSessionID = @Sess,
                       ModifiedBy = @Op, ModifiedTS = SYSDATETIME()
                WHERE  LotID = @Lot;
                UPDATE dbo.tbl_Lot
                SET    Status = 'CONFIRMED', QualityFlag = 'OK', WoID = @WoID,
                       ModifiedBy = @Op, ModifiedTS = SYSDATETIME()
                WHERE  LotID = @Lot;
                """, conn, tx))
            {
                cmd.Parameters.Add("@Lot",  SqlDbType.Int          ).Value = lotId;
                cmd.Parameters.Add("@WoID", SqlDbType.Int          ).Value = woId;
                cmd.Parameters.Add("@Op",   SqlDbType.NVarChar,  20).Value = operatorId;
                cmd.Parameters.Add("@Sess", SqlDbType.Int          ).Value = (object?)sessionId ?? DBNull.Value;
                cmd.ExecuteNonQuery();
            }

            WorkOrderRepository.BumpStepCompleted(conn, tx, stepId, 1m, operatorId);
            CloseRow(conn, tx, defectId, LotDefectRules.DispositionReworked, causeCode, note, operatorId, employeeNo);

            tx.Commit();
            return ReworkOutcome.Done;
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>폐기: LOT SCRAPPED + tbl_Lot SCRAP, 행 SCRAPPED. 실적은 건드리지 않는다(등록 시 이미 역분개됐거나 애초에 없다).</summary>
    public ReworkOutcome Scrap(int defectId, string causeCode, string? note, string operatorId, string employeeNo)
    {
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            var row = LockOpenRow(conn, tx, defectId, out var outcome);
            if (row is null) { tx.Rollback(); return outcome; }
            var (lotId, _, processCode, _, _) = row.Value;

            using (var cmd = new SqlCommand($"""
                UPDATE {LotTable(processCode)}
                SET    ConfirmStatus = 'SCRAPPED', ConfirmedAt = SYSDATETIME(), ConfirmedBy = @Op,
                       ModifiedBy = @Op, ModifiedTS = SYSDATETIME()
                WHERE  LotID = @Lot;
                UPDATE dbo.tbl_Lot
                SET    Status = 'SCRAP', QualityFlag = 'NG', ModifiedBy = @Op, ModifiedTS = SYSDATETIME()
                WHERE  LotID = @Lot;
                """, conn, tx))
            {
                cmd.Parameters.Add("@Lot", SqlDbType.Int          ).Value = lotId;
                cmd.Parameters.Add("@Op",  SqlDbType.NVarChar,  20).Value = operatorId;
                cmd.ExecuteNonQuery();
            }

            CloseRow(conn, tx, defectId, LotDefectRules.DispositionScrapped, causeCode, note, operatorId, employeeNo);
            tx.Commit();
            return ReworkOutcome.Done;
        }
        catch { tx.Rollback(); throw; }
    }
}
