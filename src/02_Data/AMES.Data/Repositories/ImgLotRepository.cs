using System.Data;
using AMES.Contracts.Dto;
using AMES.Contracts.Enums;
using AMES.Data.Connection;
using AMES.Data.Services;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// IMG(래핑) 원천 LOT — tbl_Lot(ProcessCode='IMG', 1 EA) + PR_ImgLot.
/// IMG-MAIN 의 "Core 스캔 → 완제품 라벨 → OK/NG 판정" 모델을 담당한다. INJ 와 달리 에이전트가 없으므로
/// LOT 은 터미널이 만들고 라벨은 그 자리에서 동기 출력된다 (LabelDispatcher 는 INJ 세션에서만 돈다).
/// 주 흐름은 사출 Core 스캔(CreateFromCore — tbl_Lot.ParentLotID 로 Core 연결)이고, CreateRawLot 은 예외용 발행 버튼이다.
/// 완제품 품번 = 그 코어를 코어 공정(INJ) 단계 품번으로 갖는 이 라인의 열린 WO 품번(WorkOrderRepository.OpenStepByCoreFilter) — 코어 품번이 아니다.
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
               e.PrintedCount, l.CreatedTS, pl.LotCode AS CoreLotCode
        FROM   dbo.tbl_Lot l
        JOIN   dbo.PR_ImgLot e ON e.LotID = l.LotID
        LEFT   JOIN dbo.MD_Item mi ON mi.ItemNo = l.ItemNo
        LEFT   JOIN dbo.tbl_Lot pl ON pl.LotID = l.ParentLotID
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
        CoreLotCode     = rdr["CoreLotCode"] as string,
    };

    /// <summary>
    /// 예외용 라벨 발행 버튼 — Core 없이 RAW LOT 1건 생성. 실적이 아니다: WoID 는 비워 두고 확정 시점의
    /// 열린 WO 로 채운다. 반환 DTO 는 라벨 출력용 (PrintedCount 0).
    /// </summary>
    public ImgLotDto CreateRawLot(string lineId, string itemNo, string employeeNo)
    {
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            var lot = InsertRawLot(conn, tx, lineId, itemNo, null, null, employeeNo);
            tx.Commit();
            return lot;
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>
    /// 사출 Core 스캔 — 한 트랜잭션으로:
    ///   ① Core(INJ LOT) 잠금 → ② 상태 검사(CoreLotRules) → ③ 이미 쓰인 Core 인지
    ///   → ④ 완제품 결정(이 라인에 이 Core 를 코어 단계 품번으로 갖는 열린 WO → 없으면 작업자가 고른 품번, WO 없이)
    ///   → ⑤ ParentLotID 로 연결된 RAW LOT 생성.
    /// Core 행 UPDLOCK 이 같은 Core 동시 스캔을 직렬화한다. UX_tbl_Lot_ImgParent 위반은 CoreUsed 로 돌려준다.
    /// </summary>
    /// <param name="finishedItemNo">작업자가 IMG-MAIN 좌측에서 고른 완제품 품번. 한 코어를 여러 색상 완제품이 공유하므로 주어지면 그 품번의 열린 WO 만 받고,
    /// WO 가 없으면 그 품번으로 WO 없이 만든다(유효 BOM 코어가 스캔 코어와 다르면 CoreMismatch, 마스터에 없으면 NoFinishedItem).
    /// null 이면 열린 단계 순서 규칙, 그마저 없으면 완제품을 몰라 NoFinishedItem.</param>
    public (ImgCoreOutcome Outcome, ImgLotDto? Lot, string? UsedByLotCode, string? ItemNo) CreateFromCore(
        string coreLotCode, string lineId, string employeeNo, string? finishedItemNo = null)
    {
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        int coreId = 0; string? itemNo = null;
        try
        {
            string coreStatus;
            using (var cmd = new SqlCommand("""
                SELECT l.LotID, l.ItemNo, e.ConfirmStatus
                FROM   dbo.tbl_Lot   l WITH (UPDLOCK, ROWLOCK)
                JOIN   dbo.PR_InjLot e WITH (UPDLOCK, ROWLOCK) ON e.LotID = l.LotID
                WHERE  l.LotCode = @Code AND l.ProcessCode = 'INJ';
                """, conn, tx))
            {
                cmd.Parameters.Add("@Code", SqlDbType.VarChar, 40).Value = coreLotCode;
                using var rdr = cmd.ExecuteReader();
                if (!rdr.Read()) { rdr.Close(); tx.Rollback(); return (ImgCoreOutcome.NotFound, null, null, null); }
                coreId     = (int)rdr["LotID"];
                itemNo     = rdr["ItemNo"] as string ?? string.Empty;
                coreStatus = (string)rdr["ConfirmStatus"];
            }

            var check = CoreLotRules.Check(coreStatus);
            if (check != ImgCoreOutcome.Created) { tx.Rollback(); return (check, null, null, itemNo); }

            if (FindCoreUser(conn, tx, coreId) is { } usedBy)
            { tx.Rollback(); return (ImgCoreOutcome.CoreUsed, null, usedBy, itemNo); }

            // 완제품 = 이 라인에 열린 단계가 있고 코어 공정 단계 품번이 이 Core 인 WO 의 품번.
            int? woId = null; string? finishedItem = null;
            using (var cmd = new SqlCommand("""
                SELECT TOP 1 r.WoID, w.ItemNo
                FROM   dbo.PP_WorkOrderRouting r
                JOIN   dbo.PP_WorkOrder        w ON w.WoID = r.WoID

                """ + WorkOrderRepository.OpenStepByCoreFilter + ";", conn, tx))
            {
                cmd.Parameters.Add("@Line",     SqlDbType.VarChar, 20).Value = lineId;
                cmd.Parameters.Add("@Core",     SqlDbType.VarChar, 20).Value = itemNo;
                cmd.Parameters.Add("@CoreProc", SqlDbType.VarChar, 10).Value = CoreItemResolver.CoreProcess;
                cmd.Parameters.Add("@Fg",       SqlDbType.VarChar, 20).Value = string.IsNullOrEmpty(finishedItemNo) ? DBNull.Value : finishedItemNo;
                using var rdr = cmd.ExecuteReader();
                if (rdr.Read()) { woId = (int)rdr["WoID"]; finishedItem = (string)rdr["ItemNo"]; }
            }

            // WO 가 없어도 생산은 막지 않는다(2026-10-01) — 완제품은 작업자가 좌측에서 고른 품번이다. 선택이 없으면 완제품을 모른다.
            if (finishedItem is null)
            {
                if (string.IsNullOrEmpty(finishedItemNo) || !ItemExists(conn, tx, finishedItemNo))
                { tx.Rollback(); return (ImgCoreOutcome.NoFinishedItem, null, null, itemNo); }
                // BOM 이 코어를 정할 수 있는 품번이면 스캔한 코어와 맞아야 한다. 못 정하는 품번(Self·Missing·Ambiguous)은 선택을 믿는다.
                var bom = CoreItemResolver.Read(conn, tx, finishedItemNo);
                if (bom.Outcome == CoreItemResolver.Outcome.Core && !string.Equals(bom.CoreItemNo, itemNo, StringComparison.OrdinalIgnoreCase))
                { tx.Rollback(); return (ImgCoreOutcome.CoreMismatch, null, null, itemNo); }
                finishedItem = finishedItemNo;
            }

            var lot = InsertRawLot(conn, tx, lineId, finishedItem, coreId, coreLotCode, employeeNo, woId);
            tx.Commit();
            return (ImgCoreOutcome.Created, lot, null, finishedItem);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627 && ex.Message.Contains("UX_tbl_Lot_ImgParent"))
        {
            // 잠금을 거치지 않은 경로가 먼저 연결했다 — 인덱스가 최후 방어선이다.
            tx.Rollback();
            return (ImgCoreOutcome.CoreUsed, null, FindCoreUser(conn, null, coreId), itemNo);
        }
        catch { tx.Rollback(); throw; }
    }

    static bool ItemExists(SqlConnection conn, SqlTransaction tx, string itemNo)
    {
        using var cmd = new SqlCommand("SELECT 1 FROM dbo.MD_Item WHERE ItemNo = @I AND ISNULL(ActiveFlag,1) = 1;", conn, tx);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = itemNo;
        return cmd.ExecuteScalar() is not null;
    }

    static string? FindCoreUser(SqlConnection conn, SqlTransaction? tx, int coreLotId)
    {
        using var cmd = new SqlCommand("""
            SELECT TOP 1 LotCode FROM dbo.tbl_Lot
            WHERE  ParentLotID = @Core AND ProcessCode = 'IMG'
            ORDER  BY LotID;
            """, conn, tx);
        cmd.Parameters.Add("@Core", SqlDbType.Int).Value = coreLotId;
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>
    /// RAW LOT 1건 INSERT. 라벨 V 토큰(수주처 코드)은 발행 시점 이 라인의 열린 WO → PP_CustomerOrder → MD_Customer 로
    /// 정해 LOT 에 박아 둔다 — 재출력 때 WO 가 바뀌어도 라벨이 달라지지 않는다.
    /// </summary>
    static ImgLotDto InsertRawLot(SqlConnection conn, SqlTransaction tx, string lineId, string itemNo,
                                  int? parentLotId, string? parentLotCode, string employeeNo, int? woId = null)
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
        // Core 스캔은 WO 를 이미 정했으므로 그 WO 의 수주처. 예외용 발행은 종전처럼 라인·완제품 품번의 열린 WO.
        var custSql = woId is null
            ? """
              SELECT TOP 1 c.CustomerCode
              FROM   dbo.PP_WorkOrderRouting r
              JOIN   dbo.PP_WorkOrder        w  ON w.WoID  = r.WoID
              LEFT JOIN dbo.PP_CustomerOrder so ON so.SoID = w.SoID
              LEFT JOIN dbo.MD_Customer      c  ON c.CustomerID = so.CustomerID

              """ + WorkOrderRepository.OpenStepForItemFilter + ";"
            : """
              SELECT c.CustomerCode
              FROM   dbo.PP_WorkOrder        w
              LEFT JOIN dbo.PP_CustomerOrder so ON so.SoID = w.SoID
              LEFT JOIN dbo.MD_Customer      c  ON c.CustomerID = so.CustomerID
              WHERE  w.WoID = @Wo;
              """;
        using (var cmd = new SqlCommand(custSql, conn, tx))
        {
            if (woId is null)
            {
                cmd.Parameters.Add("@Line", SqlDbType.VarChar, 20).Value = lineId;
                cmd.Parameters.Add("@Item", SqlDbType.VarChar, 20).Value = itemNo;
            }
            else cmd.Parameters.Add("@Wo", SqlDbType.Int).Value = woId.Value;
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

        var lotCode = LotNoGenerator.NextLotNo(conn, tx, lineId, DbClock.Now);

        int lotId; DateTime createdTs;
        using (var cmd = new SqlCommand("""
            INSERT INTO dbo.tbl_Lot
                (LotCode, ItemNo, WoID, LineID, ProcessCode, BatchSize, RemainingQty, ParentLotID,
                 ProducedAt, Status, QualityFlag, CreatedBy, CreatedTS)
            OUTPUT INSERTED.LotID, INSERTED.CreatedTS
            VALUES
                (@LotCode, @ItemNo, NULL, @LineID, @Proc, 1, 1, @Parent,
                 SYSDATETIME(), 'RAW', 'PENDING', @By, SYSDATETIME());
            """, conn, tx))
        {
            cmd.Parameters.Add("@LotCode", SqlDbType.VarChar, 40).Value = lotCode;
            cmd.Parameters.Add("@ItemNo",  SqlDbType.VarChar, 20).Value = itemNo;
            cmd.Parameters.Add("@LineID",  SqlDbType.VarChar, 20).Value = lineId;
            cmd.Parameters.Add("@Proc",    SqlDbType.VarChar, 10).Value = ProcessCode;
            cmd.Parameters.Add("@Parent",  SqlDbType.Int        ).Value = (object?)parentLotId ?? DBNull.Value;
            cmd.Parameters.Add("@By",      SqlDbType.VarChar, 20).Value = employeeNo;
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
            cmd.Parameters.Add("@By",    SqlDbType.VarChar, 20).Value = employeeNo;
            cmd.ExecuteNonQuery();
        }

        return new ImgLotDto
        {
            LotId = lotId, LotCode = lotCode, ItemNo = itemNo, ItemName = itemName,
            Pgn = pgn, Alc = alc, MountPos = mountPos, CustomerCode = customerCode,
            LineId = lineId, EquipId = equipId, ConfirmStatus = "RAW",
            PrintedCount = 0, CreatedTS = createdTs, CoreLotCode = parentLotCode,
        };
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
    public int IncrementPrintedCount(int lotId, string? employeeNo)
    {
        const string sql = """
            UPDATE dbo.PR_ImgLot
            SET    PrintedCount = PrintedCount + 1, ModifiedBy = @By, ModifiedTS = SYSDATETIME()
            OUTPUT INSERTED.PrintedCount
            WHERE  LotID = @LotID;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@LotID", SqlDbType.Int).Value = lotId;
        cmd.Parameters.Add("@By", SqlDbType.VarChar,   20).Value = (object?)employeeNo ?? DBNull.Value;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    /// <summary>
    /// 라인 불량 팝업의 LOT 스캔 등록 — INJ 와 같은 순서(잠금·검사 → 역분개 → PR_DefectDetail → LOT DEFECT).
    /// </summary>
    public (DefectRegisterOutcome Outcome, int DefectId, string ItemNo) RegisterDefect(
        string lotCode, string lineId, string defectCode,
        string operatorId, int? sessionId, string employeeNo)
    {
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            int lotId; string itemNo, status;
            using (var cmd = new SqlCommand("""
                SELECT l.LotID, l.ItemNo, l.LineID, e.ConfirmStatus
                FROM   dbo.tbl_Lot   l WITH (UPDLOCK, ROWLOCK)
                JOIN   dbo.PR_ImgLot e WITH (UPDLOCK, ROWLOCK) ON e.LotID = l.LotID
                WHERE  l.LotCode = @Code;
                """, conn, tx))
            {
                cmd.Parameters.Add("@Code", SqlDbType.VarChar, 40).Value = lotCode;
                using var rdr = cmd.ExecuteReader();
                if (!rdr.Read()) { rdr.Close(); tx.Rollback(); return (DefectRegisterOutcome.NotFound, 0, string.Empty); }
                lotId  = (int)rdr["LotID"];
                itemNo = rdr["ItemNo"] as string ?? string.Empty;
                status = (string)rdr["ConfirmStatus"];
                var lotLine = rdr["LineID"] as string;
                if (!string.Equals(lotLine, lineId, StringComparison.OrdinalIgnoreCase))
                { rdr.Close(); tx.Rollback(); return (DefectRegisterOutcome.WrongLine, 0, itemNo); }
            }

            // 상태 검사가 PR_DefectDetail 보다 먼저다 — DEFECT 면 여기서 tbl_Lot 잠금을 놓고 나가므로
            // ReworkRepository(PR_DefectDetail → tbl_Lot 순)와 잠금 순서가 엇갈려도 교착이 없다. 순서를 바꾸면 순환이 생긴다.
            var check = LotDefectRules.CheckRegister(status);
            if (check != DefectRegisterOutcome.Registered) { tx.Rollback(); return (check, 0, itemNo); }

            int? origResultId = null, reversalId = null, woId = null;
            if (LotDefectRules.ReversesResult(status))
                (origResultId, reversalId, woId) = LotDefectWriter.ReverseConfirmedResult(conn, tx, lotId, lineId, operatorId, sessionId, employeeNo);
            woId ??= LotDefectWriter.ResolveOpenWo(conn, tx, lineId, itemNo)?.WoId;

            var defectId = LotDefectWriter.InsertDefectDetail(conn, tx, lotId, woId, origResultId, ProcessCode, defectCode,
                                                              status, reversalId, null, operatorId, employeeNo);

            using (var cmd = new SqlCommand("""
                UPDATE dbo.PR_ImgLot
                SET    ConfirmStatus = 'DEFECT', ModifiedBy = @Op, ModifiedTS = SYSDATETIME()
                WHERE  LotID = @Lot;
                UPDATE dbo.tbl_Lot
                SET    QualityFlag = 'NG', ModifiedBy = @Op, ModifiedTS = SYSDATETIME()
                WHERE  LotID = @Lot;
                """, conn, tx))
            {
                cmd.Parameters.Add("@Lot", SqlDbType.Int          ).Value = lotId;
                cmd.Parameters.Add("@Op",  SqlDbType.VarChar,   20).Value = operatorId;
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
            return (DefectRegisterOutcome.Registered, defectId, itemNo);
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>
    /// 라벨 스캔 확정 — 한 트랜잭션으로:
    ///   ① LOT 잠금·상태 검사 → ② LOT 품번의 열린 WO 단계 해석 (INJ 와 같은 규칙, 없으면 WoID NULL 로 확정)
    ///   → ③ PR_ProductionResult 1 EA → ④ LOT CONFIRMED + 단계 CompletedQty +1.
    /// 원단 롤·본딩은 기록하지 않는다.
    /// CycleSec = 같은 라인의 직전 IMG LOT 과 이 LOT 의 생성 시각 차.
    /// </summary>
    public (ImgConfirmOutcome Outcome, int ResultId, string ItemNo, int WoId) ConfirmByLotCode(
        string lotCode, string lineId,
        string operatorId, int? sessionId, string employeeNo)
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
            switch (LotDefectRules.ConfirmBlock(status))
            {
                case LotConfirmBlock.AlreadyConfirmed: tx.Rollback(); return (ImgConfirmOutcome.AlreadyConfirmed, 0, itemNo, 0);
                case LotConfirmBlock.InRework:         tx.Rollback(); return (ImgConfirmOutcome.InRework,         0, itemNo, 0);
                case LotConfirmBlock.Scrapped:         tx.Rollback(); return (ImgConfirmOutcome.Scrapped,         0, itemNo, 0);
                case LotConfirmBlock.NgBlocked:        throw new InvalidOperationException("IMG lot cannot be NG_BLOCKED.");
            }

            // WO 가 없어도 생산은 막지 않는다(2026-10-01) — 열린 단계가 없으면 실적·LOT 의 WoID 는 NULL, 단계 반영 없음.
            int? woId = null, stepId = null;
            using (var cmd = new SqlCommand("""
                SELECT TOP 1 r.WoID, r.RoutingLineID
                FROM   dbo.PP_WorkOrderRouting r WITH (UPDLOCK, ROWLOCK)
                JOIN   dbo.PP_WorkOrder        w ON w.WoID = r.WoID

                """ + WorkOrderRepository.OpenStepForItemFilter + ";", conn, tx))
            {
                cmd.Parameters.Add("@Line", SqlDbType.VarChar, 20).Value = lineId;
                cmd.Parameters.Add("@Item", SqlDbType.VarChar, 20).Value = itemNo;
                using var rdr = cmd.ExecuteReader();
                if (rdr.Read()) { woId = (int)rdr["WoID"]; stepId = (int)rdr["RoutingLineID"]; }
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

            // 전기일·교대는 설정 DAY_CUTOFF_TIME·공통코드 WORK_SHIFT로 확정 시점 서버 시각에 판정
            var (now, prodDate, shiftCode) = ProdCalendar.ResolveNow(conn, tx);
            int resultId;
            using (var cmd = new SqlCommand("""
                INSERT INTO dbo.PR_ProductionResult
                    (EntryNo, WoID, LotID, LineID, ProcessCode, GoodQty, CycleSec,
                     OperatorID, SessionID, DefectFlag, EntryAt, ProdDate, ShiftCode, CreatedBy, CreatedTS)
                OUTPUT INSERTED.ResultID
                VALUES
                    (@EntryNo, @WoID, @LotID, @LineID, @Proc, 1, @CT,
                     @Op, @Sess, 0, @Now, @ProdDate, @Shift, @By, SYSDATETIME());
                """, conn, tx))
            {
                var entryNo = $"E{now:yyMMddHHmmssfff}-{lineId}";
                if (entryNo.Length > 28) entryNo = entryNo[..28];
                cmd.Parameters.Add("@EntryNo",  SqlDbType.VarChar, 28  ).Value = entryNo;
                cmd.Parameters.Add("@Now",      SqlDbType.DateTime2    ).Value = now;
                cmd.Parameters.Add("@ProdDate", SqlDbType.Date         ).Value = prodDate;
                cmd.Parameters.Add("@Shift",    SqlDbType.VarChar, 10  ).Value = (object?)shiftCode ?? DBNull.Value;
                cmd.Parameters.Add("@WoID",     SqlDbType.Int          ).Value = (object?)woId ?? DBNull.Value;
                cmd.Parameters.Add("@LotID",    SqlDbType.Int          ).Value = lotId;
                cmd.Parameters.Add("@LineID",   SqlDbType.VarChar, 20  ).Value = lineId;
                cmd.Parameters.Add("@Proc",     SqlDbType.VarChar, 10  ).Value = ProcessCode;
                cmd.Parameters.Add("@CT",       SqlDbType.Int          ).Value = cycleSec;
                cmd.Parameters.Add("@Op",       SqlDbType.NVarChar, 450).Value = operatorId;
                cmd.Parameters.Add("@Sess",     SqlDbType.Int          ).Value = (object?)sessionId ?? DBNull.Value;
                cmd.Parameters.Add("@By",       SqlDbType.VarChar,   20).Value = employeeNo;
                resultId = (int)cmd.ExecuteScalar()!;
            }

            using (var cmd = new SqlCommand("""
                UPDATE dbo.tbl_Lot
                SET    Status = 'CONFIRMED', QualityFlag = 'OK', WoID = @WoID,
                       ModifiedBy = @Op, ModifiedTS = SYSDATETIME()
                WHERE  LotID = @LotID;

                UPDATE dbo.PR_ImgLot
                SET    ConfirmStatus = 'CONFIRMED', ConfirmedAt = SYSDATETIME(),
                       ConfirmedBy = @Op, ConfirmedSessionID = @Sess,
                       ModifiedBy = @Op, ModifiedTS = SYSDATETIME()
                WHERE  LotID = @LotID;
                """, conn, tx))
            {
                cmd.Parameters.Add("@WoID",     SqlDbType.Int          ).Value = (object?)woId ?? DBNull.Value;
                cmd.Parameters.Add("@LotID",    SqlDbType.Int          ).Value = lotId;
                cmd.Parameters.Add("@Op",       SqlDbType.NVarChar,  20).Value = operatorId;
                cmd.Parameters.Add("@Sess",     SqlDbType.Int          ).Value = (object?)sessionId ?? DBNull.Value;
                cmd.ExecuteNonQuery();
            }

            if (stepId is int step) WorkOrderRepository.BumpStepCompleted(conn, tx, step, 1m, operatorId);

            tx.Commit();
            return (ImgConfirmOutcome.Confirmed, resultId, itemNo, woId ?? 0);
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>
    /// IMG-MAIN 좌측 패널: 스테이션 BOP 품번 ∪ 그 날 실적/일정이 있는 품번의 지정일 현황.
    /// HasOpenWo 는 날짜와 무관한 현재 상태다.
    /// INJ 판과 같은 항등식 INPUT = FINAL + NG + 미확정. 기준일은 LOT 생성일.
    /// 전부 LOT 상태로 센다: FINAL = CONFIRMED, NG = DEFECT + SCRAPPED, 미확정 = RAW.
    /// </summary>
    public List<InjItemDailyDto> GetDailyItemSummary(string lineId, string stationCode, DateTime date)
    {
        const string sql = """
            WITH bop AS (
                SELECT DISTINCT b.ItemNo
                FROM   dbo.MD_Bop b
                WHERE  b.StationCode = @Station AND ISNULL(b.ActiveFlag,1) = 1
            ),
            sched AS (
                SELECT COALESCE(st.ItemNo, w.ItemNo) AS ItemNo, SUM(ISNULL(s.PlannedQty,0)) AS PlanQty
                FROM   dbo.PP_LineSchedule s
                JOIN   dbo.PP_WorkOrder    w ON w.WoID = s.WoID
                OUTER  APPLY (SELECT TOP 1 r.ItemNo FROM dbo.PP_WorkOrderRouting r
                              WHERE  r.WoID = s.WoID AND r.LineID = s.LineID ORDER BY r.StepSeq) st
                WHERE  s.LineID = @Line AND s.ScheduleDate = @Today AND s.EntryType = 'WO'
                  AND  ISNULL(w.Status,'Draft') <> 'Cancelled'
                GROUP  BY COALESCE(st.ItemNo, w.ItemNo)
            ),
            lots AS (
                SELECT l.ItemNo,
                       COUNT(*)                                                                  AS InputQty,
                       SUM(CASE WHEN e.ConfirmStatus = 'CONFIRMED' THEN 1 ELSE 0 END)            AS ConfirmedQty,
                       SUM(CASE WHEN e.ConfirmStatus IN ('DEFECT','SCRAPPED') THEN 1 ELSE 0 END) AS NgQty,
                       SUM(CASE WHEN e.ConfirmStatus = 'RAW'       THEN 1 ELSE 0 END)            AS PendingQty
                FROM   dbo.tbl_Lot   l
                JOIN   dbo.PR_ImgLot e ON e.LotID = l.LotID
                WHERE  l.LineID = @Line
                  AND  l.CreatedTS >= @Today AND l.CreatedTS < DATEADD(day, 1, @Today)
                GROUP  BY l.ItemNo
            ),
            itemkeys AS (
                SELECT ItemNo FROM bop
                UNION SELECT ItemNo FROM sched
                UNION SELECT ItemNo FROM lots
            )
            SELECT k.ItemNo,
                   COALESCE(i.ItemName, N'')  AS ItemName,
                   ISNULL(p.PlanQty, 0)       AS PlanQty,
                   ISNULL(t.InputQty, 0)      AS InputQty,
                   ISNULL(t.ConfirmedQty, 0)  AS ConfirmedQty,
                   ISNULL(t.NgQty, 0)         AS NgQty,
                   ISNULL(t.PendingQty, 0)    AS PendingQty,
                   CASE WHEN b.ItemNo IS NULL THEN 0 ELSE 1 END AS InBop,
                   CASE WHEN EXISTS (
                        SELECT 1
                        FROM   dbo.PP_WorkOrderRouting r
                        JOIN   dbo.PP_WorkOrder        w ON w.WoID = r.WoID
                        WHERE  r.LineID = @Line AND COALESCE(r.ItemNo, w.ItemNo) = k.ItemNo
                          AND  r.Status IN ('Released','In Progress')
                          AND  ISNULL(w.Status,'Draft') <> 'Cancelled') THEN 1 ELSE 0 END AS HasOpenWo
            FROM   itemkeys k
            LEFT JOIN dbo.MD_Item i ON i.ItemNo = k.ItemNo
            LEFT JOIN bop    b ON b.ItemNo = k.ItemNo
            LEFT JOIN sched  p ON p.ItemNo = k.ItemNo
            LEFT JOIN lots   t ON t.ItemNo = k.ItemNo
            ORDER  BY InBop DESC, k.ItemNo;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Line",    SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@Station", SqlDbType.VarChar, 20).Value = stationCode;
        cmd.Parameters.Add("@Today",   SqlDbType.Date       ).Value = date.Date;
        using var rdr = cmd.ExecuteReader();
        var list = new List<InjItemDailyDto>();
        while (rdr.Read())
        {
            list.Add(new InjItemDailyDto
            {
                ItemNo     = (string)rdr["ItemNo"],
                ItemName   = (string)rdr["ItemName"],
                PlanQty    = Convert.ToDecimal(rdr["PlanQty"]),
                InputQty   = Convert.ToInt32(rdr["InputQty"]),
                NgQty      = Convert.ToInt32(rdr["NgQty"]),
                FinalQty   = Convert.ToInt32(rdr["ConfirmedQty"]),
                PendingQty = Convert.ToInt32(rdr["PendingQty"]),
                InBop      = Convert.ToInt32(rdr["InBop"]) == 1,
                HasOpenWo  = Convert.ToInt32(rdr["HasOpenWo"]) == 1,
            });
        }
        return list;
    }
}
