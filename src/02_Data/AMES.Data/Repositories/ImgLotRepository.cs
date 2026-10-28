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
        => CreateFromCoreCore(coreLotCode, lineId, employeeNo, finishedItemNo, null);

    /// <summary>오프라인에서 이미 번호를 받아 라벨까지 뽑은 LOT 의 삽입 값 — 번호·생성 시각·출력 횟수·라벨에 찍은 수주처 코드.</summary>
    sealed record OfflineCreate(string LotCode, DateTime At, int Printed, string? CustomerCode);

    /// <summary>
    /// 오프라인에서 Core 를 스캔해 이미 라벨을 뽑은 완제품 LOT 을 복구 뒤 만든다 — <see cref="CreateFromCore"/> 와 같은 검사(코어 상태·사용 여부·완제품 결정)를
    /// 서버가 다시 하되 번호·생성 시각·출력 횟수·수주처는 그때 값 그대로다. 같은 번호가 이미 있으면(재전송) 새로 만든 것 없이
    /// (<see cref="ImgCoreOutcome.Created"/>, Lot null) 돌려준다.
    /// </summary>
    public (ImgCoreOutcome Outcome, ImgLotDto? Lot, string? UsedByLotCode, string? ItemNo) CreateFromCoreWithCode(
        string coreLotCode, string lineId, string employeeNo, string? finishedItemNo,
        string lotCode, DateTime at, int printedCount, string? customerCode)
        => CreateFromCoreCore(coreLotCode, lineId, employeeNo, finishedItemNo, new OfflineCreate(lotCode, at, printedCount, customerCode));

    /// <summary>오프라인에서 라벨 발행 버튼으로 만든 완제품 LOT 을 복구 뒤 넣는다. 같은 번호가 이미 있으면 null.</summary>
    public ImgLotDto? CreateRawLotWithCode(string lineId, string itemNo, string employeeNo,
                                           string lotCode, DateTime at, int printedCount, string? customerCode)
    {
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            if (LotExists(conn, tx, lotCode)) { tx.Rollback(); return null; }
            LotNoGenerator.EnsureAtLeast(conn, tx, lotCode);
            var lot = InsertRawLot(conn, tx, lineId, itemNo, null, null, employeeNo, null, new OfflineCreate(lotCode, at, printedCount, customerCode));
            tx.Commit();
            return lot;
        }
        catch { tx.Rollback(); throw; }
    }

    public enum VoidOutcome { Voided, NotFound, WrongLine, NotRaw }

    /// <summary>
    /// 판정 전(RAW) IMG LOT 발행 취소 — 좌측에서 다른 색을 고른 채 코어를 찍는 등 잘못 발행한 라벨을 되돌린다(10-27).
    /// 한 트랜잭션: LOT 잠금·상태 확인 → 코어 재고 되살리기(<see cref="PalletStockRepository.RestoreCore"/>) → PR_ImgLot·tbl_Lot 행 삭제
    /// (실적·불량·팔레트·차감은 RAW 에 없다). 코어 연결이 풀려 같은 코어를 다시 스캔할 수 있고, LotNo 는 결번이 된다. 출력된 라벨은 버린다.
    /// </summary>
    public VoidOutcome VoidRawLot(string lotCode, string lineId, string operatorId, string employeeNo)
    {
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            int lotId; string? lotLine, status, coreCode, coreItem;
            using (var cmd = new SqlCommand("""
                SELECT l.LotID, l.LineID, e.ConfirmStatus, p.LotCode AS CoreCode, p.ItemNo AS CoreItem
                FROM   dbo.tbl_Lot l WITH (UPDLOCK, ROWLOCK)
                JOIN   dbo.PR_ImgLot e WITH (UPDLOCK, ROWLOCK) ON e.LotID = l.LotID
                LEFT JOIN dbo.tbl_Lot p ON p.LotID = l.ParentLotID
                WHERE  l.LotCode = @Code AND l.ProcessCode = 'IMG';
                """, conn, tx))
            {
                cmd.Parameters.Add("@Code", SqlDbType.VarChar, 40).Value = lotCode;
                using var rdr = cmd.ExecuteReader();
                if (!rdr.Read()) { rdr.Close(); tx.Rollback(); return VoidOutcome.NotFound; }
                lotId    = (int)rdr["LotID"];
                lotLine  = rdr["LineID"] as string;
                status   = rdr["ConfirmStatus"] as string;
                coreCode = rdr["CoreCode"] as string;
                coreItem = rdr["CoreItem"] as string;
            }
            if (!string.Equals(lotLine, lineId, StringComparison.OrdinalIgnoreCase)) { tx.Rollback(); return VoidOutcome.WrongLine; }
            if (!string.Equals(status, LotDefectRules.Raw, StringComparison.OrdinalIgnoreCase)) { tx.Rollback(); return VoidOutcome.NotRaw; }

            if (coreCode is not null && coreItem is not null)
                PalletStockRepository.RestoreCore(conn, tx, lotId, lotCode, coreCode, coreItem, operatorId, employeeNo);

            using (var cmd = new SqlCommand("""
                DELETE dbo.PR_ImgLot WHERE LotID = @Lot;
                DELETE dbo.tbl_Lot   WHERE LotID = @Lot;
                """, conn, tx))
            {
                cmd.Parameters.Add("@Lot", SqlDbType.Int).Value = lotId;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
            return VoidOutcome.Voided;
        }
        catch { tx.Rollback(); throw; }
    }

    static bool LotExists(SqlConnection conn, SqlTransaction tx, string lotCode)
    {
        using var cmd = new SqlCommand("SELECT 1 FROM dbo.tbl_Lot WHERE LotCode = @Code;", conn, tx);
        cmd.Parameters.Add("@Code", SqlDbType.VarChar, 40).Value = lotCode;
        return cmd.ExecuteScalar() is not null;
    }

    (ImgCoreOutcome Outcome, ImgLotDto? Lot, string? UsedByLotCode, string? ItemNo) CreateFromCoreCore(
        string coreLotCode, string lineId, string employeeNo, string? finishedItemNo, OfflineCreate? offline)
    {
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        int coreId = 0; string? itemNo = null;
        try
        {
            if (offline is not null && LotExists(conn, tx, offline.LotCode)) { tx.Rollback(); return (ImgCoreOutcome.Created, null, null, null); }

            string coreStatus;
            using (var cmd = new SqlCommand("dbo.SP_POP_IMG_CreateFromCore_GetLot", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
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
            using (var cmd = new SqlCommand("dbo.SP_POP_IMG_CreateFromCore_GetWorkOrderRouting", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
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
                var bom = CoreItemResolver.ReadByInjFlag(conn, tx, finishedItemNo);
                if (bom.Outcome == CoreItemResolver.Outcome.Core && !string.Equals(bom.CoreItemNo, itemNo, StringComparison.OrdinalIgnoreCase))
                { tx.Rollback(); return (ImgCoreOutcome.CoreMismatch, null, null, itemNo); }
                finishedItem = finishedItemNo;
            }

            var lot = InsertRawLot(conn, tx, lineId, finishedItem, coreId, coreLotCode, employeeNo, woId, offline);
            // 코어는 이 순간 팔레트에서 나와 완제품의 일부가 된다 — 사출 확정이 실은 코어 재고 행을 0 으로(없으면 아무것도 안 함)
            PalletStockRepository.ConsumeCore(conn, tx, lot.LotId, coreLotCode, itemNo, employeeNo);
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
        using var cmd = new SqlCommand("dbo.SP_POP_IMG_ItemExists", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = itemNo;
        return cmd.ExecuteScalar() is not null;
    }

    static string? FindCoreUser(SqlConnection conn, SqlTransaction? tx, int coreLotId)
    {
        using var cmd = new SqlCommand("dbo.SP_POP_IMG_FindCoreUser", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@Core", SqlDbType.Int).Value = coreLotId;
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>
    /// RAW LOT 1건 INSERT. 라벨 V 토큰(수주처 코드)은 발행 시점 이 라인의 열린 WO → PP_CustomerOrder → MD_Customer 로
    /// 정해 LOT 에 박아 둔다 — 재출력 때 WO 가 바뀌어도 라벨이 달라지지 않는다.
    /// </summary>
    static ImgLotDto InsertRawLot(SqlConnection conn, SqlTransaction tx, string lineId, string itemNo,
                                  int? parentLotId, string? parentLotCode, string employeeNo, int? woId = null,
                                  OfflineCreate? offline = null)
    {
        string? itemName, pgn, alc, mountPos;
        using (var cmd = new SqlCommand(
            "dbo.SP_POP_IMG_InsertRawLot_GetItem", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
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
            ? "dbo.SP_POP_IMG_InsertRawLot_GetWorkOrderRouting"
            : "dbo.SP_POP_IMG_InsertRawLot_GetWorkOrder";
        using (var cmd = new SqlCommand(custSql, conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
        {
            if (woId is null)
            {
                cmd.Parameters.Add("@Line", SqlDbType.VarChar, 20).Value = lineId;
                cmd.Parameters.Add("@Item", SqlDbType.VarChar, 20).Value = itemNo;
            }
            else cmd.Parameters.Add("@Wo", SqlDbType.Int).Value = woId.Value;
            customerCode = cmd.ExecuteScalar() as string;
        }
        // 오프라인에서 이미 찍은 라벨의 V 토큰이 정본이다 — 복구 시점의 WO 로 다시 정하면 라벨과 LOT 이 어긋난다
        if (offline is not null) customerCode = offline.CustomerCode;

        string? equipId;
        using (var cmd = new SqlCommand("dbo.SP_POP_IMG_InsertRawLot_GetEquipment", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
        {
            cmd.Parameters.Add("@L", SqlDbType.VarChar, 20).Value = lineId;
            equipId = cmd.ExecuteScalar() as string;
        }

        var lotCode = offline?.LotCode ?? LotNoGenerator.NextLotNo(conn, tx, lineId, DbClock.Now);
        if (offline is not null) LotNoGenerator.EnsureAtLeast(conn, tx, lotCode);

        int lotId; DateTime createdTs;
        using (var cmd = new SqlCommand("dbo.SP_POP_IMG_InsertRawLot_InsLot", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
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

        using (var cmd = new SqlCommand("dbo.SP_POP_IMG_InsertRawLot_InsImgLot", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
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
                UPDATE dbo.PR_ImgLot SET CreatedTS = @At, PrintedCount = @Pc WHERE LotID = @Lot;
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
            LineId = lineId, EquipId = equipId, ConfirmStatus = "RAW",
            PrintedCount = printed, CreatedTS = createdTs, CoreLotCode = parentLotCode,
        };
    }

    /// <summary>오늘 이 라인에서 발행된 LOT 전부 (RAW + CONFIRMED), 최신순 — IMG-MAIN 우측 목록.</summary>
    public List<ImgLotDto> GetTodayLots(string lineId, int top = 200)
    {
        var sql = "dbo.SP_POP_IMG_GetTodayLots";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@Line", SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@Top",  SqlDbType.Int        ).Value = top;
        using var rdr = cmd.ExecuteReader();
        var list = new List<ImgLotDto>();
        while (rdr.Read()) list.Add(MapToDto(rdr));
        return list;
    }

    public ImgLotDto? GetByLotCode(string lotCode)
    {
        var sql = "dbo.SP_POP_IMG_GetByLotCode";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@Code", SqlDbType.VarChar, 40).Value = lotCode;
        using var rdr = cmd.ExecuteReader();
        return rdr.Read() ? MapToDto(rdr) : null;
    }

    // ── 오프라인 사본용 ────────────────────────────────────────────────

    /// <summary>오프라인 라벨에 찍는 값 — 품번 정보·수주처 코드(이 라인·품번의 열린 WO 기준)·설비. 라벨 발행 때 서버가 하는 조회와 같다.</summary>
    public sealed record LabelContext(string? ItemName, string? Pgn, string? Alc, string? MountPos, string? CustomerCode, string? EquipId);

    public LabelContext GetLabelContext(string lineId, string itemNo)
    {
        using var conn = _factory.OpenConnection();
        string? itemName = null, pgn = null, alc = null, mountPos = null;
        using (var cmd = new SqlCommand("dbo.SP_POP_IMG_InsertRawLot_GetItem", conn) { CommandType = System.Data.CommandType.StoredProcedure })
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
        using (var cmd = new SqlCommand("dbo.SP_POP_IMG_InsertRawLot_GetWorkOrderRouting", conn) { CommandType = System.Data.CommandType.StoredProcedure })
        {
            cmd.Parameters.Add("@Line", SqlDbType.VarChar, 20).Value = lineId;
            cmd.Parameters.Add("@Item", SqlDbType.VarChar, 20).Value = itemNo;
            customer = cmd.ExecuteScalar() as string;
        }
        string? equip;
        using (var cmd = new SqlCommand("dbo.SP_POP_IMG_InsertRawLot_GetEquipment", conn) { CommandType = System.Data.CommandType.StoredProcedure })
        {
            cmd.Parameters.Add("@L", SqlDbType.VarChar, 20).Value = lineId;
            equip = cmd.ExecuteScalar() as string;
        }
        return new LabelContext(itemName, pgn, alc, mountPos, customer, equip);
    }

    /// <summary>이 라인의 완제품 LOT 중 <paramref name="since"/> 이후에 생성됐거나 아직 판정 전(RAW)인 것 — 오프라인 스캔이 알아보는 명부.</summary>
    public List<ImgLotDto> ListForOffline(string lineId, DateTime since)
    {
        var sql = SelectLotView + """

            WHERE  l.LineID = @Line AND l.ProcessCode = 'IMG'
              AND (l.CreatedTS >= @Since OR e.ConfirmStatus = 'RAW')
            ORDER  BY l.LotID DESC;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandTimeout = 120 };
        cmd.Parameters.Add("@Line",  SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@Since", SqlDbType.DateTime2   ).Value = since;
        using var rdr = cmd.ExecuteReader();
        var list = new List<ImgLotDto>();
        while (rdr.Read()) list.Add(MapToDto(rdr));
        return list;
    }

    /// <summary>오프라인 Core 스캔용 — 아직 완제품에 쓰이지 않은 확정(CONFIRMED) 사출 LOT. 코어는 어느 사출 라인에서 와도 되므로 라인으로 거르지 않는다.</summary>
    public sealed record CoreRow(string LotCode, string ItemNo);

    public List<CoreRow> ListCoresForOffline(DateTime since)
    {
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand("""
            SELECT l.LotCode, l.ItemNo
            FROM   dbo.tbl_Lot l
            JOIN   dbo.PR_InjLot e ON e.LotID = l.LotID
            WHERE  l.ProcessCode = 'INJ' AND e.ConfirmStatus = 'CONFIRMED' AND l.CreatedTS >= @Since
              AND  NOT EXISTS (SELECT 1 FROM dbo.tbl_Lot c WHERE c.ParentLotID = l.LotID AND c.ProcessCode = 'IMG');
            """, conn) { CommandTimeout = 120 };
        cmd.Parameters.Add("@Since", SqlDbType.DateTime2).Value = since;
        using var rdr = cmd.ExecuteReader();
        var list = new List<CoreRow>();
        while (rdr.Read()) list.Add(new CoreRow((string)rdr["LotCode"], rdr["ItemNo"] as string ?? string.Empty));
        return list;
    }

    /// <summary>이 라인에 열린 단계가 있는 WO 의 (완제품, 코어 공정 단계 품번, 수주처) — Core 스캔이 완제품을 정하는 규칙(<see cref="CreateFromCore"/> ④)의 사본.</summary>
    public sealed record CoreMapRow(int WoId, string FinishedItem, string CoreItem, string? CustomerCode);

    public List<CoreMapRow> ListCoreMap(string lineId)
    {
        var sql = $"""
            SELECT w.WoID, w.ItemNo AS FinishedItem, COALESCE(c.ItemNo, w.ItemNo) AS CoreItem
            FROM   dbo.PP_WorkOrderRouting r
            JOIN   dbo.PP_WorkOrder        w ON w.WoID = r.WoID
            JOIN   dbo.PP_WorkOrderRouting c ON c.WoID = w.WoID AND c.ProcessCode = @CoreProc
            WHERE  r.LineID = @Line AND {WorkOrderRepository.OpenStepWhere}
            ORDER  BY {WorkOrderRepository.OpenStepOrder};
            """;
        using var conn = _factory.OpenConnection();
        var rows = new List<(int WoId, string Fin, string Core)>();
        using (var cmd = new SqlCommand(sql, conn))
        {
            cmd.Parameters.Add("@Line",     SqlDbType.VarChar, 20).Value = lineId;
            cmd.Parameters.Add("@CoreProc", SqlDbType.VarChar, 10).Value = CoreItemResolver.CoreProcess;
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read()) rows.Add(((int)rdr["WoID"], (string)rdr["FinishedItem"], (string)rdr["CoreItem"]));
        }
        var list = new List<CoreMapRow>();
        foreach (var (wo, fin, core) in rows)
        {
            using var cmd = new SqlCommand("dbo.SP_POP_IMG_InsertRawLot_GetWorkOrder", conn) { CommandType = System.Data.CommandType.StoredProcedure };
            cmd.Parameters.Add("@Wo", SqlDbType.Int).Value = wo;
            list.Add(new CoreMapRow(wo, fin, core, cmd.ExecuteScalar() as string));
        }
        return list;
    }

    /// <summary>작업자가 고른 완제품의 유효 BOM 코어 — WO 가 없을 때 Core 스캔이 코어와 맞는지 보는 규칙의 사본(Outcome 은 CoreItemResolver.Outcome 이름).</summary>
    public sealed record BomCoreRow(string Outcome, string? CoreItemNo);

    public Dictionary<string, BomCoreRow> ReadBomCores(IEnumerable<string> finishedItems)
    {
        var map = new Dictionary<string, BomCoreRow>(StringComparer.OrdinalIgnoreCase);
        using var conn = _factory.OpenConnection();
        foreach (var item in finishedItems.Where(i => !string.IsNullOrEmpty(i)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var r = CoreItemResolver.ReadByInjFlag(conn, null, item);
            map[item] = new BomCoreRow(r.Outcome.ToString(), r.CoreItemNo);
        }
        return map;
    }

    /// <summary>라벨이 실제로 나온 뒤 호출. 반환 = 누적 발행 횟수.</summary>
    public int IncrementPrintedCount(int lotId, string? employeeNo)
    {
        const string sql = "dbo.SP_POP_IMG_IncrementPrintedCount";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
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
            int lotId; string itemNo, status; string? usedBy; bool shipped;
            using (var cmd = new SqlCommand("dbo.SP_POP_IMG_RegisterDefect_GetLot", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
            {
                cmd.Parameters.Add("@Code", SqlDbType.VarChar, 40).Value = lotCode;
                using var rdr = cmd.ExecuteReader();
                if (!rdr.Read()) { rdr.Close(); tx.Rollback(); return (DefectRegisterOutcome.NotFound, 0, string.Empty); }
                lotId  = (int)rdr["LotID"];
                itemNo = rdr["ItemNo"] as string ?? string.Empty;
                status = (string)rdr["ConfirmStatus"];
                (usedBy, shipped) = LotDefectWriter.ReadUsage(rdr);
                var lotLine = rdr["LineID"] as string;
                if (!string.Equals(lotLine, lineId, StringComparison.OrdinalIgnoreCase))
                { rdr.Close(); tx.Rollback(); return (DefectRegisterOutcome.WrongLine, 0, itemNo); }
            }

            // 상태 검사가 PR_DefectDetail 보다 먼저다 — DEFECT 면 여기서 tbl_Lot 잠금을 놓고 나가므로
            // ReworkRepository(PR_DefectDetail → tbl_Lot 순)와 잠금 순서가 엇갈려도 교착이 없다. 순서를 바꾸면 순환이 생긴다.
            var check = LotDefectRules.CheckRegister(status, usedBy, shipped);
            if (check != DefectRegisterOutcome.Registered) { tx.Rollback(); return (check, 0, itemNo); }

            int? origResultId = null, reversalId = null, woId = null;
            if (LotDefectRules.ReversesResult(status))
                (origResultId, reversalId, woId) = LotDefectWriter.ReverseConfirmedResult(conn, tx, lotId, lineId, operatorId, sessionId, employeeNo, occurredAt);
            woId ??= LotDefectWriter.ResolveOpenWo(conn, tx, lineId, itemNo)?.WoId;

            var defectId = LotDefectWriter.InsertDefectDetail(conn, tx, lotId, woId, origResultId, ProcessCode, defectCode,
                                                              status, reversalId, null, operatorId, employeeNo, occurredAt);

            using (var cmd = new SqlCommand("dbo.SP_POP_IMG_RegisterDefect_UpdImgLot", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
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

    /// <summary>
    /// 라벨 스캔 확정 — 한 트랜잭션으로:
    ///   ① LOT 잠금·상태 검사 → ② LOT 품번의 열린 WO 단계 해석 (INJ 와 같은 규칙, 없으면 WoID NULL 로 확정)
    ///   → ③ PR_ProductionResult 1 EA → ④ LOT CONFIRMED + 단계 CompletedQty +1.
    /// 원단 롤·본딩은 기록하지 않는다.
    /// CycleSec = 같은 라인의 직전 IMG LOT 과 이 LOT 의 생성 시각 차.
    /// </summary>
    public (ImgConfirmOutcome Outcome, int ResultId, string ItemNo, int WoId) ConfirmByLotCode(
        string lotCode, string lineId, string operatorId, int? sessionId, string employeeNo)
        => ConfirmByLotCode(lotCode, lineId, operatorId, sessionId, employeeNo, null, out _);

    public (ImgConfirmOutcome Outcome, int ResultId, string ItemNo, int WoId) ConfirmByLotCode(
        string lotCode, string lineId, string operatorId, int? sessionId, string employeeNo, out BackflushResult backflush)
        => ConfirmByLotCode(lotCode, lineId, operatorId, sessionId, employeeNo, null, out backflush);

    /// <param name="palletNo">
    /// POP 이 먼저 스캔한 팔레트(2026-10-21) — 있으면 같은 트랜잭션에서 <see cref="PalletStockRepository.Load"/> 로 WH_Inventory 에 싣고,
    /// 팔레트가 없거나·못 쓰거나·혼적·가득 참이면 확정하지 않고 Pallet* 결과로 돌려준다. null 이면 종전대로 재고 없이 확정한다(테스트·다른 호출자).
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
            int lotId; string itemNo, status; DateTime createdTs; string? labelCust = null;
            using (var cmd = new SqlCommand("dbo.SP_POP_IMG_ConfirmByLotCode_GetLot", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
            {
                cmd.Parameters.Add("@Code", SqlDbType.VarChar, 40).Value = lotCode;
                using var rdr = cmd.ExecuteReader();
                if (!rdr.Read()) { rdr.Close(); tx.Rollback(); return (ImgConfirmOutcome.NotFound, 0, string.Empty, 0); }
                lotId     = (int)rdr["LotID"];
                itemNo    = rdr["ItemNo"] as string ?? string.Empty;
                status    = (string)rdr["ConfirmStatus"];
                createdTs = (DateTime)rdr["CreatedTS"];
                for (var i = 0; i < rdr.FieldCount; i++)   // 10-28 전 프로시저에는 없는 열
                    if (rdr.GetName(i) == "CustomerCode" && !rdr.IsDBNull(i)) labelCust = rdr.GetString(i);
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

            // 팔레트 적재 — 못 실으면 확정도 하지 않는다(확정됐는데 팔레트에 없는 제품이 생기지 않게)
            if (palletNo is not null)
            {
                var load = PalletStockRepository.Load(conn, tx, lotId, lotCode, itemNo, palletNo, operatorId, employeeNo);
                if (load != PalletLoadOutcome.Ok) { tx.Rollback(); return (PalletOutcome(load), 0, itemNo, 0); }
            }

            // WO 가 없어도 생산은 막지 않는다(2026-10-01) — 열린 단계가 없으면 실적·LOT 의 WoID 는 NULL, 단계 반영 없음.
            int? woId = null, stepId = null;
            using (var cmd = new SqlCommand("dbo.SP_POP_IMG_ConfirmByLotCode_GetWorkOrderRouting", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
            {
                cmd.Parameters.Add("@Line", SqlDbType.VarChar, 20).Value = lineId;
                cmd.Parameters.Add("@Item", SqlDbType.VarChar, 20).Value = itemNo;
                if (labelCust is not null) cmd.Parameters.Add("@Cust", SqlDbType.VarChar, 20).Value = labelCust;   // 구 프로시저(열 없음)면 넘기지 않는다
                using var rdr = cmd.ExecuteReader();
                if (rdr.Read()) { woId = (int)rdr["WoID"]; stepId = (int)rdr["RoutingLineID"]; }
            }

            int cycleSec;
            using (var cmd = new SqlCommand("dbo.SP_POP_IMG_ConfirmByLotCode_GetLot2", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
            {
                cmd.Parameters.Add("@ThisTs", SqlDbType.DateTime2  ).Value = createdTs;
                cmd.Parameters.Add("@Line",   SqlDbType.VarChar, 20).Value = lineId;
                cycleSec = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
                if (cycleSec is < 0 or > 86400) cycleSec = 0;
            }

            // 전기일·교대는 설정 DAY_CUTOFF_TIME·공통코드 WORK_SHIFT로 확정 시점 서버 시각에 판정
            var (now, prodDate, shiftCode) = ProdCalendar.ResolveNow(conn, tx, occurredAt);
            int resultId;
            using (var cmd = new SqlCommand("dbo.SP_POP_IMG_ConfirmByLotCode_InsProductionResult", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
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

            using (var cmd = new SqlCommand("dbo.SP_POP_IMG_ConfirmByLotCode_UpdLot", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
            {
                cmd.Parameters.Add("@WoID",     SqlDbType.Int          ).Value = (object?)woId ?? DBNull.Value;
                cmd.Parameters.Add("@LotID",    SqlDbType.Int          ).Value = lotId;
                cmd.Parameters.Add("@Op",       SqlDbType.VarChar,   20).Value = operatorId;
                cmd.Parameters.Add("@Sess",     SqlDbType.Int          ).Value = (object?)sessionId ?? DBNull.Value;
                cmd.ExecuteNonQuery();
            }

            // BOM 자동 차감(완제품 BOM 의 MATERIAL·사출품 자식, 스캔한 코어 품번 제외) — 실패해도 확정은 커밋된다(세이브포인트)
            backflush = BackflushRepository.Apply(conn, tx, lotId, lotCode, itemNo, lineId, operatorId, employeeNo,
                                                  BackflushRules.IncludesInjection(BackflushRules.CoreScanningProcess));

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
    /// IMG-MAIN 좌측 패널: 스테이션 BOP 품번 ∪ 그 날 실적/일정이 있는 품번의 지정일 현황.
    /// HasOpenWo 는 날짜와 무관한 현재 상태다.
    /// INJ 판과 같은 항등식 INPUT = FINAL + NG + 미확정. 기준일은 LOT 생성일.
    /// 전부 LOT 상태로 센다: FINAL = CONFIRMED, NG = DEFECT + SCRAPPED, 미확정 = RAW.
    /// </summary>
    public List<InjItemDailyDto> GetDailyItemSummary(string lineId, string stationCode, DateTime date)
    {
        const string sql = "dbo.SP_POP_IMG_GetDailyItemSummary";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
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
