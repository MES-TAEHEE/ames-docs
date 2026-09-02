using System.Data;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// PP-DEE OEE 산출: PP_LineStateLog → 계산 → PP_LineOEE 스냅샷 저장.
/// 두 테이블 모두 자동 생성(최초 실행 시).
/// </summary>
public sealed class OeeRepository
{
    readonly AmesConnectionFactory _f;
    const decimal DEFAULT_TARGET = 75m;

    public OeeRepository(AmesConnectionFactory f)
    {
        _f = f;
        TryEnsureTables();
    }

    // ── Records ──────────────────────────────────────────────────────────
    public sealed record EquipSignal(
        int SignalId, string LineId, DateTime SignalTime, bool IsRunning, string? Source);

    // ── DDL ──────────────────────────────────────────────────────────────
    void TryEnsureTables()
    {
        const string ddl = """
            IF OBJECT_ID('dbo.PP_LineStateLog','U') IS NULL
            CREATE TABLE dbo.PP_LineStateLog (
                LogId             INT IDENTITY(1,1) PRIMARY KEY,
                LineId            VARCHAR(20)   NOT NULL,
                LogDate           DATE          NOT NULL,
                StateCode         VARCHAR(30)   NOT NULL DEFAULT 'LOAD',
                StartMin          INT           NOT NULL DEFAULT 0,
                EndMin            INT           NOT NULL DEFAULT 0,
                ActualOutput      INT           NULL,
                GoodOutput        INT           NULL,
                TheoreticalOutput INT           NULL,
                Notes             NVARCHAR(200) NULL,
                CreatedAt         DATETIME2     NOT NULL DEFAULT SYSDATETIME(),
                CreatedBy         VARCHAR(50)   NULL
            );

            IF OBJECT_ID('dbo.PP_EquipSignal','U') IS NULL
            CREATE TABLE dbo.PP_EquipSignal (
                SignalId   INT IDENTITY(1,1) PRIMARY KEY,
                LineId     VARCHAR(20)  NOT NULL,
                SignalTime DATETIME2    NOT NULL DEFAULT SYSDATETIME(),
                IsRunning  BIT          NOT NULL DEFAULT 0,
                Source     VARCHAR(30)  NULL DEFAULT 'WEB',
                CreatedBy  VARCHAR(50)  NULL
            );

            IF NOT EXISTS (SELECT 1 FROM sys.indexes
                           WHERE name = 'IX_PP_EquipSignal_Line_Time'
                             AND object_id = OBJECT_ID('dbo.PP_EquipSignal'))
            CREATE INDEX IX_PP_EquipSignal_Line_Time
                ON dbo.PP_EquipSignal (LineId, SignalTime DESC);

            IF OBJECT_ID('dbo.PP_LineOEE','U') IS NULL
            CREATE TABLE dbo.PP_LineOEE (
                OeeId             INT IDENTITY(1,1) PRIMARY KEY,
                LineId            VARCHAR(20)   NOT NULL,
                PeriodType        VARCHAR(10)   NOT NULL,
                PeriodStart       DATE          NOT NULL,
                PeriodEnd         DATE          NOT NULL,
                LoadMin           INT           NOT NULL DEFAULT 0,
                PlannedDownMin    INT           NOT NULL DEFAULT 0,
                UnplannedDownMin  INT           NOT NULL DEFAULT 0,
                AvailMin          INT           NOT NULL DEFAULT 0,
                Availability      DECIMAL(6,2)  NOT NULL DEFAULT 0,
                ActualOutput      INT           NOT NULL DEFAULT 0,
                TheoreticalOutput INT           NOT NULL DEFAULT 0,
                Performance       DECIMAL(6,2)  NOT NULL DEFAULT 0,
                GoodOutput        INT           NOT NULL DEFAULT 0,
                Quality           DECIMAL(6,2)  NOT NULL DEFAULT 0,
                OeeRate           DECIMAL(6,2)  NOT NULL DEFAULT 0,
                TargetOee         DECIMAL(6,2)  NOT NULL DEFAULT 75,
                CreatedAt         DATETIME2     NOT NULL DEFAULT SYSDATETIME(),
                CreatedBy         VARCHAR(50)   NULL
            );
            """;
        try
        {
            using var conn = _f.OpenConnection();
            using var cmd  = new SqlCommand(ddl, conn);
            cmd.ExecuteNonQuery();
        }
        catch { }
    }

