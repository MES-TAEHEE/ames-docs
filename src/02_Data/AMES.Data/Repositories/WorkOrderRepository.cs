using System.Data;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// CRUD over PP_WorkOrder + PR_WoAcceptance.
/// Powers INJ-03 (Confirm), INJ-04 (progress + qty bump), INJ-02 (active WO tile).
/// </summary>
public sealed class WorkOrderRepository
{
    private readonly AmesConnectionFactory _factory;
    public WorkOrderRepository(AmesConnectionFactory f) => _factory = f;

    // ── 공정 단계 타입 ───────────────────────────────────────────────
    public sealed record LineOption(string LineId, string? LineName)
    {
        public string Display => string.IsNullOrEmpty(LineName) ? LineId : $"{LineId} · {LineName}";
    }

    /// <summary>
    /// Release 다이얼로그용 템플릿 단계. BopLineId = 품목 BOP 스테이션의 라인(공정 일치 첫 행, 활성 라인만).
    /// LineRequired = 그 공정에 활성 라인이 하나라도 있으면 true. Candidates = 그 공정의 활성 라인.
    /// </summary>
    public sealed record RoutingStepPreview(
        int StepSeq, string ProcessCode, string? BopLineId, bool LineRequired,
        IReadOnlyList<LineOption> Candidates, int? StdCycleSec = null);

    public sealed record StepLineChoice(int StepSeq, string? LineId);

    public sealed record StepRow(
        int RoutingLineId, int StepSeq, string ProcessCode, string? LineId, string Status, decimal CompletedQty);

    /// <summary>Draft/Planned WO 의 라우팅 템플릿 미리보기. 그 외 상태·RoutingType NULL 이면 빈 목록.</summary>
    public List<RoutingStepPreview> PreviewRouting(int woId)
    {
        using var conn = _factory.OpenConnection();
        return ReadPreview(conn, null, woId);
    }

    /// <summary>WO 생성 전(PP-003)에 품목·라우팅으로 보는 템플릿 미리보기. StdCycleSec = 그 공정 BOP 사이클(초).</summary>
    public List<RoutingStepPreview> PreviewRouting(string itemNo, string routingType)
    {
        using var conn = _factory.OpenConnection();
        return ReadPreview(conn, null, itemNo, routingType);
    }

    internal static List<RoutingStepPreview> ReadPreview(SqlConnection conn, SqlTransaction? tx, int woId)
    {
        string? itemNo = null, routingType = null;
        using (var cmd = new SqlCommand(
            "SELECT ItemNo, RoutingType FROM dbo.PP_WorkOrder WHERE WoID = @WoID AND Status IN ('Draft','Planned');", conn, tx))
        {
            cmd.Parameters.Add("@WoID", SqlDbType.Int).Value = woId;
            using var rdr = cmd.ExecuteReader();
            if (rdr.Read()) { itemNo = rdr["ItemNo"] as string; routingType = rdr["RoutingType"] as string; }
        }
        if (itemNo is null || routingType is null) return new();
        return ReadPreview(conn, tx, itemNo, routingType);
    }

