using System.Data;
using AMES.Contracts.Dto;
using AMES.Contracts.Enums;
using AMES.Data.Connection;
using AMES.Data.Services;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// 사출 원천 LOT (tbl_Lot + PR_InjLot) 의 생성·확정·로봇 NG 차단·불량 등록. 상태 기계는 LotDefectRules.
/// 실적 확정(= PR_ProductionResult 생성)은 반드시 스캔 경유 — 스캔 전 LOT 는 실적이 아니다.
/// </summary>
public sealed class InjLotRepository
{
    private readonly AmesConnectionFactory _factory;
    public InjLotRepository(AmesConnectionFactory f) => _factory = f;

    /// <summary>
    /// PLC 금형코드(dash 제거)+색상 → 품번·캐비티 (SIS APM2120 = MD_MoldItem).
    /// MoldCodeClean 등치 seek — SIS 원본의 LIKE 접두 매칭 대신 사전계산 컬럼을 쓴다.
    /// </summary>
    public List<MoldItemDto> GetMoldItems(string moldCode, string colorCode)
    {
        const string sql = """
            SELECT m.MoldID, m.MoldCodeClean, mi.Color, mi.CavitySeq, mi.CavityPos, mi.ItemNo,
                   i.ItemName
            FROM   dbo.MD_MoldItem mi
            JOIN   dbo.MD_Mold m ON m.MoldID = mi.MoldID
            LEFT   JOIN dbo.MD_Item i ON i.ItemNo = mi.ItemNo
            WHERE  m.MoldCodeClean = @M AND mi.Color = @C AND mi.ActiveFlag = 1
            ORDER  BY mi.CavitySeq;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@M", SqlDbType.VarChar, 20).Value = moldCode;
        cmd.Parameters.Add("@C", SqlDbType.VarChar, 10).Value = colorCode;
        using var rdr = cmd.ExecuteReader();
        var list = new List<MoldItemDto>();
        while (rdr.Read())
            list.Add(new MoldItemDto
            {
                MoldCode  = (string)rdr["MoldCodeClean"],
                ColorCode = (string)rdr["Color"],
                CavityNo  = (int)rdr["CavitySeq"],
                CavityPos = rdr["CavityPos"] as string ?? string.Empty,
                ItemNo    = (string)rdr["ItemNo"],
                ItemName  = rdr["ItemName"] as string,
                MoldId    = rdr["MoldID"] as string,
            });
        return list;
    }

    /// <summary>
    /// 샷 1회 × 캐비티 1개 → 원천 LOT 1건. tbl_Lot(Status='RAW') + PR_InjLot 을
    /// 한 트랜잭션으로 생성하고, CavityNo==1 일 때만 금형 샷수를 +1 한다
    /// (2캐비티 = 같은 샷이므로 이중 가산 방지).
    /// </summary>
    public (int LotId, string LotCode) CreateRawLot(
        string lineId, string equipId, MoldItemDto map, long machineShotCount)
    {
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            var lotCode = LotNoGenerator.NextLotNo(conn, tx, lineId, DbClock.Now);
            int lotId;
            using (var cmd = new SqlCommand("""
                INSERT INTO dbo.tbl_Lot
                    (LotCode, ItemNo, WoID, LineID, ProcessCode, BatchSize, RemainingQty,
                     ProducedAt, Status, QualityFlag, CreatedBy, CreatedTS)
                OUTPUT INSERTED.LotID
                VALUES
                    (@LotCode, @ItemNo, NULL, @LineID, 'INJ', 1, 1,
                     SYSDATETIME(), 'RAW', 'PENDING', 'AGENT', SYSDATETIME());
                """, conn, tx))
            {
                cmd.Parameters.Add("@LotCode", SqlDbType.VarChar, 40).Value = lotCode;
                cmd.Parameters.Add("@ItemNo",  SqlDbType.VarChar, 20).Value = map.ItemNo;
                cmd.Parameters.Add("@LineID",  SqlDbType.VarChar, 20).Value = lineId;
                lotId = (int)cmd.ExecuteScalar()!;
            }

            using (var cmd = new SqlCommand("""
                INSERT INTO dbo.PR_InjLot
                    (LotID, EquipID, MoldCode, ColorCode, MoldID, CavityNo, CavityPos,
                     PressType, MachineShotCount, ConfirmStatus, CreatedBy, CreatedTS)
                VALUES
                    (@LotID, @Equip, @Mold, @Color, @MoldID, @CavNo, @CavPos,
                     'M', @Shot, 'RAW', 'AGENT', SYSDATETIME());
                """, conn, tx))
            {
                cmd.Parameters.Add("@LotID",  SqlDbType.Int        ).Value = lotId;
                cmd.Parameters.Add("@Equip",  SqlDbType.VarChar, 20).Value = equipId;
                cmd.Parameters.Add("@Mold",   SqlDbType.VarChar, 20).Value = map.MoldCode;
                cmd.Parameters.Add("@Color",  SqlDbType.VarChar, 10).Value = map.ColorCode;
                cmd.Parameters.Add("@MoldID", SqlDbType.VarChar, 20).Value = (object?)map.MoldId ?? DBNull.Value;
                cmd.Parameters.Add("@CavNo",  SqlDbType.Int        ).Value = map.CavityNo;
                cmd.Parameters.Add("@CavPos", SqlDbType.VarChar, 4 ).Value = map.CavityPos;
                cmd.Parameters.Add("@Shot",   SqlDbType.BigInt     ).Value = machineShotCount;
                cmd.ExecuteNonQuery();
            }

            if (map.CavityNo == 1 && !string.IsNullOrEmpty(map.MoldId))
            {
                using (var cmd = new SqlCommand("""
                    UPDATE dbo.MD_Mold
                    SET    CurrentShots    = ISNULL(CurrentShots,0) + 1,      -- 장착 후 타수 (Inj06 교체 시 리셋)
                           CumulativeShots = CumulativeShots + 1,             -- 수명 누적 (SIS CUM_SHOTS, 리셋 금지)
                           ShotsUpdatedTS  = SYSDATETIME(),
                           ModifiedBy = 'AGENT', ModifiedTS = SYSDATETIME()
                    WHERE  MoldID = @Mold;
                    """, conn, tx))
                {
                    cmd.Parameters.Add("@Mold", SqlDbType.VarChar, 20).Value = map.MoldId;
                    cmd.ExecuteNonQuery();
                }
                using (var cmd = new SqlCommand("""
                    MERGE dbo.PR_ShotCount WITH (HOLDLOCK) AS t
                    USING (SELECT @Mold AS MoldID, CAST(SYSDATETIME() AS DATE) AS D) s
                       ON t.MoldID = s.MoldID AND t.RecordDate = s.D
                    WHEN MATCHED THEN UPDATE SET
                         ShotsAdded      = ISNULL(t.ShotsAdded,0) + 1,
                         CumulativeShots = (SELECT CumulativeShots FROM dbo.MD_Mold WHERE MoldID = @Mold),
                         RecordedAt      = SYSDATETIME()
                    WHEN NOT MATCHED THEN INSERT
                         (MoldID, RecordDate, ShotsAdded, CumulativeShots, RatedShots, RecordedAt, CreatedBy, CreatedTS)
                         VALUES (@Mold, s.D, 1,
                                 (SELECT CumulativeShots FROM dbo.MD_Mold WHERE MoldID = @Mold),
                                 (SELECT RatedShots      FROM dbo.MD_Mold WHERE MoldID = @Mold),
                                 SYSDATETIME(), 'AGENT', SYSDATETIME());
                    """, conn, tx))
                {
                    cmd.Parameters.Add("@Mold", SqlDbType.VarChar, 20).Value = map.MoldId;
                    cmd.ExecuteNonQuery();
                }
            }

            tx.Commit();
            return (lotId, lotCode);
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>
    /// 수동 실적 입력 (PLC 미연결·계수 누락 보정) — 입력 수량만큼 원천 LOT 을 만든다.
    /// 에이전트 CreateRawLot 과 같은 1 LOT = 1 PCS 원천 LOT 이며, 여기서 실적을 만들지는 않는다:
    /// "실적 확정(PR_ProductionResult 생성)은 반드시 스캔 경유" 라는 이 클래스의 불변식을
    /// 수동입력도 그대로 따른다 — 발행된 라벨을 스캔해야 ConfirmByLotCode 로 확정된다.
    /// 그래서 WoID 도 비워 둔다(확정 시점에 스캔한 WO 로 채워진다).
    /// 금형 타수는 PLC 샷카운터에서만 세므로 여기서는 건드리지 않는다.
    /// 반환 = 생성된 LOT 목록 (라벨 발행용).
    /// </summary>
    public List<InjLotDto> CreateManualRawLots(
        string lineId, string itemNo, string? moldId, int qty, string employeeNo)
    {
        var lots = new List<InjLotDto>();
        if (qty <= 0) return lots;

        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            // 품번 → 캐비티/색상 매핑. 금형 미지정 WO 는 NULL 로 남긴다.
            string? moldCode = null, colorCode = null, cavityPos = null;
            int?    cavityNo = null;
            if (!string.IsNullOrEmpty(moldId))
            {
                using var cmd = new SqlCommand("dbo.SP_POP_INJ_CreateManualRawLots_GetMoldItem", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure };
                cmd.Parameters.Add("@Mold", SqlDbType.VarChar, 20).Value = moldId;
                cmd.Parameters.Add("@Item", SqlDbType.VarChar, 20).Value = itemNo;
                using var rdr = cmd.ExecuteReader();
                if (rdr.Read())
                {
                    moldCode  = rdr["MoldCodeClean"] as string;
                    colorCode = rdr["Color"]         as string;
                    cavityNo  = rdr["CavitySeq"]     as int?;
                    cavityPos = rdr["CavityPos"]     as string;
                }
            }

            string? itemName = null;
            using (var cmd = new SqlCommand(
                "dbo.SP_POP_INJ_CreateManualRawLots_GetItem", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
            {
                cmd.Parameters.Add("@Item", SqlDbType.VarChar, 20).Value = itemNo;
                itemName = cmd.ExecuteScalar() as string;
            }

            string? equipId = null;
            using (var cmd = new SqlCommand("dbo.SP_POP_INJ_CreateManualRawLots_GetEquipment", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
            {
                cmd.Parameters.Add("@L", SqlDbType.VarChar, 20).Value = lineId;
                equipId = cmd.ExecuteScalar() as string;
            }

            for (var i = 0; i < qty; i++)
            {
                var lotCode = LotNoGenerator.NextLotNo(conn, tx, lineId, DbClock.Now);

                int lotId; DateTime createdTs;
                using (var cmd = new SqlCommand("dbo.SP_POP_INJ_CreateManualRawLots_InsLot", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
                {
                    cmd.Parameters.Add("@LotCode", SqlDbType.VarChar, 40).Value = lotCode;
                    cmd.Parameters.Add("@ItemNo",  SqlDbType.VarChar, 20).Value = itemNo;
                    cmd.Parameters.Add("@LineID",  SqlDbType.VarChar, 20).Value = lineId;
                    cmd.Parameters.Add("@By",      SqlDbType.VarChar, 20).Value = employeeNo;
                    using var rdr = cmd.ExecuteReader();
                    rdr.Read();
                    lotId     = (int)rdr["LotID"];
                    createdTs = (DateTime)rdr["CreatedTS"];
                }

                // MachineShotCount / PressType 은 PLC 값이라 수동 LOT 에는 없다. CreatedBy 가 발행한 사번이다(에이전트는 'AGENT').
                using (var cmd = new SqlCommand("dbo.SP_POP_INJ_CreateManualRawLots_InsInjLot", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
                {
                    cmd.Parameters.Add("@LotID",  SqlDbType.Int           ).Value = lotId;
                    cmd.Parameters.Add("@Equip",  SqlDbType.VarChar, 20   ).Value = (object?)equipId   ?? DBNull.Value;
                    cmd.Parameters.Add("@Mold",   SqlDbType.VarChar, 20   ).Value = (object?)moldCode  ?? DBNull.Value;
                    cmd.Parameters.Add("@Color",  SqlDbType.VarChar, 10   ).Value = (object?)colorCode ?? DBNull.Value;
                    cmd.Parameters.Add("@MoldID", SqlDbType.VarChar, 20   ).Value = (object?)moldId    ?? DBNull.Value;
                    cmd.Parameters.Add("@CavNo",  SqlDbType.Int           ).Value = (object?)cavityNo  ?? DBNull.Value;
                    cmd.Parameters.Add("@CavPos", SqlDbType.VarChar, 4    ).Value = (object?)cavityPos ?? DBNull.Value;
                    cmd.Parameters.Add("@By",     SqlDbType.VarChar,    20).Value = employeeNo;
                    cmd.ExecuteNonQuery();
                }

                lots.Add(new InjLotDto
                {
                    LotId         = lotId,
                    LotCode       = lotCode,
                    ItemNo        = itemNo,
                    ItemName      = itemName,
                    LineId        = lineId,
                    EquipId       = equipId,
                    MoldCode      = moldCode,
                    ColorCode     = colorCode,
                    MoldId        = moldId,
                    CavityNo      = cavityNo,
                    CavityPos     = cavityPos,
                    ConfirmStatus = "RAW",
                    PrintedCount  = 0,
                    CreatedTS     = createdTs,
                });
            }

            tx.Commit();
            return lots;
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>수동 LOT 을 만들 때 쓰는 품번·금형·설비 정보 — 오프라인 사본이 미리 받아 두고, 그 자리에서 LOT·라벨을 만든다.</summary>
    public sealed record ManualLotContext(string? ItemName, string? MoldCode, string? ColorCode, int? CavityNo, string? CavityPos, string? EquipId);

    public ManualLotContext GetManualContext(string lineId, string itemNo, string? moldId)
    {
        using var conn = _factory.OpenConnection();
        string? moldCode = null, colorCode = null, cavityPos = null;
        int? cavityNo = null;
        if (!string.IsNullOrEmpty(moldId))
        {
            using var cmd = new SqlCommand("dbo.SP_POP_INJ_CreateManualRawLots_GetMoldItem", conn) { CommandType = System.Data.CommandType.StoredProcedure };
            cmd.Parameters.Add("@Mold", SqlDbType.VarChar, 20).Value = moldId;
            cmd.Parameters.Add("@Item", SqlDbType.VarChar, 20).Value = itemNo;
            using var rdr = cmd.ExecuteReader();
            if (rdr.Read())
            {
                moldCode  = rdr["MoldCodeClean"] as string;
                colorCode = rdr["Color"]         as string;
                cavityNo  = rdr["CavitySeq"]     as int?;
                cavityPos = rdr["CavityPos"]     as string;
            }
        }
        string? itemName;
        using (var cmd = new SqlCommand("dbo.SP_POP_INJ_CreateManualRawLots_GetItem", conn) { CommandType = System.Data.CommandType.StoredProcedure })
        {
            cmd.Parameters.Add("@Item", SqlDbType.VarChar, 20).Value = itemNo;
            itemName = cmd.ExecuteScalar() as string;
        }
        string? equipId;
        using (var cmd = new SqlCommand("dbo.SP_POP_INJ_CreateManualRawLots_GetEquipment", conn) { CommandType = System.Data.CommandType.StoredProcedure })
        {
            cmd.Parameters.Add("@L", SqlDbType.VarChar, 20).Value = lineId;
            equipId = cmd.ExecuteScalar() as string;
        }
        return new ManualLotContext(itemName, moldCode, colorCode, cavityNo, cavityPos, equipId);
    }

    /// <summary>
    /// 오프라인 터미널이 쓸 LotNo 구간을 미리 확보한다(<see cref="LotNoGenerator.Reserve"/>) — 한 트랜잭션, 날짜·라인별.
    /// 확보한 번호는 SYS_LotSeq 에서 이미 빠져 있어 InjAgent·다른 터미널의 채번과 겹치지 않는다.
    /// </summary>
    /// <summary>오프라인 사본용 — 그 날·그 라인의 LotNo 헤더와 DB 의 마지막 순번(읽기만).</summary>
    public (string Prefix, string Header, int LastSeq) ReadLotNoCursor(string lineId, DateTime date)
    {
        using var conn = _factory.OpenConnection();
        return LotNoGenerator.ReadCursor(conn, lineId, date);
    }

    public LotNoGenerator.LotNoBlock ReserveLotNos(string lineId, DateTime date, int count)
    {
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            var block = LotNoGenerator.Reserve(conn, tx, lineId, date, count);
            tx.Commit();
            return block;
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>
    /// 오프라인에서 이미 번호를 받아 라벨까지 뽑은 수동 LOT 을 복구 뒤 DB 에 넣는다 — 번호·생성 시각은 그때 것 그대로.
    /// 이미 같은 LotCode 가 있으면(재전송) 건너뛴다. 라벨을 오프라인에서 뽑았으므로 PrintedCount 를 그 횟수로 넣어
    /// 라벨 디스패처가 복구 뒤 같은 LOT 을 또 뽑지 않게 한다. 반환 = 새로 만든 LOT.
    /// </summary>
    public List<InjLotDto> CreateManualRawLotsWithCodes(
        string lineId, string itemNo, string? moldId, string employeeNo,
        IReadOnlyList<(string LotCode, DateTime At, int PrintedCount)> lots)
    {
        var created = new List<InjLotDto>();
        if (lots.Count == 0) return created;

        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            string? moldCode = null, colorCode = null, cavityPos = null;
            int?    cavityNo = null;
            if (!string.IsNullOrEmpty(moldId))
            {
                using var cmd = new SqlCommand("dbo.SP_POP_INJ_CreateManualRawLots_GetMoldItem", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure };
                cmd.Parameters.Add("@Mold", SqlDbType.VarChar, 20).Value = moldId;
                cmd.Parameters.Add("@Item", SqlDbType.VarChar, 20).Value = itemNo;
                using var rdr = cmd.ExecuteReader();
                if (rdr.Read())
                {
                    moldCode  = rdr["MoldCodeClean"] as string;
                    colorCode = rdr["Color"]         as string;
                    cavityNo  = rdr["CavitySeq"]     as int?;
                    cavityPos = rdr["CavityPos"]     as string;
                }
            }

            string? itemName;
            using (var cmd = new SqlCommand("dbo.SP_POP_INJ_CreateManualRawLots_GetItem", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
            {
                cmd.Parameters.Add("@Item", SqlDbType.VarChar, 20).Value = itemNo;
                itemName = cmd.ExecuteScalar() as string;
            }

            string? equipId;
            using (var cmd = new SqlCommand("dbo.SP_POP_INJ_CreateManualRawLots_GetEquipment", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
            {
                cmd.Parameters.Add("@L", SqlDbType.VarChar, 20).Value = lineId;
                equipId = cmd.ExecuteScalar() as string;
            }

            foreach (var (lotCode, at, printed) in lots)
            {
                using (var exists = new SqlCommand("SELECT 1 FROM dbo.tbl_Lot WHERE LotCode = @Code;", conn, tx))
                {
                    exists.Parameters.Add("@Code", SqlDbType.VarChar, 40).Value = lotCode;
                    if (exists.ExecuteScalar() is not null) continue;
                }
                LotNoGenerator.EnsureAtLeast(conn, tx, lotCode);

                int lotId;
                using (var cmd = new SqlCommand("dbo.SP_POP_INJ_CreateManualRawLots_InsLot", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
                {
                    cmd.Parameters.Add("@LotCode", SqlDbType.VarChar, 40).Value = lotCode;
                    cmd.Parameters.Add("@ItemNo",  SqlDbType.VarChar, 20).Value = itemNo;
                    cmd.Parameters.Add("@LineID",  SqlDbType.VarChar, 20).Value = lineId;
                    cmd.Parameters.Add("@By",      SqlDbType.VarChar, 20).Value = employeeNo;
                    using var rdr = cmd.ExecuteReader();
                    rdr.Read();
                    lotId = (int)rdr["LotID"];
                }
                using (var cmd = new SqlCommand("dbo.SP_POP_INJ_CreateManualRawLots_InsInjLot", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
                {
                    cmd.Parameters.Add("@LotID",  SqlDbType.Int           ).Value = lotId;
                    cmd.Parameters.Add("@Equip",  SqlDbType.VarChar, 20   ).Value = (object?)equipId   ?? DBNull.Value;
                    cmd.Parameters.Add("@Mold",   SqlDbType.VarChar, 20   ).Value = (object?)moldCode  ?? DBNull.Value;
                    cmd.Parameters.Add("@Color",  SqlDbType.VarChar, 10   ).Value = (object?)colorCode ?? DBNull.Value;
                    cmd.Parameters.Add("@MoldID", SqlDbType.VarChar, 20   ).Value = (object?)moldId    ?? DBNull.Value;
                    cmd.Parameters.Add("@CavNo",  SqlDbType.Int           ).Value = (object?)cavityNo  ?? DBNull.Value;
                    cmd.Parameters.Add("@CavPos", SqlDbType.VarChar, 4    ).Value = (object?)cavityPos ?? DBNull.Value;
                    cmd.Parameters.Add("@By",     SqlDbType.VarChar,    20).Value = employeeNo;
                    cmd.ExecuteNonQuery();
                }

                // 프로시저는 생성 시각을 서버 시각으로 찍는다 — LOT 생성일이 현황·실적 집계의 기준이라 발생 시각으로 되돌린다.
                using (var cmd = new SqlCommand("""
                    UPDATE dbo.tbl_Lot SET CreatedTS = @At, ProducedAt = @At WHERE LotID = @Lot;
                    UPDATE dbo.PR_InjLot SET CreatedTS = @At, PrintedCount = @Pc WHERE LotID = @Lot;
                    """, conn, tx))
                {
                    cmd.Parameters.Add("@At",  SqlDbType.DateTime2).Value = at;
                    cmd.Parameters.Add("@Pc",  SqlDbType.Int      ).Value = printed;
                    cmd.Parameters.Add("@Lot", SqlDbType.Int      ).Value = lotId;
                    cmd.ExecuteNonQuery();
                }

                created.Add(new InjLotDto
                {
                    LotId = lotId, LotCode = lotCode, ItemNo = itemNo, ItemName = itemName, LineId = lineId,
                    EquipId = equipId, MoldCode = moldCode, ColorCode = colorCode, MoldId = moldId,
                    CavityNo = cavityNo, CavityPos = cavityPos, ConfirmStatus = "RAW", PrintedCount = printed, CreatedTS = at,
                });
            }

            tx.Commit();
            return created;
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>
    /// 오프라인 사본용 — 이 라인의 LOT 중 <paramref name="since"/> 이후에 생성됐거나 아직 확정 대기(RAW·NG_BLOCKED)인 것.
    /// 확정 대기 LOT 은 날짜와 무관하게 전부 담는다 — 오래된 라벨을 오프라인에서 스캔해도 알아볼 수 있어야 한다.
    /// </summary>
    public List<InjLotDto> ListForOffline(string lineId, DateTime since)
    {
        var sql = SelectLotView + """

            WHERE  l.LineID = @Line
              AND (l.CreatedTS >= @Since OR e.ConfirmStatus IN ('RAW','NG_BLOCKED'))
            ORDER  BY l.LotID DESC;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandTimeout = 120 };
        cmd.Parameters.Add("@Line",  SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@Since", SqlDbType.DateTime2   ).Value = since;
        using var rdr = cmd.ExecuteReader();
        var list = new List<InjLotDto>();
        while (rdr.Read()) list.Add(MapToDto(rdr));
        return list;
    }

