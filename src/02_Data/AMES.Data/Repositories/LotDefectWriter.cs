using System.Data;
using AMES.Data.Services;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// INJ·IMG 불량 등록이 공유하는 쓰기 조각. 전부 호출측 트랜잭션에 참여한다 — 부분 반영이 없어야
/// 역분개·단계 차감·PR_DefectDetail·LOT 상태가 같이 성공하거나 같이 실패한다.
/// </summary>
internal static class LotDefectWriter
{
    /// <summary>
    /// 확정 후 LOT 의 실적을 되돌린다: 원래 확정 실적을 찾아 GoodQty −1 역분개 행을 넣고 LOT 라인의 단계를 −1.
    /// <paramref name="lotLineId"/> 는 tbl_Lot.LineID — 재작업 후 다시 불량이면 최신 +1 실적이 LINE-RWK-01
    /// (라우팅 단계 없음) 이라 그 행의 LineID 로는 단계를 못 찾는다. 역분개 행 자체는 실적 행의 라인으로 남긴다.
    /// 반환: (원래 실적 ID, 역분개 실적 ID, WoID). 원래 실적이 없으면 (null, null, null) — 아무것도 쓰지 않는다.
    /// </summary>
    internal static (int? OrigResultId, int? ReversalResultId, int? WoId) ReverseConfirmedResult(
        SqlConnection conn, SqlTransaction tx, int lotId, string lotLineId,
        string operatorId, int? sessionId, string employeeNo)
    {
        int origId, woId; string lineId, processCode; string? moldId;
        using (var cmd = new SqlCommand("""
            SELECT TOP 1 ResultID, WoID, LineID, ProcessCode, MoldID
            FROM   dbo.PR_ProductionResult
            WHERE  LotID = @Lot AND GoodQty > 0
            ORDER  BY ResultID DESC;
            """, conn, tx))
        {
            cmd.Parameters.Add("@Lot", SqlDbType.Int).Value = lotId;
            using var rdr = cmd.ExecuteReader();
            if (!rdr.Read()) return (null, null, null);
            origId      = (int)rdr["ResultID"];
            woId        = (int)rdr["WoID"];
            lineId      = (string)rdr["LineID"];
            processCode = rdr["ProcessCode"] as string ?? "INJ";
            moldId      = rdr["MoldID"] as string;
        }

        var (now, prodDate, shiftCode) = ProdCalendar.ResolveNow(conn, tx);
        int reversalId;
        // DefectFlag = 0: 불량 자체는 PR_DefectDetail 이 센다. 여기에 1 을 주면 상세 Qty 와 겹쳐 보고서가 이중 계상한다.
        // 역분개 행은 GoodQty < 0 과 EntryNo 의 'R' 접두로 식별한다.
        using (var cmd = new SqlCommand("""
            INSERT INTO dbo.PR_ProductionResult
                (EntryNo, WoID, LotID, LineID, ProcessCode, GoodQty, CycleSec,
                 MoldID, OperatorID, SessionID, DefectFlag, EntryAt, ProdDate, ShiftCode, CreatedBy, CreatedTS)
            OUTPUT INSERTED.ResultID
            VALUES
                (@EntryNo, @WoID, @LotID, @LineID, @Proc, -1, 0,
                 @Mold, @Op, @Sess, 0, @Now, @ProdDate, @Shift, @By, SYSDATETIME());
            """, conn, tx))
        {
            var entryNo = $"R{now:yyMMddHHmmssfff}-{lineId}";
            if (entryNo.Length > 28) entryNo = entryNo[..28];
            cmd.Parameters.Add("@EntryNo",  SqlDbType.VarChar, 28  ).Value = entryNo;
            cmd.Parameters.Add("@WoID",     SqlDbType.Int          ).Value = woId;
            cmd.Parameters.Add("@LotID",    SqlDbType.Int          ).Value = lotId;
            cmd.Parameters.Add("@LineID",   SqlDbType.VarChar, 20  ).Value = lineId;
            cmd.Parameters.Add("@Proc",     SqlDbType.VarChar, 10  ).Value = processCode;
            cmd.Parameters.Add("@Mold",     SqlDbType.VarChar, 20  ).Value = (object?)moldId ?? DBNull.Value;
            cmd.Parameters.Add("@Op",       SqlDbType.NVarChar, 450).Value = operatorId;
            cmd.Parameters.Add("@Sess",     SqlDbType.Int          ).Value = (object?)sessionId ?? DBNull.Value;
            cmd.Parameters.Add("@Now",      SqlDbType.DateTime2    ).Value = now;
            cmd.Parameters.Add("@ProdDate", SqlDbType.Date         ).Value = prodDate;
            cmd.Parameters.Add("@Shift",    SqlDbType.VarChar, 10  ).Value = (object?)shiftCode ?? DBNull.Value;
            cmd.Parameters.Add("@By",       SqlDbType.VarChar, 50  ).Value = employeeNo;
            reversalId = (int)cmd.ExecuteScalar()!;
        }

        // 단계 행이 없으면(백필 전 WO 등) 실적 행만 남긴다 — 단계가 없는데 예외로 등록을 막을 이유는 없다.
        var stepId = WorkOrderRepository.FindStepId(conn, tx, woId, lotLineId);
        if (stepId is int s) WorkOrderRepository.BumpStepCompleted(conn, tx, s, -1m, operatorId);

        return (origId, reversalId, woId);
    }

