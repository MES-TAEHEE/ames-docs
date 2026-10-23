using System.Data;
using AMES.Contracts.Dto;
using AMES.Contracts.Enums;
using AMES.Data.Connection;
using AMES.Data.Services;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// PNT(도장) 원천 LOT — tbl_Lot(ProcessCode='PNT', 1 EA) + PR_PntLot.
/// PNT-MAIN 의 "라벨 발행 → OK/NG 판정 → 확정" 모델을 담당한다(2026-10-22 사용자 결정). IMG 와 같은 완제품 LOT 모델이지만
/// 코어 스캔이 없다 — 도장이 끝난 뒤 작업자가 좌측에서 고른 완제품 품번으로 라벨 발행 버튼이 RAW LOT 을 만들고(CreateRawLot),
/// 완제품 고객 표준 라벨을 그 자리에서 출력한 뒤 판정 OK 가 ConfirmByLotCode 로 확정한다. tbl_Lot.ParentLotID 는 늘 NULL.
/// 확정 차감은 BOM 의 MATERIAL 자식에 더해 사출품 자식(MD_Item.InjFlag = 1)도 라인 재고에서 뺀다 — 코어를 스캔하지 않아
/// IMG 의 ConsumeCore 를 대신한다(BackflushRules.IncludesInjection).
/// DTO 는 IMG 와 공용 ImgLotDto(완제품 LOT 뷰) — CoreLotCode·원단·본딩 필드는 PNT 에서 항상 null 이다.
/// </summary>
public sealed class PntLotRepository
{
    private readonly AmesConnectionFactory _factory;
    public PntLotRepository(AmesConnectionFactory f) => _factory = f;

    const string ProcessCode = "PNT";

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
    /// 라벨 발행 버튼 — 좌측에서 고른 완제품 품번으로 RAW LOT 1건 생성. 실적이 아니다: WoID 는 비워 두고 확정 시점의
    /// 열린 WO 로 채운다. 반환 DTO 는 라벨 출력용 (PrintedCount 0).
    /// </summary>
    public ImgLotDto CreateRawLot(string lineId, string itemNo, string employeeNo)
    {
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            var lot = InsertRawLot(conn, tx, lineId, itemNo, employeeNo);
            tx.Commit();
            return lot;
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>
    /// RAW LOT 1건 INSERT. 라벨 V 토큰(수주처 코드)은 발행 시점 이 라인·품번의 열린 WO → PP_CustomerOrder → MD_Customer 로
    /// 정해 LOT 에 박아 둔다 — 재출력 때 WO 가 바뀌어도 라벨이 달라지지 않는다.
    /// </summary>
    /// <summary>오프라인에서 이미 번호를 받아 라벨까지 뽑은 LOT 의 삽입 값 — 번호·생성 시각·출력 횟수·라벨에 찍은 수주처 코드.</summary>
    sealed record OfflineCreate(string LotCode, DateTime At, int Printed, string? CustomerCode);

    /// <summary>오프라인 발행 버튼으로 이미 라벨을 뽑은 LOT 을 복구 뒤 넣는다. 같은 번호가 이미 있으면(재전송) null.</summary>
    public ImgLotDto? CreateRawLotWithCode(string lineId, string itemNo, string employeeNo,
                                           string lotCode, DateTime at, int printedCount, string? customerCode)
    {
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            using (var exists = new SqlCommand("SELECT 1 FROM dbo.tbl_Lot WHERE LotCode = @Code;", conn, tx))
            {
                exists.Parameters.Add("@Code", SqlDbType.VarChar, 40).Value = lotCode;
                if (exists.ExecuteScalar() is not null) { tx.Rollback(); return null; }
            }
            var lot = InsertRawLot(conn, tx, lineId, itemNo, employeeNo, new OfflineCreate(lotCode, at, printedCount, customerCode));
            tx.Commit();
            return lot;
        }
        catch { tx.Rollback(); throw; }
    }

    // ── 오프라인 사본용 ────────────────────────────────────────────────

    /// <summary>오프라인 라벨에 찍는 값 — 품번 정보·수주처 코드(이 라인·품번의 열린 WO 기준)·설비. 라벨 발행 때 서버가 하는 조회와 같다.</summary>
    public sealed record LabelContext(string? ItemName, string? Pgn, string? Alc, string? MountPos, string? CustomerCode, string? EquipId);