    // 호출부가 WHERE 를 이어 붙인다 — 이 상수에 WHERE 를 넣으면 4곳이 동시에 깨진다.
    const string SelectLotView = """
        SELECT l.LotID, l.LotCode, l.ItemNo, mi.ItemName, l.LineID,
               e.EquipID, e.MoldCode, e.ColorCode, e.MoldID, e.CavityNo, e.CavityPos,
               e.PressType, e.ConfirmStatus, e.MachineShotCount, e.PrintedCount, l.CreatedTS,
               ri.OverallNg,
               CASE WHEN ri.InspectionID IS NULL THEN NULL ELSE
                 LTRIM(STUFF(
                   CASE WHEN ri.ShortMold = 'NG' THEN N', 미성형 NG'   ELSE N'' END +
                   CASE WHEN ri.WeldLine  = 'NG' THEN N', 웰드라인 NG' ELSE N'' END +
                   CASE WHEN ri.Gas       = 'NG' THEN N', 가스 NG'     ELSE N'' END +
                   CASE WHEN ri.Weight    = 'NG' THEN N', 중량 NG'     ELSE N'' END,
                 1, 1, N'')) END AS InspectionSummary
        FROM   dbo.tbl_Lot l
        JOIN   dbo.PR_InjLot e ON e.LotID = l.LotID
        LEFT   JOIN dbo.MD_Item mi ON mi.ItemNo = l.ItemNo
        OUTER  APPLY (SELECT TOP 1 * FROM dbo.PR_RobotInspection r
                      WHERE r.LotID = l.LotID ORDER BY r.InspectionID DESC) ri
        """;

