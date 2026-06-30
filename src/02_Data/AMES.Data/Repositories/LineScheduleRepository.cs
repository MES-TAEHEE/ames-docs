using System.Data;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// PP-LSB Line Schedule Board — pattern bands (MD_LinePattern) + WO slot placements (PP_LineSchedule).
/// MD_LinePattern is auto-created on first use; PP_LineSchedule is owned by PpRepository schema.
/// </summary>
public sealed class LineScheduleRepository
{
    readonly AmesConnectionFactory _f;

    public LineScheduleRepository(AmesConnectionFactory f)
    {
        _f = f;
        TryEnsureTables();
    }

    // ── Records ──────────────────────────────────────────────────────────────
    public sealed record PatternBand(
        int PatternId, string LineId, string DayType,
        int StartMin, int EndMin, string BandType, string? Label);

    public sealed record WoSlot(
        int ScheduleId, string LineId, DateTime? ScheduleDate,
        int? WoId, string? WoNumber, string? ItemName,
        int StartMin, int EndMin, decimal PlannedQty, string? Status);

    // ── DDL (idempotent) ─────────────────────────────────────────────────────
    void TryEnsureTables()
    {
        const string ddl = """
            IF OBJECT_ID('dbo.MD_LinePattern','U') IS NULL
            CREATE TABLE dbo.MD_LinePattern (
                PatternId  INT IDENTITY(1,1) PRIMARY KEY,
                LineId     VARCHAR(20)   NOT NULL,
                DayType    VARCHAR(20)   NOT NULL DEFAULT 'WORKDAY',
                StartMin   INT           NOT NULL,
                EndMin     INT           NOT NULL,
                BandType   VARCHAR(20)   NOT NULL DEFAULT 'WORK',
                Label      NVARCHAR(100) NULL,
                UpdatedAt  DATETIME2     NOT NULL DEFAULT SYSDATETIME(),
                UpdatedBy  VARCHAR(50)   NULL
            );
            """;
        try
        {
            using var conn = _f.OpenConnection();
            using var cmd  = new SqlCommand(ddl, conn);
            cmd.ExecuteNonQuery();
        }
        catch { /* DB not available at startup — will retry on first query */ }
    }