    public LabelContext GetLabelContext(string lineId, string itemNo)
    {
        using var conn = _factory.OpenConnection();
        string? itemName = null, pgn = null, alc = null, mountPos = null;
        using (var cmd = new SqlCommand("dbo.SP_POP_PNT_InsertRawLot_GetItem", conn) { CommandType = CommandType.StoredProcedure })
        {
            cmd.Parameters.Add("@Item", SqlDbType.VarChar, 20).Value = itemNo;
            using var rdr = cmd.ExecuteReader();
            if (rdr.Read())
            {
                itemName = rdr["ItemName"] as string; pgn = rdr["PGN"] as string;
                alc = rdr["ALC"] as string; mountPos = rdr["MountPos"] as string;
            }
        }
        string? customer;
        using (var cmd = new SqlCommand("dbo.SP_POP_PNT_InsertRawLot_GetWorkOrderRouting", conn) { CommandType = CommandType.StoredProcedure })
        {
            cmd.Parameters.Add("@Line", SqlDbType.VarChar, 20).Value = lineId;
            cmd.Parameters.Add("@Item", SqlDbType.VarChar, 20).Value = itemNo;
            customer = cmd.ExecuteScalar() as string;
        }
        string? equip;
        using (var cmd = new SqlCommand("dbo.SP_POP_PNT_InsertRawLot_GetEquipment", conn) { CommandType = CommandType.StoredProcedure })
        {
            cmd.Parameters.Add("@L", SqlDbType.VarChar, 20).Value = lineId;
            equip = cmd.ExecuteScalar() as string;
        }
        return new LabelContext(itemName, pgn, alc, mountPos, customer, equip);
    }

