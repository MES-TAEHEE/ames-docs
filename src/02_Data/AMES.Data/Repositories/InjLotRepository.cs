using System.Data;
using AMES.Contracts.Dto;
using AMES.Contracts.Enums;
using AMES.Data.Connection;
using AMES.Data.Services;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// 사출 원천 LOT (tbl_Lot 'RAW' + PR_InjLot) 의 생성·확정·NG 전이.
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
            var lotCode = LotNoGenerator.NextLotNo(conn, tx, lineId, DateTime.Now);
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
                using var cmd = new SqlCommand("""
                    SELECT TOP 1 m.MoldCodeClean, mi.Color, mi.CavitySeq, mi.CavityPos
                    FROM   dbo.MD_MoldItem mi
                    JOIN   dbo.MD_Mold m ON m.MoldID = mi.MoldID
                    WHERE  mi.MoldID = @Mold AND mi.ItemNo = @Item AND mi.ActiveFlag = 1
                    ORDER  BY mi.CavitySeq;
                    """, conn, tx);
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
                "SELECT ItemName FROM dbo.MD_Item WHERE ItemNo = @Item;", conn, tx))
            {
                cmd.Parameters.Add("@Item", SqlDbType.VarChar, 20).Value = itemNo;
                itemName = cmd.ExecuteScalar() as string;
            }

            string? equipId = null;
            using (var cmd = new SqlCommand("""
                SELECT TOP 1 EquipID FROM dbo.MD_Equipment
                WHERE  LineID = @L AND ISNULL(ActiveFlag,1) = 1
                ORDER  BY EquipID;
                """, conn, tx))
            {
                cmd.Parameters.Add("@L", SqlDbType.VarChar, 20).Value = lineId;
                equipId = cmd.ExecuteScalar() as string;
            }

            for (var i = 0; i < qty; i++)
            {
                var lotCode = LotNoGenerator.NextLotNo(conn, tx, lineId, DateTime.Now);

                int lotId; DateTime createdTs;
                using (var cmd = new SqlCommand("""
                    INSERT INTO dbo.tbl_Lot
                        (LotCode, ItemNo, WoID, LineID, ProcessCode, BatchSize, RemainingQty,
                         ProducedAt, Status, QualityFlag, CreatedBy, CreatedTS)
                    OUTPUT INSERTED.LotID, INSERTED.CreatedTS
                    VALUES
                        (@LotCode, @ItemNo, NULL, @LineID, 'INJ', 1, 1,
                         SYSDATETIME(), 'RAW', 'PENDING', @By, SYSDATETIME());
                    """, conn, tx))
                {
                    cmd.Parameters.Add("@LotCode", SqlDbType.VarChar, 40).Value = lotCode;
                    cmd.Parameters.Add("@ItemNo",  SqlDbType.VarChar, 20).Value = itemNo;
                    cmd.Parameters.Add("@LineID",  SqlDbType.VarChar, 20).Value = lineId;
                    cmd.Parameters.Add("@By",      SqlDbType.VarChar, 50).Value = employeeNo;
                    using var rdr = cmd.ExecuteReader();
                    rdr.Read();
                    lotId     = (int)rdr["LotID"];
                    createdTs = (DateTime)rdr["CreatedTS"];
                }

                // MachineShotCount / PressType 은 PLC 값이라 수동 LOT 에는 없다. CreatedBy 로 출처를 남긴다.
                using (var cmd = new SqlCommand("""
                    INSERT INTO dbo.PR_InjLot
                        (LotID, EquipID, MoldCode, ColorCode, MoldID, CavityNo, CavityPos,
                         PressType, MachineShotCount, ConfirmStatus, CreatedBy, CreatedTS)
                    VALUES
                        (@LotID, @Equip, @Mold, @Color, @MoldID, @CavNo, @CavPos,
                         NULL, NULL, 'RAW', 'MANUAL', SYSDATETIME());
                    """, conn, tx))
                {
                    cmd.Parameters.Add("@LotID",  SqlDbType.Int           ).Value = lotId;
                    cmd.Parameters.Add("@Equip",  SqlDbType.VarChar, 20   ).Value = (object?)equipId   ?? DBNull.Value;
                    cmd.Parameters.Add("@Mold",   SqlDbType.VarChar, 20   ).Value = (object?)moldCode  ?? DBNull.Value;
                    cmd.Parameters.Add("@Color",  SqlDbType.VarChar, 10   ).Value = (object?)colorCode ?? DBNull.Value;
                    cmd.Parameters.Add("@MoldID", SqlDbType.VarChar, 20   ).Value = (object?)moldId    ?? DBNull.Value;
                    cmd.Parameters.Add("@CavNo",  SqlDbType.Int           ).Value = (object?)cavityNo  ?? DBNull.Value;
                    cmd.Parameters.Add("@CavPos", SqlDbType.VarChar, 4    ).Value = (object?)cavityPos ?? DBNull.Value;
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
        var sql = SelectLotView + """

            WHERE  l.LineID = @Line AND e.ConfirmStatus IN ('RAW','NG_BLOCKED')
            ORDER  BY l.CreatedTS DESC
            OFFSET 0 ROWS FETCH NEXT @Top ROWS ONLY;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Line", SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@Top",  SqlDbType.Int        ).Value = top;
        using var rdr = cmd.ExecuteReader();
        var list = new List<InjLotDto>();
        while (rdr.Read()) list.Add(MapToDto(rdr));
        return list;
    }

    /// <summary>Inj05 로봇 NG 목록 — 수동 불량 확정 대기.</summary>
    public List<InjLotDto> GetNgBlocked(string lineId)
    {
        var sql = SelectLotView + """

            WHERE  l.LineID = @Line AND e.ConfirmStatus = 'NG_BLOCKED'
            ORDER  BY l.CreatedTS DESC;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Line", SqlDbType.VarChar, 20).Value = lineId;
        using var rdr = cmd.ExecuteReader();
        var list = new List<InjLotDto>();
        while (rdr.Read()) list.Add(MapToDto(rdr));
        return list;
    }

    /// <summary>라벨 재출력용 단건 조회.</summary>
    public InjLotDto? GetByLotCode(string lotCode)
    {
        var sql = SelectLotView + """

            WHERE  l.LotCode = @Code;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Code", SqlDbType.VarChar, 40).Value = lotCode;
        using var rdr = cmd.ExecuteReader();
        return rdr.Read() ? MapToDto(rdr) : null;
    }

    /// <summary>라벨 발행 성공 시 +1 (디스패처 자동 발행·재출력 버튼 공용). 반환 = 누적 횟수.</summary>
    public int IncrementPrintedCount(int lotId)
    {
        const string sql = """
            UPDATE dbo.PR_InjLot
            SET    PrintedCount = PrintedCount + 1, ModifiedTS = SYSDATETIME()
            OUTPUT INSERTED.PrintedCount
            WHERE  LotID = @L;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@L", SqlDbType.Int).Value = lotId;
        return cmd.ExecuteScalar() as int? ?? 0;
    }

    /// <summary>
    /// 라벨 디스패처 워터마크 — 세션 시작 시점의 라인 최대 INJ LotID.
    /// ProcessCode 로 좁히는 이유: 같은 라인의 비-INJ LOT 이 워터마크를 실제 최신 사출 LOT
    /// 너머로 밀면 그 사이 미출력분이 영구 제외된다(워터마크는 세션 중 전진하지 않는다).
    /// </summary>
    public int GetMaxLotId(string lineId)
    {
        const string sql = "SELECT ISNULL(MAX(LotID),0) FROM dbo.tbl_Lot WHERE LineID = @Line AND ProcessCode = 'INJ';";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
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
        var sql = """
            DECLARE @claimed TABLE (LotID INT);

            UPDATE TOP (@Top) e
            SET    e.PrintClaimTS = SYSDATETIME(), e.PrintClaimStation = @Station
            OUTPUT INSERTED.LotID INTO @claimed
            FROM   dbo.PR_InjLot e
            JOIN   dbo.tbl_Lot   l ON l.LotID = e.LotID
            WHERE  l.LineID       = @Line
              AND  e.LotID        > @After
              AND  e.PrintedCount = 0
              AND  (e.PrintClaimTS IS NULL
                    OR e.PrintClaimTS < DATEADD(second, -@Stale, SYSDATETIME()));

            """ + SelectLotView + """

            WHERE  l.LotID IN (SELECT LotID FROM @claimed)
            ORDER  BY l.LotID;
            """;

        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
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
        const string sql = """
            UPDATE dbo.PR_InjLot
            SET    PrintClaimTS = NULL, PrintClaimStation = NULL
            WHERE  LotID = @L AND PrintedCount = 0 AND PrintClaimStation = @Station;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@L",       SqlDbType.Int        ).Value = lotId;
        cmd.Parameters.Add("@Station", SqlDbType.VarChar, 20).Value = stationId;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 스캔 확정: RAW → CONFIRMED + PR_ProductionResult 생성 + 단계 실적 +1(WorkOrderRepository.BumpStepCompleted).
    /// 대상은 호출자가 아니라 이 라인 · LOT 품번으로 정한 단계(PP_WorkOrderRouting) 행이다 —
    /// 같은 라인에서 해당 품번을 만드는 단계 중 빠른순(단계 Status In Progress 우선 →
    /// 헤더 Priority → DueDate → WoID) 첫 건. 없으면 NoWoForItem.
    /// 이 조회는 단계 행만 잠근다(UPDLOCK, ROWLOCK) — 헤더 잠금은 BumpStepCompleted 가
    /// 단계 → 헤더 순으로 다시 잡으므로, 여기서 헤더까지 같이 잠그면 RecordCycle 과
    /// 잠금 순서가 엇갈려 교착 가능성이 생긴다.
    /// CycleSec = 같은 설비의 직전 원천 LOT 과 이 LOT 의 CreatedTS 차 (샷 발생 시각 기준).
    /// </summary>
    public (InjConfirmOutcome Outcome, int ResultId, string ItemNo, int WoId) ConfirmByLotCode(
        string lotCode, string lineId,
        string operatorId, int? sessionId, string employeeNo)
    {
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            int lotId; string itemNo, status; string? equipId, moldId, cavityPos; DateTime createdTs;
            using (var cmd = new SqlCommand("""
                SELECT l.LotID, l.ItemNo, l.LineID, l.CreatedTS,
                       e.ConfirmStatus, e.EquipID, e.MoldID, e.CavityPos
                FROM   dbo.tbl_Lot l WITH (UPDLOCK, ROWLOCK)
                JOIN   dbo.PR_InjLot e WITH (UPDLOCK, ROWLOCK) ON e.LotID = l.LotID
                WHERE  l.LotCode = @Code;
                """, conn, tx))
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

            if (status is "CONFIRMED") { tx.Rollback(); return (InjConfirmOutcome.AlreadyConfirmed, 0, itemNo, 0); }
            if (status is "NG_BLOCKED" or "NG_CONFIRMED") { tx.Rollback(); return (InjConfirmOutcome.NgBlocked, 0, itemNo, 0); }

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
                if (!rdr.Read()) { rdr.Close(); tx.Rollback(); return (InjConfirmOutcome.NoWoForItem, 0, itemNo, 0); }
                woId   = (int)rdr["WoID"];
                stepId = (int)rdr["RoutingLineID"];
            }

            int cycleSec;
            using (var cmd = new SqlCommand("""
                SELECT ISNULL(DATEDIFF(SECOND, MAX(pl.CreatedTS), @ThisTs), 0)
                FROM   dbo.tbl_Lot pl
                JOIN   dbo.PR_InjLot pe ON pe.LotID = pl.LotID
                WHERE  pe.EquipID = @Equip AND pe.CavityPos = @Pos AND pl.CreatedTS < @ThisTs;
                """, conn, tx))
            {
                cmd.Parameters.Add("@ThisTs", SqlDbType.DateTime2   ).Value = createdTs;
                cmd.Parameters.Add("@Equip",  SqlDbType.VarChar, 20 ).Value = (object?)equipId ?? DBNull.Value;
                cmd.Parameters.Add("@Pos",    SqlDbType.VarChar, 4  ).Value = (object?)cavityPos ?? DBNull.Value;
                cycleSec = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
                if (cycleSec is < 0 or > 86400) cycleSec = 0;
            }

            int resultId;
            using (var cmd = new SqlCommand("""
                INSERT INTO dbo.PR_ProductionResult
                    (EntryNo, WoID, LotID, LineID, ProcessCode, GoodQty, CycleSec,
                     MoldID, OperatorID, SessionID, DefectFlag, EntryAt, CreatedBy, CreatedTS)
                OUTPUT INSERTED.ResultID
                VALUES
                    (@EntryNo, @WoID, @LotID, @LineID, 'INJ', 1, @CT,
                     @Mold, @Op, @Sess, 0, SYSDATETIME(), @By, SYSDATETIME());
                """, conn, tx))
            {
                var entryNo = $"E{DateTime.Now:yyMMddHHmmssfff}-{lineId}";
                if (entryNo.Length > 28) entryNo = entryNo[..28];
                cmd.Parameters.Add("@EntryNo", SqlDbType.VarChar, 28  ).Value = entryNo;
                cmd.Parameters.Add("@WoID",    SqlDbType.Int          ).Value = woId;
                cmd.Parameters.Add("@LotID",   SqlDbType.Int          ).Value = lotId;
                cmd.Parameters.Add("@LineID",  SqlDbType.VarChar, 20  ).Value = lineId;
                cmd.Parameters.Add("@CT",      SqlDbType.Int          ).Value = cycleSec;
                cmd.Parameters.Add("@Mold",    SqlDbType.VarChar, 20  ).Value = (object?)moldId ?? DBNull.Value;
                cmd.Parameters.Add("@Op",      SqlDbType.NVarChar, 450).Value = operatorId;
                cmd.Parameters.Add("@Sess",    SqlDbType.Int          ).Value = (object?)sessionId ?? DBNull.Value;
                cmd.Parameters.Add("@By",      SqlDbType.VarChar, 50  ).Value = employeeNo;
                resultId = (int)cmd.ExecuteScalar()!;
            }

            using (var cmd = new SqlCommand("""
                UPDATE dbo.tbl_Lot
                SET    Status = 'CONFIRMED', QualityFlag = 'OK', WoID = @WoID,
                       ModifiedBy = @Op, ModifiedTS = SYSDATETIME()
                WHERE  LotID = @LotID;

                UPDATE dbo.PR_InjLot
                SET    ConfirmStatus = 'CONFIRMED', ConfirmedAt = SYSDATETIME(),
                       ConfirmedBy = @Op, ConfirmedSessionID = @Sess,
                       ModifiedBy = @Op, ModifiedTS = SYSDATETIME()
                WHERE  LotID = @LotID;
                """, conn, tx))
            {
                cmd.Parameters.Add("@WoID",  SqlDbType.Int          ).Value = woId;
                cmd.Parameters.Add("@LotID", SqlDbType.Int          ).Value = lotId;
                cmd.Parameters.Add("@Op",    SqlDbType.NVarChar, 450).Value = operatorId;
                cmd.Parameters.Add("@Sess",  SqlDbType.Int          ).Value = (object?)sessionId ?? DBNull.Value;
                cmd.ExecuteNonQuery();
            }

            WorkOrderRepository.BumpStepCompleted(conn, tx, stepId, 1m, operatorId);

            tx.Commit();
            return (InjConfirmOutcome.Confirmed, resultId, itemNo, woId);
        }
        catch { tx.Rollback(); throw; }
    }

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

    /// <summary>Inj05 수동 불량 확정 후 상태 마감: NG_BLOCKED → NG_CONFIRMED.</summary>
    public void MarkNgConfirmed(int lotId, string operatorId)
    {
        const string sql = """
            BEGIN TRAN;
            UPDATE dbo.PR_InjLot
            SET    ConfirmStatus = 'NG_CONFIRMED', ConfirmedAt = SYSDATETIME(),
                   ConfirmedBy = @Op, ModifiedBy = @Op, ModifiedTS = SYSDATETIME()
            WHERE  LotID = @Lot AND ConfirmStatus = 'NG_BLOCKED';
            IF @@ROWCOUNT = 1
              UPDATE dbo.tbl_Lot
              SET    Status = 'NG', ModifiedBy = @Op, ModifiedTS = SYSDATETIME()
              WHERE  LotID = @Lot;
            COMMIT;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Lot", SqlDbType.Int          ).Value = lotId;
        cmd.Parameters.Add("@Op",  SqlDbType.NVarChar, 450).Value = operatorId;
        cmd.ExecuteNonQuery();
    }

    /// <summary>Inj02 카드용: 오늘 샷수(캐비티1 기준) / 미확정 / NG 차단 카운트.</summary>
    public (int TodayShots, int RawCount, int NgBlocked) GetTodayStats(string lineId)
    {
        const string sql = """
            SELECT
              (SELECT COUNT(*) FROM dbo.tbl_Lot l JOIN dbo.PR_InjLot e ON e.LotID = l.LotID
               WHERE l.LineID = @Line AND e.CavityNo = 1
                 AND l.CreatedTS >= CAST(SYSDATETIME() AS date)
                 AND l.CreatedTS <  DATEADD(day, 1, CAST(SYSDATETIME() AS date))) AS TodayShots,
              (SELECT COUNT(*) FROM dbo.tbl_Lot l JOIN dbo.PR_InjLot e ON e.LotID = l.LotID
               WHERE l.LineID = @Line AND e.ConfirmStatus = 'RAW') AS RawCount,
              (SELECT COUNT(*) FROM dbo.tbl_Lot l JOIN dbo.PR_InjLot e ON e.LotID = l.LotID
               WHERE l.LineID = @Line AND e.ConfirmStatus = 'NG_BLOCKED') AS NgBlocked;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Line", SqlDbType.VarChar, 20).Value = lineId;
        using var rdr = cmd.ExecuteReader();
        if (!rdr.Read()) return (0, 0, 0);
        return (Convert.ToInt32(rdr["TodayShots"]), Convert.ToInt32(rdr["RawCount"]), Convert.ToInt32(rdr["NgBlocked"]));
    }

    /// <summary>
    /// INJ-MAIN 좌측 패널: 스테이션 BOP 품번 ∪ 오늘 실적/일정이 있는 품번의 당일 현황.
    /// 모든 LOT 수치는 LOT 생성일이 오늘인 것만 센다 — 확정 시각 기준으로 하면
    /// 어제 생성·오늘 확정 LOT 이 INPUT 과 FINAL 에 다른 날로 잡혀 항등식이 깨진다.
    /// 로봇 NG 는 상태(NG_BLOCKED/NG_CONFIRMED)로만 세고 PR_DefectDetail 은 LotID 없는
    /// 수동 등록분만 더한다 — NG 확정은 둘 다 남기므로 같이 더하면 이중 계상.
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
                  AND  ISNULL(w.Status,'Draft') <> 'Cancelled'
                GROUP  BY w.ItemNo
            ),
            lots AS (
                SELECT l.ItemNo,
                       COUNT(*)                                                                    AS InputQty,
                       SUM(CASE WHEN e.ConfirmStatus = 'CONFIRMED' THEN 1 ELSE 0 END)              AS ConfirmedQty,
                       SUM(CASE WHEN e.ConfirmStatus IN ('NG_BLOCKED','NG_CONFIRMED') THEN 1 ELSE 0 END) AS NgLotQty,
                       SUM(CASE WHEN e.ConfirmStatus = 'RAW' THEN 1 ELSE 0 END)                    AS PendingQty
                FROM   dbo.tbl_Lot   l
                JOIN   dbo.PR_InjLot e ON e.LotID = l.LotID
                WHERE  l.LineID = @Line
                  AND  l.CreatedTS >= @Today AND l.CreatedTS < DATEADD(day, 1, @Today)
                GROUP  BY l.ItemNo
            ),
            manual AS (
                SELECT w.ItemNo, SUM(ISNULL(d.Qty,0)) AS ManualDefect
                FROM   dbo.PR_DefectDetail d
                JOIN   dbo.PP_WorkOrder    w ON w.WoID = d.WoID
                WHERE  d.LotID IS NULL
                  AND  d.ProcessCode = 'INJ'
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
                   ISNULL(t.NgLotQty, 0)      AS NgLotQty,
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
        using var rdr = cmd.ExecuteReader();
        var list = new List<InjItemDailyDto>();
        while (rdr.Read())
        {
            var confirmed = Convert.ToInt32(rdr["ConfirmedQty"]);
            var ngLots    = Convert.ToInt32(rdr["NgLotQty"]);
            var manual    = Convert.ToInt32(rdr["ManualDefect"]);
            list.Add(new InjItemDailyDto
            {
                ItemNo     = (string)rdr["ItemNo"],
                ItemName   = (string)rdr["ItemName"],
                PlanQty    = Convert.ToDecimal(rdr["PlanQty"]),
                InputQty   = Convert.ToInt32(rdr["InputQty"]),
                NgQty      = ngLots + manual,
                FinalQty   = Math.Max(0, confirmed - manual),
                PendingQty = Convert.ToInt32(rdr["PendingQty"]),
                InBop      = Convert.ToInt32(rdr["InBop"]) == 1,
                HasOpenWo  = Convert.ToInt32(rdr["HasOpenWo"]) == 1,
            });
        }
        return list;
    }
}
