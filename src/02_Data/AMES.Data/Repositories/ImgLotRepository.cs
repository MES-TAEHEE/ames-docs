using System.Data;
using AMES.Contracts.Dto;
using AMES.Contracts.Enums;
using AMES.Data.Connection;
using AMES.Data.Services;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// IMG(래핑) 원천 LOT — tbl_Lot(ProcessCode='IMG', 1 EA) + PR_ImgLot.
/// IMG-MAIN 의 "라벨 발행 → 스캔 확정" 모델을 담당한다. INJ 와 달리 에이전트가
/// 없으므로 LOT 은 오직 터미널의 라벨 발행 버튼이 만들고, 라벨은 그 자리에서
/// 동기 출력된다 (LabelDispatcher 는 INJ 세션에서만 돈다).
/// </summary>
public sealed class ImgLotRepository
{
    private readonly AmesConnectionFactory _factory;
    public ImgLotRepository(AmesConnectionFactory f) => _factory = f;

    const string ProcessCode = "IMG";

    // 호출부가 WHERE 를 이어 붙인다.
    const string SelectLotView = """
        SELECT l.LotID, l.LotCode, l.ItemNo, mi.ItemName, mi.PGN, mi.ALC, mi.MountPos, l.LineID,
               e.EquipID, e.CustomerCode, e.ConfirmStatus, e.ConfirmedAt,
               e.FabricRollLotID, e.FabricConsumedM, e.BondSetupID,
               e.PrintedCount, l.CreatedTS
        FROM   dbo.tbl_Lot l
        JOIN   dbo.PR_ImgLot e ON e.LotID = l.LotID
        LEFT   JOIN dbo.MD_Item mi ON mi.ItemNo = l.ItemNo
        """;

    static ImgLotDto MapToDto(SqlDataReader rdr) => new()
    {
        LotId           = (int)rdr["LotID"],
        LotCode         = (string)rdr["LotCode"],
        ItemNo          = rdr["ItemNo"]   as string ?? string.Empty,
        ItemName        = rdr["ItemName"] as string,
        Pgn             = rdr["PGN"]      as string,
        Alc             = rdr["ALC"]      as string,
        MountPos        = rdr["MountPos"] as string,
        CustomerCode    = rdr["CustomerCode"] as string,
        LineId          = rdr["LineID"]   as string,
        EquipId         = rdr["EquipID"]  as string,
        ConfirmStatus   = (string)rdr["ConfirmStatus"],
        ConfirmedAt     = rdr["ConfirmedAt"]     as DateTime?,
        FabricRollLotId = rdr["FabricRollLotID"] as int?,
        FabricConsumedM = rdr["FabricConsumedM"] as decimal?,
        BondSetupId     = rdr["BondSetupID"]     as int?,
        PrintedCount    = (int)rdr["PrintedCount"],
        CreatedTS       = rdr["CreatedTS"] as DateTime? ?? default,
    };