    /// <summary>이 라인의 PNT LOT 중 <paramref name="since"/> 이후에 생성됐거나 아직 판정 전(RAW)인 것 — 오프라인 스캔이 알아보는 명부.</summary>
    public List<ImgLotDto> ListForOffline(string lineId, DateTime since)
    {
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand("""
            SELECT l.LotID, l.LotCode, l.ItemNo, mi.ItemName, mi.PGN, mi.ALC, mi.MountPos, l.LineID,
                   e.EquipID, e.CustomerCode, e.ConfirmStatus, e.ConfirmedAt,
                   CAST(NULL AS INT) AS FabricRollLotID, CAST(NULL AS DECIMAL(8,3)) AS FabricConsumedM, CAST(NULL AS INT) AS BondSetupID,
                   e.PrintedCount, l.CreatedTS, CAST(NULL AS VARCHAR(40)) AS CoreLotCode
            FROM   dbo.tbl_Lot l
            JOIN   dbo.PR_PntLot e ON e.LotID = l.LotID
            LEFT   JOIN dbo.MD_Item mi ON mi.ItemNo = l.ItemNo
            WHERE  l.LineID = @Line AND l.ProcessCode = 'PNT'
              AND (l.CreatedTS >= @Since OR e.ConfirmStatus = 'RAW')
            ORDER  BY l.LotID DESC;
            """, conn) { CommandTimeout = 120 };
        cmd.Parameters.Add("@Line",  SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@Since", SqlDbType.DateTime2   ).Value = since;
        using var rdr = cmd.ExecuteReader();
        var list = new List<ImgLotDto>();
        while (rdr.Read()) list.Add(MapToDto(rdr));
        return list;
    }

    static ImgLotDto InsertRawLot(SqlConnection conn, SqlTransaction tx, string lineId, string itemNo, string employeeNo,
                                  OfflineCreate? offline = null)
    {
        string? itemName, pgn, alc, mountPos;
        using (var cmd = new SqlCommand("dbo.SP_POP_PNT_InsertRawLot_GetItem", conn, tx) { CommandType = CommandType.StoredProcedure })
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
        using (var cmd = new SqlCommand("dbo.SP_POP_PNT_InsertRawLot_GetWorkOrderRouting", conn, tx) { CommandType = CommandType.StoredProcedure })
        {
            cmd.Parameters.Add("@Line", SqlDbType.VarChar, 20).Value = lineId;
            cmd.Parameters.Add("@Item", SqlDbType.VarChar, 20).Value = itemNo;
            customerCode = cmd.ExecuteScalar() as string;
        }
        // 오프라인에서 이미 찍은 라벨의 V 토큰이 정본이다 — 복구 시점의 WO 로 다시 정하면 라벨과 LOT 이 어긋난다
        if (offline is not null) customerCode = offline.CustomerCode;

        string? equipId;
        using (var cmd = new SqlCommand("dbo.SP_POP_PNT_InsertRawLot_GetEquipment", conn, tx) { CommandType = CommandType.StoredProcedure })
        {
            cmd.Parameters.Add("@L", SqlDbType.VarChar, 20).Value = lineId;
            equipId = cmd.ExecuteScalar() as string;
        }

        var lotCode = offline?.LotCode ?? LotNoGenerator.NextLotNo(conn, tx, lineId, DbClock.Now);

        int lotId; DateTime createdTs;
        using (var cmd = new SqlCommand("dbo.SP_POP_PNT_InsertRawLot_InsLot", conn, tx) { CommandType = CommandType.StoredProcedure })
        {
            cmd.Parameters.Add("@LotCode", SqlDbType.VarChar, 40).Value = lotCode;
            cmd.Parameters.Add("@ItemNo",  SqlDbType.VarChar, 20).Value = itemNo;
            cmd.Parameters.Add("@LineID",  SqlDbType.VarChar, 20).Value = lineId;
            cmd.Parameters.Add("@Proc",    SqlDbType.VarChar, 10).Value = ProcessCode;
            cmd.Parameters.Add("@By",      SqlDbType.VarChar, 20).Value = employeeNo;
            using var rdr = cmd.ExecuteReader();
            rdr.Read();
            lotId     = (int)rdr["LotID"];
            createdTs = (DateTime)rdr["CreatedTS"];
        }

        using (var cmd = new SqlCommand("dbo.SP_POP_PNT_InsertRawLot_InsPntLot", conn, tx) { CommandType = CommandType.StoredProcedure })
        {
            cmd.Parameters.Add("@LotID", SqlDbType.Int        ).Value = lotId;
            cmd.Parameters.Add("@Equip", SqlDbType.VarChar, 20).Value = (object?)equipId      ?? DBNull.Value;
            cmd.Parameters.Add("@Cust",  SqlDbType.VarChar, 20).Value = (object?)customerCode ?? DBNull.Value;
            cmd.Parameters.Add("@By",    SqlDbType.VarChar, 20).Value = employeeNo;
            cmd.ExecuteNonQuery();
        }

        // 프로시저는 생성 시각을 서버 시각으로 찍는다 — LOT 생성일이 현황·실적 집계의 기준이라 오프라인 LOT 은 발생 시각으로 되돌린다.
        var printed = 0;
        if (offline is not null)
        {
            using var fix = new SqlCommand("""
                UPDATE dbo.tbl_Lot SET CreatedTS = @At, ProducedAt = @At WHERE LotID = @Lot;
                UPDATE dbo.PR_PntLot SET CreatedTS = @At, PrintedCount = @Pc WHERE LotID = @Lot;
                """, conn, tx);
            fix.Parameters.Add("@At",  SqlDbType.DateTime2).Value = offline.At;
            fix.Parameters.Add("@Pc",  SqlDbType.Int      ).Value = offline.Printed;
            fix.Parameters.Add("@Lot", SqlDbType.Int      ).Value = lotId;
            fix.ExecuteNonQuery();
            createdTs = offline.At; printed = offline.Printed;
        }

        return new ImgLotDto
        {
            LotId = lotId, LotCode = lotCode, ItemNo = itemNo, ItemName = itemName,
            Pgn = pgn, Alc = alc, MountPos = mountPos, CustomerCode = customerCode,
            LineId = lineId, EquipId = equipId, ConfirmStatus = LotDefectRules.Raw,
            PrintedCount = printed, CreatedTS = createdTs, CoreLotCode = null,
        };
    }

    /// <summary>오늘 이 라인에서 발행된 LOT 전부 (RAW + CONFIRMED + DEFECT…), 최신순 — PNT-MAIN 우측 목록.</summary>
    public List<ImgLotDto> GetTodayLots(string lineId, int top = 200)
    {
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand("dbo.SP_POP_PNT_GetTodayLots", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.Add("@Line", SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@Top",  SqlDbType.Int        ).Value = top;
        using var rdr = cmd.ExecuteReader();
        var list = new List<ImgLotDto>();
        while (rdr.Read()) list.Add(MapToDto(rdr));
        return list;
    }

    /// <summary>PNT LOT 만 — 다른 공정(INJ 코어·IMG 완제품)의 LotNo 는 null.</summary>
    public ImgLotDto? GetByLotCode(string lotCode)
    {
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand("dbo.SP_POP_PNT_GetByLotCode", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.Add("@Code", SqlDbType.VarChar, 40).Value = lotCode;
        using var rdr = cmd.ExecuteReader();
        return rdr.Read() ? MapToDto(rdr) : null;
    }

    /// <summary>라벨이 실제로 나온 뒤 호출. 반환 = 누적 발행 횟수.</summary>
    public int IncrementPrintedCount(int lotId, string? employeeNo)
    {
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand("dbo.SP_POP_PNT_IncrementPrintedCount", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.Add("@LotID", SqlDbType.Int).Value = lotId;
        cmd.Parameters.Add("@By", SqlDbType.VarChar,   20).Value = (object?)employeeNo ?? DBNull.Value;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    /// <summary>
    /// 라인 불량 팝업·판정 NG 의 LOT 등록 — INJ·IMG 와 같은 순서(잠금·검사 → 역분개 → PR_DefectDetail → LOT DEFECT).
    /// </summary>
    public (DefectRegisterOutcome Outcome, int DefectId, string ItemNo) RegisterDefect(
        string lotCode, string lineId, string defectCode,
        string operatorId, int? sessionId, string employeeNo)
        => RegisterDefect(lotCode, lineId, defectCode, operatorId, sessionId, employeeNo, out _);

    /// <param name="unload">팔레트에 실려 있던 LOT 이면 같은 트랜잭션에서 재고 행을 지운 결과 — 위치가 있던 행이면 화면이 경고한다.</param>
    public (DefectRegisterOutcome Outcome, int DefectId, string ItemNo) RegisterDefect(
        string lotCode, string lineId, string defectCode,
        string operatorId, int? sessionId, string employeeNo,
        out PalletStockRepository.PalletUnloadResult unload)
        => RegisterDefect(lotCode, lineId, defectCode, operatorId, sessionId, employeeNo, null, out unload);

    /// <param name="occurredAt">오프라인에서 등록한 불량을 복구 뒤 반영할 때 그 발생 시각 — 역분개 실적·불량 상세의 시각이 이 시각이 된다.</param>
    public (DefectRegisterOutcome Outcome, int DefectId, string ItemNo) RegisterDefect(
        string lotCode, string lineId, string defectCode,
        string operatorId, int? sessionId, string employeeNo,
        DateTime? occurredAt,
        out PalletStockRepository.PalletUnloadResult unload)
    {
        unload = PalletStockRepository.PalletUnloadResult.None;
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            int lotId; string itemNo, status;
            using (var cmd = new SqlCommand("dbo.SP_POP_PNT_RegisterDefect_GetLot", conn, tx) { CommandType = CommandType.StoredProcedure })
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

            // 상태 검사가 PR_DefectDetail 보다 먼저다 — ReworkRepository(PR_DefectDetail → tbl_Lot 순)와 잠금 순서가 엇갈려도 교착이 없다.
            var check = LotDefectRules.CheckRegister(status);
            if (check != DefectRegisterOutcome.Registered) { tx.Rollback(); return (check, 0, itemNo); }

            int? origResultId = null, reversalId = null, woId = null;
            if (LotDefectRules.ReversesResult(status))
                (origResultId, reversalId, woId) = LotDefectWriter.ReverseConfirmedResult(conn, tx, lotId, lineId, operatorId, sessionId, employeeNo, occurredAt);
            woId ??= LotDefectWriter.ResolveOpenWo(conn, tx, lineId, itemNo)?.WoId;

            var defectId = LotDefectWriter.InsertDefectDetail(conn, tx, lotId, woId, origResultId, ProcessCode, defectCode,
                                                              status, reversalId, null, operatorId, employeeNo, occurredAt);

            using (var cmd = new SqlCommand("dbo.SP_POP_PNT_RegisterDefect_UpdPntLot", conn, tx) { CommandType = CommandType.StoredProcedure })
            {
                cmd.Parameters.Add("@Lot", SqlDbType.Int          ).Value = lotId;
                cmd.Parameters.Add("@Op",  SqlDbType.VarChar,   20).Value = operatorId;
                cmd.ExecuteNonQuery();
            }

            // 팔레트에 실린 채 확정된 LOT 은 재고 행을 지운다 — 불량품이 팔레트 적재 수·FG 출고·APS 가용재고에 남지 않게(10-22)
            unload = PalletStockRepository.Unload(conn, tx, lotId, lotCode, itemNo, PalletStockRepository.DefectSourceType,
                                                  $"DEFECT {defectCode}", operatorId, employeeNo);

            tx.Commit();
            return (DefectRegisterOutcome.Registered, defectId, itemNo);
        }
        catch { tx.Rollback(); throw; }
    }

    public (ImgConfirmOutcome Outcome, int ResultId, string ItemNo, int WoId) ConfirmByLotCode(
        string lotCode, string lineId, string operatorId, int? sessionId, string employeeNo)
        => ConfirmByLotCode(lotCode, lineId, operatorId, sessionId, employeeNo, null, out _);

    /// <summary>
    /// 판정 OK — 한 트랜잭션으로:
    ///   ① LOT 잠금·상태 검사 → ② 팔레트 적재(palletNo 가 있을 때) → ③ LOT 품번의 열린 WO 단계 해석(없으면 WoID NULL)
    ///   → ④ PR_ProductionResult 1 EA → ⑤ LOT CONFIRMED → ⑥ BOM 차감(MATERIAL + 사출품 자식) → ⑦ 단계 CompletedQty +1.
    /// CycleSec = 같은 라인의 직전 PNT LOT 과 이 LOT 의 생성 시각 차.
    /// </summary>
    /// <param name="palletNo">
    /// POP 이 먼저 스캔한 팔레트 — 있으면 같은 트랜잭션에서 <see cref="PalletStockRepository.Load"/> 로 WH_Inventory 에 싣고,
    /// 팔레트가 없거나·못 쓰거나·혼적·가득 참이면 확정하지 않고 Pallet* 결과로 돌려준다. null 이면 재고 없이 확정한다(테스트·다른 호출자).
    /// </param>
    /// <param name="backflush">같은 트랜잭션에서 돈 BOM 자동 차감 결과 — 확정되지 않으면 <see cref="BackflushResult.None"/>.</param>
    public (ImgConfirmOutcome Outcome, int ResultId, string ItemNo, int WoId) ConfirmByLotCode(
        string lotCode, string lineId,
        string operatorId, int? sessionId, string employeeNo, string? palletNo, out BackflushResult backflush)
        => ConfirmByLotCode(lotCode, lineId, operatorId, sessionId, employeeNo, palletNo, null, out backflush);

    /// <param name="occurredAt">오프라인에서 일어난 판정 OK 를 복구 뒤 반영할 때 그 발생 시각 — 실적의 EntryAt·전기일·교대가 이 시각 기준이 된다. 비우면 서버 시각.</param>
    public (ImgConfirmOutcome Outcome, int ResultId, string ItemNo, int WoId) ConfirmByLotCode(
        string lotCode, string lineId,
        string operatorId, int? sessionId, string employeeNo, string? palletNo, DateTime? occurredAt, out BackflushResult backflush)
    {
        backflush = BackflushResult.None;
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            int lotId; string itemNo, status; DateTime createdTs;
            using (var cmd = new SqlCommand("dbo.SP_POP_PNT_ConfirmByLotCode_GetLot", conn, tx) { CommandType = CommandType.StoredProcedure })
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
                case LotConfirmBlock.NgBlocked:        throw new InvalidOperationException("PNT lot cannot be NG_BLOCKED.");
            }

            // 팔레트 적재 — 못 실으면 확정도 하지 않는다(확정됐는데 팔레트에 없는 제품이 생기지 않게)
            if (palletNo is not null)
            {
                var load = PalletStockRepository.Load(conn, tx, lotId, lotCode, itemNo, palletNo, operatorId, employeeNo);
                if (load != PalletLoadOutcome.Ok) { tx.Rollback(); return (PalletOutcome(load), 0, itemNo, 0); }
            }

            // WO 가 없어도 생산은 막지 않는다(2026-10-01) — 열린 단계가 없으면 실적·LOT 의 WoID 는 NULL, 단계 반영 없음.
            int? woId = null, stepId = null;
            using (var cmd = new SqlCommand("dbo.SP_POP_PNT_ConfirmByLotCode_GetWorkOrderRouting", conn, tx) { CommandType = CommandType.StoredProcedure })
            {
                cmd.Parameters.Add("@Line", SqlDbType.VarChar, 20).Value = lineId;
                cmd.Parameters.Add("@Item", SqlDbType.VarChar, 20).Value = itemNo;
                using var rdr = cmd.ExecuteReader();
                if (rdr.Read()) { woId = (int)rdr["WoID"]; stepId = (int)rdr["RoutingLineID"]; }
            }

            int cycleSec;
            using (var cmd = new SqlCommand("dbo.SP_POP_PNT_ConfirmByLotCode_GetLot2", conn, tx) { CommandType = CommandType.StoredProcedure })
            {
                cmd.Parameters.Add("@ThisTs", SqlDbType.DateTime2  ).Value = createdTs;
                cmd.Parameters.Add("@Line",   SqlDbType.VarChar, 20).Value = lineId;
                cycleSec = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
                if (cycleSec is < 0 or > 86400) cycleSec = 0;
            }

            // 전기일·교대는 설정 DAY_CUTOFF_TIME·공통코드 WORK_SHIFT로 확정 시점 서버 시각에 판정
            var (now, prodDate, shiftCode) = ProdCalendar.ResolveNow(conn, tx, occurredAt);
            int resultId;
            using (var cmd = new SqlCommand("dbo.SP_POP_PNT_ConfirmByLotCode_InsProductionResult", conn, tx) { CommandType = CommandType.StoredProcedure })
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

            using (var cmd = new SqlCommand("dbo.SP_POP_PNT_ConfirmByLotCode_UpdLot", conn, tx) { CommandType = CommandType.StoredProcedure })
            {
                cmd.Parameters.Add("@WoID",     SqlDbType.Int          ).Value = (object?)woId ?? DBNull.Value;
                cmd.Parameters.Add("@LotID",    SqlDbType.Int          ).Value = lotId;
                cmd.Parameters.Add("@Op",       SqlDbType.VarChar,   20).Value = operatorId;
                cmd.Parameters.Add("@Sess",     SqlDbType.Int          ).Value = (object?)sessionId ?? DBNull.Value;
                cmd.ExecuteNonQuery();
            }

            // BOM 자동 차감 — PNT 는 MATERIAL 자식 + 사출품(InjFlag) 자식(코어 스캔이 없어 여기서 코어를 소비한다). 실패해도 확정은 커밋된다(세이브포인트)
            backflush = BackflushRepository.Apply(conn, tx, lotId, lotCode, itemNo, lineId, operatorId, employeeNo,
                                                  BackflushRules.IncludesInjection(ProcessCode));

            if (stepId is int step) WorkOrderRepository.BumpStepCompleted(conn, tx, step, 1m, operatorId);

            tx.Commit();
            return (ImgConfirmOutcome.Confirmed, resultId, itemNo, woId ?? 0);
        }
        catch { tx.Rollback(); throw; }
    }

    static ImgConfirmOutcome PalletOutcome(PalletLoadOutcome o) => o switch
    {
        PalletLoadOutcome.NotFound => ImgConfirmOutcome.PalletNotFound,
        PalletLoadOutcome.Mixed    => ImgConfirmOutcome.PalletMixed,
        PalletLoadOutcome.Full     => ImgConfirmOutcome.PalletFull,
        _                          => ImgConfirmOutcome.PalletUnavailable,
    };

    /// <summary>
    /// PNT-MAIN 좌측 패널: 스테이션 BOP 품번 ∪ 그 날 실적/일정이 있는 품번의 지정일 현황. HasOpenWo 는 날짜와 무관한 현재 상태다.
    /// INJ·IMG 판과 같은 항등식 INPUT = FINAL + NG + 미확정(LOT 생성일 기준): FINAL = CONFIRMED, NG = DEFECT + SCRAPPED, 미확정 = RAW.
    /// </summary>
    public List<InjItemDailyDto> GetDailyItemSummary(string lineId, string stationCode, DateTime date)
    {
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand("dbo.SP_POP_PNT_GetDailyItemSummary", conn) { CommandType = CommandType.StoredProcedure };
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
