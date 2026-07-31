using System.Data;
using AMES.Data.Connection;
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
    public void PublishSchedule(string lineId, DateTime date, string actor)
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand("""
            UPDATE dbo.PP_LineSchedule
            SET    Status='PUBLISHED', PublishedAt=SYSDATETIME(), PublishedBy=@By,
                   ModifiedBy=@By, ModifiedTS=SYSDATETIME()
            WHERE  LineID=@LineId AND ScheduleDate=@Date;
            """, conn);
        cmd.Parameters.Add("@LineId", SqlDbType.VarChar,  20).Value = lineId;
        cmd.Parameters.Add("@Date",   SqlDbType.Date).Value         = date.Date;
        cmd.Parameters.Add("@By",     SqlDbType.NVarChar, 450).Value = actor;
        cmd.ExecuteNonQuery();
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

    // 라인 배치용 WO 후보 (Released/In Progress). MD_Item 미등록 품목도 포함하도록 LEFT JOIN.
    public sealed record WoRow(int WoId, string? WoNumber, string? ItemNo, string? ItemName, decimal OpenQty, string? Status);

    public List<WoRow> ListLineWos(string lineId)
    {
        const string sql = """
            SELECT w.WoID, w.WoNumber, w.ItemNo, i.ItemName,
                   ISNULL(w.OpenQty,0) AS OpenQty, w.Status
            FROM   dbo.PP_WorkOrder w
            LEFT JOIN dbo.MD_Item i ON i.ItemNo = w.ItemNo
            WHERE  w.LineID = @LineId
              AND  w.Status IN ('Released','In Progress')
            ORDER  BY CASE WHEN w.Status='In Progress' THEN 0 ELSE 1 END, w.WoID;
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