    private static List<RoutingStepPreview> ReadPreview(SqlConnection conn, SqlTransaction? tx, string itemNo, string routingType)
    {
        const string sql = """
            SELECT rs.StepSeq, rs.ProcessCode,
                   (SELECT TOP 1 st.LineID
                    FROM   dbo.MD_Bop b
                    JOIN   dbo.MD_Station    st ON st.StationCode = b.StationCode
                    JOIN   dbo.MD_Line       sl ON sl.LineID      = st.LineID
                    JOIN   dbo.MD_WorkCenter sw ON sw.WCID        = sl.WCID
                    WHERE  b.ItemNo = @ItemNo AND b.RoutingType = @RT AND ISNULL(b.ActiveFlag,1) = 1
                      AND  sw.ProcessCode = rs.ProcessCode
                      AND  ISNULL(sl.Status,'ACTIVE') <> 'INACTIVE'
                    ORDER  BY b.StepSeq) AS BopLineID,
                   CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.MD_Line l
                                          JOIN dbo.MD_WorkCenter wc ON wc.WCID = l.WCID
                                          WHERE wc.ProcessCode = rs.ProcessCode
                                            AND ISNULL(l.Status,'ACTIVE') <> 'INACTIVE')
                             THEN 1 ELSE 0 END AS bit) AS LineRequired,
                   (SELECT TOP 1 CAST(b.StdCycleTime AS int)
                    FROM   dbo.MD_Bop b
                    JOIN   dbo.MD_Station    st ON st.StationCode = b.StationCode
                    JOIN   dbo.MD_Line       sl ON sl.LineID      = st.LineID
                    JOIN   dbo.MD_WorkCenter sw ON sw.WCID        = sl.WCID
                    WHERE  b.ItemNo = @ItemNo AND b.RoutingType = @RT
                      AND  sw.ProcessCode = rs.ProcessCode
                    ORDER  BY b.StepSeq) AS StdCycleSec
            FROM   dbo.MD_RoutingStep rs
            WHERE  rs.RoutingType = @RT AND ISNULL(rs.ActiveFlag,1) = 1
            ORDER  BY rs.StepSeq;

            SELECT wc.ProcessCode, l.LineID, l.LineName
            FROM   dbo.MD_Line l
            JOIN   dbo.MD_WorkCenter wc ON wc.WCID = l.WCID
            WHERE  ISNULL(l.Status,'ACTIVE') <> 'INACTIVE'
              AND  wc.ProcessCode IN (SELECT ProcessCode FROM dbo.MD_RoutingStep
                                      WHERE RoutingType = @RT AND ISNULL(ActiveFlag,1) = 1)
            ORDER  BY wc.ProcessCode, l.LineID;
            """;
        using var cmd = new SqlCommand(sql, conn, tx);
        cmd.Parameters.Add("@ItemNo", SqlDbType.VarChar, 20).Value = itemNo;
        cmd.Parameters.Add("@RT",     SqlDbType.Char, 1).Value     = routingType;
        using var rdr = cmd.ExecuteReader();

        var raw = new List<(int Seq, string Proc, string? Bop, bool Req, int? Cyc)>();
        while (rdr.Read())
            raw.Add((Convert.ToInt32(rdr["StepSeq"]), (string)rdr["ProcessCode"],
                     rdr["BopLineID"] as string, (bool)rdr["LineRequired"], rdr["StdCycleSec"] as int?));

        var cands = new Dictionary<string, List<LineOption>>();
        if (rdr.NextResult())
            while (rdr.Read())
            {
                var proc = (string)rdr["ProcessCode"];
                if (!cands.TryGetValue(proc, out var list)) cands[proc] = list = new();
                list.Add(new LineOption((string)rdr["LineID"], rdr["LineName"] as string));
            }

        return raw.Select(r => new RoutingStepPreview(
                r.Seq, r.Proc, r.Bop, r.Req,
                cands.TryGetValue(r.Proc, out var c) ? c : Array.Empty<LineOption>(), r.Cyc))
            .ToList();
    }

    /// <summary>
    /// All WOs across all lines for the PP-004 management grid.
    /// Returns Draft + Planned + Released + In Progress always; Closed/Cancelled only within the last <paramref name="recentClosedDays"/> days.
    /// </summary>
    public List<WorkOrderDto> ListAll(int recentClosedDays = 30)
    {
        const string sql = """
            SELECT w.WoID, w.WoNumber, w.ItemNo, i.ItemName,
                   w.OrderQty, w.OpenQty, w.CompletedQty, w.LineID,
                   w.MoldID, w.RecipeID, w.DueDate, w.Status, w.TerminalLock,
                   ISNULL(w.Priority,5) AS Priority,
                   w.RoutingType,
                   CAST(
                       CASE WHEN EXISTS(SELECT 1 FROM dbo.MD_BOM bm WHERE bm.ParentItemNo = w.ItemNo)
                             AND EXISTS(SELECT 1 FROM dbo.MD_BOP bp WHERE bp.ItemNo        = w.ItemNo)
                            THEN 1 ELSE 0 END
                   AS BIT) AS Phase0Complete,
                   NULL AS SapRef,
                   so.SoNumber AS SoNumber,
                   (SELECT STRING_AGG(CAST(COALESCE(r.LineID, r.ProcessCode + N'(—)') AS nvarchar(40)), N' → ')
                               WITHIN GROUP (ORDER BY r.StepSeq)
                    FROM dbo.PP_WorkOrderRouting r WHERE r.WoID = w.WoID) AS RouteLines
            FROM   dbo.PP_WorkOrder w
            JOIN   dbo.MD_Item      i  ON i.ItemNo = w.ItemNo
            LEFT JOIN dbo.PP_CustomerOrder so ON so.SoID = w.SoID
            WHERE  w.Status IN ('Draft','Planned','Released','In Progress')
               OR (w.Status = 'Closed'
                   AND w.ActualEnd >= DATEADD(day, -@Days, CAST(GETDATE() AS date)))
               OR (w.Status = 'Cancelled'
                   AND w.ModifiedTS >= DATEADD(day, -@Days, CAST(GETDATE() AS date)))
            ORDER  BY CASE w.Status
                           WHEN 'In Progress' THEN 0
                           WHEN 'Released'    THEN 1
                           WHEN 'Draft'       THEN 2
                           ELSE 3 END,
                      ISNULL(w.Priority,5),
                      ISNULL(w.DueDate,'9999-12-31'),
                      w.WoID;
            """;

        return Query(sql, cmd => cmd.Parameters.Add("@Days", SqlDbType.Int).Value = recentClosedDays);
    }

