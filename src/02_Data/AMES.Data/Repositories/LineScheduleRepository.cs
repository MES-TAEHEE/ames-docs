using System.Data;
using AMES.Data.Connection;
using AMES.Data.Scheduling;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// PP-LSB Line Schedule Board — WO/PM 배치 (PP_LineSchedule). 밴드는 MD_LineTimeSegment 파생.
/// </summary>
public sealed class LineScheduleRepository
{
    readonly AmesConnectionFactory _f;

    public LineScheduleRepository(AmesConnectionFactory f)
    {
        _f = f;
    }

    // ── Records ──────────────────────────────────────────────────────────────
    public sealed record WoSlot(
        int ScheduleId, string LineId, DateTime? ScheduleDate,
        int? WoId, string? WoNumber, string? ItemName,
        int StartMin, int EndMin, decimal PlannedQty, string? Status);

    // ── DB 서버 시각 (Now line 앵커; 이후 진행은 클라이언트 시계로 계산) ──────────
    public DateTime GetDbNow()
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand("SELECT SYSDATETIME();", conn);
        return (DateTime)cmd.ExecuteScalar()!;
    }

    // ── WO Slot CRUD (PP_LineSchedule) ───────────────────────────────────────
    public List<WoSlot> GetSlots(string lineId, DateTime date)
    {
        const string sql = """
            SELECT s.ScheduleID, s.LineID, s.ScheduleDate,
                   s.WoID, w.WoNumber, i.ItemName,
                   ISNULL(s.StartMin,0)   AS StartMin,
                   ISNULL(s.EndMin,0)     AS EndMin,
                   ISNULL(s.PlannedQty,0) AS PlannedQty,
                   s.Status
            FROM   dbo.PP_LineSchedule s
            LEFT JOIN dbo.PP_WorkOrder w ON w.WoID  = s.WoID
            LEFT JOIN dbo.MD_Item      i ON i.ItemNo = w.ItemNo
            WHERE  s.LineID       = @LineId
              AND  s.ScheduleDate = @Date
            ORDER  BY s.StartMin;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@LineId", SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@Date",   SqlDbType.Date).Value        = date.Date;
        using var rdr = cmd.ExecuteReader();
        var list = new List<WoSlot>();
        while (rdr.Read())
            list.Add(new WoSlot(
                (int)rdr["ScheduleID"], (string)rdr["LineID"],
                rdr["ScheduleDate"] as DateTime?,
                rdr["WoID"] as int?, rdr["WoNumber"] as string, rdr["ItemName"] as string,
                (int)rdr["StartMin"], (int)rdr["EndMin"],
                rdr.GetDecimal(rdr.GetOrdinal("PlannedQty")),
                rdr["Status"] as string));
        return list;
    }

    public void PlaceWo(string lineId, DateTime date, int woId,
                        int startMin, int endMin, decimal plannedQty)
    {
        const string sql = """
            INSERT INTO dbo.PP_LineSchedule
                   (LineID, ScheduleDate, WoID, StartMin, EndMin, PlannedQty, Status)
            VALUES (@LineId, @Date, @WoId, @Start, @End, @Qty, 'Planned');
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@LineId", SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@Date",   SqlDbType.Date).Value        = date.Date;
        cmd.Parameters.Add("@WoId",   SqlDbType.Int).Value         = woId;
        cmd.Parameters.Add("@Start",  SqlDbType.Int).Value         = startMin;
        cmd.Parameters.Add("@End",    SqlDbType.Int).Value         = endMin;
        cmd.Parameters.Add("@Qty",    SqlDbType.Decimal).Value     = plannedQty;
        cmd.ExecuteNonQuery();
    }

    public void RemoveSlot(int scheduleId)
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(
            "DELETE FROM dbo.PP_LineSchedule WHERE ScheduleID = @Id;", conn);
        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = scheduleId;
        cmd.ExecuteNonQuery();
    }

    // ── PP-LSB : 패턴 참조 스케줄 (PatternID + WO 매핑 + Publish) ──────────────
    // PP_LineSchedule.PatternID = 선택된 MD_LineTimePattern, 밴드는 MD_LineTimeSegment에서 파생(스냅샷 아님).
    public sealed record ScheduleRow(
        int ScheduleId, string? PatternId, int? WoId, string? WoNumber, string? ItemName,
        int StartMin, int EndMin, decimal PlannedQty, string? Status,
        DateTime? PublishedAt, string? PublishedBy,
        string? EntryType, string? Title, string? RefType, int? RefId);

    public List<ScheduleRow> GetSchedule(string lineId, DateTime date)
    {
        const string sql = """
            SELECT s.ScheduleID, s.PatternID, s.WoID, w.WoNumber, i.ItemName,
                   ISNULL(s.StartMin,0)   AS StartMin,
                   ISNULL(s.EndMin,0)     AS EndMin,
                   ISNULL(s.PlannedQty,0) AS PlannedQty,
                   s.Status, s.PublishedAt, s.PublishedBy,
                   s.EntryType, s.Title, s.RefType, s.RefID
            FROM   dbo.PP_LineSchedule s
            LEFT JOIN dbo.PP_WorkOrder w ON w.WoID   = s.WoID
            LEFT JOIN dbo.MD_Item      i ON i.ItemNo = w.ItemNo
            WHERE  s.LineID = @LineId AND s.ScheduleDate = @Date
            ORDER  BY s.StartMin;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@LineId", SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@Date",   SqlDbType.Date).Value        = date.Date;
        using var rdr = cmd.ExecuteReader();
        var list = new List<ScheduleRow>();
        while (rdr.Read())
            list.Add(new ScheduleRow(
                (int)rdr["ScheduleID"],
                rdr["PatternID"]   as string,
                rdr["WoID"]        as int?,
                rdr["WoNumber"]    as string,
                rdr["ItemName"]    as string,
                Convert.ToInt32(rdr["StartMin"]),
                Convert.ToInt32(rdr["EndMin"]),
                rdr.GetDecimal(rdr.GetOrdinal("PlannedQty")),
                rdr["Status"]      as string,
                rdr["PublishedAt"] as DateTime?,
                rdr["PublishedBy"] as string,
                rdr["EntryType"]   as string,
                rdr["Title"]       as string,
                rdr["RefType"]     as string,
                rdr["RefID"]       as int?));
        return list;
    }

    // 적용: (라인, 일자)의 기존 행을 지우고 패턴 + WO 배치 + PM 밴드를 Draft로 저장.
    public void SaveSchedule(string lineId, DateTime date, string? patternId,
        IEnumerable<(int WoId, int StartMin, int EndMin, decimal Qty)> slots,
        IEnumerable<(int StartMin, int EndMin, string? Title, string? RefType, int? RefId)> pmBands,
        string actor)
    {
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            using (var del = new SqlCommand(
                "DELETE FROM dbo.PP_LineSchedule WHERE LineID=@LineId AND ScheduleDate=@Date;", conn, tx))
            {
                del.Parameters.Add("@LineId", SqlDbType.VarChar, 20).Value = lineId;
                del.Parameters.Add("@Date",   SqlDbType.Date).Value        = date.Date;
                del.ExecuteNonQuery();
            }

            var rows = slots.Where(s => s.EndMin > s.StartMin).ToList();
            var pms  = pmBands.Where(p => p.EndMin > p.StartMin).ToList();
            // 상태값은 공통코드 SCHEDULE_STATUS(DRAFT/PUBLISHED) 참조.
            // WO·PM 모두 없어도 패턴/상태 보관용 placeholder 행 1개는 남긴다.
            if (rows.Count == 0 && pms.Count == 0)
                InsertRow(conn, tx, lineId, date, patternId, "WO", null, null, null, 0m, null, null, null, "DRAFT", actor);
            else
            {
                foreach (var s in rows)
                    InsertRow(conn, tx, lineId, date, patternId, "WO", s.WoId, s.StartMin, s.EndMin, s.Qty, null, null, null, "DRAFT", actor);
                foreach (var p in pms)
                    InsertRow(conn, tx, lineId, date, patternId, "PM", null, p.StartMin, p.EndMin, 0m, p.Title, p.RefType, p.RefId, "DRAFT", actor);
            }

            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    static void InsertRow(SqlConnection conn, SqlTransaction tx, string lineId, DateTime date,
        string? patternId, string entryType, int? woId, int? startMin, int? endMin, decimal qty,
        string? title, string? refType, int? refId, string status, string actor)
    {
        using var cmd = new SqlCommand("""
            INSERT INTO dbo.PP_LineSchedule
                   (LineID, ScheduleDate, WoID, StartMin, EndMin, PlannedQty, PatternID,
                    EntryType, Title, RefType, RefID, Status, CreatedBy, CreatedTS)
            VALUES (@LineId, @Date, @WoId, @Start, @End, @Qty, @Pattern,
                    @EntryType, @Title, @RefType, @RefID, @Status, @By, SYSDATETIME());
            """, conn, tx);
        cmd.Parameters.Add("@LineId",    SqlDbType.VarChar, 20).Value  = lineId;
        cmd.Parameters.Add("@Date",      SqlDbType.Date).Value         = date.Date;
        cmd.Parameters.Add("@WoId",      SqlDbType.Int).Value          = (object?)woId ?? DBNull.Value;
        cmd.Parameters.Add("@Start",     SqlDbType.SmallInt).Value     = startMin is int sv ? (short)sv : (object)DBNull.Value;
        cmd.Parameters.Add("@End",       SqlDbType.SmallInt).Value     = endMin   is int ev ? (short)ev : (object)DBNull.Value;
        cmd.Parameters.Add("@Qty",       SqlDbType.Decimal).Value      = qty;
        cmd.Parameters.Add("@Pattern",   SqlDbType.VarChar, 20).Value  = (object?)patternId ?? DBNull.Value;
        cmd.Parameters.Add("@EntryType", SqlDbType.VarChar, 10).Value  = entryType;
        cmd.Parameters.Add("@Title",     SqlDbType.NVarChar, 100).Value = (object?)title ?? DBNull.Value;
        cmd.Parameters.Add("@RefType",   SqlDbType.VarChar, 10).Value  = (object?)refType ?? DBNull.Value;
        cmd.Parameters.Add("@RefID",     SqlDbType.Int).Value          = (object?)refId ?? DBNull.Value;
        cmd.Parameters.Add("@Status",    SqlDbType.VarChar, 20).Value  = status;
        cmd.Parameters.Add("@By",        SqlDbType.VarChar, 50).Value  = actor;
        cmd.ExecuteNonQuery();
    }

    // 발행: (라인, 일자)의 모든 행을 PUBLISHED + 발행정보 기록. (상태값 = 공통코드 SCHEDULE_STATUS)
    //   + 해당 일자의 분단위 가동/구간유형을 PP_ProductionCalendarOverride 에 스냅샷(확정)한다.
    public void PublishSchedule(string lineId, DateTime date, string actor)
    {
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            using (var cmd = new SqlCommand("""
                UPDATE dbo.PP_LineSchedule
                SET    Status='PUBLISHED', PublishedAt=SYSDATETIME(), PublishedBy=@By,
                       ModifiedBy=@By, ModifiedTS=SYSDATETIME()
                WHERE  LineID=@LineId AND ScheduleDate=@Date;
                """, conn, tx))
            {
                cmd.Parameters.Add("@LineId", SqlDbType.VarChar,  20).Value = lineId;
                cmd.Parameters.Add("@Date",   SqlDbType.Date).Value         = date.Date;
                cmd.Parameters.Add("@By",     SqlDbType.NVarChar, 450).Value = actor;
                cmd.ExecuteNonQuery();
            }

            SnapshotCalendarOverride(conn, tx, lineId, date, actor);
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    // Publish 스냅샷 — 패턴 세그먼트(MD_LineTimeSegment) + PM 밴드(EntryType='PM')를 분단위로 합성해
    // OperatingFlag/SegmentFlag(CHAR 1440) 를 만들고 PP_ProductionCalendarOverride 에 upsert.
    // 상태→(op,seg) 문자 인코딩은 공통코드 SEGMENT_STATE.Attribute1 'op:seg' 를 그대로 사용(하드코딩 없음).
    static void SnapshotCalendarOverride(SqlConnection conn, SqlTransaction tx,
                                         string lineId, DateTime date, string actor)
    {
        // 1) SEGMENT_STATE 코드 → (op, seg) 문자 맵
        var stateMap = new Dictionary<string, (char Op, char Seg)>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = new SqlCommand(
            "SELECT CodeValue, Attribute1 FROM dbo.MD_CodeItem WHERE GroupCode='SEGMENT_STATE';", conn, tx))
        using (var rdr = cmd.ExecuteReader())
            while (rdr.Read())
            {
                if (rdr["CodeValue"] as string is not { } cv) continue;
                char op = '0', sg = '0';
                if (rdr["Attribute1"] as string is { Length: > 0 } a1)
                {
                    var parts = a1.Split(':');
                    if (parts.Length > 0 && parts[0].Length > 0) op = parts[0][0];
                    if (parts.Length > 1 && parts[1].Length > 0) sg = parts[1][0];
                }
                stateMap[cv] = (op, sg);
            }
        var pm = stateMap.TryGetValue("PM", out var pmv) ? pmv : ('0', '9');

        var opArr = new char[1440]; var sgArr = new char[1440];
        for (int i = 0; i < 1440; i++) { opArr[i] = '0'; sgArr[i] = '0'; }

        // 2) 발행 대상의 PatternID / DayType (스케줄 행에 보관된 값)
        string? patternId = null, dayType = null;
        using (var cmd = new SqlCommand("""
            SELECT TOP 1 s.PatternID, p.DayType
            FROM   dbo.PP_LineSchedule s
            LEFT JOIN dbo.MD_LineTimePattern p ON p.PatternID = s.PatternID
            WHERE  s.LineID=@LineId AND s.ScheduleDate=@Date AND s.PatternID IS NOT NULL;
            """, conn, tx))
        {
            cmd.Parameters.Add("@LineId", SqlDbType.VarChar, 20).Value = lineId;
            cmd.Parameters.Add("@Date",   SqlDbType.Date).Value        = date.Date;
            using var rdr = cmd.ExecuteReader();
            if (rdr.Read()) { patternId = rdr["PatternID"] as string; dayType = rdr["DayType"] as string; }
        }

        // 3) 패턴 세그먼트를 분단위로 칠함
        if (!string.IsNullOrEmpty(patternId))
            using (var cmd = new SqlCommand(
                "SELECT StartMin, EndMin, SegmentState FROM dbo.MD_LineTimeSegment WHERE PatternID=@P;", conn, tx))
            {
                cmd.Parameters.Add("@P", SqlDbType.VarChar, 20).Value = patternId;
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    if (rdr["StartMin"] is not (short or int) || rdr["EndMin"] is not (short or int)) continue;
                    int s = Convert.ToInt32(rdr["StartMin"]), e = Convert.ToInt32(rdr["EndMin"]);
                    var st = rdr["SegmentState"] as string ?? "";
                    var (op, sg) = stateMap.TryGetValue(st, out var v) ? v : ('0', '0');
                    Paint(opArr, sgArr, s, e, op, sg);
                }
            }

        // 4) PM 밴드로 덮어씀 (가동시간 침범분 = 예방보전)
        using (var cmd = new SqlCommand("""
            SELECT StartMin, EndMin FROM dbo.PP_LineSchedule
            WHERE  LineID=@LineId AND ScheduleDate=@Date AND EntryType='PM'
              AND  StartMin IS NOT NULL AND EndMin IS NOT NULL;
            """, conn, tx))
        {
            cmd.Parameters.Add("@LineId", SqlDbType.VarChar, 20).Value = lineId;
            cmd.Parameters.Add("@Date",   SqlDbType.Date).Value        = date.Date;
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
                Paint(opArr, sgArr, Convert.ToInt32(rdr["StartMin"]), Convert.ToInt32(rdr["EndMin"]), pm.Item1, pm.Item2);
        }

        string opStr = new string(opArr), sgStr = new string(sgArr);
        int totalOp = 0;
        for (int i = 0; i < 1440; i++) if (opArr[i] == '1') totalOp++;
        // MD_LineTimePattern.TotalPlannedDownMin 과 동일 정의: 비가동 = 전체(1440) − 가동 (IDLE 포함).
        // 이래야 PM(가동 침범)이 늘어난 만큼 비가동도 정확히 증가한다.
        int totalDown = 1440 - totalOp;

        // 5) PP_ProductionCalendarOverride upsert (키: OverrideDate + LineID)
        using (var cmd = new SqlCommand("""
            UPDATE dbo.PP_ProductionCalendarOverride
            SET    DayType=@DayType, PatternID=@Pattern,
                   TotalOperatingMin=@TOp, TotalPlannedDownMin=@TDown,
                   OperatingFlag=@OpF, SegmentFlag=@SgF,
                   ModifiedBy=@By, ModifiedTS=SYSDATETIME()
            WHERE  OverrideDate=@Date AND LineID=@LineId;
            IF @@ROWCOUNT = 0
                INSERT INTO dbo.PP_ProductionCalendarOverride
                       (OverrideDate, LineID, DayType, PatternID,
                        TotalOperatingMin, TotalPlannedDownMin, OperatingFlag, SegmentFlag,
                        CreatedBy, CreatedTS)
                VALUES (@Date, @LineId, @DayType, @Pattern,
                        @TOp, @TDown, @OpF, @SgF, @By, SYSDATETIME());
            """, conn, tx))
        {
            cmd.Parameters.Add("@Date",    SqlDbType.Date).Value          = date.Date;
            cmd.Parameters.Add("@LineId",  SqlDbType.VarChar, 20).Value   = lineId;
            cmd.Parameters.Add("@DayType", SqlDbType.VarChar, 20).Value   = (object?)dayType ?? DBNull.Value;
            cmd.Parameters.Add("@Pattern", SqlDbType.VarChar, 20).Value   = (object?)patternId ?? DBNull.Value;
            cmd.Parameters.Add("@TOp",     SqlDbType.Int).Value           = totalOp;
            cmd.Parameters.Add("@TDown",   SqlDbType.Int).Value           = totalDown;
            cmd.Parameters.Add("@OpF",     SqlDbType.Char, 1440).Value    = opStr;
            cmd.Parameters.Add("@SgF",     SqlDbType.Char, 1440).Value    = sgStr;
            cmd.Parameters.Add("@By",      SqlDbType.NVarChar, 450).Value = actor;
            cmd.ExecuteNonQuery();
        }
    }

    static void Paint(char[] opArr, char[] sgArr, int start, int end, char op, char sg)
    {
        int s = Math.Max(0, start), e = Math.Min(1440, end);
        for (int m = s; m < e; m++) { opArr[m] = op; sgArr[m] = sg; }
    }

    // 초기화: (라인, 일자)의 미발행(DRAFT) 스케줄 행 삭제. 발행된 행은 보존.
    public void DeleteSchedule(string lineId, DateTime date)
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand("""
            DELETE FROM dbo.PP_LineSchedule
            WHERE  LineID=@LineId AND ScheduleDate=@Date AND Status='DRAFT';
            """, conn);
        cmd.Parameters.Add("@LineId", SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@Date",   SqlDbType.Date).Value        = date.Date;
        cmd.ExecuteNonQuery();
    }

    // ── PP-003 자동 배치용 하루 능력 ──────────────────────────────────────────
    /// <summary>
    /// (라인, 일자)의 패턴·가동 밴드·기존 점유(WO 슬롯 + PM 밴드). 패턴은 그 날 저장 행의 PatternID →
    /// 라인 전용 ACTIVE 패턴 → 전역 패턴 순으로 해석한다 (PP-LSB 보드의 PatternOptions 순서와 동일).
    /// OperatingMin 은 PM 을 뺀 가동분, RemainMin 은 거기서 WO 슬롯을 뺀 값 (보드 KPI 와 같은 정의).
    /// </summary>
    public sealed record DayCapacity(
        string? PatternId, int DayStart,
        IReadOnlyList<SlotPacker.Interval> OperatingBands,
        IReadOnlyList<SlotPacker.Interval> Occupied,
        int OperatingMin, int WoLoadMin, int? LastWoEnd)
    {
        public int RemainMin => OperatingMin - WoLoadMin;
    }

    public DayCapacity GetDayCapacity(string lineId, DateTime date)
    {
        using var conn = _f.OpenConnection();
        return ReadDayCapacity(conn, null, lineId, date);
    }

    internal static DayCapacity ReadDayCapacity(SqlConnection conn, SqlTransaction? tx, string lineId, DateTime date)
    {
        const string sql = """
            DECLARE @Pat varchar(20) =
                (SELECT TOP 1 PatternID FROM dbo.PP_LineSchedule
                 WHERE  LineID = @LineId AND ScheduleDate = @Date AND PatternID IS NOT NULL
                 ORDER  BY ScheduleID);
            IF @Pat IS NULL
                SELECT TOP 1 @Pat = p.PatternID
                FROM   dbo.MD_LineTimePattern p
                WHERE  (p.LineID = @LineId OR p.LineID IS NULL)
                  AND  ISNULL(p.Status,'ACTIVE') = 'ACTIVE'
                  AND  (p.EffectiveFrom IS NULL OR p.EffectiveFrom <= @Date)
                  AND  (p.EffectiveTo   IS NULL OR p.EffectiveTo   >= @Date)
                ORDER  BY CASE WHEN p.LineID IS NULL THEN 1 ELSE 0 END, p.PatternID;
            SELECT @Pat AS PatternID;

            SELECT s.StartMin, s.EndMin, s.SegmentState, ISNULL(c.SortOrder, 9999) AS ShiftSort
            FROM   dbo.MD_LineTimeSegment s
            LEFT JOIN dbo.MD_CodeItem c ON c.GroupCode = 'WORK_SHIFT' AND c.CodeValue = s.ShiftCode
            WHERE  s.PatternID = @Pat
              AND  s.StartMin IS NOT NULL AND s.EndMin IS NOT NULL AND s.EndMin > s.StartMin;

            SELECT s.EntryType, ISNULL(s.StartMin,0) AS StartMin, ISNULL(s.EndMin,0) AS EndMin
            FROM   dbo.PP_LineSchedule s
            WHERE  s.LineID = @LineId AND s.ScheduleDate = @Date
              AND  ISNULL(s.EndMin,0) > ISNULL(s.StartMin,0);
            """;
        using var cmd = new SqlCommand(sql, conn, tx);
        cmd.Parameters.Add("@LineId", SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@Date",   SqlDbType.Date).Value        = date.Date;
        using var rdr = cmd.ExecuteReader();

        string? patternId = rdr.Read() ? rdr["PatternID"] as string : null;

        var segs = new List<(int Start, int End, string State, int ShiftSort)>();
        if (rdr.NextResult())
            while (rdr.Read())
                segs.Add((Convert.ToInt32(rdr["StartMin"]), Convert.ToInt32(rdr["EndMin"]),
                          rdr["SegmentState"] as string ?? "", Convert.ToInt32(rdr["ShiftSort"])));

        var wo = new List<SlotPacker.Interval>();
        var pm = new List<SlotPacker.Interval>();
        if (rdr.NextResult())
            while (rdr.Read())
            {
                var iv = new SlotPacker.Interval(Convert.ToInt32(rdr["StartMin"]), Convert.ToInt32(rdr["EndMin"]));
                if (rdr["EntryType"] as string == "PM") pm.Add(iv); else wo.Add(iv);
            }

        // 하루 시작 = 첫 교대(SortOrder 최소)의 가장 이른 시작 — 보드 AdjustRange 와 동일
        int dayStart = 6 * 60;
        if (segs.Count > 0)
        {
            int minSort = segs.Min(s => s.ShiftSort);
            dayStart = segs.Where(s => s.ShiftSort == minSort).Min(s => s.Start);
        }
        var operating = segs.Where(s => s.State == "OPERATING")
                            .Select(s => new SlotPacker.Interval(s.Start, s.End)).ToList();
        int operatingMin = operating.Sum(b => Subtract(b, pm).Sum(x => x.EndMin - x.StartMin));
        int woLoad       = wo.Sum(w => w.EndMin - w.StartMin);
        int Axis(int m) { int r = (m - dayStart) % 1440; return r < 0 ? r + 1440 : r; }
        int? lastWoEnd = wo.Count == 0 ? null : wo.MaxBy(w => Axis(w.StartMin) + (w.EndMin - w.StartMin)).EndMin;

        return new DayCapacity(patternId, dayStart, operating, wo.Concat(pm).ToList(), operatingMin, woLoad, lastWoEnd);
    }

    // band 에서 holes 를 뺀 잔여 구간 (보드 SubtractPm 과 같은 규칙)
    static IEnumerable<SlotPacker.Interval> Subtract(SlotPacker.Interval band, IEnumerable<SlotPacker.Interval> holes)
    {
        int cur = band.StartMin;
        foreach (var h in holes.Where(h => h.StartMin < band.EndMin && band.StartMin < h.EndMin).OrderBy(h => h.StartMin))
        {
            int hs = Math.Max(band.StartMin, h.StartMin);
            if (hs > cur) yield return new(cur, hs);
            cur = Math.Max(cur, Math.Min(band.EndMin, h.EndMin));
        }
        if (cur < band.EndMin) yield return new(cur, band.EndMin);
    }

    /// <summary>호출자 트랜잭션 안에서 WO 슬롯 1행 추가(DRAFT). 그 날의 다른 행은 건드리지 않는다.</summary>
    internal static void AppendWoSlot(SqlConnection conn, SqlTransaction tx, string lineId, DateTime date,
        string? patternId, int woId, int startMin, int endMin, decimal qty, string actor)
        => InsertRow(conn, tx, lineId, date, patternId, "WO", woId, startMin, endMin, qty, null, null, null, "DRAFT", actor);

    // 라인 배치용 WO 후보 (Released/In Progress). MD_Item 미등록 품목도 포함하도록 LEFT JOIN.
    public sealed record WoRow(int WoId, string? WoNumber, string? ItemNo, string? ItemName, decimal OpenQty, string? Status);

    public List<WoRow> ListLineWos(string lineId)
    {
        const string sql = """
            SELECT w.WoID, w.WoNumber, w.ItemNo, i.ItemName,
                   ISNULL(w.OpenQty,0) AS OpenQty, r.Status
            FROM   dbo.PP_WorkOrderRouting r
            JOIN   dbo.PP_WorkOrder w ON w.WoID = r.WoID
            LEFT JOIN dbo.MD_Item i ON i.ItemNo = w.ItemNo
            WHERE  r.LineID = @LineId
              AND  r.Status IN ('Released','In Progress')
              AND  ISNULL(w.Status,'Draft') <> 'Cancelled'
            ORDER  BY CASE WHEN r.Status='In Progress' THEN 0 ELSE 1 END, w.WoID;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@LineId", SqlDbType.VarChar, 20).Value = lineId;
        using var rdr = cmd.ExecuteReader();
        var list = new List<WoRow>();
        while (rdr.Read())
            list.Add(new WoRow(
                (int)rdr["WoID"],
                rdr["WoNumber"] as string,
                rdr["ItemNo"]   as string,
                rdr["ItemName"] as string,
                rdr.GetDecimal(rdr.GetOrdinal("OpenQty")),
                rdr["Status"]   as string));
        return list;
    }

    // ── PM 후보 (MNT_PMSchedule → 해당 라인 설비) ─────────────────────────────
    // 라인은 설비(MD_Equipment.LineID)로 해석. 마감(완료)된 PM은 제외.
    public sealed record PmCandidate(
        int PmScheduleId, string EquipId, string? EquipName, string? PmType, DateTime? NextDueDate);

    public List<PmCandidate> ListDuePmForLine(string lineId)
    {
        const string sql = """
            SELECT p.PMScheduleID, p.EquipID, e.EquipName, p.PMType, p.NextDueDate
            FROM   dbo.MNT_PMSchedule p
            JOIN   dbo.MD_Equipment   e ON e.EquipID = p.EquipID
            WHERE  e.LineID = @LineId
              AND  ISNULL(p.Status,'') NOT IN ('CLOSED','DONE','CANCELLED')
            ORDER  BY p.NextDueDate, p.EquipID;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@LineId", SqlDbType.VarChar, 20).Value = lineId;
        using var rdr = cmd.ExecuteReader();
        var list = new List<PmCandidate>();
        while (rdr.Read())
            list.Add(new PmCandidate(
                (int)rdr["PMScheduleID"],
                (string)rdr["EquipID"],
                rdr["EquipName"]   as string,
                rdr["PMType"]      as string,
                rdr["NextDueDate"] as DateTime?));
        return list;
    }

    // ── Utility ──────────────────────────────────────────────────────────────
    public List<string> ListLines()
    {
        const string sql = """
            SELECT DISTINCT LineID FROM dbo.PP_WorkOrder
            WHERE LineID IS NOT NULL ORDER BY LineID;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        using var rdr  = cmd.ExecuteReader();
        var list = new List<string>();
        while (rdr.Read()) list.Add((string)rdr["LineID"]);
        return list;
    }
}