    // ── Lines ─────────────────────────────────────────────────────────────
    public List<string> ListLines()
    {
        const string sql = """
            SELECT DISTINCT LineId FROM (
                SELECT LineID AS LineId FROM dbo.PP_WorkOrderRouting WHERE LineID IS NOT NULL
                UNION
                SELECT LineId FROM dbo.PP_LineStateLog
            ) t ORDER BY LineId;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        using var rdr  = cmd.ExecuteReader();
        var list = new List<string>();
        while (rdr.Read()) list.Add((string)rdr["LineId"]);
        return list;
    }

    // ── State Log CRUD ────────────────────────────────────────────────────
    public List<LineStateLogDto> GetStateLogs(string lineId, DateTime startDate, DateTime endDate)
    {
        const string sql = """
            SELECT LogId, LineId, LogDate, StateCode, StartMin, EndMin,
                   ActualOutput, GoodOutput, TheoreticalOutput, Notes, CreatedAt, CreatedBy
            FROM   dbo.PP_LineStateLog
            WHERE  LineId  = @LineId
              AND  LogDate BETWEEN @Start AND @End
            ORDER  BY LogDate, StartMin;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@LineId", SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@Start",  SqlDbType.Date).Value        = startDate.Date;
        cmd.Parameters.Add("@End",    SqlDbType.Date).Value        = endDate.Date;
        using var rdr = cmd.ExecuteReader();
        var list = new List<LineStateLogDto>();
        while (rdr.Read())
            list.Add(new()
            {
                LogId     = (int)rdr["LogId"],
                LineId    = (string)rdr["LineId"],
                LogDate   = (DateTime)rdr["LogDate"],
                StateCode = (string)rdr["StateCode"],
                StartMin  = (int)rdr["StartMin"],
                EndMin    = (int)rdr["EndMin"],
                ActualOutput      = rdr["ActualOutput"]      is DBNull ? null : (int?)rdr["ActualOutput"],
                GoodOutput        = rdr["GoodOutput"]        is DBNull ? null : (int?)rdr["GoodOutput"],
                TheoreticalOutput = rdr["TheoreticalOutput"] is DBNull ? null : (int?)rdr["TheoreticalOutput"],
                Notes     = rdr["Notes"]     as string,
                CreatedAt = (DateTime)rdr["CreatedAt"],
                CreatedBy = rdr["CreatedBy"] as string,
            });
        return list;
    }

    public void AddStateLog(string lineId, DateTime logDate, string stateCode,
        int startMin, int endMin, int? actualOutput, int? goodOutput,
        int? theoreticalOutput, string? notes, string? createdBy)
    {
        const string sql = """
            INSERT INTO dbo.PP_LineStateLog
                   (LineId,LogDate,StateCode,StartMin,EndMin,
                    ActualOutput,GoodOutput,TheoreticalOutput,Notes,CreatedBy)
            VALUES (@LineId,@Date,@State,@Start,@End,
                    @Actual,@Good,@Theo,@Notes,@By);
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@LineId", SqlDbType.VarChar, 20).Value   = lineId;
        cmd.Parameters.Add("@Date",   SqlDbType.Date).Value          = logDate.Date;
        cmd.Parameters.Add("@State",  SqlDbType.VarChar, 30).Value   = stateCode;
        cmd.Parameters.Add("@Start",  SqlDbType.Int).Value           = startMin;
        cmd.Parameters.Add("@End",    SqlDbType.Int).Value           = endMin;
        cmd.Parameters.Add("@Actual", SqlDbType.Int).Value           = (object?)actualOutput       ?? DBNull.Value;
        cmd.Parameters.Add("@Good",   SqlDbType.Int).Value           = (object?)goodOutput         ?? DBNull.Value;
        cmd.Parameters.Add("@Theo",   SqlDbType.Int).Value           = (object?)theoreticalOutput  ?? DBNull.Value;
        cmd.Parameters.Add("@Notes",  SqlDbType.NVarChar, 200).Value = (object?)notes              ?? DBNull.Value;
        cmd.Parameters.Add("@By",     SqlDbType.VarChar, 50).Value   = (object?)createdBy          ?? DBNull.Value;
        cmd.ExecuteNonQuery();
    }

    public void DeleteStateLog(int logId)
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand("DELETE FROM dbo.PP_LineStateLog WHERE LogId=@Id;", conn);
        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = logId;
        cmd.ExecuteNonQuery();
    }

    // ── OEE Calculation ───────────────────────────────────────────────────
    /// <summary>PP_LineStateLog 집계 → OEE = A × P × Q 산출 (저장 안 함)</summary>
    public OeeSnapshotDto ComputeOee(
        string lineId, string periodType, DateTime startDate, DateTime endDate,
        decimal targetOee = DEFAULT_TARGET)
    {
        const string sql = """
            SELECT
                StateCode,
                SUM(CASE WHEN EndMin > StartMin THEN EndMin - StartMin ELSE 0 END) AS TotalMin,
                SUM(ISNULL(ActualOutput, 0))      AS TotalActual,
                SUM(ISNULL(GoodOutput, 0))        AS TotalGood,
                SUM(ISNULL(TheoreticalOutput, 0)) AS TotalTheo
            FROM   dbo.PP_LineStateLog
            WHERE  LineId  = @LineId
              AND  LogDate BETWEEN @Start AND @End
            GROUP  BY StateCode;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@LineId", SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@Start",  SqlDbType.Date).Value        = startDate.Date;
        cmd.Parameters.Add("@End",    SqlDbType.Date).Value        = endDate.Date;

        int loadMin = 0, plannedDown = 0, unplannedDown = 0;
        int actualOutput = 0, goodOutput = 0, theoreticalOutput = 0;

        using (var rdr = cmd.ExecuteReader())
        {
            while (rdr.Read())
            {
                var code  = (string)rdr["StateCode"];
                var mins  = Convert.ToInt32(rdr["TotalMin"]);
                actualOutput      += Convert.ToInt32(rdr["TotalActual"]);
                goodOutput        += Convert.ToInt32(rdr["TotalGood"]);
                theoreticalOutput += Convert.ToInt32(rdr["TotalTheo"]);
                switch (code)
                {
                    case "LOAD":           loadMin      += mins; break;
                    case "PLANNED_DOWN":   plannedDown  += mins; break;
                    case "UNPLANNED_DOWN": unplannedDown += mins; break;
                }
            }
        }

        // 부하시간이 없으면 기간 × 8h 기본값
        if (loadMin == 0)
            loadMin = ((endDate.Date - startDate.Date).Days + 1) * 480;

        int     availMin = Math.Max(0, loadMin - plannedDown - unplannedDown);
        decimal avail    = loadMin > 0 ? Math.Round((decimal)availMin / loadMin * 100m, 2) : 0m;
        decimal perf     = theoreticalOutput > 0
            ? Math.Min(100m, Math.Round((decimal)actualOutput / theoreticalOutput * 100m, 2))
            : (availMin > 0 ? 100m : 0m);
        decimal qual     = actualOutput > 0
            ? Math.Round((decimal)goodOutput / actualOutput * 100m, 2)
            : (perf > 0 ? 100m : 0m);
        decimal oee      = Math.Round(avail / 100m * perf / 100m * qual / 100m * 100m, 2);

        return new()
        {
            LineId           = lineId,
            PeriodType       = periodType,
            PeriodStart      = startDate.Date,
            PeriodEnd        = endDate.Date,
            LoadMin          = loadMin,
            PlannedDownMin   = plannedDown,
            UnplannedDownMin = unplannedDown,
            AvailMin         = availMin,
            Availability     = avail,
            ActualOutput     = actualOutput,
            TheoreticalOutput = theoreticalOutput,
            Performance      = perf,
            GoodOutput       = goodOutput,
            Quality          = qual,
            OeeRate          = oee,
            TargetOee        = targetOee,
            CreatedAt        = DateTime.Now,
        };
    }

    // ── Snapshot CRUD ─────────────────────────────────────────────────────
    public int SaveSnapshot(OeeSnapshotDto s, string? savedBy)
    {
        const string sql = """
            INSERT INTO dbo.PP_LineOEE
                   (LineId,PeriodType,PeriodStart,PeriodEnd,
                    LoadMin,PlannedDownMin,UnplannedDownMin,AvailMin,
                    Availability,ActualOutput,TheoreticalOutput,Performance,
                    GoodOutput,Quality,OeeRate,TargetOee,CreatedBy)
            VALUES (@LineId,@PType,@PStart,@PEnd,
                    @Load,@PlDn,@UnDn,@Avail,
                    @A,@Actual,@Theo,@P,
                    @Good,@Q,@Oee,@Target,@By);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@LineId", SqlDbType.VarChar, 20).Value  = s.LineId;
        cmd.Parameters.Add("@PType",  SqlDbType.VarChar, 10).Value  = s.PeriodType;
        cmd.Parameters.Add("@PStart", SqlDbType.Date).Value         = s.PeriodStart;
        cmd.Parameters.Add("@PEnd",   SqlDbType.Date).Value         = s.PeriodEnd;
        cmd.Parameters.Add("@Load",   SqlDbType.Int).Value          = s.LoadMin;
        cmd.Parameters.Add("@PlDn",   SqlDbType.Int).Value          = s.PlannedDownMin;
        cmd.Parameters.Add("@UnDn",   SqlDbType.Int).Value          = s.UnplannedDownMin;
        cmd.Parameters.Add("@Avail",  SqlDbType.Int).Value          = s.AvailMin;
        cmd.Parameters.Add("@A",      SqlDbType.Decimal).Value      = s.Availability;
        cmd.Parameters.Add("@Actual", SqlDbType.Int).Value          = s.ActualOutput;
        cmd.Parameters.Add("@Theo",   SqlDbType.Int).Value          = s.TheoreticalOutput;
        cmd.Parameters.Add("@P",      SqlDbType.Decimal).Value      = s.Performance;
        cmd.Parameters.Add("@Good",   SqlDbType.Int).Value          = s.GoodOutput;
        cmd.Parameters.Add("@Q",      SqlDbType.Decimal).Value      = s.Quality;
        cmd.Parameters.Add("@Oee",    SqlDbType.Decimal).Value      = s.OeeRate;
        cmd.Parameters.Add("@Target", SqlDbType.Decimal).Value      = s.TargetOee;
        cmd.Parameters.Add("@By",     SqlDbType.VarChar, 50).Value  = (object?)savedBy ?? DBNull.Value;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public List<OeeSnapshotDto> GetSnapshots(
        string lineId, string? periodType = null, DateTime? from = null,
        DateTime? to = null, int limit = 60)
    {
        var sql = $"""
            SELECT TOP (@Limit)
                OeeId,LineId,PeriodType,PeriodStart,PeriodEnd,
                LoadMin,PlannedDownMin,UnplannedDownMin,AvailMin,
                Availability,ActualOutput,TheoreticalOutput,Performance,
                GoodOutput,Quality,OeeRate,TargetOee,CreatedAt,CreatedBy
            FROM dbo.PP_LineOEE
            WHERE LineId = @LineId
            {(periodType != null ? "AND PeriodType = @PType " : "")}
            {(from        != null ? "AND PeriodStart >= @From " : "")}
            {(to          != null ? "AND PeriodEnd   <= @To "   : "")}
            ORDER BY PeriodStart DESC;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Limit",  SqlDbType.Int).Value          = limit;
        cmd.Parameters.Add("@LineId", SqlDbType.VarChar, 20).Value  = lineId;
        if (periodType != null) cmd.Parameters.Add("@PType", SqlDbType.VarChar, 10).Value = periodType;
        if (from != null) cmd.Parameters.Add("@From", SqlDbType.Date).Value = from.Value.Date;
        if (to   != null) cmd.Parameters.Add("@To",   SqlDbType.Date).Value = to.Value.Date;
        using var rdr = cmd.ExecuteReader();
        var list = new List<OeeSnapshotDto>();
        while (rdr.Read())
            list.Add(new()
            {
                OeeId    = (int)rdr["OeeId"],
                LineId   = (string)rdr["LineId"],
                PeriodType  = (string)rdr["PeriodType"],
                PeriodStart = (DateTime)rdr["PeriodStart"],
                PeriodEnd   = (DateTime)rdr["PeriodEnd"],
                LoadMin          = (int)rdr["LoadMin"],
                PlannedDownMin   = (int)rdr["PlannedDownMin"],
                UnplannedDownMin = (int)rdr["UnplannedDownMin"],
                AvailMin         = (int)rdr["AvailMin"],
                Availability     = (decimal)rdr["Availability"],
                ActualOutput     = (int)rdr["ActualOutput"],
                TheoreticalOutput = (int)rdr["TheoreticalOutput"],
                Performance      = (decimal)rdr["Performance"],
                GoodOutput       = (int)rdr["GoodOutput"],
                Quality          = (decimal)rdr["Quality"],
                OeeRate          = (decimal)rdr["OeeRate"],
                TargetOee        = (decimal)rdr["TargetOee"],
                CreatedAt        = (DateTime)rdr["CreatedAt"],
                CreatedBy        = rdr["CreatedBy"] as string,
            });
        return list;
    }

    public void DeleteSnapshot(int oeeId)
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand("DELETE FROM dbo.PP_LineOEE WHERE OeeId=@Id;", conn);
        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = oeeId;
        cmd.ExecuteNonQuery();
    }

    // ── Equipment Signal (PP-ODM 실시간 신호) ────────────────────────────
    /// <summary>라인 가동/정지 신호를 PP_EquipSignal에 기록한다.</summary>
    public void SetEquipSignal(string lineId, bool isRunning, string? source = "WEB", string? createdBy = null)
    {
        const string sql = """
            INSERT INTO dbo.PP_EquipSignal (LineId, IsRunning, Source, CreatedBy)
            VALUES (@LineId, @Running, @Source, @By);
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@LineId",  SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@Running", SqlDbType.Bit).Value         = isRunning;
        cmd.Parameters.Add("@Source",  SqlDbType.VarChar, 30).Value = (object?)source    ?? DBNull.Value;
        cmd.Parameters.Add("@By",      SqlDbType.VarChar, 50).Value = (object?)createdBy ?? DBNull.Value;
        cmd.ExecuteNonQuery();
    }

    /// <summary>라인별 최신 신호 1건씩 반환 (현재 가동/정지 상태).</summary>
    public Dictionary<string, EquipSignal> GetLatestSignalPerLine()
    {
        const string sql = """
            SELECT s.SignalId, s.LineId, s.SignalTime, s.IsRunning, s.Source
            FROM   dbo.PP_EquipSignal s
            INNER JOIN (
                SELECT LineId, MAX(SignalTime) AS MaxTime
                FROM   dbo.PP_EquipSignal
                GROUP  BY LineId
            ) m ON s.LineId = m.LineId AND s.SignalTime = m.MaxTime;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        using var rdr  = cmd.ExecuteReader();
        var dict = new Dictionary<string, EquipSignal>(StringComparer.OrdinalIgnoreCase);
        while (rdr.Read())
        {
            var sig = new EquipSignal(
                (int)rdr["SignalId"], (string)rdr["LineId"],
                (DateTime)rdr["SignalTime"], Convert.ToBoolean(rdr["IsRunning"]),
                rdr["Source"] as string);
            dict[sig.LineId] = sig;
        }
        return dict;
    }

    /// <summary>특정 라인의 최근 N분 신호 이력 (타임라인 표시용).</summary>
    public List<EquipSignal> GetSignalHistory(string lineId, int minutesBack = 60)
    {
        const string sql = """
            SELECT TOP 500 SignalId, LineId, SignalTime, IsRunning, Source
            FROM   dbo.PP_EquipSignal
            WHERE  LineId     = @LineId
              AND  SignalTime > DATEADD(minute, -@M, SYSDATETIME())
            ORDER  BY SignalTime ASC;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@LineId", SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@M",      SqlDbType.Int).Value         = minutesBack;
        using var rdr = cmd.ExecuteReader();
        var list = new List<EquipSignal>();
        while (rdr.Read())
            list.Add(new EquipSignal(
                (int)rdr["SignalId"], (string)rdr["LineId"],
                (DateTime)rdr["SignalTime"], Convert.ToBoolean(rdr["IsRunning"]),
                rdr["Source"] as string));
        return list;
    }
}
