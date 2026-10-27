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

    public void RemoveSlot(int scheduleId)
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(
            "DELETE FROM dbo.PP_LineSchedule WHERE ScheduleID = @Id;", conn);
        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = scheduleId;
        cmd.ExecuteNonQuery();
    }

    // ── PP-LSB : 패턴 참조 스케줄 (PatternID + WO 매핑) ──────────────────────────
    // PP_LineSchedule.PatternID = 선택된 MD_LineTimePattern, 밴드는 MD_LineTimeSegment에서 파생(스냅샷 아님).
    public sealed record ScheduleRow(
        int ScheduleId, string? PatternId, int? WoId, string? WoNumber, string? ItemName,
        int StartMin, int EndMin, decimal PlannedQty, string? Status,
        string? EntryType, string? Title, string? RefType, int? RefId,
        string? MoldId = null, string? ItemNo = null);

    public sealed record ScheduleAuditRow(string Kind, string Time, string? WoNumber, decimal Qty, string? MoldId, string? Title, string? Ref);
    public sealed record ScheduleAuditView(string? PatternId, IReadOnlyList<ScheduleAuditRow> Rows);

    /// <summary>
    /// PP-LSB 「적용」·「초기화」 감사 로그용 요약(10-14) — 행마다 종류(WO·PM·MC)·시각(HH:mm–HH:mm)·WO 번호·수량·금형·제목·연계를
    /// 시작 시각 순으로. 행이 없으면 null(=그 라인·날짜에 저장된 계획 없음). 테스트: LineScheduleAuditViewTests.
    /// </summary>
    public static ScheduleAuditView? AuditView(IEnumerable<ScheduleRow> rows)
    {
        var list = rows.OrderBy(r => r.StartMin).ThenBy(r => r.ScheduleId).ToList();
        if (list.Count == 0) return null;
        static string Hm(int m) => $"{m / 60:00}:{m % 60:00}";
        return new ScheduleAuditView(
            list.Select(r => r.PatternId).FirstOrDefault(p => !string.IsNullOrEmpty(p)),
            list.Select(r => new ScheduleAuditRow(
                string.IsNullOrEmpty(r.EntryType) ? "WO" : r.EntryType!,
                $"{Hm(r.StartMin)}–{Hm(r.EndMin)}",
                r.WoNumber ?? (r.WoId is int w ? $"#{w}" : null),
                r.PlannedQty, r.MoldId, r.Title,
                string.IsNullOrEmpty(r.RefType) ? null : $"{r.RefType}:{r.RefId}")).ToList());
    }

    /// <summary>보드가 읽은 (라인, 일자) 행 집합의 지문 — 적용·초기화 때 DB 의 현재 행과 비교해 그 사이 바뀌었으면 거부한다.</summary>
    public static string VersionOf(IEnumerable<ScheduleRow> rows)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var r in rows.OrderBy(r => r.ScheduleId))
            sb.Append(r.ScheduleId).Append('|').Append(r.PatternId).Append('|').Append(r.WoId).Append('|')
              .Append(r.StartMin).Append('|').Append(r.EndMin).Append('|')
              .Append(r.PlannedQty.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('|')
              .Append(r.Status).Append('|').Append(r.EntryType).Append('|').Append(r.Title).Append('|')
              .Append(r.RefType).Append('|').Append(r.RefId).Append('|').Append(r.MoldId).Append('\n');
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash, 0, 16);
    }

    public List<ScheduleRow> GetSchedule(string lineId, DateTime date)
    {
        using var conn = _f.OpenConnection();
        return ReadSchedule(conn, null, lineId, date, forUpdate: false);
    }

    // forUpdate: 읽은 행에 UPDLOCK — 적용·초기화가 확인한 행을 지울 때까지 다른 쪽이 그 행을 바꾸지 못하게
    static List<ScheduleRow> ReadSchedule(SqlConnection conn, SqlTransaction? tx, string lineId, DateTime date, bool forUpdate)
    {
        var sql = $"""
            SELECT s.ScheduleID, s.PatternID, s.WoID, w.WoNumber, i.ItemName,
                   ISNULL(s.StartMin,0)   AS StartMin,
                   ISNULL(s.EndMin,0)     AS EndMin,
                   ISNULL(s.PlannedQty,0) AS PlannedQty,
                   s.Status,
                   s.EntryType, s.Title, s.RefType, s.RefID, s.MoldID, s.ItemNo
            FROM   dbo.PP_LineSchedule s {(forUpdate ? "WITH (UPDLOCK)" : "")}
            LEFT JOIN dbo.PP_WorkOrder w ON w.WoID   = s.WoID
            LEFT JOIN dbo.MD_Item      i ON i.ItemNo = w.ItemNo
            WHERE  s.LineID = @LineId AND s.ScheduleDate = @Date
            ORDER  BY s.StartMin, s.ScheduleID;
            """;
        using var cmd  = new SqlCommand(sql, conn, tx);
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
                rdr["EntryType"]   as string,
                rdr["Title"]       as string,
                rdr["RefType"]     as string,
                rdr["RefID"]       as int?,
                rdr["MoldID"]      as string,
                rdr["ItemNo"]      as string));
        return list;
    }

    /// <summary>보드를 연 뒤 다른 사람(다른 보드·PP-003·APS WO 생성)이 그 라인·일자의 행을 바꿔 적용·초기화를 거부했다.</summary>
    public sealed class ScheduleChangedException(string lineId, DateTime date)
        : InvalidOperationException($"PP_LineSchedule {lineId} {date:yyyy-MM-dd} changed since it was loaded.");

    // 적용: (라인, 일자)의 기존 행을 지우고 패턴 + WO 배치 + PM 밴드 + 금형 교체(MC) 블록을 Draft로 저장.
    //       APS 형제 0분 행(WoID 있음·EndMin ≤ StartMin·수량 > 0)은 보드가 그 WO 에 실제 슬롯을 주지 않는 한 되살린다.
    //       expectedVersion(보드가 읽은 행의 VersionOf)이 지금 행과 다르면 아무것도 바꾸지 않고 ScheduleChangedException —
    //       예전에는 보드를 연 사이 PP-003·APS 가 넣은 슬롯까지 지워 조용히 미배치로 돌렸다(10-10). null 이면 확인하지 않는다(테스트·내부용).
    public void SaveSchedule(string lineId, DateTime date, string? patternId,
        IEnumerable<(int WoId, int StartMin, int EndMin, decimal Qty, string? MoldId)> slots,
        IEnumerable<(int StartMin, int EndMin, string? Title, string? RefType, int? RefId)> pmBands,
        string actor,
        IEnumerable<(int StartMin, int EndMin, string? Title, int? WoRefId, string? MoldId)>? mcBlocks = null,
        string? expectedVersion = null)
    {
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            var current = LockAndCheck(conn, tx, lineId, date, expectedVersion);

            // APS 형제 품번 슬롯(PpRepository.Aps.cs — 대표가 시간을 점유하고 형제는 StartMin = EndMin 행에 PlannedQty 만)은
            // 보드가 0분 행을 싣지 않아 slots 로 돌아오지 않는다. POP 은 분과 무관하게 PlannedQty 를 계획으로 읽으므로 지우기 전에 읽어 두고 되살린다
            var preserved = current
                .Where(r => r.EntryType == "WO" && r.WoId.HasValue && r.EndMin <= r.StartMin && r.PlannedQty > 0)
                .Select(r => (WoId: r.WoId!.Value, StartMin: (int?)r.StartMin, Qty: r.PlannedQty, r.ItemNo))
                .ToList();
            // 보드는 슬롯 품번(ItemNo — APS BOM 규칙 사출 슬롯의 자식 품번)을 모르므로 지우기 전 값을 이어 준다: 같은 WO·금형 → 같은 WO 에 품번이 하나뿐이면 그 품번
            var timed = current.Where(r => r.EntryType == "WO" && r.WoId.HasValue && r.EndMin > r.StartMin && r.ItemNo is not null).ToList();
            string? ItemFor(int woId, string? moldId)
            {
                var byMold = timed.Where(r => r.WoId == woId && string.Equals(r.MoldId, moldId, StringComparison.OrdinalIgnoreCase))
                                  .Select(r => r.ItemNo!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (byMold.Count == 1) return byMold[0];
                var byWo = timed.Where(r => r.WoId == woId).Select(r => r.ItemNo!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                return byWo.Count == 1 ? byWo[0] : null;
            }

            DeleteRows(conn, tx, lineId, date, current.Select(r => r.ScheduleId));

            var rows = slots.Where(s => s.EndMin > s.StartMin).ToList();
            var pms  = pmBands.Where(p => p.EndMin > p.StartMin).ToList();
            var mcs  = (mcBlocks ?? Array.Empty<(int, int, string?, int?, string?)>()).Where(m => m.EndMin > m.StartMin).ToList();
            // 보드가 그 WO 에 실제 슬롯을 주면 그 슬롯이 0분 표지를 대신한다
            var boardWos = rows.Select(s => s.WoId).ToHashSet();
            var keep     = preserved.Where(p => !boardWos.Contains(p.WoId)).ToList();
            // 상태값은 공통코드 SCHEDULE_STATUS(DRAFT) — 발행(PUBLISHED)은 10-14 폐지.
            // WO·PM·MC·형제 행 모두 없어도 패턴/상태 보관용 placeholder 행 1개는 남긴다.
            if (rows.Count == 0 && pms.Count == 0 && mcs.Count == 0 && keep.Count == 0)
                InsertRow(conn, tx, lineId, date, patternId, "WO", null, null, null, 0m, null, null, null, "DRAFT", actor);
            else
            {
                foreach (var s in rows)
                    InsertRow(conn, tx, lineId, date, patternId, "WO", s.WoId, s.StartMin, s.EndMin, s.Qty, null, null, null, "DRAFT", actor, s.MoldId, ItemFor(s.WoId, s.MoldId));
                foreach (var p in keep)
                    InsertRow(conn, tx, lineId, date, patternId, "WO", p.WoId, p.StartMin, p.StartMin, p.Qty, null, null, null, "DRAFT", actor, null, p.ItemNo);
                foreach (var p in pms)
                    InsertRow(conn, tx, lineId, date, patternId, "PM", null, p.StartMin, p.EndMin, 0m, p.Title, p.RefType, p.RefId, "DRAFT", actor);
                foreach (var m in mcs)
                    InsertRow(conn, tx, lineId, date, patternId, "MC", null, m.StartMin, m.EndMin, 0m, m.Title, "WO", m.WoRefId, "DRAFT", actor, m.MoldId);
            }

            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    static void InsertRow(SqlConnection conn, SqlTransaction tx, string lineId, DateTime date,
        string? patternId, string entryType, int? woId, int? startMin, int? endMin, decimal qty,
        string? title, string? refType, int? refId, string status, string actor, string? moldId = null, string? itemNo = null)
    {
        using var cmd = new SqlCommand("""
            INSERT INTO dbo.PP_LineSchedule
                   (LineID, ScheduleDate, WoID, StartMin, EndMin, PlannedQty, PatternID,
                    EntryType, Title, RefType, RefID, Status, MoldID, ItemNo, CreatedBy, CreatedTS)
            VALUES (@LineId, @Date, @WoId, @Start, @End, @Qty, @Pattern,
                    @EntryType, @Title, @RefType, @RefID, @Status, @MoldId, @ItemNo, @By, SYSDATETIME());
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
        cmd.Parameters.Add("@MoldId",    SqlDbType.VarChar, 20).Value  = (object?)moldId ?? DBNull.Value;
        cmd.Parameters.Add("@ItemNo",    SqlDbType.VarChar, 20).Value  = (object?)itemNo ?? DBNull.Value;
        cmd.Parameters.Add("@By",        SqlDbType.VarChar, 20).Value  = actor;
        cmd.ExecuteNonQuery();
    }

    // 초기화: (라인, 일자)의 저장 스케줄 행 삭제. expectedVersion 규칙은 SaveSchedule 과 같다.
    public void DeleteSchedule(string lineId, DateTime date, string? expectedVersion = null)
    {
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            var current = LockAndCheck(conn, tx, lineId, date, expectedVersion);
            DeleteRows(conn, tx, lineId, date, current.Select(r => r.ScheduleId));
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    // 같은 (라인, 일자)를 고치는 보드끼리는 앱 잠금으로 줄을 세우고(빈 날짜는 잠글 행이 없어 둘 다 통과할 수 있다),
    // 지금 행을 UPDLOCK 으로 읽어 보드가 본 지문과 비교한다. PP-003·APS 처럼 앱 잠금을 쓰지 않는 쪽이 확인 뒤에 넣은 행은
    // DeleteRows 가 확인한 행 ID 만 지우므로 남는다
    static List<ScheduleRow> LockAndCheck(SqlConnection conn, SqlTransaction tx, string lineId, DateTime date, string? expectedVersion)
    {
        using (var lk = new SqlCommand("""
            DECLARE @rc int;
            EXEC @rc = sp_getapplock @Resource = @Res, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
            SELECT @rc;
            """, conn, tx))
        {
            lk.Parameters.Add("@Res", SqlDbType.NVarChar, 255).Value = $"PP_LineSchedule|{lineId}|{date:yyyy-MM-dd}";
            if (Convert.ToInt32(lk.ExecuteScalar()) < 0)
                throw new TimeoutException($"PP_LineSchedule {lineId} {date:yyyy-MM-dd} is being saved by another user.");
        }
        var current = ReadSchedule(conn, tx, lineId, date, forUpdate: true);
        if (expectedVersion is not null && VersionOf(current) != expectedVersion)
            throw new ScheduleChangedException(lineId, date);
        return current;
    }

    static void DeleteRows(SqlConnection conn, SqlTransaction tx, string lineId, DateTime date, IEnumerable<int> scheduleIds)
    {
        var ids = string.Join(',', scheduleIds);
        if (ids.Length == 0) return;
        using var del = new SqlCommand("""
            DELETE FROM dbo.PP_LineSchedule
            WHERE  LineID = @LineId AND ScheduleDate = @Date
              AND  ScheduleID IN (SELECT CAST(value AS int) FROM STRING_SPLIT(@Ids, ','));
            """, conn, tx);
        del.Parameters.Add("@LineId", SqlDbType.VarChar, 20).Value = lineId;
        del.Parameters.Add("@Date",   SqlDbType.Date).Value        = date.Date;
        del.Parameters.Add("@Ids",    SqlDbType.VarChar, -1).Value = ids;
        del.ExecuteNonQuery();
    }

    // ── PP-003 자동 배치용 하루 능력 ──────────────────────────────────────────
    /// <summary>
    /// (라인, 일자)의 패턴·가동 밴드·기존 점유(WO 슬롯 + PM 밴드). 패턴은 그 날 저장 행의 PatternID →
    /// 라인 전용 ACTIVE 패턴 → 전역 패턴 순으로 해석한다 (PP-LSB 보드의 PatternOptions 순서와 동일).
    /// 호출자가 patternId 를 주면(APS 경로, 2026-10-06) 그 해석을 전부 건너뛰고 그 패턴의 세그먼트를 읽는다 — 존재·상태는 호출자 책임.
    /// OperatingMin 은 PM 을 뺀 가동분, RemainMin 은 거기서 WO 슬롯을 뺀 값 (보드 KPI 와 같은 정의).
    /// </summary>
    public sealed record DayCapacity(
        string? PatternId, int DayStart,
        IReadOnlyList<SlotPacker.Interval> OperatingBands,
        IReadOnlyList<SlotPacker.Interval> Occupied,
        int OperatingMin, int WoLoadMin, int? LastWoEnd,
        string? LastMoldId = null,
        // APS(AMES.Data.Aps.ShiftBands.Split)용 — OPERATING 세그먼트 × 교대 SortOrder, PM 차감 전. 기본값이 있어 Occupy 의 with 식은 그대로 동작한다.
        IReadOnlyList<(SlotPacker.Interval Band, int ShiftSort, string ShiftCode)>? ShiftBands = null)
    {
        public int RemainMin => OperatingMin - WoLoadMin;
    }

    public DayCapacity GetDayCapacity(string lineId, DateTime date, string? patternId = null)
    {
        using var conn = _f.OpenConnection();
        return ReadDayCapacity(conn, null, lineId, date, patternId);
    }

    internal static DayCapacity ReadDayCapacity(SqlConnection conn, SqlTransaction? tx, string lineId, DateTime date, string? patternId = null)
    {
        const string sql = """
            DECLARE @Pat varchar(20) = @Override;
            IF @Pat IS NULL
                SET @Pat =
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

            SELECT s.StartMin, s.EndMin, s.SegmentState, ISNULL(c.SortOrder, 9999) AS ShiftSort, ISNULL(s.ShiftCode, '') AS ShiftCode
            FROM   dbo.MD_LineTimeSegment s
            LEFT JOIN dbo.MD_CodeItem c ON c.GroupCode = 'WORK_SHIFT' AND c.CodeValue = s.ShiftCode
            WHERE  s.PatternID = @Pat
              AND  s.StartMin IS NOT NULL AND s.EndMin IS NOT NULL AND s.EndMin > s.StartMin;

            SELECT s.EntryType, ISNULL(s.StartMin,0) AS StartMin, ISNULL(s.EndMin,0) AS EndMin, s.MoldID
            FROM   dbo.PP_LineSchedule s
            WHERE  s.LineID = @LineId AND s.ScheduleDate = @Date
              AND  ISNULL(s.EndMin,0) > ISNULL(s.StartMin,0);
            """;
        using var cmd = new SqlCommand(sql, conn, tx);
        cmd.Parameters.Add("@LineId",   SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@Date",     SqlDbType.Date).Value        = date.Date;
        cmd.Parameters.Add("@Override", SqlDbType.VarChar, 20).Value = (object?)(string.IsNullOrWhiteSpace(patternId) ? null : patternId.Trim()) ?? DBNull.Value;
        using var rdr = cmd.ExecuteReader();

        patternId = rdr.Read() ? rdr["PatternID"] as string : null;

        var segs = new List<(int Start, int End, string State, int ShiftSort, string ShiftCode)>();
        if (rdr.NextResult())
            while (rdr.Read())
                segs.Add((Convert.ToInt32(rdr["StartMin"]), Convert.ToInt32(rdr["EndMin"]),
                          rdr["SegmentState"] as string ?? "", Convert.ToInt32(rdr["ShiftSort"]), (string)rdr["ShiftCode"]));

        var wo = new List<(SlotPacker.Interval Iv, string? Mold)>();
        var pm = new List<SlotPacker.Interval>();
        if (rdr.NextResult())
            while (rdr.Read())
            {
                var iv = new SlotPacker.Interval(Convert.ToInt32(rdr["StartMin"]), Convert.ToInt32(rdr["EndMin"]));
                // MC(금형 교체) 는 PM 처럼 가동을 깎는 게 아니라 WO 와 같은 부하다
                if (rdr["EntryType"] as string == "PM") pm.Add(iv); else wo.Add((iv, rdr["MoldID"] as string));
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
        var shiftBands = segs.Where(s => s.State == "OPERATING")
                             .Select(s => (Band: new SlotPacker.Interval(s.Start, s.End), s.ShiftSort, s.ShiftCode)).ToList();
        int operatingMin = operating.Sum(b => Subtract(b, pm).Sum(x => x.EndMin - x.StartMin));
        int woLoad       = wo.Sum(w => w.Iv.EndMin - w.Iv.StartMin);
        int Axis(int m) { int r = (m - dayStart) % 1440; return r < 0 ? r + 1440 : r; }
        int AxisEnd((SlotPacker.Interval Iv, string? Mold) w) => Axis(w.Iv.StartMin) + (w.Iv.EndMin - w.Iv.StartMin);
        int?    lastWoEnd  = wo.Count == 0 ? null : wo.MaxBy(AxisEnd).Iv.EndMin;
        // 마지막 슬롯에 금형이 없으면(구 데이터·IMG) 그 날의 마지막 금형 있는 슬롯으로
        string? lastMoldId = wo.Where(w => w.Mold is not null).OrderByDescending(AxisEnd).Select(w => w.Mold).FirstOrDefault();

        return new DayCapacity(patternId, dayStart, operating, wo.Select(w => w.Iv).Concat(pm).ToList(),
                               operatingMin, woLoad, lastWoEnd, lastMoldId, shiftBands);
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

    /// <summary>
    /// date 이전 그 라인의 마지막 금형. 금형이 기록된(WO·MC) 가장 늦은 날짜를 찾아 그 날의 LastMoldId(축 기준)를 쓰고,
    /// 없으면 MNT_EquipmentStatus.MountedMoldID(현재 장착, Date=MinValue). 그것도 없으면 null.
    /// </summary>
    public (DateTime Date, string MoldId)? GetLineLastMoldBefore(string lineId, DateTime date)
    {
        using var conn = _f.OpenConnection();
        return LineLastMoldBefore(conn, null, lineId, date);
    }

    internal static (DateTime Date, string MoldId)? LineLastMoldBefore(SqlConnection conn, SqlTransaction? tx, string lineId, DateTime date)
    {
        DateTime? lastDate;
        using (var cmd = new SqlCommand("""
            SELECT MAX(ScheduleDate) FROM dbo.PP_LineSchedule
            WHERE  LineID = @LineId AND ScheduleDate < @Date AND MoldID IS NOT NULL
              AND  EntryType IN ('WO','MC') AND ISNULL(EndMin,0) > ISNULL(StartMin,0);
            """, conn, tx))
        {
            cmd.Parameters.Add("@LineId", SqlDbType.VarChar, 20).Value = lineId;
            cmd.Parameters.Add("@Date",   SqlDbType.Date).Value        = date.Date;
            lastDate = cmd.ExecuteScalar() as DateTime?;
        }
        if (lastDate is DateTime ld && ReadDayCapacity(conn, tx, lineId, ld).LastMoldId is { } mold)
            return (ld, mold);

        using (var cmd = new SqlCommand("""
            SELECT TOP 1 MountedMoldID FROM dbo.MNT_EquipmentStatus
            WHERE  LineID = @LineId AND MountedMoldID IS NOT NULL
            ORDER  BY ISNULL(ModifiedTS, CreatedTS) DESC, EquipStatusID DESC;
            """, conn, tx))
        {
            cmd.Parameters.Add("@LineId", SqlDbType.VarChar, 20).Value = lineId;
            return cmd.ExecuteScalar() is string mounted ? (DateTime.MinValue, mounted) : null;
        }
    }

    /// <summary>호출자 트랜잭션 안에서 WO 슬롯 1행 추가(DRAFT). 그 날의 다른 행은 건드리지 않는다. itemNo = 슬롯이 만드는 품번(NULL 이면 단계 품번 → WO 품번).</summary>
    internal static void AppendWoSlot(SqlConnection conn, SqlTransaction tx, string lineId, DateTime date,
        string? patternId, int woId, int startMin, int endMin, decimal qty, string? moldId, string actor, string? itemNo = null)
        => InsertRow(conn, tx, lineId, date, patternId, "WO", woId, startMin, endMin, qty, null, null, null, "DRAFT", actor, moldId, itemNo);

    /// <summary>슬롯 품번 해석 — 별칭 s = PP_LineSchedule, w = PP_WorkOrder. FROM 절 뒤에 <see cref="SlotItemApply"/> 를 붙이고 <see cref="SlotItemExpr"/> 로 읽는다.</summary>
    internal const string SlotItemApply = "OUTER APPLY (SELECT TOP 1 r0.ItemNo FROM dbo.PP_WorkOrderRouting r0 WHERE r0.WoID = s.WoID AND r0.LineID = s.LineID ORDER BY r0.StepSeq) sti";
    internal const string SlotItemExpr  = "COALESCE(s.ItemNo, sti.ItemNo, w.ItemNo)";

    /// <summary>금형 교체 블록 1행(EntryType MC, WoID 없음, RefType WO/RefID = 뒤따르는 WO). WO 취소 시 같이 지워진다.</summary>
    internal static void AppendMoldChangeSlot(SqlConnection conn, SqlTransaction tx, string lineId, DateTime date,
        string? patternId, int woId, string? fromMoldId, string toMoldId, int startMin, int endMin, string actor)
        => InsertRow(conn, tx, lineId, date, patternId, "MC", null, startMin, endMin, 0m,
                     $"{fromMoldId ?? ""}→{toMoldId}", "WO", woId, "DRAFT", actor, toMoldId);

    // 라인 배치용 WO 후보 (Released/In Progress). MD_Item 미등록 품목도 포함하도록 LEFT JOIN.
    // PlacedElsewhereQty = 이 라인의 다른 날짜에 이미 놓인 그 WO 슬롯 수량(시간을 점유한 슬롯만 — APS 형제 0분 행은 다른 품번 몫이라 뺀다).
    // OpenQty 는 WO 생성 때 주문 수량으로 채운 뒤 바뀌지 않으므로, 둘의 차가 아직 이 라인에 배치되지 않은 수량이다(10-13 수동 배치 기본값).
    public sealed record WoRow(int WoId, string? WoNumber, string? ItemNo, string? ItemName, decimal OpenQty, string? Status,
        decimal PlacedElsewhereQty = 0)
    {
        public decimal UnplacedQty => Math.Max(0, OpenQty - PlacedElsewhereQty);
    }

    public List<WoRow> ListLineWos(string lineId, DateTime boardDate)
    {
        const string sql = """
            SELECT w.WoID, w.WoNumber, w.ItemNo, i.ItemName,
                   ISNULL(w.OpenQty,0) AS OpenQty, r.Status,
                   ISNULL((SELECT SUM(s.PlannedQty) FROM dbo.PP_LineSchedule s
                           WHERE s.WoID = w.WoID AND s.LineID = @LineId AND s.EntryType = 'WO'
                             AND s.ScheduleDate <> @Date AND s.EndMin > s.StartMin), 0) AS PlacedElsewhereQty
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
        cmd.Parameters.Add("@Date", SqlDbType.Date).Value = boardDate.Date;
        using var rdr = cmd.ExecuteReader();
        var list = new List<WoRow>();
        while (rdr.Read())
            list.Add(new WoRow(
                (int)rdr["WoID"],
                rdr["WoNumber"] as string,
                rdr["ItemNo"]   as string,
                rdr["ItemName"] as string,
                rdr.GetDecimal(rdr.GetOrdinal("OpenQty")),
                rdr["Status"]   as string,
                Convert.ToDecimal(rdr["PlacedElsewhereQty"])));
        return list;
    }

    // ── PM 후보 (MNT_PMSchedule → 해당 라인 설비) ─────────────────────────────
    // 라인은 설비(MD_Equipment.LineID)로 해석. 취소된 PM 과 보전 PM(MAINT — 라인을 멈추지 않는 점검, PmRules)은 제외.
    public sealed record PmCandidate(
        int PmScheduleId, string EquipId, string? EquipName, string? PmType, DateTime? NextDueDate, string? PmClass = null);

    public List<PmCandidate> ListDuePmForLine(string lineId)
    {
        const string sql = """
            SELECT p.PMScheduleID, p.EquipID, e.EquipName, p.PMType, p.NextDueDate, p.PMClass
            FROM   dbo.MNT_PMSchedule p
            JOIN   dbo.MD_Equipment   e ON e.EquipID = p.EquipID
            WHERE  e.LineID = @LineId
              AND  ISNULL(p.Status,'') <> 'CANCELLED'
              AND  ISNULL(p.PMClass,'') <> 'MAINT'
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
                rdr["NextDueDate"] as DateTime?,
                rdr["PMClass"]     as string));
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
