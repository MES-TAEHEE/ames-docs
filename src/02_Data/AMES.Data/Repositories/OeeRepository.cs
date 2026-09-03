using System.Data;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// PP-OEE 라인 OEE.
///   읽기: PP_LineOEE(라인×일자×교대 스냅샷) · PP_LineDowntimeLog(손실 사유 분할)
///   계산: PP_LineStateLog(분단위 상태) + PR_ProductionResult/PR_DefectDetail(실적·불량) + MD_Bop(표준 사이클) → PP_LineOEE 저장
/// 스키마 정본은 dist/AMES_Schema.sql — 여기서 테이블을 만들지 않는다.
/// 비율 컬럼은 DB 소수(0.92) ↔ DTO 백분율(92.0) 로 이 클래스에서만 변환한다.
/// </summary>
public sealed class OeeRepository
{
    readonly AmesConnectionFactory _f;

    public OeeRepository(AmesConnectionFactory f) => _f = f;

    // ── Records ──────────────────────────────────────────────────────────
    public sealed record LineRef(string LineId, string? Name, string? NameEn);
    public sealed record DowntimeByReason(string LineId, string? ReasonCode, int Minutes);
    public sealed record EquipSignal(
        int SignalId, string LineId, DateTime SignalTime, bool IsRunning, string? Source);

    // ── Lines ─────────────────────────────────────────────────────────────
    /// <summary>라우팅 단계·상태 로그에 등장한 라인 ID (DowntimeMonitor 공용).</summary>
    public List<string> ListLines()
    {
        const string sql = """
            SELECT DISTINCT LineId FROM (
                SELECT LineID AS LineId FROM dbo.PP_WorkOrderRouting WHERE LineID IS NOT NULL
                UNION
                SELECT LineID FROM dbo.PP_LineStateLog WHERE LineID IS NOT NULL
            ) t ORDER BY LineId;
            """;
        return Query(sql, r => (string)r["LineId"]);
    }

    /// <summary>마스터 기준 활성 라인 (코드 + 명칭).</summary>
    public List<LineRef> ListLineRefs()
    {
        const string sql = """
            SELECT LineID, LineName, LineNameEn
            FROM   dbo.MD_Line
            WHERE  ISNULL(Status, 'ACTIVE') <> 'INACTIVE'
            ORDER  BY LineID;
            """;
        return Query(sql, r => new LineRef((string)r["LineID"], r["LineName"] as string, r["LineNameEn"] as string));
    }