    // ── Pattern CRUD ─────────────────────────────────────────────────────────
    public List<PatternBand> GetPattern(string lineId, string dayType = "WORKDAY")
    {
        const string sql = """
            SELECT PatternId, LineId, DayType, StartMin, EndMin, BandType, Label
            FROM   dbo.MD_LinePattern
            WHERE  LineId  = @LineId
              AND  DayType = @DayType
            ORDER  BY StartMin;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@LineId",  SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@DayType", SqlDbType.VarChar, 20).Value = dayType;
        using var rdr = cmd.ExecuteReader();
        var list = new List<PatternBand>();
        while (rdr.Read())
            list.Add(new PatternBand(
                (int)rdr["PatternId"],
                (string)rdr["LineId"],
                (string)rdr["DayType"],
                (int)rdr["StartMin"],
                (int)rdr["EndMin"],
                (string)rdr["BandType"],
                rdr["Label"] as string));
        return list;
    }

    public int AddBand(string lineId, string dayType, int startMin, int endMin,
                       string bandType, string? label, string? updatedBy)
    {
        const string sql = """
            INSERT INTO dbo.MD_LinePattern
                   (LineId, DayType, StartMin, EndMin, BandType, Label, UpdatedBy)
            VALUES (@LineId, @DayType, @Start, @End, @BandType, @Label, @By);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@LineId",  SqlDbType.VarChar, 20).Value  = lineId;
        cmd.Parameters.Add("@DayType", SqlDbType.VarChar, 20).Value  = dayType;
        cmd.Parameters.Add("@Start",   SqlDbType.Int).Value          = startMin;
        cmd.Parameters.Add("@End",     SqlDbType.Int).Value          = endMin;
        cmd.Parameters.Add("@BandType",SqlDbType.VarChar, 20).Value  = bandType;
        cmd.Parameters.Add("@Label",   SqlDbType.NVarChar,100).Value = (object?)label ?? DBNull.Value;
        cmd.Parameters.Add("@By",      SqlDbType.VarChar, 50).Value  = (object?)updatedBy ?? DBNull.Value;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>
    /// "페인트 붓" 삽입 — 새 범위 [newStart, newEnd]와 겹치는 기존 밴드를
    /// 잘라내거나 분할한 뒤 새 밴드를 삽입한다. 트랜잭션으로 원자적 처리.
    /// </summary>
    public int PaintBand(string lineId, string dayType, int newStart, int newEnd,
                         string bandType, string? label, string? updatedBy)
    {
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            // 1) 겹치는 기존 밴드 조회
            var overlapping = new List<PatternBand>();
            using (var cmd = new SqlCommand("""
                SELECT PatternId, LineId, DayType, StartMin, EndMin, BandType, Label
                FROM   dbo.MD_LinePattern
                WHERE  LineId  = @LineId
                  AND  DayType = @DayType
                  AND  StartMin < @End
                  AND  EndMin   > @Start
                ORDER  BY StartMin;
                """, conn, tx))
            {
                cmd.Parameters.Add("@LineId",  SqlDbType.VarChar, 20).Value = lineId;
                cmd.Parameters.Add("@DayType", SqlDbType.VarChar, 20).Value = dayType;
                cmd.Parameters.Add("@Start",   SqlDbType.Int).Value         = newStart;
                cmd.Parameters.Add("@End",     SqlDbType.Int).Value         = newEnd;
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                    overlapping.Add(new PatternBand(
                        (int)rdr["PatternId"], (string)rdr["LineId"], (string)rdr["DayType"],
                        (int)rdr["StartMin"],  (int)rdr["EndMin"],
                        (string)rdr["BandType"], rdr["Label"] as string));
            }

            // 2) 각 겹치는 밴드 처리
            foreach (var band in overlapping)
            {
                bool leftFree  = band.StartMin < newStart; // 새 범위 왼쪽에 잔여 구간 있음
                bool rightFree = band.EndMin   > newEnd;   // 새 범위 오른쪽에 잔여 구간 있음

                if (!leftFree && !rightFree)
                {
                    // 완전히 포함 → 삭제
                    using var cmd = new SqlCommand(
                        "DELETE FROM dbo.MD_LinePattern WHERE PatternId=@Id;", conn, tx);
                    cmd.Parameters.Add("@Id", SqlDbType.Int).Value = band.PatternId;
                    cmd.ExecuteNonQuery();
                }
                else if (leftFree && rightFree)
                {
                    // 새 범위가 기존 밴드 중간을 뚫음 → 왼쪽 잔여로 수정 + 오른쪽 잔여 신규 삽입
                    using (var cmd = new SqlCommand(
                        "UPDATE dbo.MD_LinePattern SET EndMin=@End,UpdatedAt=SYSDATETIME(),UpdatedBy=@By WHERE PatternId=@Id;",
                        conn, tx))
                    {
                        cmd.Parameters.Add("@Id",  SqlDbType.Int).Value         = band.PatternId;
                        cmd.Parameters.Add("@End", SqlDbType.Int).Value         = newStart;
                        cmd.Parameters.Add("@By",  SqlDbType.VarChar,50).Value  = (object?)updatedBy ?? DBNull.Value;
                        cmd.ExecuteNonQuery();
                    }
                    using (var cmd = new SqlCommand("""
                        INSERT INTO dbo.MD_LinePattern (LineId,DayType,StartMin,EndMin,BandType,Label,UpdatedBy)
                        VALUES (@LineId,@DayType,@Start,@End,@BandType,@Label,@By);
                        """, conn, tx))
                    {
                        cmd.Parameters.Add("@LineId",  SqlDbType.VarChar, 20).Value  = lineId;
                        cmd.Parameters.Add("@DayType", SqlDbType.VarChar, 20).Value  = dayType;
                        cmd.Parameters.Add("@Start",   SqlDbType.Int).Value          = newEnd;
                        cmd.Parameters.Add("@End",     SqlDbType.Int).Value          = band.EndMin;
                        cmd.Parameters.Add("@BandType",SqlDbType.VarChar, 20).Value  = band.BandType;
                        cmd.Parameters.Add("@Label",   SqlDbType.NVarChar,100).Value = (object?)band.Label ?? DBNull.Value;
                        cmd.Parameters.Add("@By",      SqlDbType.VarChar, 50).Value  = (object?)updatedBy ?? DBNull.Value;
                        cmd.ExecuteNonQuery();
                    }
                }
                else if (leftFree)
                {
                    // 오른쪽이 새 범위와 겹침 → EndMin을 newStart로 잘라냄
                    using var cmd = new SqlCommand(
                        "UPDATE dbo.MD_LinePattern SET EndMin=@End,UpdatedAt=SYSDATETIME(),UpdatedBy=@By WHERE PatternId=@Id;",
                        conn, tx);
                    cmd.Parameters.Add("@Id",  SqlDbType.Int).Value         = band.PatternId;
                    cmd.Parameters.Add("@End", SqlDbType.Int).Value         = newStart;
                    cmd.Parameters.Add("@By",  SqlDbType.VarChar,50).Value  = (object?)updatedBy ?? DBNull.Value;
                    cmd.ExecuteNonQuery();
                }
                else // rightFree
                {
                    // 왼쪽이 새 범위와 겹침 → StartMin을 newEnd로 민다
                    using var cmd = new SqlCommand(
                        "UPDATE dbo.MD_LinePattern SET StartMin=@Start,UpdatedAt=SYSDATETIME(),UpdatedBy=@By WHERE PatternId=@Id;",
                        conn, tx);
                    cmd.Parameters.Add("@Id",    SqlDbType.Int).Value         = band.PatternId;
                    cmd.Parameters.Add("@Start", SqlDbType.Int).Value         = newEnd;
                    cmd.Parameters.Add("@By",    SqlDbType.VarChar,50).Value  = (object?)updatedBy ?? DBNull.Value;
                    cmd.ExecuteNonQuery();
                }
            }

            // 3) 새 밴드 삽입
            int newId;
            using (var cmd = new SqlCommand("""
                INSERT INTO dbo.MD_LinePattern (LineId,DayType,StartMin,EndMin,BandType,Label,UpdatedBy)
                VALUES (@LineId,@DayType,@Start,@End,@BandType,@Label,@By);
                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """, conn, tx))
            {
                cmd.Parameters.Add("@LineId",  SqlDbType.VarChar, 20).Value  = lineId;
                cmd.Parameters.Add("@DayType", SqlDbType.VarChar, 20).Value  = dayType;
                cmd.Parameters.Add("@Start",   SqlDbType.Int).Value          = newStart;
                cmd.Parameters.Add("@End",     SqlDbType.Int).Value          = newEnd;
                cmd.Parameters.Add("@BandType",SqlDbType.VarChar, 20).Value  = bandType;
                cmd.Parameters.Add("@Label",   SqlDbType.NVarChar,100).Value = (object?)label ?? DBNull.Value;
                cmd.Parameters.Add("@By",      SqlDbType.VarChar, 50).Value  = (object?)updatedBy ?? DBNull.Value;
                newId = Convert.ToInt32(cmd.ExecuteScalar());
            }

            tx.Commit();
            return newId;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public void DeleteBand(int patternId)
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(
            "DELETE FROM dbo.MD_LinePattern WHERE PatternId = @Id;", conn);
        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = patternId;
        cmd.ExecuteNonQuery();
    }

    public void ClearPattern(string lineId, string dayType)
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(
            "DELETE FROM dbo.MD_LinePattern WHERE LineId=@LineId AND DayType=@DayType;", conn);
        cmd.Parameters.Add("@LineId",  SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@DayType", SqlDbType.VarChar, 20).Value = dayType;
        cmd.ExecuteNonQuery();
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