    /// <summary>
    /// 이 라인에 배정된 공정 단계가 열려 있는 WO (단계 Status Released/In Progress).
    /// 반환 DTO 의 LineId·Status·CompletedQty 는 단계 값이다 (WorkOrderDto 주석 참고).
    /// </summary>
    public List<WorkOrderDto> ListForLine(string lineId)
    {
        const string sql = """
            SELECT w.WoID, w.WoNumber, w.ItemNo, i.ItemName,
                   w.OrderQty, w.OpenQty, r.CompletedQty, r.LineID,
                   w.MoldID, w.RecipeID, w.DueDate, r.Status, r.TerminalLock,
                   ISNULL(w.Priority,5) AS Priority, w.RoutingType,
                   r.RoutingLineID, r.StepSeq, r.ProcessCode
            FROM   dbo.PP_WorkOrderRouting r
            JOIN   dbo.PP_WorkOrder w ON w.WoID   = r.WoID
            JOIN   dbo.MD_Item      i ON i.ItemNo = w.ItemNo
            WHERE  r.LineID = @LineID
              AND  r.Status IN ('Released','In Progress')
              AND  ISNULL(w.Status,'Draft') <> 'Cancelled'
            ORDER  BY CASE WHEN r.Status='In Progress' THEN 0 ELSE 1 END,
                      ISNULL(w.Priority,5),
                      ISNULL(w.DueDate,'9999-12-31'),
                      w.WoID, r.StepSeq;
            """;

        return Query(sql, cmd => cmd.Parameters.Add("@LineID", SqlDbType.VarChar, 20).Value = lineId);
    }

    /// <summary>Single WO by id (used by INJ-04 after Accept).</summary>
    public WorkOrderDto? GetById(int woId)
    {
        const string sql = """
            SELECT TOP 1 w.WoID, w.WoNumber, w.ItemNo, i.ItemName,
                   w.OrderQty, w.OpenQty, w.CompletedQty, w.LineID,
                   w.MoldID, w.RecipeID, w.DueDate, w.Status, w.TerminalLock,
                   ISNULL(w.Priority,5) AS Priority
            FROM   dbo.PP_WorkOrder w
            JOIN   dbo.MD_Item      i ON i.ItemNo = w.ItemNo
            WHERE  w.WoID = @WoID;
            """;

        return Query(sql, cmd => cmd.Parameters.Add("@WoID", SqlDbType.Int).Value = woId)
               .FirstOrDefault();
    }

