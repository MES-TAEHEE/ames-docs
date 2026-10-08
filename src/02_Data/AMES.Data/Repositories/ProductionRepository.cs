using System.Data;
using AMES.Data.Connection;
using AMES.Data.Services;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// Writes PR_ProductionResult (INJ-04 each cycle) + tbl_Lot when a cycle
/// produces output. Also serves hourly + daily roll-ups for INJ-02/07.
/// </summary>
public sealed class ProductionRepository
{
    private readonly AmesConnectionFactory _factory;
    public ProductionRepository(AmesConnectionFactory f) => _factory = f;

    /// <summary>
    /// Records one production cycle as a single batch lot. Increments the step CompletedQty
    /// on (WoID, LineID) and creates a lot row. Returns the new ResultID + the post-update completed qty.
    /// Throws InvalidOperationException if the WO has no routing step on lineId.
    ///
    /// Mold shots are NOT touched here — shot counts come from the PLC shot counter only
    /// (see InjLotRepository.CreateRawLot). INJ manual entry uses
    /// InjLotRepository.CreateManualLots instead, which keeps the 1 lot = 1 pcs model.
    /// </summary>
    public (int ResultId, int LotId, decimal NewCompletedQty) RecordCycle(
        int     woId,
        string  itemNo,
        string  lineId,
        string  processCode,
        int     goodQty,
        int     cycleSec,
        string? moldId,
        string  operatorId,
        int?    sessionId,
        string  employeeNo,
        bool    defectFlag)
    {
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            // 1) tbl_Lot row first (parent for both production + future defect rows)
            int lotId;
            using (var cmd = new SqlCommand("""
                INSERT INTO dbo.tbl_Lot
                    (LotCode, ItemNo, WoID, LineID, ProcessCode, BatchSize, RemainingQty,
                     ProducedAt, Status, QualityFlag, CreatedBy, CreatedTS)
                OUTPUT INSERTED.LotID
                VALUES
                    (@LotCode, @ItemNo, @WoID, @LineID, @Proc, @Qty, @Qty,
                     SYSDATETIME(), 'OPEN', 'PENDING', @By, SYSDATETIME());
                """, conn, tx))
            {
                var lotCode = LotNoGenerator.NextLotNo(conn, tx, lineId, DbClock.Now);
                cmd.Parameters.Add("@LotCode", SqlDbType.VarChar, 40).Value = lotCode;
                cmd.Parameters.Add("@ItemNo",  SqlDbType.VarChar, 20).Value = itemNo;
                cmd.Parameters.Add("@WoID",    SqlDbType.Int       ).Value = woId;
                cmd.Parameters.Add("@LineID",  SqlDbType.VarChar, 20).Value = lineId;
                cmd.Parameters.Add("@Proc",    SqlDbType.VarChar, 10).Value = processCode;
                cmd.Parameters.Add("@Qty",     SqlDbType.Decimal   ).Value = (decimal)goodQty;
                cmd.Parameters.Add("@By",      SqlDbType.VarChar, 20).Value = employeeNo;
                lotId = (int)cmd.ExecuteScalar()!;
            }

            // 2) PR_ProductionResult — 전기일·교대는 설정 DAY_CUTOFF_TIME·공통코드 WORK_SHIFT로 서버 시각에 판정
            var (now, prodDate, shiftCode) = ProdCalendar.ResolveNow(conn, tx);
            int resultId;
            using (var cmd = new SqlCommand("""
                INSERT INTO dbo.PR_ProductionResult
                    (EntryNo, WoID, LotID, LineID, ProcessCode, GoodQty, CycleSec,
                     MoldID, OperatorID, SessionID, DefectFlag, EntryAt, ProdDate, ShiftCode, CreatedBy, CreatedTS)
                OUTPUT INSERTED.ResultID
                VALUES
                    (@EntryNo, @WoID, @LotID, @LineID, @Proc, @Good, @CT,
                     @Mold, @Op, @Sess, @DF, @Now, @ProdDate, @Shift, @By, SYSDATETIME());
                """, conn, tx))
            {
                var entryNo = $"{processCode}-{now:yyyyMMdd}-{lineId}-{now:HHmmssfff}";
                if (entryNo.Length > 28) entryNo = entryNo[..28];
                cmd.Parameters.Add("@EntryNo", SqlDbType.VarChar, 28).Value = entryNo;
                cmd.Parameters.Add("@Now",      SqlDbType.DateTime2     ).Value = now;
                cmd.Parameters.Add("@ProdDate", SqlDbType.Date          ).Value = prodDate;
                cmd.Parameters.Add("@Shift",    SqlDbType.VarChar, 10   ).Value = (object?)shiftCode ?? DBNull.Value;
                cmd.Parameters.Add("@WoID",    SqlDbType.Int           ).Value = woId;
                cmd.Parameters.Add("@LotID",   SqlDbType.Int           ).Value = lotId;
                cmd.Parameters.Add("@LineID",  SqlDbType.VarChar, 20   ).Value = lineId;
                cmd.Parameters.Add("@Proc",    SqlDbType.VarChar, 10   ).Value = processCode;
                cmd.Parameters.Add("@Good",    SqlDbType.Int           ).Value = goodQty;
                cmd.Parameters.Add("@CT",      SqlDbType.Int           ).Value = cycleSec;
                cmd.Parameters.Add("@Mold",    SqlDbType.VarChar, 20   ).Value = (object?)moldId ?? DBNull.Value;
                cmd.Parameters.Add("@Op",      SqlDbType.NVarChar, 450 ).Value = operatorId;
                cmd.Parameters.Add("@Sess",    SqlDbType.Int           ).Value = (object?)sessionId ?? DBNull.Value;
                cmd.Parameters.Add("@DF",      SqlDbType.Bit           ).Value = defectFlag;
                cmd.Parameters.Add("@By",      SqlDbType.VarChar,    20).Value = employeeNo;
                resultId = (int)cmd.ExecuteScalar()!;
            }

            // 3) 단계 실적 반영 (WoID + LineID 로 단계 행 특정)
            var stepId = WorkOrderRepository.FindStepId(conn, tx, woId, lineId)
                ?? throw new InvalidOperationException($"WO {woId} has no routing step on line {lineId}.");
            var newCompleted = WorkOrderRepository.BumpStepCompleted(conn, tx, stepId, goodQty, operatorId);

            tx.Commit();
            return (resultId, lotId, newCompleted);
        }
        catch { tx.Rollback(); throw; }
    }
}
