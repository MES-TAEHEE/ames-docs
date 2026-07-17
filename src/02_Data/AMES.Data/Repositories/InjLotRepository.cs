using System.Data;
using AMES.Contracts.Dto;
using AMES.Contracts.Enums;
using AMES.Data.Connection;
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

    public List<MoldItemMapDto> GetMoldItems(string moldCode, string colorCode)
    {
        const string sql = """
            SELECT m.MoldCode, m.ColorCode, m.CavityNo, m.CavityPos, m.ItemNo, m.MoldID,
                   i.ItemName
            FROM   dbo.MD_MoldItemMap m
            LEFT   JOIN dbo.MD_Item i ON i.ItemNo = m.ItemNo
            WHERE  m.MoldCode = @M AND m.ColorCode = @C AND m.ActiveFlag = 1
            ORDER  BY m.CavityNo;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@M", SqlDbType.VarChar, 20).Value = moldCode;
        cmd.Parameters.Add("@C", SqlDbType.VarChar, 10).Value = colorCode;
        using var rdr = cmd.ExecuteReader();
        var list = new List<MoldItemMapDto>();
        while (rdr.Read())
            list.Add(new MoldItemMapDto
            {
                MoldCode  = (string)rdr["MoldCode"],
                ColorCode = (string)rdr["ColorCode"],
                CavityNo  = (int)rdr["CavityNo"],
                CavityPos = (string)rdr["CavityPos"],
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
        string lineId, string equipId, MoldItemMapDto map, long machineShotCount)
    {
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            var lotCode = $"L{DateTime.Now:yyMMddHHmmssfff}-{lineId}-{map.CavityPos}";
            if (lotCode.Length > 40) throw new InvalidOperationException($"LotCode too long: {lotCode}");
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
                    SET    CurrentShots = ISNULL(CurrentShots,0) + 1,
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
                         CumulativeShots = (SELECT CurrentShots FROM dbo.MD_Mold WHERE MoldID = @Mold),
                         RecordedAt      = SYSDATETIME()
                    WHEN NOT MATCHED THEN INSERT
                         (MoldID, RecordDate, ShotsAdded, CumulativeShots, RatedShots, RecordedAt, CreatedBy, CreatedTS)
                         VALUES (@Mold, s.D, 1,
                                 (SELECT CurrentShots FROM dbo.MD_Mold WHERE MoldID = @Mold),
                                 (SELECT RatedShots   FROM dbo.MD_Mold WHERE MoldID = @Mold),
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

    const string SelectLotView = """
        SELECT l.LotID, l.LotCode, l.ItemNo, mi.ItemName, l.LineID,
               e.EquipID, e.MoldCode, e.ColorCode, e.MoldID, e.CavityNo, e.CavityPos,
               e.PressType, e.ConfirmStatus, e.MachineShotCount, l.CreatedTS,
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

    /// <summary>
    /// 스캔 확정: RAW → CONFIRMED + PR_ProductionResult 생성 + WO 수량 증가.
    /// CycleSec = 같은 설비의 직전 원천 LOT 과 이 LOT 의 CreatedTS 차 (샷 발생 시각 기준).
    /// </summary>
    public (InjConfirmOutcome Outcome, int ResultId, string ItemNo) ConfirmByLotCode(
        string lotCode, string lineId, int woId,
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
                if (!rdr.Read()) { rdr.Close(); tx.Rollback(); return (InjConfirmOutcome.NotFound, 0, string.Empty); }
                lotId     = (int)rdr["LotID"];
                itemNo    = rdr["ItemNo"] as string ?? string.Empty;
                status    = (string)rdr["ConfirmStatus"];
                equipId   = rdr["EquipID"] as string;
                moldId    = rdr["MoldID"]  as string;
                cavityPos = rdr["CavityPos"] as string;
                createdTs = (DateTime)rdr["CreatedTS"];
                var lotLine = rdr["LineID"] as string;
                if (!string.Equals(lotLine, lineId, StringComparison.OrdinalIgnoreCase))
                { rdr.Close(); tx.Rollback(); return (InjConfirmOutcome.WrongLine, 0, itemNo); }
            }

            if (status is "CONFIRMED") { tx.Rollback(); return (InjConfirmOutcome.AlreadyConfirmed, 0, itemNo); }
            if (status is "NG_BLOCKED" or "NG_CONFIRMED") { tx.Rollback(); return (InjConfirmOutcome.NgBlocked, 0, itemNo); }

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

                UPDATE dbo.PP_WorkOrder
                SET    CompletedQty = ISNULL(CompletedQty,0) + 1,
                       Status       = CASE WHEN ISNULL(CompletedQty,0) + 1 >= ISNULL(OrderQty,0)
                                            THEN 'Closed' ELSE Status END,
                       ActualEnd    = CASE WHEN ISNULL(CompletedQty,0) + 1 >= ISNULL(OrderQty,0)
                                            THEN SYSDATETIME() ELSE ActualEnd END,
                       ModifiedBy   = @Op, ModifiedTS = SYSDATETIME()
                WHERE  WoID = @WoID;
                """, conn, tx))
            {
                cmd.Parameters.Add("@WoID",  SqlDbType.Int          ).Value = woId;
                cmd.Parameters.Add("@LotID", SqlDbType.Int          ).Value = lotId;
                cmd.Parameters.Add("@Op",    SqlDbType.NVarChar, 450).Value = operatorId;
                cmd.Parameters.Add("@Sess",  SqlDbType.Int          ).Value = (object?)sessionId ?? DBNull.Value;
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
            return (InjConfirmOutcome.Confirmed, resultId, itemNo);
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
                 AND CAST(l.CreatedTS AS DATE) = CAST(SYSDATETIME() AS DATE)) AS TodayShots,
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
}
