using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.InjAgent.Tests;

/// <summary>
/// WO 생성 시 품목 RoutingType 필수 규칙. AMES_DEV 통합 테스트, DB 미기동 시 skip.
/// </summary>
public class WorkOrderRepositoryTests
{
    static readonly string Conn =
        Environment.GetEnvironmentVariable("AMES_TEST_CONN")
        ?? "Server=192.168.2.137,1433;Database=AMES_DEV;User Id=ames_app;Password=!Dev2026;TrustServerCertificate=True;Encrypt=True;Connect Timeout=10;";

    const string ItemNoRouting = "ITEST-WO-NORT";
    const string ItemRoutingA  = "ITEST-WO-RTA";
    const string ItemRoutingB  = "ITEST-WO-RTB";

    static AmesConnectionFactory? TryFactory()
    {
        try
        {
            var f = new AmesConnectionFactory(Conn);
            using var c = f.OpenConnection();
            return f;
        }
        catch { return null; }
    }

    static void Exec(AmesConnectionFactory f, string sql, params (string, object)[] ps)
    {
        using var conn = f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    static object? Scalar(AmesConnectionFactory f, string sql, params (string, object)[] ps)
    {
        using var conn = f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        return cmd.ExecuteScalar();
    }

    /// <summary>품목 3개(라우팅 NULL / A / B) + A 품목 BOP(ST-INJ-01, ST-IMG-01). B 품목은 BOP 없음.</summary>
    static void SeedItems(AmesConnectionFactory f)
    {
        CleanupItems(f);
        Exec(f, """
            INSERT INTO dbo.MD_Item (ItemNo, ItemName, RoutingType, ActiveFlag, CreatedBy)
            VALUES (@A, N'ITEST no routing', NULL, 1, 'ITEST'),
                   (@B, N'ITEST routing A',  'A',  1, 'ITEST'),
                   (@C, N'ITEST routing B',  'B',  1, 'ITEST');
            INSERT INTO dbo.MD_Bop (BOPID, ItemNo, RoutingType, StepSeq, StationCode, ActiveFlag, CreatedBy)
            VALUES ('ITEST-BOP-A-10', @B, 'A', 10, 'ST-INJ-01', 1, 'ITEST'),
                   ('ITEST-BOP-A-20', @B, 'A', 20, 'ST-IMG-01', 1, 'ITEST');
            """, ("@A", ItemNoRouting), ("@B", ItemRoutingA), ("@C", ItemRoutingB));
    }

    static void CleanupItems(AmesConnectionFactory f)
    {
        Exec(f, """
            DELETE a FROM dbo.PR_WoAcceptance a
              JOIN dbo.PP_WorkOrder w ON w.WoID = a.WoID WHERE w.ItemNo IN (@A, @B, @C);
            DELETE r FROM dbo.PP_WorkOrderRouting r
              JOIN dbo.PP_WorkOrder w ON w.WoID = r.WoID WHERE w.ItemNo IN (@A, @B, @C);
            DELETE FROM dbo.PP_WorkOrder     WHERE ItemNo IN (@A, @B, @C);
            DELETE FROM dbo.PP_CustomerOrder WHERE ItemNo IN (@A, @B, @C);
            DELETE FROM dbo.MD_Bop           WHERE ItemNo IN (@A, @B, @C);
            DELETE FROM dbo.MD_Item          WHERE ItemNo IN (@A, @B, @C);
            """, ("@A", ItemNoRouting), ("@B", ItemRoutingA), ("@C", ItemRoutingB));
    }

    static int CreateDraft(AmesConnectionFactory f, string itemNo, decimal qty = 10)
    {
        var wo = new WorkOrderRepository(f).CreateManualWo(itemNo, qty, DateTime.Today.AddDays(7), "itest");
        Assert.NotEqual(string.Empty, wo);
        return (int)Scalar(f, "SELECT WoID FROM dbo.PP_WorkOrder WHERE WoNumber = @W;", ("@W", wo))!;
    }

    [SkippableFact]
    public void CreateManualWo_rejects_item_without_routing()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var repo = new WorkOrderRepository(f);

            var wo = repo.CreateManualWo(ItemNoRouting, 10, DateTime.Today.AddDays(7), "itest");

            Assert.Equal(string.Empty, wo);
            var n = (int)Scalar(f, "SELECT COUNT(*) FROM dbo.PP_WorkOrder WHERE ItemNo = @I;", ("@I", ItemNoRouting))!;
            Assert.Equal(0, n);
        }
        finally { CleanupItems(f); }
    }

    [SkippableFact]
    public void CreateManualWo_copies_routing_from_item()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var repo = new WorkOrderRepository(f);

            var wo = repo.CreateManualWo(ItemRoutingA, 10, DateTime.Today.AddDays(7), "itest");

            Assert.NotEqual(string.Empty, wo);
            var rt = Scalar(f, "SELECT RoutingType FROM dbo.PP_WorkOrder WHERE WoNumber = @W;", ("@W", wo)) as string;
            Assert.Equal("A", rt);
        }
        finally { CleanupItems(f); }
    }

    [SkippableFact]
    public void CreateWorkOrdersForOrders_skips_item_without_routing_and_copies_routing()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var soNoRouting = (int)Scalar(f, """
                INSERT INTO dbo.PP_CustomerOrder (SoNumber, SoLineNo, ItemNo, OrderQty, RequestedDeliveryDate, Status, CreatedBy)
                OUTPUT INSERTED.SoID
                VALUES ('SO-ITEST-NORT', 1, @I, 50, DATEADD(day, 7, CAST(GETDATE() AS date)), 'Confirmed', 'ITEST');
                """, ("@I", ItemNoRouting))!;
            var soRoutingA = (int)Scalar(f, """
                INSERT INTO dbo.PP_CustomerOrder (SoNumber, SoLineNo, ItemNo, OrderQty, RequestedDeliveryDate, Status, CreatedBy)
                OUTPUT INSERTED.SoID
                VALUES ('SO-ITEST-RTA', 1, @I, 50, DATEADD(day, 7, CAST(GETDATE() AS date)), 'Confirmed', 'ITEST');
                """, ("@I", ItemRoutingA))!;

            var repo    = new PpRepository(f);
            var created = repo.CreateWorkOrdersForOrders(new[] { soNoRouting, soRoutingA }, "itest", useNetReq: false);

            Assert.Single(created);
            var n = (int)Scalar(f, "SELECT COUNT(*) FROM dbo.PP_WorkOrder WHERE SoID = @S;", ("@S", soNoRouting))!;
            Assert.Equal(0, n);
            var rt = Scalar(f, "SELECT RoutingType FROM dbo.PP_WorkOrder WHERE SoID = @S;", ("@S", soRoutingA)) as string;
            Assert.Equal("A", rt);
        }
        finally { CleanupItems(f); }
    }

    // ── PreviewRouting ─────────────────────────────────────────────

    [SkippableFact]
    public void PreviewRouting_returns_bop_line_per_step_and_candidates()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var woId  = CreateDraft(f, ItemRoutingA);
            var steps = new WorkOrderRepository(f).PreviewRouting(woId);

            Assert.Equal(2, steps.Count);
            var inj = steps[0]; var img = steps[1];

            Assert.Equal(1, inj.StepSeq); Assert.Equal("INJ", inj.ProcessCode);
            Assert.Equal("LINE-INJ-01", inj.BopLineId);
            Assert.True(inj.LineRequired);
            Assert.Contains(inj.Candidates, c => c.LineId == "LINE-INJ-01");
            Assert.Contains(inj.Candidates, c => c.LineId == "LINE-INJ-02");
            Assert.DoesNotContain(inj.Candidates, c => c.LineId == "LINE-IMG-01");

            Assert.Equal(2, img.StepSeq); Assert.Equal("IMG", img.ProcessCode);
            Assert.Equal("LINE-IMG-01", img.BopLineId);
            Assert.True(img.LineRequired);
        }
        finally { CleanupItems(f); }
    }

    [SkippableFact]
    public void PreviewRouting_marks_processes_without_active_line_as_optional()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var woId  = CreateDraft(f, ItemRoutingB);
            var steps = new WorkOrderRepository(f).PreviewRouting(woId);

            Assert.Equal(4, steps.Count);
            Assert.All(steps, s => Assert.Null(s.BopLineId));          // B 품목은 BOP 없음
            Assert.True (steps.Single(s => s.ProcessCode == "INJ").LineRequired);
            Assert.True (steps.Single(s => s.ProcessCode == "PNT").LineRequired);
            Assert.False(steps.Single(s => s.ProcessCode == "QC").LineRequired);   // LINE-QC-01 INACTIVE
            Assert.Empty(steps.Single(s => s.ProcessCode == "QC").Candidates);
            Assert.False(steps.Single(s => s.ProcessCode == "FG").LineRequired);   // FG 라인 없음
        }
        finally { CleanupItems(f); }
    }

    [SkippableFact]
    public void PreviewRouting_is_empty_for_non_draft_wo()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var woId = CreateDraft(f, ItemRoutingA);
            Exec(f, "UPDATE dbo.PP_WorkOrder SET Status = 'Cancelled' WHERE WoID = @W;", ("@W", woId));
            Assert.Empty(new WorkOrderRepository(f).PreviewRouting(woId));
        }
        finally { CleanupItems(f); }
    }

    // ── ReleaseWo ──────────────────────────────────────────────────

    static WorkOrderRepository.StepLineChoice[] StepsA(string? inj, string? img) =>
        new[] { new WorkOrderRepository.StepLineChoice(1, inj), new WorkOrderRepository.StepLineChoice(2, img) };

    [SkippableFact]
    public void ReleaseWo_rejects_missing_required_line_and_changes_nothing()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var woId = CreateDraft(f, ItemRoutingA);
            var repo = new WorkOrderRepository(f);

            var ex = Assert.Throws<InvalidOperationException>(
                () => repo.ReleaseWo(woId, StepsA(null, "LINE-IMG-01"), "itest"));
            Assert.Contains("Step 1 INJ", ex.Message);

            Assert.Equal("Draft", Scalar(f, "SELECT Status FROM dbo.PP_WorkOrder WHERE WoID = @W;", ("@W", woId)));
            Assert.Equal(0, (int)Scalar(f, "SELECT COUNT(*) FROM dbo.PP_WorkOrderRouting WHERE WoID = @W;", ("@W", woId))!);
        }
        finally { CleanupItems(f); }
    }

    [SkippableFact]
    public void ReleaseWo_rejects_line_of_another_process()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var woId = CreateDraft(f, ItemRoutingA);
            Assert.Throws<InvalidOperationException>(
                () => new WorkOrderRepository(f).ReleaseWo(woId, StepsA("LINE-IMG-01", "LINE-INJ-01"), "itest"));
            Assert.Equal("Draft", Scalar(f, "SELECT Status FROM dbo.PP_WorkOrder WHERE WoID = @W;", ("@W", woId)));
        }
        finally { CleanupItems(f); }
    }

    [SkippableFact]
    public void ReleaseWo_rejects_step_set_mismatch()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var woId = CreateDraft(f, ItemRoutingA);
            Assert.Throws<InvalidOperationException>(
                () => new WorkOrderRepository(f).ReleaseWo(woId,
                        new[] { new WorkOrderRepository.StepLineChoice(1, "LINE-INJ-01") }, "itest"));
        }
        finally { CleanupItems(f); }
    }

    [SkippableFact]
    public void ReleaseWo_creates_released_steps_and_leaves_header_line_null()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var woId = CreateDraft(f, ItemRoutingA);
            var n = new WorkOrderRepository(f).ReleaseWo(woId, StepsA("LINE-INJ-02", "LINE-IMG-01"), "itest");
            Assert.Equal(1, n);

            Assert.Equal("Released", Scalar(f, "SELECT Status FROM dbo.PP_WorkOrder WHERE WoID = @W;", ("@W", woId)));
            Assert.Equal(DBNull.Value, Scalar(f, "SELECT LineID FROM dbo.PP_WorkOrder WHERE WoID = @W;", ("@W", woId)));

            var steps = new WorkOrderRepository(f).ListSteps(woId);
            Assert.Equal(2, steps.Count);
            Assert.Equal(("INJ", "LINE-INJ-02", "Released", 0m), (steps[0].ProcessCode, steps[0].LineId, steps[0].Status, steps[0].CompletedQty));
            Assert.Equal(("IMG", "LINE-IMG-01", "Released", 0m), (steps[1].ProcessCode, steps[1].LineId, steps[1].Status, steps[1].CompletedQty));
        }
        finally { CleanupItems(f); }
    }

    [SkippableFact]
    public void ReleaseWo_stores_null_line_for_optional_steps()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var woId = CreateDraft(f, ItemRoutingB);
            var n = new WorkOrderRepository(f).ReleaseWo(woId, new[]
            {
                new WorkOrderRepository.StepLineChoice(1, "LINE-INJ-01"),
                new WorkOrderRepository.StepLineChoice(2, "LINE-PNT-01"),
                new WorkOrderRepository.StepLineChoice(3, "LINE-QC-01"),   // 무시되어야 함(라인 불필요 공정)
                new WorkOrderRepository.StepLineChoice(4, null),
            }, "itest");
            Assert.Equal(1, n);

            var steps = new WorkOrderRepository(f).ListSteps(woId);
            Assert.Equal(4, steps.Count);
            Assert.Null(steps.Single(s => s.ProcessCode == "QC").LineId);
            Assert.Null(steps.Single(s => s.ProcessCode == "FG").LineId);
            Assert.Equal("LINE-PNT-01", steps.Single(s => s.ProcessCode == "PNT").LineId);
        }
        finally { CleanupItems(f); }
    }

    [SkippableFact]
    public void ReleaseWo_returns_zero_for_already_released_wo()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var woId = CreateDraft(f, ItemRoutingA);
            var repo = new WorkOrderRepository(f);
            repo.ReleaseWo(woId, StepsA("LINE-INJ-01", "LINE-IMG-01"), "itest");
            Assert.Equal(0, repo.ReleaseWo(woId, StepsA("LINE-INJ-01", "LINE-IMG-01"), "itest"));
        }
        finally { CleanupItems(f); }
    }

    // ── BumpStepCompleted ──────────────────────────────────────────

    static decimal Bump(AmesConnectionFactory f, int routingLineId, decimal qty)
    {
        using var conn = f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        var n = WorkOrderRepository.BumpStepCompleted(conn, tx, routingLineId, qty, "itest");
        tx.Commit();
        return n;
    }

    static (string Status, decimal Completed, bool HasEnd) Header(AmesConnectionFactory f, int woId)
    {
        using var conn = f.OpenConnection();
        using var cmd  = new SqlCommand(
            "SELECT Status, ISNULL(CompletedQty,0) AS C, CASE WHEN ActualEnd IS NULL THEN 0 ELSE 1 END AS E FROM dbo.PP_WorkOrder WHERE WoID = @W;", conn);
        cmd.Parameters.AddWithValue("@W", woId);
        using var r = cmd.ExecuteReader(); r.Read();
        return ((string)r["Status"], (decimal)r["C"], (int)r["E"] == 1);
    }

    [SkippableFact]
    public void BumpStepCompleted_syncs_header_only_from_last_line_step()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var repo = new WorkOrderRepository(f);
            var woId = CreateDraft(f, ItemRoutingA, qty: 10);
            repo.ReleaseWo(woId, StepsA("LINE-INJ-01", "LINE-IMG-01"), "itest");
            var steps = repo.ListSteps(woId);
            var inj = steps[0].RoutingLineId; var img = steps[1].RoutingLineId;

            Assert.Equal(4m, Bump(f, inj, 4));
            Assert.Equal(("Released", 0m, false), Header(f, woId));          // INJ 는 헤더에 안 올라간다

            Assert.Equal(6m, Bump(f, img, 6));
            Assert.Equal(("Released", 6m, false), Header(f, woId));          // 마지막 라인 단계 → 동기화

            Assert.Equal(10m, Bump(f, img, 4));
            Assert.Equal(("Closed", 10m, true), Header(f, woId));           // OrderQty 도달 → Closed

            steps = repo.ListSteps(woId);
            Assert.Equal("Released", steps[0].Status);                        // INJ 단계는 그대로
            Assert.Equal(("Closed", 10m), (steps[1].Status, steps[1].CompletedQty));

            var stepEndBefore   = Scalar(f, "SELECT ActualEnd FROM dbo.PP_WorkOrderRouting WHERE RoutingLineID = @R;", ("@R", img));
            var headerEndBefore = Scalar(f, "SELECT ActualEnd FROM dbo.PP_WorkOrder WHERE WoID = @W;", ("@W", woId));

            Assert.Equal(11m, Bump(f, img, 1));                                // overshoot — 목표 이미 도달
            Assert.Equal(("Closed", 11m, true), Header(f, woId));
            steps = repo.ListSteps(woId);
            Assert.Equal(("Closed", 11m), (steps[1].Status, steps[1].CompletedQty));

            var stepEndAfter   = Scalar(f, "SELECT ActualEnd FROM dbo.PP_WorkOrderRouting WHERE RoutingLineID = @R;", ("@R", img));
            var headerEndAfter = Scalar(f, "SELECT ActualEnd FROM dbo.PP_WorkOrder WHERE WoID = @W;", ("@W", woId));
            Assert.Equal(stepEndBefore, stepEndAfter);                         // ActualEnd 는 첫 도달 시점에 고정
            Assert.Equal(headerEndBefore, headerEndAfter);
        }
        finally { CleanupItems(f); }
    }

    [SkippableFact]
    public void BumpStepCompleted_treats_last_line_step_as_last_for_routing_b()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var repo = new WorkOrderRepository(f);
            var woId = CreateDraft(f, ItemRoutingB, qty: 5);
            repo.ReleaseWo(woId, new[]
            {
                new WorkOrderRepository.StepLineChoice(1, "LINE-INJ-01"),
                new WorkOrderRepository.StepLineChoice(2, "LINE-PNT-01"),
                new WorkOrderRepository.StepLineChoice(3, null),
                new WorkOrderRepository.StepLineChoice(4, null),
            }, "itest");
            var pnt = repo.ListSteps(woId).Single(s => s.ProcessCode == "PNT").RoutingLineId;

            Bump(f, pnt, 5);
            Assert.Equal(("Closed", 5m, true), Header(f, woId));   // QC·FG 는 라인 없음 → PNT 가 마지막
        }
        finally { CleanupItems(f); }
    }

    [SkippableFact]
    public void BumpStepCompleted_throws_for_unknown_step()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Assert.ThrowsAny<SqlException>(() => Bump(f, -1, 1));
    }

    [SkippableFact]
    public void FindStepId_resolves_step_by_wo_and_line()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var repo = new WorkOrderRepository(f);
            var woId = CreateDraft(f, ItemRoutingA);
            repo.ReleaseWo(woId, StepsA("LINE-INJ-01", "LINE-IMG-01"), "itest");
            var img = repo.ListSteps(woId)[1].RoutingLineId;

            using var conn = f.OpenConnection();
            using var tx   = conn.BeginTransaction();
            Assert.Equal(img, WorkOrderRepository.FindStepId(conn, tx, woId, "LINE-IMG-01"));
            Assert.Null(WorkOrderRepository.FindStepId(conn, tx, woId, "LINE-PNT-01"));
            tx.Rollback();
        }
        finally { CleanupItems(f); }
    }

    // ── 라인 범위 조회 · 접수 ─────────────────────────────────────

    [SkippableFact]
    public void ListForLine_shows_wo_on_every_step_line_with_step_values()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var repo = new WorkOrderRepository(f);
            var woId = CreateDraft(f, ItemRoutingA, qty: 10);
            repo.ReleaseWo(woId, StepsA("LINE-INJ-02", "LINE-IMG-01"), "itest");
            Bump(f, repo.ListSteps(woId)[0].RoutingLineId, 3);

            var onInj = repo.ListForLine("LINE-INJ-02").Single(w => w.WoId == woId);
            Assert.Equal(("INJ", 1, "LINE-INJ-02", 3m, "Released"), (onInj.ProcessCode, onInj.StepSeq, onInj.LineId, onInj.CompletedQty, onInj.Status));
            Assert.NotNull(onInj.RoutingLineId);

            var onImg = repo.ListForLine("LINE-IMG-01").Single(w => w.WoId == woId);
            Assert.Equal(("IMG", 2, "LINE-IMG-01", 0m), (onImg.ProcessCode, onImg.StepSeq, onImg.LineId, onImg.CompletedQty));

            Assert.DoesNotContain(repo.ListForLine("LINE-INJ-01"), w => w.WoId == woId);
        }
        finally { CleanupItems(f); }
    }

    [SkippableFact]
    public void ListForLine_keeps_open_earlier_step_after_header_closes()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var repo = new WorkOrderRepository(f);
            var woId = CreateDraft(f, ItemRoutingA, qty: 5);
            repo.ReleaseWo(woId, StepsA("LINE-INJ-01", "LINE-IMG-01"), "itest");

            // 마지막 라인 단계(IMG)를 채우면 헤더가 먼저 Closed 된다. INJ 단계는 아직 열려 있어야 한다.
            Bump(f, repo.ListSteps(woId)[1].RoutingLineId, 5);
            Assert.Equal(("Closed", 5m, true), Header(f, woId));

            var onInj = repo.ListForLine("LINE-INJ-01").Single(w => w.WoId == woId);
            Assert.Equal("Released", onInj.Status);
            Assert.DoesNotContain(repo.ListForLine("LINE-IMG-01"), w => w.WoId == woId);
        }
        finally { CleanupItems(f); }
    }

    [SkippableFact]
    public void ListForLine_hides_cancelled_wo()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var repo = new WorkOrderRepository(f);
            var woId = CreateDraft(f, ItemRoutingA, qty: 5);
            repo.ReleaseWo(woId, StepsA("LINE-INJ-01", "LINE-IMG-01"), "itest");
            Assert.Equal(1, repo.CancelWo(woId, "itest"));

            Assert.DoesNotContain(repo.ListForLine("LINE-INJ-01"), w => w.WoId == woId);
            Assert.DoesNotContain(repo.ListForLine("LINE-IMG-01"), w => w.WoId == woId);
        }
        finally { CleanupItems(f); }
    }

    [SkippableFact]
    public void AcceptWo_marks_step_and_header_in_progress_and_active_lookup_finds_it()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var repo = new WorkOrderRepository(f);
            var woId = CreateDraft(f, ItemRoutingA);
            repo.ReleaseWo(woId, StepsA("LINE-INJ-01", "LINE-IMG-01"), "itest");
            var img = repo.ListSteps(woId)[1].RoutingLineId;

            var acceptId = repo.AcceptWo(img, "IMG-T1", "itest-op", "E-ITEST", "{}");
            Assert.True(acceptId > 0);

            var steps = repo.ListSteps(woId);
            Assert.Equal("Released",    steps[0].Status);
            Assert.Equal("In Progress", steps[1].Status);
            Assert.Equal(("In Progress", 0m, false), Header(f, woId));
            Assert.Equal("IMG-T1", Scalar(f, "SELECT TerminalLock FROM dbo.PP_WorkOrder WHERE WoID = @W;", ("@W", woId)));

            var active = repo.GetActiveForTerminal("LINE-IMG-01", "IMG-T1");
            Assert.NotNull(active);
            Assert.Equal((woId, img, "IMG"), (active!.WoId, active.RoutingLineId, active.ProcessCode));

            Assert.NotEqual(woId, repo.GetActiveForTerminal("LINE-INJ-01", "INJ-T1")?.WoId);   // INJ 단계는 아직 Released
        }
        finally { CleanupItems(f); }
    }

    [SkippableFact]
    public void AcceptWo_on_two_lines_keeps_each_terminals_active_step()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var repo = new WorkOrderRepository(f);
            var woId = CreateDraft(f, ItemRoutingA);
            repo.ReleaseWo(woId, StepsA("LINE-INJ-01", "LINE-IMG-01"), "itest");
            var steps = repo.ListSteps(woId);
            var inj = steps[0].RoutingLineId; var img = steps[1].RoutingLineId;

            repo.AcceptWo(inj, "INJ-T1", "itest-op", "E-ITEST", "{}");
            repo.AcceptWo(img, "IMG-T1", "itest-op", "E-ITEST", "{}");

            var activeInj = repo.GetActiveForTerminal("LINE-INJ-01", "INJ-T1");
            Assert.NotNull(activeInj);
            Assert.Equal(("INJ", "INJ-T1"), (activeInj!.ProcessCode, activeInj.TerminalLock));

            var activeImg = repo.GetActiveForTerminal("LINE-IMG-01", "IMG-T1");
            Assert.NotNull(activeImg);
            Assert.Equal(("IMG", "IMG-T1"), (activeImg!.ProcessCode, activeImg.TerminalLock));

            Assert.NotEqual(woId, repo.GetActiveForTerminal("LINE-INJ-01", "INJ-T2")?.WoId);   // 같은 라인, 다른 터미널
        }
        finally { CleanupItems(f); }
    }

    [SkippableFact]
    public void ListAll_carries_route_lines_for_released_wo()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var repo = new WorkOrderRepository(f);
            var draft = CreateDraft(f, ItemRoutingA);
            var rel   = CreateDraft(f, ItemRoutingB);
            repo.ReleaseWo(rel, new[]
            {
                new WorkOrderRepository.StepLineChoice(1, "LINE-INJ-01"),
                new WorkOrderRepository.StepLineChoice(2, "LINE-PNT-02"),
                new WorkOrderRepository.StepLineChoice(3, null),
                new WorkOrderRepository.StepLineChoice(4, null),
            }, "itest");

            var all = repo.ListAll();
            Assert.Null(all.Single(w => w.WoId == draft).RouteLines);
            Assert.Equal("LINE-INJ-01 → LINE-PNT-02 → QC(—) → FG(—)", all.Single(w => w.WoId == rel).RouteLines);
            Assert.Null(all.Single(w => w.WoId == rel).RoutingLineId);
        }
        finally { CleanupItems(f); }
    }
}