    /// <summary>
    /// 라벨 발행 버튼 — RAW LOT 1건 생성. 실적이 아니다: WoID 는 비워 두고 확정 시점의
    /// 열린 WO 로 채운다. 반환 DTO 는 라벨 출력용 (PrintedCount 0).
    /// 라벨 V 토큰(수주처 코드)은 발행 시점 이 라인의 열린 WO → PP_CustomerOrder → MD_Customer 로
    /// 정해 LOT 에 박아 둔다 — 재출력 때 WO 가 바뀌어도 라벨이 달라지지 않는다.
    /// </summary>
    public ImgLotDto CreateRawLot(string lineId, string itemNo, string employeeNo)
    {
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            string? itemName, pgn, alc, mountPos;
            using (var cmd = new SqlCommand(
                "SELECT ItemName, PGN, ALC, MountPos FROM dbo.MD_Item WHERE ItemNo = @Item;", conn, tx))
            {
                cmd.Parameters.Add("@Item", SqlDbType.VarChar, 20).Value = itemNo;
                using var rdr = cmd.ExecuteReader();
                if (rdr.Read())
                {
                    itemName = rdr["ItemName"] as string;
                    pgn      = rdr["PGN"]      as string;
                    alc      = rdr["ALC"]      as string;
                    mountPos = rdr["MountPos"] as string;
                }
                else itemName = pgn = alc = mountPos = null;
            }

            string? customerCode;
            using (var cmd = new SqlCommand("""
                SELECT TOP 1 c.CustomerCode
                FROM   dbo.PP_WorkOrderRouting r
                JOIN   dbo.PP_WorkOrder        w  ON w.WoID  = r.WoID
                LEFT JOIN dbo.PP_CustomerOrder so ON so.SoID = w.SoID
                LEFT JOIN dbo.MD_Customer      c  ON c.CustomerID = so.CustomerID

                """ + WorkOrderRepository.OpenStepForItemFilter + ";", conn, tx))
            {
                cmd.Parameters.Add("@Line", SqlDbType.VarChar, 20).Value = lineId;
                cmd.Parameters.Add("@Item", SqlDbType.VarChar, 20).Value = itemNo;
                customerCode = cmd.ExecuteScalar() as string;
            }

            string? equipId;
            using (var cmd = new SqlCommand("""
                SELECT TOP 1 EquipID FROM dbo.MD_Equipment
                WHERE  LineID = @L AND ISNULL(ActiveFlag,1) = 1
                ORDER  BY EquipID;
                """, conn, tx))
            {
                cmd.Parameters.Add("@L", SqlDbType.VarChar, 20).Value = lineId;
                equipId = cmd.ExecuteScalar() as string;
            }

            var lotCode = LotNoGenerator.NextLotNo(conn, tx, lineId, DateTime.Now);

            int lotId; DateTime createdTs;
            using (var cmd = new SqlCommand("""
                INSERT INTO dbo.tbl_Lot
                    (LotCode, ItemNo, WoID, LineID, ProcessCode, BatchSize, RemainingQty,
                     ProducedAt, Status, QualityFlag, CreatedBy, CreatedTS)
                OUTPUT INSERTED.LotID, INSERTED.CreatedTS
                VALUES
                    (@LotCode, @ItemNo, NULL, @LineID, @Proc, 1, 1,
                     SYSDATETIME(), 'RAW', 'PENDING', @By, SYSDATETIME());
                """, conn, tx))
            {
                cmd.Parameters.Add("@LotCode", SqlDbType.VarChar, 40).Value = lotCode;
                cmd.Parameters.Add("@ItemNo",  SqlDbType.VarChar, 20).Value = itemNo;
                cmd.Parameters.Add("@LineID",  SqlDbType.VarChar, 20).Value = lineId;
                cmd.Parameters.Add("@Proc",    SqlDbType.VarChar, 10).Value = ProcessCode;
                cmd.Parameters.Add("@By",      SqlDbType.VarChar, 50).Value = employeeNo;
                using var rdr = cmd.ExecuteReader();
                rdr.Read();
                lotId     = (int)rdr["LotID"];
                createdTs = (DateTime)rdr["CreatedTS"];
            }

            using (var cmd = new SqlCommand("""
                INSERT INTO dbo.PR_ImgLot (LotID, EquipID, CustomerCode, ConfirmStatus, PrintedCount, CreatedBy, CreatedTS)
                VALUES (@LotID, @Equip, @Cust, 'RAW', 0, @By, SYSDATETIME());
                """, conn, tx))
            {
                cmd.Parameters.Add("@LotID", SqlDbType.Int        ).Value = lotId;
                cmd.Parameters.Add("@Equip", SqlDbType.VarChar, 20).Value = (object?)equipId      ?? DBNull.Value;
                cmd.Parameters.Add("@Cust",  SqlDbType.VarChar, 20).Value = (object?)customerCode ?? DBNull.Value;
                cmd.Parameters.Add("@By",    SqlDbType.VarChar, 50).Value = employeeNo;
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
            return new ImgLotDto
            {
                LotId = lotId, LotCode = lotCode, ItemNo = itemNo, ItemName = itemName,
                Pgn = pgn, Alc = alc, MountPos = mountPos, CustomerCode = customerCode,
                LineId = lineId, EquipId = equipId, ConfirmStatus = "RAW",
                PrintedCount = 0, CreatedTS = createdTs,
            };
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>오늘 이 라인에서 발행된 LOT 전부 (RAW + CONFIRMED), 최신순 — IMG-MAIN 우측 목록.</summary>
    public List<ImgLotDto> GetTodayLots(string lineId, int top = 200)
    {
        var sql = SelectLotView + """

            WHERE  l.LineID = @Line
              AND  l.CreatedTS >= CAST(SYSDATETIME() AS date)
              AND  l.CreatedTS <  DATEADD(day, 1, CAST(SYSDATETIME() AS date))
            ORDER  BY l.CreatedTS DESC, l.LotID DESC
            OFFSET 0 ROWS FETCH NEXT @Top ROWS ONLY;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Line", SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@Top",  SqlDbType.Int        ).Value = top;
        using var rdr = cmd.ExecuteReader();
        var list = new List<ImgLotDto>();
        while (rdr.Read()) list.Add(MapToDto(rdr));
        return list;
    }

    public ImgLotDto? GetByLotCode(string lotCode)
    {
        var sql = SelectLotView + "\nWHERE l.LotCode = @Code;";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Code", SqlDbType.VarChar, 40).Value = lotCode;
        using var rdr = cmd.ExecuteReader();
        return rdr.Read() ? MapToDto(rdr) : null;
    }

    /// <summary>라벨이 실제로 나온 뒤 호출. 반환 = 누적 발행 횟수.</summary>
    public int IncrementPrintedCount(int lotId)
    {
        const string sql = """
            UPDATE dbo.PR_ImgLot
            SET    PrintedCount = PrintedCount + 1, ModifiedTS = SYSDATETIME()
            OUTPUT INSERTED.PrintedCount
            WHERE  LotID = @LotID;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@LotID", SqlDbType.Int).Value = lotId;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    /// <summary>
    /// 라벨 스캔 확정 — 한 트랜잭션으로:
    ///   ① LOT 잠금·상태 검사 → ② LOT 품번의 열린 WO 단계 해석 (INJ 와 같은 규칙)
    ///   → ③ PR_ProductionResult 1 EA → ④ 원단 롤 차감 + PR_FabricDeductionLog (롤이 있을 때)
    ///   → ⑤ PR_BondCycleLog (본딩 설정이 있을 때) → ⑥ LOT CONFIRMED + 단계 CompletedQty +1.
    /// 롤 잔량이 부족해도 확정은 막지 않는다 — 실물은 이미 만들어졌다. 남은 만큼만 차감하고
    /// 실제 차감량을 LOT 에 남긴다.
    /// CycleSec = 같은 라인의 직전 IMG LOT 과 이 LOT 의 생성 시각 차.
    /// </summary>
    public (ImgConfirmOutcome Outcome, int ResultId, string ItemNo, int WoId) ConfirmByLotCode(
        string lotCode, string lineId,
        string operatorId, int? sessionId, string employeeNo,
        int? fabricRollLotId, decimal fabricConsumedM,
        BondSetupDto? bond)
    {
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            int lotId; string itemNo, status; DateTime createdTs;
            using (var cmd = new SqlCommand("""
                SELECT l.LotID, l.ItemNo, l.LineID, l.CreatedTS, e.ConfirmStatus
                FROM   dbo.tbl_Lot   l WITH (UPDLOCK, ROWLOCK)
                JOIN   dbo.PR_ImgLot e WITH (UPDLOCK, ROWLOCK) ON e.LotID = l.LotID
                WHERE  l.LotCode = @Code;
                """, conn, tx))
            {
                cmd.Parameters.Add("@Code", SqlDbType.VarChar, 40).Value = lotCode;
                using var rdr = cmd.ExecuteReader();
                if (!rdr.Read()) { rdr.Close(); tx.Rollback(); return (ImgConfirmOutcome.NotFound, 0, string.Empty, 0); }
                lotId     = (int)rdr["LotID"];
                itemNo    = rdr["ItemNo"] as string ?? string.Empty;
                status    = (string)rdr["ConfirmStatus"];
                createdTs = (DateTime)rdr["CreatedTS"];
                var lotLine = rdr["LineID"] as string;
                if (!string.Equals(lotLine, lineId, StringComparison.OrdinalIgnoreCase))
                { rdr.Close(); tx.Rollback(); return (ImgConfirmOutcome.WrongLine, 0, itemNo, 0); }
            }
            if (status == "CONFIRMED") { tx.Rollback(); return (ImgConfirmOutcome.AlreadyConfirmed, 0, itemNo, 0); }

            int woId, stepId;
            using (var cmd = new SqlCommand("""
                SELECT TOP 1 r.WoID, r.RoutingLineID
                FROM   dbo.PP_WorkOrderRouting r WITH (UPDLOCK, ROWLOCK)
                JOIN   dbo.PP_WorkOrder        w ON w.WoID = r.WoID

                """ + WorkOrderRepository.OpenStepForItemFilter + ";", conn, tx))
            {
                cmd.Parameters.Add("@Line", SqlDbType.VarChar, 20).Value = lineId;
                cmd.Parameters.Add("@Item", SqlDbType.VarChar, 20).Value = itemNo;
                using var rdr = cmd.ExecuteReader();
                if (!rdr.Read()) { rdr.Close(); tx.Rollback(); return (ImgConfirmOutcome.NoWoForItem, 0, itemNo, 0); }
                woId   = (int)rdr["WoID"];
                stepId = (int)rdr["RoutingLineID"];
            }

            int cycleSec;
            using (var cmd = new SqlCommand("""
                SELECT ISNULL(DATEDIFF(SECOND, MAX(pl.CreatedTS), @ThisTs), 0)
                FROM   dbo.tbl_Lot pl
                JOIN   dbo.PR_ImgLot pe ON pe.LotID = pl.LotID
                WHERE  pl.LineID = @Line AND pl.CreatedTS < @ThisTs;
                """, conn, tx))
            {
                cmd.Parameters.Add("@ThisTs", SqlDbType.DateTime2  ).Value = createdTs;
                cmd.Parameters.Add("@Line",   SqlDbType.VarChar, 20).Value = lineId;
                cycleSec = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
                if (cycleSec is < 0 or > 86400) cycleSec = 0;
            }

            // ④ 원단 차감 — FabricRepository.DeductFromRoll 과 같은 규칙을 같은 트랜잭션 안에서.
            //    차감 로그(PR_FabricDeductionLog)는 ResultID 가 필요해 ③ 실적 INSERT 뒤에 쓴다.
            decimal? consumed = null;
            decimal  rollBefore = 0m, rollAfter = 0m;
            if (fabricRollLotId is int rollId && fabricConsumedM > 0)
            {
                using (var cmd = new SqlCommand(
                    "SELECT ISNULL(RemainingQty,0) FROM dbo.tbl_Lot WITH (UPDLOCK, ROWLOCK) WHERE LotID = @L;", conn, tx))
                {
                    cmd.Parameters.Add("@L", SqlDbType.Int).Value = rollId;
                    rollBefore = Convert.ToDecimal(cmd.ExecuteScalar() ?? 0m);
                }
                consumed  = Math.Min(rollBefore, fabricConsumedM);
                rollAfter = rollBefore - consumed.Value;

                using (var cmd = new SqlCommand("""
                    UPDATE dbo.tbl_Lot
                    SET    RemainingQty = @After,
                           Status       = CASE WHEN @After <= 0 THEN 'EXHAUSTED' ELSE Status END,
                           ModifiedBy   = @Op, ModifiedTS = SYSDATETIME()
                    WHERE  LotID = @L;
                    """, conn, tx))
                {
                    cmd.Parameters.Add("@After", SqlDbType.Decimal       ).Value = rollAfter;
                    cmd.Parameters.Add("@L",     SqlDbType.Int           ).Value = rollId;
                    cmd.Parameters.Add("@Op",    SqlDbType.NVarChar, 450 ).Value = operatorId;
                    cmd.ExecuteNonQuery();
                }
            }

            int resultId;
            using (var cmd = new SqlCommand("""
                INSERT INTO dbo.PR_ProductionResult
                    (EntryNo, WoID, LotID, LineID, ProcessCode, GoodQty, CycleSec,
                     FabricRollID, FabricConsumedM, BondTempAvg,
                     OperatorID, SessionID, DefectFlag, EntryAt, CreatedBy, CreatedTS)
                OUTPUT INSERTED.ResultID
                VALUES
                    (@EntryNo, @WoID, @LotID, @LineID, @Proc, 1, @CT,
                     @Roll, @Consumed, @BondTemp,
                     @Op, @Sess, 0, SYSDATETIME(), @By, SYSDATETIME());
                """, conn, tx))
            {
                var entryNo = $"E{DateTime.Now:yyMMddHHmmssfff}-{lineId}";
                if (entryNo.Length > 28) entryNo = entryNo[..28];
                cmd.Parameters.Add("@EntryNo",  SqlDbType.VarChar, 28  ).Value = entryNo;
                cmd.Parameters.Add("@WoID",     SqlDbType.Int          ).Value = woId;
                cmd.Parameters.Add("@LotID",    SqlDbType.Int          ).Value = lotId;
                cmd.Parameters.Add("@LineID",   SqlDbType.VarChar, 20  ).Value = lineId;
                cmd.Parameters.Add("@Proc",     SqlDbType.VarChar, 10  ).Value = ProcessCode;
                cmd.Parameters.Add("@CT",       SqlDbType.Int          ).Value = cycleSec;
                cmd.Parameters.Add("@Roll",     SqlDbType.Int          ).Value = (object?)fabricRollLotId ?? DBNull.Value;
                cmd.Parameters.Add("@Consumed", SqlDbType.Decimal      ).Value = (object?)consumed ?? DBNull.Value;
                cmd.Parameters.Add("@BondTemp", SqlDbType.Decimal      ).Value = (object?)bond?.TempSp ?? DBNull.Value;
                cmd.Parameters.Add("@Op",       SqlDbType.NVarChar, 450).Value = operatorId;
                cmd.Parameters.Add("@Sess",     SqlDbType.Int          ).Value = (object?)sessionId ?? DBNull.Value;
                cmd.Parameters.Add("@By",       SqlDbType.VarChar, 50  ).Value = employeeNo;
                resultId = (int)cmd.ExecuteScalar()!;
            }

            if (consumed is decimal c && fabricRollLotId is int logRollId)
            {
                using var cmd = new SqlCommand("""
                    INSERT INTO dbo.PR_FabricDeductionLog
                        (FabricRollLotID, ResultID, ConsumedM, BeforeM, AfterM, DeductedAt, CreatedBy, CreatedTS)
                    VALUES (@L, @R, @C, @Before, @After, SYSDATETIME(), @By, SYSDATETIME());
                    """, conn, tx);
                cmd.Parameters.Add("@L",      SqlDbType.Int        ).Value = logRollId;
                cmd.Parameters.Add("@R",      SqlDbType.Int        ).Value = resultId;
                cmd.Parameters.Add("@C",      SqlDbType.Decimal    ).Value = c;
                cmd.Parameters.Add("@Before", SqlDbType.Decimal    ).Value = rollBefore;
                cmd.Parameters.Add("@After",  SqlDbType.Decimal    ).Value = rollAfter;
                cmd.Parameters.Add("@By",     SqlDbType.VarChar, 50).Value = employeeNo;
                cmd.ExecuteNonQuery();
            }

            if (bond is not null)
            {
                using var cmd = new SqlCommand("""
                    INSERT INTO dbo.PR_BondCycleLog
                        (ResultID, BondSetupID, PressureAvg, TempAvg, HoldActualSec,
                         TensionAvg, WithinSpec, SampledAt, CreatedBy, CreatedTS)
                    VALUES (@R, @B, @P, @T, @H, @Tn, 1, SYSDATETIME(), @By, SYSDATETIME());
                    """, conn, tx);
                cmd.Parameters.Add("@R",  SqlDbType.Int        ).Value = resultId;
                cmd.Parameters.Add("@B",  SqlDbType.Int        ).Value = bond.BondSetupId;
                cmd.Parameters.Add("@P",  SqlDbType.Decimal    ).Value = bond.PressureSp;
                cmd.Parameters.Add("@T",  SqlDbType.Decimal    ).Value = bond.TempSp;
                cmd.Parameters.Add("@H",  SqlDbType.Int        ).Value = bond.HoldSecSp;
                cmd.Parameters.Add("@Tn", SqlDbType.Decimal    ).Value = (object?)bond.TensionSp ?? DBNull.Value;
                cmd.Parameters.Add("@By", SqlDbType.VarChar, 50).Value = employeeNo;
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new SqlCommand("""
                UPDATE dbo.tbl_Lot
                SET    Status = 'CONFIRMED', QualityFlag = 'OK', WoID = @WoID,
                       ModifiedBy = @Op, ModifiedTS = SYSDATETIME()
                WHERE  LotID = @LotID;

                UPDATE dbo.PR_ImgLot
                SET    ConfirmStatus = 'CONFIRMED', ConfirmedAt = SYSDATETIME(),
                       ConfirmedBy = @Op, ConfirmedSessionID = @Sess,
                       FabricRollLotID = @Roll, FabricConsumedM = @Consumed, BondSetupID = @Bond,
                       ModifiedBy = @Op, ModifiedTS = SYSDATETIME()
                WHERE  LotID = @LotID;
                """, conn, tx))
            {
                cmd.Parameters.Add("@WoID",     SqlDbType.Int          ).Value = woId;
                cmd.Parameters.Add("@LotID",    SqlDbType.Int          ).Value = lotId;
                cmd.Parameters.Add("@Op",       SqlDbType.NVarChar, 450).Value = operatorId;
                cmd.Parameters.Add("@Sess",     SqlDbType.Int          ).Value = (object?)sessionId ?? DBNull.Value;
                cmd.Parameters.Add("@Roll",     SqlDbType.Int          ).Value = (object?)fabricRollLotId ?? DBNull.Value;
                cmd.Parameters.Add("@Consumed", SqlDbType.Decimal      ).Value = (object?)consumed ?? DBNull.Value;
                cmd.Parameters.Add("@Bond",     SqlDbType.Int          ).Value = (object?)bond?.BondSetupId ?? DBNull.Value;
                cmd.ExecuteNonQuery();
            }

            WorkOrderRepository.BumpStepCompleted(conn, tx, stepId, 1m, operatorId);

            tx.Commit();
            return (ImgConfirmOutcome.Confirmed, resultId, itemNo, woId);
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>
    /// IMG-MAIN 좌측 패널: 스테이션 BOP 품번 ∪ 오늘 실적/일정이 있는 품번의 당일 현황.
    /// INJ 판과 같은 항등식 INPUT = FINAL + NG + 미확정. 기준일은 LOT 생성일.
    /// NG 는 오늘 등록된 IMG 수동 불량(PR_DefectDetail, LotID 없음)이고 FINAL 은
    /// 확정 LOT 수에서 그 불량을 뺀 값(0 미만은 0).
    /// </summary>
    public List<InjItemDailyDto> GetDailyItemSummary(string lineId, string stationCode)
    {
        const string sql = """
            DECLARE @Today date = CAST(SYSDATETIME() AS date);

            WITH bop AS (
                SELECT DISTINCT b.ItemNo
                FROM   dbo.MD_Bop b
                WHERE  b.StationCode = @Station AND ISNULL(b.ActiveFlag,1) = 1
            ),
            sched AS (
                SELECT w.ItemNo, SUM(ISNULL(s.PlannedQty,0)) AS PlanQty
                FROM   dbo.PP_LineSchedule s
                JOIN   dbo.PP_WorkOrder    w ON w.WoID = s.WoID
                WHERE  s.LineID = @Line AND s.ScheduleDate = @Today AND s.EntryType = 'WO'
                GROUP  BY w.ItemNo
            ),
            lots AS (
                SELECT l.ItemNo,
                       COUNT(*)                                                       AS InputQty,
                       SUM(CASE WHEN e.ConfirmStatus = 'CONFIRMED' THEN 1 ELSE 0 END) AS ConfirmedQty,
                       SUM(CASE WHEN e.ConfirmStatus = 'RAW'       THEN 1 ELSE 0 END) AS PendingQty
                FROM   dbo.tbl_Lot   l
                JOIN   dbo.PR_ImgLot e ON e.LotID = l.LotID
                WHERE  l.LineID = @Line
                  AND  l.CreatedTS >= @Today AND l.CreatedTS < DATEADD(day, 1, @Today)
                GROUP  BY l.ItemNo
            ),
            manual AS (
                SELECT w.ItemNo, SUM(ISNULL(d.Qty,0)) AS ManualDefect
                FROM   dbo.PR_DefectDetail d
                JOIN   dbo.PP_WorkOrder    w ON w.WoID = d.WoID
                WHERE  d.LotID IS NULL
                  AND  d.ProcessCode = @Proc
                  AND  CAST(d.DetectedAt AS date) = @Today
                  AND  EXISTS (SELECT 1 FROM dbo.PP_WorkOrderRouting r
                               WHERE  r.WoID = w.WoID AND r.LineID = @Line)
                GROUP  BY w.ItemNo
            ),
            itemkeys AS (
                SELECT ItemNo FROM bop
                UNION SELECT ItemNo FROM sched
                UNION SELECT ItemNo FROM lots
                UNION SELECT ItemNo FROM manual
            )
            SELECT k.ItemNo,
                   COALESCE(i.ItemName, N'')  AS ItemName,
                   ISNULL(p.PlanQty, 0)       AS PlanQty,
                   ISNULL(t.InputQty, 0)      AS InputQty,
                   ISNULL(t.ConfirmedQty, 0)  AS ConfirmedQty,
                   ISNULL(t.PendingQty, 0)    AS PendingQty,
                   ISNULL(m.ManualDefect, 0)  AS ManualDefect,
                   CASE WHEN b.ItemNo IS NULL THEN 0 ELSE 1 END AS InBop,
                   CASE WHEN EXISTS (
                        SELECT 1
                        FROM   dbo.PP_WorkOrderRouting r
                        JOIN   dbo.PP_WorkOrder        w ON w.WoID = r.WoID
                        WHERE  r.LineID = @Line AND w.ItemNo = k.ItemNo
                          AND  r.Status IN ('Released','In Progress')
                          AND  ISNULL(w.Status,'Draft') <> 'Cancelled') THEN 1 ELSE 0 END AS HasOpenWo
            FROM   itemkeys k
            LEFT JOIN dbo.MD_Item i ON i.ItemNo = k.ItemNo
            LEFT JOIN bop    b ON b.ItemNo = k.ItemNo
            LEFT JOIN sched  p ON p.ItemNo = k.ItemNo
            LEFT JOIN lots   t ON t.ItemNo = k.ItemNo
            LEFT JOIN manual m ON m.ItemNo = k.ItemNo
            ORDER  BY InBop DESC, k.ItemNo;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Line",    SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@Station", SqlDbType.VarChar, 20).Value = stationCode;
        cmd.Parameters.Add("@Proc",    SqlDbType.VarChar, 10).Value = ProcessCode;
        using var rdr = cmd.ExecuteReader();
        var list = new List<InjItemDailyDto>();
        while (rdr.Read())
        {
            var confirmed = Convert.ToInt32(rdr["ConfirmedQty"]);
            var manual    = Convert.ToInt32(rdr["ManualDefect"]);
            list.Add(new InjItemDailyDto
            {
                ItemNo     = (string)rdr["ItemNo"],
                ItemName   = (string)rdr["ItemName"],
                PlanQty    = Convert.ToDecimal(rdr["PlanQty"]),
                InputQty   = Convert.ToInt32(rdr["InputQty"]),
                NgQty      = manual,
                FinalQty   = Math.Max(0, confirmed - manual),
                PendingQty = Convert.ToInt32(rdr["PendingQty"]),
                InBop      = Convert.ToInt32(rdr["InBop"]) == 1,
                HasOpenWo  = Convert.ToInt32(rdr["HasOpenWo"]) == 1,
            });
        }
        return list;
    }
}