    static InjLotDto MapToDto(SqlDataReader rdr) => new()
    {
        LotId             = (int)rdr["LotID"],
        LotCode           = (string)rdr["LotCode"],
        ItemNo            = rdr["ItemNo"]    as string ?? string.Empty,
        ItemName          = rdr["ItemName"]  as string,
        LineId            = rdr["LineID"]    as string,
        EquipId           = rdr["EquipID"]   as string,
        MoldCode          = rdr["MoldCode"]  as string,
        ColorCode         = rdr["ColorCode"] as string,
        MoldId            = rdr["MoldID"]    as string,
        CavityNo          = rdr["CavityNo"]  as int?,
        CavityPos         = rdr["CavityPos"] as string,
        PressType         = rdr["PressType"] as string,
        ConfirmStatus     = (string)rdr["ConfirmStatus"],
        MachineShotCount  = rdr["MachineShotCount"] as long?,
        PrintedCount      = (int)rdr["PrintedCount"],
        CreatedTS         = rdr["CreatedTS"] as DateTime? ?? default,
        OverallNg         = rdr["OverallNg"] as bool?,
        InspectionSummary = rdr["InspectionSummary"] as string,
    };

    /// <summary>Inj04 스캔 대기 목록 — RAW + NG_BLOCKED (차단 사유 표시용) 최신순.</summary>
    public List<InjLotDto> GetUnconfirmed(string lineId, int top = 30)
    {
        var sql = "dbo.SP_POP_INJ_GetUnconfirmed";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@Line", SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@Top",  SqlDbType.Int        ).Value = top;
        using var rdr = cmd.ExecuteReader();
        var list = new List<InjLotDto>();
        while (rdr.Read()) list.Add(MapToDto(rdr));
        return list;
    }

    /// <summary>라벨 재출력용 단건 조회.</summary>
    public InjLotDto? GetByLotCode(string lotCode)
    {
        var sql = "dbo.SP_POP_INJ_GetByLotCode";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@Code", SqlDbType.VarChar, 40).Value = lotCode;
        using var rdr = cmd.ExecuteReader();
        return rdr.Read() ? MapToDto(rdr) : null;
    }

    /// <summary>라벨 발행 성공 시 +1 (디스패처 자동 발행·재출력 버튼 공용). 반환 = 누적 횟수.</summary>
    public int IncrementPrintedCount(int lotId, string? employeeNo)
    {
        const string sql = "dbo.SP_POP_INJ_IncrementPrintedCount";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@L", SqlDbType.Int).Value = lotId;
        cmd.Parameters.Add("@By", SqlDbType.VarChar,   20).Value = (object?)employeeNo ?? DBNull.Value;
        return cmd.ExecuteScalar() as int? ?? 0;
    }

    /// <summary>출력이 실패했을 때 선행 카운트를 되돌린다. 0 이하로 내려가지 않는다(프로시저 가드).</summary>
    public int DecrementPrintedCount(int lotId, string? employeeNo)
    {
        const string sql = "dbo.SP_POP_INJ_DecrementPrintedCount";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@L", SqlDbType.Int).Value = lotId;
        cmd.Parameters.Add("@By", SqlDbType.VarChar,   20).Value = (object?)employeeNo ?? DBNull.Value;
        return cmd.ExecuteScalar() as int? ?? 0;
    }

    /// <summary>
    /// 라벨 디스패처 워터마크 — 세션 시작 시점의 라인 최대 INJ LotID.
    /// ProcessCode 로 좁히는 이유: 같은 라인의 비-INJ LOT 이 워터마크를 실제 최신 사출 LOT
    /// 너머로 밀면 그 사이 미출력분이 영구 제외된다(워터마크는 세션 중 전진하지 않는다).
    /// </summary>
    public int GetMaxLotId(string lineId)
    {
        const string sql = "dbo.SP_POP_INJ_GetMaxLotId";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@Line", SqlDbType.VarChar, 20).Value = lineId;
        return cmd.ExecuteScalar() as int? ?? 0;
    }

    /// <summary>
    /// 미출력 LOT 을 원자적으로 선점하고 라벨 조립용 전체 데이터를 돌려준다.
    /// 같은 라인에 Pop 터미널이 여러 대여도 한 터미널만 각 LOT 을 가져간다.
    /// staleSeconds 가 지난 선점은 회수 대상 — 선점 직후 터미널이 죽어도 라벨이 유실되지 않는다.
    ///
    /// 불변식: staleSeconds > top × 프린터 최악 지연(5초 = ZplPrinter 연결 2초 + 송신 3초).
    /// 이 관계가 깨지면 첫 터미널이 배치를 처리하는 중에 다른 터미널이 뒷부분을 회수해
    /// 같은 라벨이 두 장 나간다. 현재 값: 60 > 5 × 5 = 25.
    ///
    /// 호출측 계약 — 선점만 하므로 뒤처리는 호출자 책임이다:
    ///   출력 성공 → IncrementPrintedCount(lotId)
    ///   출력 실패 → ReleasePrintClaim(lotId, stationId)
    /// 둘 다 안 부르면 staleSeconds 후 자동 재시도된다(= 라벨이 한 장 더 나간다).
    /// </summary>
    public List<InjLotDto> ClaimForPrint(string lineId, int afterLotId, string stationId,
                                         int staleSeconds = 60, int top = 5)
    {
        // ModifiedTS 는 찍지 않는다 — 선점은 디스패처 내부 상태이지 업무 변경이 아니다.
        var sql = "dbo.SP_POP_INJ_ClaimForPrint";

        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@Line",    SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@After",   SqlDbType.Int        ).Value = afterLotId;
        cmd.Parameters.Add("@Station", SqlDbType.VarChar, 20).Value = stationId;
        cmd.Parameters.Add("@Stale",   SqlDbType.Int        ).Value = staleSeconds;
        cmd.Parameters.Add("@Top",     SqlDbType.Int        ).Value = top;
        using var rdr = cmd.ExecuteReader();
        var list = new List<InjLotDto>();
        while (rdr.Read()) list.Add(MapToDto(rdr));
        return list;
    }

    /// <summary>출력 실패 시 선점 반납 — 다음 틱에 재시도된다.</summary>
    public void ReleasePrintClaim(int lotId, string stationId)
    {
        // 내가 선점한 것만 반납한다. 스테일 회수로 다른 터미널이 이미 가져간 LOT 을
        // 뒤늦은 실패 보고가 풀어버리면 그 터미널이 출력 중인 라벨이 재선점된다.
        const string sql = "dbo.SP_POP_INJ_ReleasePrintClaim";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@L",       SqlDbType.Int        ).Value = lotId;
        cmd.Parameters.Add("@Station", SqlDbType.VarChar, 20).Value = stationId;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 스캔 확정: RAW → CONFIRMED + PR_ProductionResult 생성 + 단계 실적 +1(WorkOrderRepository.BumpStepCompleted).
    /// 대상은 호출자가 아니라 이 라인 · LOT 품번으로 정한 단계(PP_WorkOrderRouting) 행이다 —
    /// 같은 라인에서 해당 품번을 만드는 단계 중 빠른순(단계 Status In Progress 우선 →
    /// 헤더 Priority → DueDate → WoID) 첫 건. 없으면 WO 없이 확정한다(WoID NULL, 단계 반영 없음 — 2026-10-01 사용자 결정).
    /// 이 조회는 단계 행만 잠근다(UPDLOCK, ROWLOCK) — 헤더 잠금은 BumpStepCompleted 가
    /// 단계 → 헤더 순으로 다시 잡으므로, 여기서 헤더까지 같이 잠그면 RecordCycle 과
    /// 잠금 순서가 엇갈려 교착 가능성이 생긴다.
    /// CycleSec = 같은 설비의 직전 원천 LOT 과 이 LOT 의 CreatedTS 차 (샷 발생 시각 기준).
    /// </summary>
    public (InjConfirmOutcome Outcome, int ResultId, string ItemNo, int WoId) ConfirmByLotCode(
        string lotCode, string lineId, string operatorId, int? sessionId, string employeeNo)
        => ConfirmByLotCode(lotCode, lineId, operatorId, sessionId, employeeNo, null, out _);

    public (InjConfirmOutcome Outcome, int ResultId, string ItemNo, int WoId) ConfirmByLotCode(
        string lotCode, string lineId, string operatorId, int? sessionId, string employeeNo, out BackflushResult backflush)
        => ConfirmByLotCode(lotCode, lineId, operatorId, sessionId, employeeNo, null, out backflush);

    /// <param name="palletNo">
    /// POP 이 먼저 스캔한 팔레트(2026-10-21) — 있으면 같은 트랜잭션에서 <see cref="PalletStockRepository.Load"/> 로 WH_Inventory 에 싣고,
    /// 팔레트가 없거나·못 쓰거나·혼적·가득 참이면 확정하지 않고 Pallet* 결과로 돌려준다. null 이면 종전대로 재고 없이 확정한다(테스트·다른 호출자).
    /// </param>
    /// <param name="backflush">같은 트랜잭션에서 돈 BOM 자동 차감 결과 — 확정되지 않으면 <see cref="BackflushResult.None"/>.</param>
    public (InjConfirmOutcome Outcome, int ResultId, string ItemNo, int WoId) ConfirmByLotCode(
        string lotCode, string lineId,
        string operatorId, int? sessionId, string employeeNo, string? palletNo, out BackflushResult backflush)
        => ConfirmByLotCode(lotCode, lineId, operatorId, sessionId, employeeNo, palletNo, null, out backflush);

    /// <param name="occurredAt">
    /// 오프라인에서 일어난 확정을 복구 뒤에 반영할 때 그 발생 시각 — 실적의 EntryAt·전기일·교대가 이 시각 기준이 된다.
    /// 비우면 종전대로 서버 시각.
    /// </param>
    public (InjConfirmOutcome Outcome, int ResultId, string ItemNo, int WoId) ConfirmByLotCode(
        string lotCode, string lineId,
        string operatorId, int? sessionId, string employeeNo, string? palletNo, DateTime? occurredAt, out BackflushResult backflush)
    {
        backflush = BackflushResult.None;
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            int lotId; string itemNo, status; string? equipId, moldId, cavityPos; DateTime createdTs;
            using (var cmd = new SqlCommand("dbo.SP_POP_INJ_ConfirmByLotCode_GetLot", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
            {
                cmd.Parameters.Add("@Code", SqlDbType.VarChar, 40).Value = lotCode;
                using var rdr = cmd.ExecuteReader();
                if (!rdr.Read()) { rdr.Close(); tx.Rollback(); return (InjConfirmOutcome.NotFound, 0, string.Empty, 0); }
                lotId     = (int)rdr["LotID"];
                itemNo    = rdr["ItemNo"] as string ?? string.Empty;
                status    = (string)rdr["ConfirmStatus"];
                equipId   = rdr["EquipID"] as string;
                moldId    = rdr["MoldID"]  as string;
                cavityPos = rdr["CavityPos"] as string;
                createdTs = (DateTime)rdr["CreatedTS"];
                var lotLine = rdr["LineID"] as string;
                if (!string.Equals(lotLine, lineId, StringComparison.OrdinalIgnoreCase))
                { rdr.Close(); tx.Rollback(); return (InjConfirmOutcome.WrongLine, 0, itemNo, 0); }
            }

            switch (LotDefectRules.ConfirmBlock(status))
            {
                case LotConfirmBlock.AlreadyConfirmed: tx.Rollback(); return (InjConfirmOutcome.AlreadyConfirmed, 0, itemNo, 0);
                case LotConfirmBlock.NgBlocked:        tx.Rollback(); return (InjConfirmOutcome.NgBlocked,        0, itemNo, 0);
                case LotConfirmBlock.InRework:         tx.Rollback(); return (InjConfirmOutcome.InRework,         0, itemNo, 0);
                case LotConfirmBlock.Scrapped:         tx.Rollback(); return (InjConfirmOutcome.Scrapped,         0, itemNo, 0);
            }

            // 팔레트 적재 — 못 실으면 확정도 하지 않는다(확정됐는데 팔레트에 없는 제품이 생기지 않게)
            if (palletNo is not null)
            {
                var load = PalletStockRepository.Load(conn, tx, lotId, lotCode, itemNo, palletNo, operatorId, employeeNo);
                if (load != PalletLoadOutcome.Ok) { tx.Rollback(); return (PalletOutcome(load), 0, itemNo, 0); }
            }

            // WO 가 없어도 생산은 막지 않는다(2026-10-01) — 열린 단계가 없으면 실적·LOT 의 WoID 는 NULL, 단계 반영 없음.
            int? woId = null, stepId = null;
            using (var cmd = new SqlCommand("dbo.SP_POP_INJ_ConfirmByLotCode_GetWorkOrderRouting", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
            {
                cmd.Parameters.Add("@Line", SqlDbType.VarChar, 20).Value = lineId;
                cmd.Parameters.Add("@Item", SqlDbType.VarChar, 20).Value = itemNo;
                using var rdr = cmd.ExecuteReader();
                if (rdr.Read()) { woId = (int)rdr["WoID"]; stepId = (int)rdr["RoutingLineID"]; }
            }

            int cycleSec;
            using (var cmd = new SqlCommand("dbo.SP_POP_INJ_ConfirmByLotCode_GetLot2", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
            {
                cmd.Parameters.Add("@ThisTs", SqlDbType.DateTime2   ).Value = createdTs;
                cmd.Parameters.Add("@Equip",  SqlDbType.VarChar, 20 ).Value = (object?)equipId ?? DBNull.Value;
                cmd.Parameters.Add("@Pos",    SqlDbType.VarChar, 4  ).Value = (object?)cavityPos ?? DBNull.Value;
                cycleSec = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
                if (cycleSec is < 0 or > 86400) cycleSec = 0;
            }

            // 전기일·교대는 설정 DAY_CUTOFF_TIME·공통코드 WORK_SHIFT로 확정 시점 서버 시각에 판정
            var (now, prodDate, shiftCode) = ProdCalendar.ResolveNow(conn, tx, occurredAt);
            int resultId;
            using (var cmd = new SqlCommand("dbo.SP_POP_INJ_ConfirmByLotCode_InsProductionResult", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
            {
                var entryNo = $"E{now:yyMMddHHmmssfff}-{lineId}";
                if (entryNo.Length > 28) entryNo = entryNo[..28];
                cmd.Parameters.Add("@EntryNo", SqlDbType.VarChar, 28  ).Value = entryNo;
                cmd.Parameters.Add("@Now",      SqlDbType.DateTime2    ).Value = now;
                cmd.Parameters.Add("@ProdDate", SqlDbType.Date         ).Value = prodDate;
                cmd.Parameters.Add("@Shift",    SqlDbType.VarChar, 10  ).Value = (object?)shiftCode ?? DBNull.Value;
                cmd.Parameters.Add("@WoID",    SqlDbType.Int          ).Value = (object?)woId ?? DBNull.Value;
                cmd.Parameters.Add("@LotID",   SqlDbType.Int          ).Value = lotId;
                cmd.Parameters.Add("@LineID",  SqlDbType.VarChar, 20  ).Value = lineId;
                cmd.Parameters.Add("@CT",      SqlDbType.Int          ).Value = cycleSec;
                cmd.Parameters.Add("@Mold",    SqlDbType.VarChar, 20  ).Value = (object?)moldId ?? DBNull.Value;
                cmd.Parameters.Add("@Op",      SqlDbType.NVarChar, 450).Value = operatorId;
                cmd.Parameters.Add("@Sess",    SqlDbType.Int          ).Value = (object?)sessionId ?? DBNull.Value;
                cmd.Parameters.Add("@By",      SqlDbType.VarChar,   20).Value = employeeNo;
                resultId = (int)cmd.ExecuteScalar()!;
            }

            using (var cmd = new SqlCommand("dbo.SP_POP_INJ_ConfirmByLotCode_UpdLot", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
            {
                cmd.Parameters.Add("@WoID",  SqlDbType.Int          ).Value = (object?)woId ?? DBNull.Value;
                cmd.Parameters.Add("@LotID", SqlDbType.Int          ).Value = lotId;
                cmd.Parameters.Add("@Op",    SqlDbType.VarChar,   20).Value = operatorId;
                cmd.Parameters.Add("@Sess",  SqlDbType.Int          ).Value = (object?)sessionId ?? DBNull.Value;
                cmd.ExecuteNonQuery();
            }

            // BOM 자동 차감 — 실패해도 확정은 커밋된다(세이브포인트). 단계 반영보다 앞에 두어 WO 유무와 무관하게 돈다.
            backflush = BackflushRepository.Apply(conn, tx, lotId, lotCode, itemNo, lineId, operatorId, employeeNo);

            if (stepId is int step) WorkOrderRepository.BumpStepCompleted(conn, tx, step, 1m, operatorId);

            tx.Commit();
            return (InjConfirmOutcome.Confirmed, resultId, itemNo, woId ?? 0);
        }
        catch { tx.Rollback(); throw; }
    }

    static InjConfirmOutcome PalletOutcome(PalletLoadOutcome o) => o switch
    {
        PalletLoadOutcome.NotFound => InjConfirmOutcome.PalletNotFound,
        PalletLoadOutcome.Mixed    => InjConfirmOutcome.PalletMixed,
        PalletLoadOutcome.Full     => InjConfirmOutcome.PalletFull,
        _                          => InjConfirmOutcome.PalletUnavailable,
    };

    /// <summary>로봇 검사 판정 저장 (에이전트).</summary>
    public void SaveInspection(int lotId, string equipId, string cavityPos,
        string shortMold, string weldLine, string gas, string weight, bool overallNg)
    {
        const string sql = """
            INSERT INTO dbo.PR_RobotInspection
                (LotID, EquipID, CavityPos, ShortMold, WeldLine, Gas, Weight,
                 OverallNg, ReceivedAt, CreatedBy, CreatedTS)
            VALUES
                (@Lot, @Equip, @Pos, @P1, @P2, @P3, @P4, @Ng, SYSDATETIME(), 'AGENT', SYSDATETIME());
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Lot",   SqlDbType.Int       ).Value = lotId;
        cmd.Parameters.Add("@Equip", SqlDbType.VarChar,20).Value = equipId;
        cmd.Parameters.Add("@Pos",   SqlDbType.VarChar, 4).Value = cavityPos;
        cmd.Parameters.Add("@P1",    SqlDbType.VarChar, 4).Value = shortMold;
        cmd.Parameters.Add("@P2",    SqlDbType.VarChar, 4).Value = weldLine;
        cmd.Parameters.Add("@P3",    SqlDbType.VarChar, 4).Value = gas;
        cmd.Parameters.Add("@P4",    SqlDbType.VarChar, 4).Value = weight;
        cmd.Parameters.Add("@Ng",    SqlDbType.Bit       ).Value = overallNg;
        cmd.ExecuteNonQuery();
    }

    /// <summary>로봇 NG 수신 → 스캔 차단 (RAW 인 경우에만).</summary>
    public void MarkNgBlocked(int lotId)
    {
        const string sql = """
            BEGIN TRAN;
            UPDATE dbo.PR_InjLot
            SET    ConfirmStatus = 'NG_BLOCKED', ModifiedBy = 'AGENT', ModifiedTS = SYSDATETIME()
            WHERE  LotID = @Lot AND ConfirmStatus = 'RAW';
            IF @@ROWCOUNT = 1
              UPDATE dbo.tbl_Lot
              SET    QualityFlag = 'NG', ModifiedBy = 'AGENT', ModifiedTS = SYSDATETIME()
              WHERE  LotID = @Lot;
            COMMIT;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Lot", SqlDbType.Int).Value = lotId;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 라인 불량 팝업의 LOT 스캔 등록 — 한 트랜잭션으로:
    ///   ① LOT 잠금·라인 검증·상태 검사(LotDefectRules) → ② CONFIRMED 였으면 실적 역분개(−1)·단계 −1
    ///   → ③ PR_DefectDetail 1행 (Disposition NULL = 재작업 대기) → ④ LOT DEFECT + tbl_Lot QualityFlag NG.
    /// RAW·NG_BLOCKED 는 실적이 없으므로 WO 는 열린 단계로 해석만 하고(없으면 NULL) 수리 시점에 다시 정한다.
    /// 로봇 NG(NG_BLOCKED) 는 검사 요약을 ReasonNote 에 남긴다.
    /// </summary>
    public (DefectRegisterOutcome Outcome, int DefectId, string ItemNo) RegisterDefect(
        string lotCode, string lineId, string defectCode,
        string operatorId, int? sessionId, string employeeNo)
        => RegisterDefect(lotCode, lineId, defectCode, operatorId, sessionId, employeeNo, out _);

    /// <param name="unload">팔레트에 실려 있던 LOT 이면 같은 트랜잭션에서 재고 행을 지운 결과(<see cref="PalletStockRepository.Unload"/>) — 위치가 있던 행이면 화면이 경고한다.</param>
    public (DefectRegisterOutcome Outcome, int DefectId, string ItemNo) RegisterDefect(
        string lotCode, string lineId, string defectCode,
        string operatorId, int? sessionId, string employeeNo,
        out PalletStockRepository.PalletUnloadResult unload)
        => RegisterDefect(lotCode, lineId, defectCode, operatorId, sessionId, employeeNo, null, out unload);

    /// <param name="occurredAt">오프라인에서 등록한 불량을 복구 뒤에 반영할 때 그 발생 시각 — 역분개 실적·불량 상세의 시각이 이 시각이 된다.</param>
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
            int lotId; string itemNo, status; string? inspection;
            using (var cmd = new SqlCommand("dbo.SP_POP_INJ_RegisterDefect_GetLot", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
            {
                cmd.Parameters.Add("@Code", SqlDbType.VarChar, 40).Value = lotCode;
                using var rdr = cmd.ExecuteReader();
                if (!rdr.Read()) { rdr.Close(); tx.Rollback(); return (DefectRegisterOutcome.NotFound, 0, string.Empty); }
                lotId      = (int)rdr["LotID"];
                itemNo     = rdr["ItemNo"] as string ?? string.Empty;
                status     = (string)rdr["ConfirmStatus"];
                inspection = rdr["InspectionSummary"] as string;
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
                (origResultId, reversalId, woId) = LotDefectWriter.ReverseConfirmedResult(conn, tx, lotId, lineId, operatorId, sessionId, employeeNo, occurredAt);
            woId ??= LotDefectWriter.ResolveOpenWo(conn, tx, lineId, itemNo)?.WoId;

            var note = status == LotDefectRules.NgBlocked ? $"ROBOT NG: {inspection ?? "NG"}" : null;
            var defectId = LotDefectWriter.InsertDefectDetail(conn, tx, lotId, woId, origResultId, "INJ", defectCode,
                                                              status, reversalId, note, operatorId, employeeNo, occurredAt);

            using (var cmd = new SqlCommand("dbo.SP_POP_INJ_RegisterDefect_UpdInjLot", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
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

    /// <summary>Inj02 카드용: 오늘 샷수(캐비티1 기준) / 미확정 / NG 차단 카운트.</summary>
    public (int TodayShots, int RawCount, int NgBlocked) GetTodayStats(string lineId)
    {
        const string sql = "dbo.SP_POP_INJ_GetTodayStats";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@Line", SqlDbType.VarChar, 20).Value = lineId;
        using var rdr = cmd.ExecuteReader();
        if (!rdr.Read()) return (0, 0, 0);
        return (Convert.ToInt32(rdr["TodayShots"]), Convert.ToInt32(rdr["RawCount"]), Convert.ToInt32(rdr["NgBlocked"]));
    }

    /// <summary>
    /// INJ-MAIN 좌측 패널: 스테이션 BOP 품번 ∪ 그 날 실적/일정이 있는 품번의 지정일 현황.
    /// 모든 수치는 LOT 생성일이 지정일인 것만 센다 — 확정 시각 기준으로 하면
    /// 어제 생성·오늘 확정 LOT 이 INPUT 과 FINAL 에 다른 날로 잡혀 항등식이 깨진다.
    /// HasOpenWo 는 날짜와 무관한 현재 상태다.
    /// PLAN·HasOpenWo 는 단계 생산 품번(COALESCE(PP_WorkOrderRouting.ItemNo, WO 품번)) 기준 — INJ 스테이션은 코어 품번으로 센다.
    /// 전부 LOT 상태로 센다: FINAL = CONFIRMED, NG = NG_BLOCKED + DEFECT + SCRAPPED, 미확정 = RAW.
    /// PR_DefectDetail 은 읽지 않는다 — 불량은 LOT 상태에 이미 반영돼 있어 더하면 이중 계상이다.
    /// </summary>
    public List<InjItemDailyDto> GetDailyItemSummary(string lineId, string stationCode, DateTime date)
    {
        const string sql = "dbo.SP_POP_INJ_GetDailyItemSummary";
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