    /// <summary>이 터미널이 진행 중인 단계의 WO. LineId·Status·CompletedQty 는 단계 값.</summary>
    public WorkOrderDto? GetActiveForTerminal(string lineId, string terminalId)
    {
        // Ranked by the most recent WO Confirm (PR_WoAcceptance.AcceptedAt), not
        // r.ActualStart — ActualStart is set once (first accept) and never bumped
        // on re-accept, so it can't tell which WO the operator switched to last.
        const string sql = """
            SELECT TOP 1 w.WoID, w.WoNumber, w.ItemNo, i.ItemName,
                   w.OrderQty, w.OpenQty, r.CompletedQty, r.LineID,
                   w.MoldID, w.RecipeID, w.DueDate, r.Status, r.TerminalLock,
                   ISNULL(w.Priority,5) AS Priority, w.RoutingType,
                   r.RoutingLineID, r.StepSeq, r.ProcessCode
            FROM   dbo.PP_WorkOrderRouting r
            JOIN   dbo.PP_WorkOrder w ON w.WoID   = r.WoID
            JOIN   dbo.MD_Item      i ON i.ItemNo = w.ItemNo
            OUTER APPLY (
                SELECT MAX(a.AcceptedAt) AS LastAcceptedAt
                FROM   dbo.PR_WoAcceptance a
                WHERE  a.WoID = w.WoID AND a.TerminalID = @TerminalID
            ) la
            WHERE  r.LineID = @LineID
              AND  r.Status = 'In Progress'
              AND  ISNULL(w.Status,'Draft') <> 'Cancelled'
              AND (r.TerminalLock = @TerminalID OR r.TerminalLock IS NULL)
            ORDER  BY la.LastAcceptedAt DESC, r.ActualStart DESC, r.StepSeq;
            """;

        return Query(sql, cmd =>
        {
            cmd.Parameters.Add("@LineID",     SqlDbType.VarChar, 20).Value = lineId;
            cmd.Parameters.Add("@TerminalID", SqlDbType.VarChar, 20).Value = terminalId;
        }).FirstOrDefault();
    }