    public DateTime? LatestSnapshotDate()
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand("SELECT MAX(PeriodDate) FROM dbo.PP_LineOEE;", conn);
        return cmd.ExecuteScalar() is DateTime d ? d : null;
    }

    // ── Snapshots (PP_LineOEE) ────────────────────────────────────────────
    public List<OeeSnapshotDto> GetSnapshots(DateTime from, DateTime to, string? shiftCode = null, string? lineId = null)
    {
        const string sql = """
            SELECT OeeSnapshotID, LineID, PeriodDate, ShiftCode,
                   ISNULL(LoadingMin,0)       AS LoadingMin,
                   ISNULL(PlannedDownMin,0)   AS PlannedDownMin,
                   ISNULL(UnplannedDownMin,0) AS UnplannedDownMin,
                   ISNULL(OperatingMin,0)     AS OperatingMin,
                   ISNULL(TotalProducedQty,0) AS TotalProducedQty,
                   ISNULL(GoodQty,0)          AS GoodQty,
                   ISNULL(Availability,0)     AS Availability,
                   ISNULL(Performance,0)      AS Performance,
                   ISNULL(Quality,0)          AS Quality,
                   ISNULL(OEE,0)              AS OEE,
                   CreatedTS, CreatedBy
            FROM   dbo.PP_LineOEE
            WHERE  PeriodDate BETWEEN @From AND @To
              AND  (@Shift IS NULL OR ShiftCode = @Shift)
              AND  (@Line  IS NULL OR LineID    = @Line)
            ORDER  BY PeriodDate, LineID, ShiftCode;
            """;
        return Query(sql, MapSnapshot,
            ("@From",  SqlDbType.Date,    from.Date),
            ("@To",    SqlDbType.Date,    to.Date),
            ("@Shift", SqlDbType.VarChar, (object?)shiftCode ?? DBNull.Value),
            ("@Line",  SqlDbType.VarChar, (object?)lineId    ?? DBNull.Value));
    }

    static OeeSnapshotDto MapSnapshot(SqlDataReader r) => new()
    {
        OeeSnapshotId    = (int)r["OeeSnapshotID"],
        LineId           = (string)r["LineID"],
        PeriodDate       = (DateTime)r["PeriodDate"],
        ShiftCode        = r["ShiftCode"] as string,
        LoadingMin       = (int)r["LoadingMin"],
        PlannedDownMin   = (int)r["PlannedDownMin"],
        UnplannedDownMin = (int)r["UnplannedDownMin"],
        OperatingMin     = (int)r["OperatingMin"],
        TotalProducedQty = (decimal)r["TotalProducedQty"],
        GoodQty          = (decimal)r["GoodQty"],
        Availability     = Pct((decimal)r["Availability"]),
        Performance      = Pct((decimal)r["Performance"]),
        Quality          = Pct((decimal)r["Quality"]),
        Oee              = Pct((decimal)r["OEE"]),
        CreatedTs        = r["CreatedTS"] as DateTime?,
        CreatedBy        = r["CreatedBy"] as string,
    };

    static decimal Pct(decimal fraction) => Math.Round(fraction * 100m, 2);
    static decimal Frac(decimal pct)     => Math.Round(pct / 100m, 4);

    /// <summary>같은 (라인, 일자, 교대) 스냅샷이 있으면 갱신, 없으면 삽입. 저장된 행의 ID 를 돌려준다.</summary>
    public int SaveSnapshot(OeeSnapshotDto s, string? savedBy)
    {
        const string sql = """
            DECLARE @Id INT;
            UPDATE dbo.PP_LineOEE
            SET    LoadingMin=@Load, PlannedDownMin=@PlDn, UnplannedDownMin=@UnDn, OperatingMin=@Op,
                   TotalProducedQty=@Prod, GoodQty=@Good,
                   Availability=@A, Performance=@P, Quality=@Q, OEE=@Oee,
                   ModifiedBy=@By, ModifiedTS=SYSDATETIME()
            WHERE  LineID=@Line AND PeriodDate=@Date
              AND  ((ShiftCode IS NULL AND @Shift IS NULL) OR ShiftCode=@Shift);
            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO dbo.PP_LineOEE
                       (LineID, PeriodDate, ShiftCode,
                        LoadingMin, PlannedDownMin, UnplannedDownMin, OperatingMin,
                        TotalProducedQty, GoodQty, Availability, Performance, Quality, OEE,
                        CreatedBy, CreatedTS)
                VALUES (@Line, @Date, @Shift,
                        @Load, @PlDn, @UnDn, @Op,
                        @Prod, @Good, @A, @P, @Q, @Oee,
                        @By, SYSDATETIME());
                SET @Id = CAST(SCOPE_IDENTITY() AS INT);
            END
            ELSE
                SELECT @Id = OeeSnapshotID FROM dbo.PP_LineOEE
                WHERE  LineID=@Line AND PeriodDate=@Date
                  AND  ((ShiftCode IS NULL AND @Shift IS NULL) OR ShiftCode=@Shift);
            SELECT @Id;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Line",  SqlDbType.VarChar, 20).Value   = s.LineId;
        cmd.Parameters.Add("@Date",  SqlDbType.Date).Value          = s.PeriodDate.Date;
        cmd.Parameters.Add("@Shift", SqlDbType.VarChar, 10).Value   = (object?)s.ShiftCode ?? DBNull.Value;
        cmd.Parameters.Add("@Load",  SqlDbType.Int).Value           = s.LoadingMin;
        cmd.Parameters.Add("@PlDn",  SqlDbType.Int).Value           = s.PlannedDownMin;
        cmd.Parameters.Add("@UnDn",  SqlDbType.Int).Value           = s.UnplannedDownMin;
        cmd.Parameters.Add("@Op",    SqlDbType.Int).Value           = s.OperatingMin;
        AddDec(cmd, "@Prod", s.TotalProducedQty);
        AddDec(cmd, "@Good", s.GoodQty);
        AddDec(cmd, "@A",    Frac(s.Availability));
        AddDec(cmd, "@P",    Frac(s.Performance));
        AddDec(cmd, "@Q",    Frac(s.Quality));
        AddDec(cmd, "@Oee",  Frac(s.Oee));
        cmd.Parameters.Add("@By", SqlDbType.NVarChar, 450).Value = string.IsNullOrWhiteSpace(savedBy) ? "web" : savedBy;
        return Convert.ToInt32(cmd.ExecuteScalar());

        static void AddDec(SqlCommand c, string name, decimal v)
        {
            var p = c.Parameters.Add(name, SqlDbType.Decimal);
            p.Precision = 14; p.Scale = 4; p.Value = v;
        }
    }

    public void DeleteSnapshot(int oeeSnapshotId)
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand("DELETE FROM dbo.PP_LineOEE WHERE OeeSnapshotID=@Id;", conn);
        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = oeeSnapshotId;
        cmd.ExecuteNonQuery();
    }

    // ── Downtime reasons (손실 분할용) ────────────────────────────────────
    public List<DowntimeByReason> GetDowntimeByReason(DateTime from, DateTime to)
    {
        const string sql = """
            SELECT LineID, ReasonCode,
                   SUM(ISNULL(DurationMin, DATEDIFF(minute, StartTS, ISNULL(EndTS, SYSDATETIME())))) AS Minutes
            FROM   dbo.PP_LineDowntimeLog
            WHERE  LineID IS NOT NULL
              AND  StartTS >= @From AND StartTS < @ToExcl
            GROUP  BY LineID, ReasonCode;
            """;
        return Query(sql,
            r => new DowntimeByReason((string)r["LineID"], r["ReasonCode"] as string, Convert.ToInt32(r["Minutes"])),
            ("@From",   SqlDbType.DateTime2, from.Date),
            ("@ToExcl", SqlDbType.DateTime2, to.Date.AddDays(1)));
    }

    // ── State log (PP_LineStateLog, 읽기 전용) ────────────────────────────
    public List<LineStateLogDto> GetStateLogs(string lineId, DateTime windowStart, DateTime windowEnd)
    {
        const string sql = """
            SELECT StateLogID, LineID, MinuteTS, State, PlanState, ISNULL(RunFlag,0) AS RunFlag, WoID
            FROM   dbo.PP_LineStateLog
            WHERE  LineID = @Line AND MinuteTS >= @S AND MinuteTS < @E
            ORDER  BY MinuteTS;
            """;
        return Query(sql, r => new LineStateLogDto
            {
                StateLogId = (long)r["StateLogID"],
                LineId     = (string)r["LineID"],
                MinuteTs   = (DateTime)r["MinuteTS"],
                State      = r["State"] as string,
                PlanState  = r["PlanState"] as string,
                RunFlag    = Convert.ToBoolean(r["RunFlag"]),
                WoId       = r["WoID"] as int?,
            },
            ("@Line", SqlDbType.VarChar,   lineId),
            ("@S",    SqlDbType.DateTime2, windowStart),
            ("@E",    SqlDbType.DateTime2, windowEnd));
    }

    // ── OEE 계산 (저장 안 함) ─────────────────────────────────────────────
    /// <summary>
    /// [windowStart, windowEnd) 구간의 분단위 상태 로그와 실적으로 A·P·Q 를 산출한다.
    ///   Loading   = 로그된 전체 분 (시드 스냅샷과 동일하게 계획 정지를 포함)
    ///   Planned   = PlanState 가 PLAN-RUN 이 아닌 분
    ///   Operating = PLAN-RUN 이면서 State='RUN' 인 분
    ///   Q         = 양품 / (양품 + 불량)           — PR_ProductionResult · PR_DefectDetail
    ///   P         = Σ(수량 × BOP 표준 사이클) / 가동 초 — 표준 사이클이 하나라도 없으면 100% 로 간주(PerformanceAssumed)
    /// 로그가 한 줄도 없으면 null.
    /// </summary>
    public OeeSnapshotDto? ComputeOee(string lineId, DateTime windowStart, DateTime windowEnd, string? shiftCode)
    {
        const string stateSql = """
            SELECT COUNT(*) AS LoadingMin,
                   SUM(CASE WHEN ISNULL(PlanState,'PLAN-RUN') <> 'PLAN-RUN' THEN 1 ELSE 0 END) AS PlannedDownMin,
                   SUM(CASE WHEN ISNULL(PlanState,'PLAN-RUN') =  'PLAN-RUN' AND State = 'RUN' THEN 1 ELSE 0 END) AS OperatingMin
            FROM   dbo.PP_LineStateLog
            WHERE  LineID = @Line AND MinuteTS >= @S AND MinuteTS < @E;
            """;
        const string resultSql = """
            SELECT ISNULL(SUM(r.GoodQty), 0)            AS GoodQty,
                   ISNULL(SUM(d.DefQty), 0)             AS DefectQty,
                   SUM(CASE WHEN b.StdCycle IS NOT NULL
                            THEN (ISNULL(r.GoodQty,0) + ISNULL(d.DefQty,0)) * b.StdCycle END) AS StdSec,
                   SUM(CASE WHEN b.StdCycle IS NOT NULL THEN 1 ELSE 0 END) AS RowsWithStd,
                   COUNT(*)                             AS Rows
            FROM   dbo.PR_ProductionResult r
            LEFT JOIN (SELECT ResultID, SUM(ISNULL(Qty,0)) AS DefQty
                       FROM dbo.PR_DefectDetail GROUP BY ResultID) d ON d.ResultID = r.ResultID
            LEFT JOIN dbo.PP_WorkOrder w ON w.WoID = r.WoID
            OUTER APPLY (SELECT AVG(CAST(bp.StdCycleTime AS decimal(12,3))) AS StdCycle
                         FROM   dbo.MD_Bop bp
                         WHERE  bp.ItemNo = w.ItemNo
                           AND  ISNULL(bp.ActiveFlag, 1) = 1
                           AND  (w.RoutingType IS NULL OR bp.RoutingType = w.RoutingType)) b
            WHERE  r.LineID = @Line AND r.EntryAt >= @S AND r.EntryAt < @E;
            """;

        using var conn = _f.OpenConnection();

        int loading, planned, operating;
        using (var cmd = new SqlCommand(stateSql, conn))
        {
            AddWindow(cmd, lineId, windowStart, windowEnd);
            using var rdr = cmd.ExecuteReader();
            if (!rdr.Read()) return null;
            loading = Convert.ToInt32(rdr["LoadingMin"]);
            if (loading == 0) return null;
            planned   = Convert.ToInt32(rdr["PlannedDownMin"]);
            operating = Convert.ToInt32(rdr["OperatingMin"]);
        }

        decimal good, defect; decimal? stdSec; int rowsWithStd, rows;
        using (var cmd = new SqlCommand(resultSql, conn))
        {
            AddWindow(cmd, lineId, windowStart, windowEnd);
            using var rdr = cmd.ExecuteReader();
            rdr.Read();
            good        = Convert.ToDecimal(rdr["GoodQty"]);
            defect      = Convert.ToDecimal(rdr["DefectQty"]);
            stdSec      = rdr["StdSec"] is DBNull ? null : Convert.ToDecimal(rdr["StdSec"]);
            rowsWithStd = Convert.ToInt32(rdr["RowsWithStd"]);
            rows        = Convert.ToInt32(rdr["Rows"]);
        }

        int     unplanned = Math.Max(0, loading - planned - operating);
        decimal produced  = good + defect;
        decimal a = Math.Round((decimal)operating / loading * 100m, 2);
        decimal q = produced > 0 ? Math.Round(good / produced * 100m, 2) : 100m;

        bool assumed = rows == 0 || rowsWithStd < rows || stdSec is null || operating == 0;
        decimal p = assumed
            ? 100m
            : Math.Min(100m, Math.Round(stdSec!.Value / (operating * 60m) * 100m, 2));

        return new OeeSnapshotDto
        {
            LineId             = lineId,
            PeriodDate         = windowStart.Date,
            ShiftCode          = shiftCode,
            LoadingMin         = loading,
            PlannedDownMin     = planned,
            UnplannedDownMin   = unplanned,
            OperatingMin       = operating,
            TotalProducedQty   = produced,
            GoodQty            = good,
            Availability       = a,
            Performance        = p,
            Quality            = q,
            Oee                = Math.Round(a / 100m * p / 100m * q / 100m * 100m, 2),
            PerformanceAssumed = assumed,
            CreatedTs          = DateTime.Now,
        };

        static void AddWindow(SqlCommand cmd, string line, DateTime s, DateTime e)
        {
            cmd.Parameters.Add("@Line", SqlDbType.VarChar, 20).Value = line;
            cmd.Parameters.Add("@S",    SqlDbType.DateTime2).Value   = s;
            cmd.Parameters.Add("@E",    SqlDbType.DateTime2).Value   = e;
        }
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
        var dict = new Dictionary<string, EquipSignal>(StringComparer.OrdinalIgnoreCase);
        foreach (var sig in Query(sql, MapSignal)) dict[sig.LineId] = sig;
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
        return Query(sql, MapSignal,
            ("@LineId", SqlDbType.VarChar, lineId),
            ("@M",      SqlDbType.Int,     minutesBack));
    }

    static EquipSignal MapSignal(SqlDataReader r) => new(
        (int)r["SignalId"], (string)r["LineId"],
        (DateTime)r["SignalTime"], Convert.ToBoolean(r["IsRunning"]),
        r["Source"] as string);

    // ── Helper ────────────────────────────────────────────────────────────
    List<T> Query<T>(string sql, Func<SqlDataReader, T> map, params (string Name, SqlDbType Type, object Value)[] ps)
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        foreach (var (name, type, value) in ps)
            cmd.Parameters.Add(name, type).Value = value;
        using var rdr = cmd.ExecuteReader();
        var list = new List<T>();
        while (rdr.Read()) list.Add(map(rdr));
        return list;
    }
}