    /// <summary>불량 LOT 1건 = PR_DefectDetail 1행. Qty 는 항상 1, Disposition NULL(재작업 대기).</summary>
    internal static int InsertDefectDetail(
        SqlConnection conn, SqlTransaction tx,
        int lotId, int? woId, int? resultId, string processCode, string defectCode,
        string priorStatus, int? reversalResultId, string? reasonNote,
        string operatorId, string employeeNo)
    {
        using var cmd = new SqlCommand("""
            INSERT INTO dbo.PR_DefectDetail
                (ResultID, WoID, LotID, ProcessCode, DefectCode, Qty, ReasonNote,
                 PriorStatus, ReversalResultID, DetectedAt, RegisteredBy, CreatedBy, CreatedTS)
            OUTPUT INSERTED.DefectID
            VALUES
                (@R, @W, @L, @P, @C, 1, @N,
                 @Prior, @Rev, SYSDATETIME(), @Op, @By, SYSDATETIME());
            """, conn, tx);
        cmd.Parameters.Add("@R",     SqlDbType.Int          ).Value = (object?)resultId ?? DBNull.Value;
        cmd.Parameters.Add("@W",     SqlDbType.Int          ).Value = (object?)woId ?? DBNull.Value;
        cmd.Parameters.Add("@L",     SqlDbType.Int          ).Value = lotId;
        cmd.Parameters.Add("@P",     SqlDbType.VarChar,  10 ).Value = processCode;
        cmd.Parameters.Add("@C",     SqlDbType.VarChar,  16 ).Value = defectCode;
        cmd.Parameters.Add("@N",     SqlDbType.NVarChar, 500).Value = (object?)reasonNote ?? DBNull.Value;
        cmd.Parameters.Add("@Prior", SqlDbType.VarChar,  16 ).Value = priorStatus;
        cmd.Parameters.Add("@Rev",   SqlDbType.Int          ).Value = (object?)reversalResultId ?? DBNull.Value;
        cmd.Parameters.Add("@Op",    SqlDbType.NVarChar, 450).Value = operatorId;
        cmd.Parameters.Add("@By",    SqlDbType.VarChar,  50 ).Value = employeeNo;
        return (int)cmd.ExecuteScalar()!;
    }

    /// <summary>이 라인에서 품번의 열린 WO 단계 (ConfirmByLotCode 와 같은 규칙, 단계 행 잠금). 없으면 null.</summary>
    internal static (int WoId, int StepId)? ResolveOpenWo(SqlConnection conn, SqlTransaction tx, string lineId, string itemNo)
    {
        using var cmd = new SqlCommand("""
            SELECT TOP 1 r.WoID, r.RoutingLineID
            FROM   dbo.PP_WorkOrderRouting r WITH (UPDLOCK, ROWLOCK)
            JOIN   dbo.PP_WorkOrder        w ON w.WoID = r.WoID

            """ + WorkOrderRepository.OpenStepForItemFilter + ";", conn, tx);
        cmd.Parameters.Add("@Line", SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@Item", SqlDbType.VarChar, 20).Value = itemNo;
        using var rdr = cmd.ExecuteReader();
        if (!rdr.Read()) return null;
        return ((int)rdr["WoID"], (int)rdr["RoutingLineID"]);
    }
}