    /// <summary>
    /// 공정 단계를 이 터미널에 접수. 단계·헤더 In Progress, 단계·헤더 TerminalLock (조회는 단계 값 기준). 체크리스트는 PR_WoAcceptance(WoID) 에 기록.
    /// Returns the new AcceptID. 단계가 없으면 SqlException(50001).
    /// </summary>
    public int AcceptWo(int routingLineId, string terminalId, string operatorId,
                        string employeeNo, string checkResultsJson)
    {
        const string sql = """
            DECLARE @WoID int = (SELECT WoID FROM dbo.PP_WorkOrderRouting WHERE RoutingLineID = @RL);
            IF @WoID IS NULL THROW 50001, 'Routing step not found.', 1;

            DECLARE @Out TABLE (AcceptID int);
            INSERT INTO dbo.PR_WoAcceptance
                (WoID, TerminalID, OperatorID, AcceptedAt, CheckResults, CheckPassed, CreatedBy, CreatedTS)
            OUTPUT INSERTED.AcceptID INTO @Out
            VALUES (@WoID, @TerminalID, @OperatorID, SYSDATETIME(), @Checks, 1, @CreatedBy, SYSDATETIME());

            UPDATE dbo.PP_WorkOrderRouting
            SET    Status       = 'In Progress',
                   ActualStart  = ISNULL(ActualStart, SYSDATETIME()),
                   TerminalLock = @TerminalID,
                   ModifiedBy   = @OperatorID, ModifiedTS = SYSDATETIME()
            WHERE  RoutingLineID = @RL;

            UPDATE dbo.PP_WorkOrder
            SET    Status       = 'In Progress',
                   TerminalLock = @TerminalID,
                   ActualStart  = ISNULL(ActualStart, SYSDATETIME()),
                   ModifiedBy   = @OperatorID, ModifiedTS = SYSDATETIME()
            WHERE  WoID = @WoID AND ISNULL(Status,'Draft') <> 'Cancelled';

            SELECT AcceptID FROM @Out;
            """;
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            using var cmd = new SqlCommand(sql, conn, tx);
            cmd.Parameters.Add("@RL",         SqlDbType.Int           ).Value = routingLineId;
            cmd.Parameters.Add("@TerminalID", SqlDbType.VarChar, 20   ).Value = terminalId;
            cmd.Parameters.Add("@OperatorID", SqlDbType.NVarChar, 450 ).Value = operatorId;
            cmd.Parameters.Add("@Checks",     SqlDbType.NVarChar      ).Value = checkResultsJson;
            cmd.Parameters.Add("@CreatedBy",  SqlDbType.VarChar, 50   ).Value = employeeNo;
            var acceptId = (int)cmd.ExecuteScalar()!;
            tx.Commit();
            return acceptId;
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>
    /// 실적 반영 단일 진입점. 호출측 트랜잭션에 참여한다.
    /// 단계 CompletedQty += qty. 헤더 OrderQty 도달 시 단계 Closed·ActualEnd.
    /// 이 단계가 "라인이 있는 마지막 단계"(LineID NOT NULL 중 최대 StepSeq)면 헤더 CompletedQty 를 동기화하고,
    /// 도달 시 헤더 Closed·ActualEnd. 반환: 단계의 새 CompletedQty. 단계가 없으면 SqlException(50001).
    /// </summary>
    internal static decimal BumpStepCompleted(SqlConnection conn, SqlTransaction tx, int routingLineId, decimal qty, string actor)
    {
        const string sql = """
            DECLARE @WoID int, @Seq int, @OrderQty decimal(14,3), @New decimal(14,3), @LastSeq int;

            SELECT @WoID = r.WoID, @Seq = r.StepSeq, @OrderQty = ISNULL(w.OrderQty, 0)
            FROM   dbo.PP_WorkOrderRouting r WITH (UPDLOCK, ROWLOCK)
            JOIN   dbo.PP_WorkOrder        w WITH (UPDLOCK, ROWLOCK) ON w.WoID = r.WoID
            WHERE  r.RoutingLineID = @RL;
            IF @WoID IS NULL THROW 50001, 'Routing step not found.', 1;

            UPDATE dbo.PP_WorkOrderRouting
            SET    CompletedQty = CompletedQty + @Qty,
                   Status       = CASE WHEN CompletedQty + @Qty >= @OrderQty THEN 'Closed' ELSE Status END,
                   ActualEnd    = CASE WHEN CompletedQty + @Qty >= @OrderQty AND ActualEnd IS NULL THEN SYSDATETIME() ELSE ActualEnd END,
                   ModifiedBy   = @Actor, ModifiedTS = SYSDATETIME()
            WHERE  RoutingLineID = @RL;

            SELECT @New = CompletedQty FROM dbo.PP_WorkOrderRouting WHERE RoutingLineID = @RL;
            SELECT @LastSeq = MAX(StepSeq) FROM dbo.PP_WorkOrderRouting WHERE WoID = @WoID AND LineID IS NOT NULL;

            IF @Seq = @LastSeq
                UPDATE dbo.PP_WorkOrder
                SET    CompletedQty = @New,
                       Status       = CASE WHEN @New >= @OrderQty THEN 'Closed' ELSE Status END,
                       ActualEnd    = CASE WHEN @New >= @OrderQty AND ActualEnd IS NULL THEN SYSDATETIME() ELSE ActualEnd END,
                       ModifiedBy   = @Actor, ModifiedTS = SYSDATETIME()
                WHERE  WoID = @WoID;

            SELECT @New;
            """;
        using var cmd = new SqlCommand(sql, conn, tx);
        cmd.Parameters.Add("@RL",    SqlDbType.Int).Value           = routingLineId;
        cmd.Parameters.Add("@Qty",   SqlDbType.Decimal).Precision   = 14;
        cmd.Parameters["@Qty"].Scale = 3;
        cmd.Parameters["@Qty"].Value = qty;
        cmd.Parameters.Add("@Actor", SqlDbType.NVarChar, 450).Value = actor;
        return (decimal)cmd.ExecuteScalar()!;
    }

    /// <summary>(WoID, LineID) 로 단계 행을 찾는다. 같은 라인에 단계가 둘이면 StepSeq 가 작은 쪽. 없으면 null.</summary>
    internal static int? FindStepId(SqlConnection conn, SqlTransaction tx, int woId, string lineId)
    {
        using var cmd = new SqlCommand("""
            SELECT TOP 1 RoutingLineID FROM dbo.PP_WorkOrderRouting
            WHERE  WoID = @WoID AND LineID = @LineID
            ORDER  BY StepSeq;
            """, conn, tx);
        cmd.Parameters.Add("@WoID",   SqlDbType.Int).Value         = woId;
        cmd.Parameters.Add("@LineID", SqlDbType.VarChar, 20).Value = lineId;
        return cmd.ExecuteScalar() is int id ? id : null;
    }

    /// <summary>
    /// Updates DueDate for a WO (PP-CAL drag reschedule). Ignores Closed WOs.
    /// </summary>
    public void UpdateDueDate(int woId, DateTime newDate)
    {
        const string sql = """
            UPDATE dbo.PP_WorkOrder
               SET DueDate = @DueDate
             WHERE WoID   = @WoID
               AND Status NOT IN ('Closed');
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@WoID",   SqlDbType.Int).Value          = woId;
        cmd.Parameters.Add("@DueDate", SqlDbType.Date).Value        = newDate.Date;
        cmd.ExecuteNonQuery();
    }

    // ── PP-004 lifecycle actions ─────────────────────────────────────

    /// <summary>
    /// WO 를 Released 로 전환하고 공정 단계 행(PP_WorkOrderRouting)을 생성. Draft/Planned 만.
    /// steps 는 다이얼로그가 확정한 단계별 라인. 템플릿과 다시 대조해 검증하고, 실패하면 아무것도 바꾸지 않는다.
    /// 헤더 LineID 는 쓰지 않는다. 반환: 1(발행) / 0(대상 아님).
    /// </summary>
    public int ReleaseWo(int woId, IReadOnlyList<StepLineChoice> steps, string actor)
    {
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            if (ReleaseCore(conn, tx, woId, steps, actor) == 0) { tx.Rollback(); return 0; }
            tx.Commit();
            return 1;
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>
    /// ReleaseWo 본체 — 호출자의 트랜잭션 안에서 실행하며 커밋/롤백은 호출자가 한다 (PP-003 일괄 생성이 공유).
    /// 반환 0 = 대상 아님(변경 없음). 검증 실패는 예외.
    /// </summary>
    internal static int ReleaseCore(SqlConnection conn, SqlTransaction tx, int woId, IReadOnlyList<StepLineChoice> steps, string actor)
    {
        string? status; string? routingType;
        // 헤더→단계 순서로 잠그는 유일한 경로. Draft/Planned 만 대상이라 단계 행·터미널이 없어 단계→헤더 경로와 교차하지 않는다.
        using (var cmd = new SqlCommand(
            "SELECT Status, RoutingType FROM dbo.PP_WorkOrder WITH (UPDLOCK, ROWLOCK) WHERE WoID = @WoID;", conn, tx))
        {
            cmd.Parameters.Add("@WoID", SqlDbType.Int).Value = woId;
            using var rdr = cmd.ExecuteReader();
            if (!rdr.Read()) return 0;
            status      = rdr["Status"]      as string;
            routingType = rdr["RoutingType"] as string;
        }
        if (status is not ("Draft" or "Planned")) return 0;
        if (routingType is null)
            throw new InvalidOperationException("WO has no RoutingType; routing template cannot be resolved.");

        var template = ReadPreview(conn, tx, woId);
        ValidateStepChoices(template, steps);

        using (var cmd = new SqlCommand("""
            UPDATE dbo.PP_WorkOrder
               SET Status     = 'Released',
                   ReleasedAt = SYSDATETIME(),
                   ReleasedBy = @Actor,
                   ModifiedTS = SYSDATETIME(),
                   ModifiedBy = @Actor
             WHERE WoID = @WoID AND Status IN ('Draft','Planned');
            DELETE FROM dbo.PP_WorkOrderRouting WHERE WoID = @WoID;
            """, conn, tx))
        {
            cmd.Parameters.Add("@WoID",  SqlDbType.Int).Value           = woId;
            cmd.Parameters.Add("@Actor", SqlDbType.NVarChar, 450).Value = actor;
            cmd.ExecuteNonQuery();
        }

        const string insSql = """
            INSERT INTO dbo.PP_WorkOrderRouting
                   (WoID, StepSeq, ProcessCode, LineID, StdCycleSec, StdYieldPct, Status, CompletedQty, CreatedBy, CreatedTS)
            SELECT @WoID, @Seq, @Proc, @LineID,
                   (SELECT TOP 1 CAST(b.StdCycleTime AS int)
                    FROM   dbo.MD_Bop b
                    JOIN   dbo.MD_Station    st ON st.StationCode = b.StationCode
                    JOIN   dbo.MD_Line       sl ON sl.LineID      = st.LineID
                    JOIN   dbo.MD_WorkCenter sw ON sw.WCID        = sl.WCID
                    WHERE  b.ItemNo = w.ItemNo AND b.RoutingType = w.RoutingType
                      AND  sw.ProcessCode = @Proc
                    ORDER  BY b.StepSeq),
                   NULL, 'Released', 0, @Actor, SYSDATETIME()
            FROM   dbo.PP_WorkOrder w
            WHERE  w.WoID = @WoID;
            """;
        var choice = steps.ToDictionary(s => s.StepSeq, s => s.LineId);
        foreach (var t in template)
        {
            using var ins = new SqlCommand(insSql, conn, tx);
            ins.Parameters.Add("@WoID",   SqlDbType.Int).Value           = woId;
            ins.Parameters.Add("@Seq",    SqlDbType.Int).Value           = t.StepSeq;
            ins.Parameters.Add("@Proc",   SqlDbType.VarChar, 10).Value   = t.ProcessCode;
            ins.Parameters.Add("@LineID", SqlDbType.VarChar, 20).Value   =
                t.LineRequired ? (object)choice[t.StepSeq]! : DBNull.Value;
            ins.Parameters.Add("@Actor",  SqlDbType.NVarChar, 450).Value = actor;
            ins.ExecuteNonQuery();
        }
        return 1;
    }
    /// <summary>템플릿 단계 집합과 steps 가 1:1 이고, 라인 필수 단계는 그 공정의 활성 라인이 지정됐는지.</summary>
    private static void ValidateStepChoices(IReadOnlyList<RoutingStepPreview> template, IReadOnlyList<StepLineChoice> steps)
    {
        var bySeq = new Dictionary<int, string?>();
        foreach (var s in steps)
            if (!bySeq.TryAdd(s.StepSeq, s.LineId))
                throw new InvalidOperationException($"Step {s.StepSeq}: duplicated.");
        if (template.Count == 0 || bySeq.Count != template.Count || template.Any(t => !bySeq.ContainsKey(t.StepSeq)))
            throw new InvalidOperationException("Routing steps do not match the template.");

        foreach (var t in template)
        {
            if (!t.LineRequired) continue;
            var lineId = bySeq[t.StepSeq];
            if (string.IsNullOrWhiteSpace(lineId))
                throw new InvalidOperationException($"Step {t.StepSeq} {t.ProcessCode}: line required.");
            if (!t.Candidates.Any(c => c.LineId == lineId))
                throw new InvalidOperationException($"Step {t.StepSeq} {t.ProcessCode}: '{lineId}' is not an active {t.ProcessCode} line.");
        }
    }

    /// <summary>WO 의 공정 단계 행 (PP-04 상세 펼침용). StepSeq 순.</summary>
    public List<StepRow> ListSteps(int woId)
    {
        const string sql = """
            SELECT RoutingLineID, StepSeq, ProcessCode, LineID, Status, CompletedQty
            FROM   dbo.PP_WorkOrderRouting
            WHERE  WoID = @WoID
            ORDER  BY StepSeq;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@WoID", SqlDbType.Int).Value = woId;
        using var rdr = cmd.ExecuteReader();
        var list = new List<StepRow>();
        while (rdr.Read())
            list.Add(new StepRow(
                (int)rdr["RoutingLineID"],
                Convert.ToInt32(rdr["StepSeq"]),
                (string)rdr["ProcessCode"],
                rdr["LineID"] as string,
                rdr["Status"] as string ?? "Released",
                rdr["CompletedQty"] as decimal? ?? 0m));
        return list;
    }

    /// <summary>WOë¥¼ Cancelledë¡œ ì „í™˜ (Draft/Planned/Releasedë§Œ). ë³€ê²½ í–‰ìˆ˜ ë°˜í™˜.</summary>
    public int CancelWo(int woId, string actor)
    {
        const string sql = """
            UPDATE dbo.PP_WorkOrder
               SET Status     = 'Cancelled',
                   ModifiedTS = SYSDATETIME(),
                   ModifiedBy = @Actor
             WHERE WoID   = @WoID
               AND Status IN ('Draft','Planned','Released');
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@WoID",  SqlDbType.Int).Value          = woId;
        cmd.Parameters.Add("@Actor", SqlDbType.NVarChar, 450).Value = actor;
        return cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// ìˆ˜ë™ Draft WO ìƒì„± (SO ë¯¸ì—°ê³„). WoNumber = WO-yyyyMMdd-NNN.
    /// í’ˆëª©ë§ˆìŠ¤í„°ì— ì¡´ìž¬í•˜ëŠ” í’ˆë²ˆë§Œ ìƒì„±. ìƒì„±ëœ WoNumber ë°˜í™˜(ì‹¤íŒ¨ ì‹œ ë¹ˆ ë¬¸ìžì—´).
    /// </summary>
    public string CreateManualWo(string itemNo, decimal qty, DateTime? due, string actor)
    {
        var prefix = $"WO-{DateTime.Today:yyyyMMdd}-";
        const string insSql = """
            INSERT INTO dbo.PP_WorkOrder
                   (WoNumber, ItemNo, OrderQty, OpenQty, DueDate, RoutingType, Status, CreatedBy, CreatedTS)
            SELECT @Wo, i.ItemNo, @Qty, @Qty, @Due, i.RoutingType, 'Draft', @Actor, SYSDATETIME()
            FROM   dbo.MD_Item i
            WHERE  i.ItemNo = @ItemNo
              AND  i.RoutingType IS NOT NULL;
            """;
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            var seq = PpRepository.NextWoSeq(conn, tx, prefix);

            var wo = $"{prefix}{(seq + 1):D3}";
            using var ins = new SqlCommand(insSql, conn, tx);
            ins.Parameters.Add("@Wo",     SqlDbType.VarChar, 20).Value = wo;
            ins.Parameters.Add("@ItemNo", SqlDbType.VarChar, 20).Value = itemNo;
            ins.Parameters.Add("@Qty",    SqlDbType.Decimal).Precision = 14;
            ins.Parameters["@Qty"].Scale = 3;
            ins.Parameters["@Qty"].Value  = qty;
            ins.Parameters.Add("@Due",    SqlDbType.Date).Value        = (object?)due?.Date ?? DBNull.Value;
            ins.Parameters.Add("@Actor",  SqlDbType.NVarChar, 450).Value = actor;

            var affected = ins.ExecuteNonQuery();
            tx.Commit();
            return affected == 1 ? wo : string.Empty;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // â”€â”€ helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    private static bool HasColumn(SqlDataReader rdr, string name)
    {
        for (int i = 0; i < rdr.FieldCount; i++)
            if (rdr.GetName(i) == name) return true;
        return false;
    }

    private List<WorkOrderDto> Query(string sql, Action<SqlCommand> bind)
    {
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        bind(cmd);
        using var rdr  = cmd.ExecuteReader();
        var list = new List<WorkOrderDto>();
        while (rdr.Read())
        {
            list.Add(new WorkOrderDto
            {
                WoId          = (int)rdr["WoID"],
                WoNumber      = rdr["WoNumber"] as string ?? string.Empty,
                ItemNo        = (string)rdr["ItemNo"],
                ItemName      = (string)rdr["ItemName"],
                OrderQty      = rdr["OrderQty"]     as decimal? ?? 0,
                OpenQty       = rdr["OpenQty"]      as decimal? ?? 0,
                CompletedQty  = rdr["CompletedQty"] as decimal? ?? 0,
                LineId        = rdr["LineID"]       as string ?? string.Empty,
                MoldId        = rdr["MoldID"]       as string,
                RecipeId      = rdr["RecipeID"]     as string,
                DueDate       = rdr["DueDate"]      as DateTime?,
                Status        = rdr["Status"]       as string ?? "Unknown",
                TerminalLock  = rdr["TerminalLock"] as string,
                Priority      = Convert.ToInt32(rdr["Priority"]),
                RoutingType   = HasColumn(rdr, "RoutingType")   ? rdr["RoutingType"]   as string  : null,
                Phase0Complete = HasColumn(rdr, "Phase0Complete") && rdr["Phase0Complete"] is true,
                SapRef        = HasColumn(rdr, "SapRef")        ? rdr["SapRef"]        as string  : null,
                SoNumber      = HasColumn(rdr, "SoNumber")      ? rdr["SoNumber"]      as string  : null,
                RoutingLineId = HasColumn(rdr, "RoutingLineID") ? rdr["RoutingLineID"] as int?    : null,
                StepSeq       = HasColumn(rdr, "StepSeq") && rdr["StepSeq"] is not DBNull ? (int?)Convert.ToInt32(rdr["StepSeq"]) : null,
                ProcessCode   = HasColumn(rdr, "ProcessCode")   ? rdr["ProcessCode"]   as string  : null,
                RouteLines    = HasColumn(rdr, "RouteLines")    ? rdr["RouteLines"]    as string  : null,
            });
        }
        return list;
    }
}
