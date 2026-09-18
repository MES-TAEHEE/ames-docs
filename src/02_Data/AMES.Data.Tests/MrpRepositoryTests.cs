using AMES.Data.Connection;
using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

/// <summary>
/// PP-005 MRP 실행·스냅샷·부족 PR 생성. AMES_DEV 통합 테스트, DB 미기동 시 skip.
/// 열린 WO 전체를 계산하므로 다른 데이터가 섞이지만, 검증은 ITEST-MRP-* 자재 행만 본다.
/// </summary>
public class MrpRepositoryTests
{
    const string Fg = "ITEST-MRP-FG";
    const string Sub = "ITEST-MRP-SUB";
    const string Rm = "ITEST-MRP-RM";
    const string Ok = "ITEST-MRP-OK";
    const string Ver = "V-ITEST-MRP-01";
    const string Actor = "ITEST";

    static readonly DateTime Due = DateTime.Today.AddDays(20);

    /// <summary>FG → SUB(×2) → RM(×3, 스크랩 10%) / FG → OK(×1). WO 10 EA 잔량 8. RM 재고 4·발주중 2·L/T 5. OK 재고 충분.</summary>
    static void Seed(AmesConnectionFactory f)
    {
        Cleanup(f);
        Exec(f, """
            INSERT INTO dbo.MD_Item (ItemNo, ItemName, ItemType, DefaultUOM, LeadTimeDays, ActiveFlag, CreatedBy)
            VALUES (@FG, N'ITEST MRP FG', 'ASSY', 'EA', NULL, 1, @By),
                   (@SUB, N'ITEST MRP SUB', 'SUB', 'EA', NULL, 1, @By),
                   (@RM, N'ITEST MRP RM', 'MATERIAL', 'KG', 5, 1, @By),
                   (@OK, N'ITEST MRP OK', 'MATERIAL', 'EA', 3, 1, @By);
            INSERT INTO dbo.MD_BomVersion (VersionID, RootItemNo, VersionNo, EffFrom, Status, CreatedBy)
            VALUES (@Ver, @FG, '01', '2020-01-01', 'APPROVED', @By);
            INSERT INTO dbo.MD_Bom (BOMID, ParentItemNo, CompItemNo, BOMLevel, QtyPer, ScrapPct, VersionID, ActiveFlag, CreatedBy)
            VALUES ('ITEST-MRP-B1', @FG, @SUB, 1, 2, 0, @Ver, 1, @By),
                   ('ITEST-MRP-B2', @SUB, @RM, 2, 3, 10, @Ver, 1, @By),
                   ('ITEST-MRP-B3', @FG, @OK, 1, 1, 0, @Ver, 1, @By),
                   ('ITEST-MRP-B4', @FG, 'ITEST-MRP-INACTIVE', 1, 99, 0, @Ver, 0, @By);
            INSERT INTO dbo.PP_WorkOrder (WoNumber, ItemNo, OrderQty, CompletedQty, Status, DueDate, CreatedBy)
            VALUES ('ITEST-MRP-WO-1', @FG, 10, 2, 'Released', @Due, @By),
                   ('ITEST-MRP-WO-2', @FG, 50, 0, 'Completed', @Due, @By);
            INSERT INTO dbo.WH_Inventory (ItemNo, LocationID, OnHandQty, ReservedQty, Status, CreatedBy)
            VALUES (@RM, 'WH-A-01', 5, 1, 'OK', @By), (@OK, 'WH-A-01', 100, 0, 'OK', @By);
            INSERT INTO dbo.WH_PurchaseOrder (PoNumber, PoLineNo, ItemNo, OrderQty, ReceivedQty, Status, CreatedBy)
            VALUES ('ITEST-MRP-PO', 1, @RM, 3, 1, 'Open', @By),
                   ('ITEST-MRP-PO', 2, @RM, 7, 7, 'Received', @By);
            """, ("@FG", Fg), ("@SUB", Sub), ("@RM", Rm), ("@OK", Ok), ("@Ver", Ver), ("@Due", Due), ("@By", Actor));
    }

    static void Cleanup(AmesConnectionFactory f)
    {
        Exec(f, """
            DELETE FROM dbo.PP_MRPResultWo WHERE ItemNo LIKE 'ITEST-MRP-%' OR MrpRunID IN (SELECT MrpRunID FROM dbo.PP_MRPLog WHERE CreatedBy = @By);
            DELETE FROM dbo.PP_MRPResult   WHERE ItemNo LIKE 'ITEST-MRP-%' OR MrpRunID IN (SELECT MrpRunID FROM dbo.PP_MRPLog WHERE CreatedBy = @By);
            DELETE FROM dbo.PP_MRPLog      WHERE CreatedBy = @By;
            DELETE FROM dbo.PP_PurchaseRequest WHERE ItemNo LIKE 'ITEST-MRP-%';
            DELETE FROM dbo.WH_PurchaseOrder   WHERE ItemNo LIKE 'ITEST-MRP-%';
            DELETE FROM dbo.WH_Inventory       WHERE ItemNo LIKE 'ITEST-MRP-%';
            DELETE FROM dbo.PP_WorkOrder       WHERE ItemNo LIKE 'ITEST-MRP-%';
            DELETE FROM dbo.MD_Bom             WHERE VersionID = @Ver;
            DELETE FROM dbo.MD_BomVersion      WHERE VersionID = @Ver;
            DELETE FROM dbo.MD_Item            WHERE ItemNo LIKE 'ITEST-MRP-%';
            """, ("@Ver", Ver), ("@By", Actor));
    }

    static PpRepository.MrpMaterialRow Row(PpRepository.MrpSnapshot s, string itemNo)
        => Assert.Single(s.Materials, m => m.ItemNo == itemNo);

    [SkippableFact]
    public void RunMrp_explodes_open_wo_and_snapshots_shortage()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f!);
        try
        {
            var repo = new PpRepository(f!);
            var runId = repo.RunMrp(Actor);

            var snap = repo.GetLatestMrp();
            Assert.NotNull(snap);
            Assert.Equal(runId, snap!.Run.MrpRunId);
            Assert.Equal("Completed", snap.Run.Status);
            Assert.True(snap.Run.WosConsidered >= 1);

            var rm = Row(snap, Rm);
            Assert.Equal(52.8m, rm.Required);           // 8 × 2 × 3 × 1.1
            Assert.Equal(4m, rm.Stock);                 // 5 − 1
            Assert.Equal(2m, rm.OnOrder);               // Open PO 3 − 1 (Received PO 제외)
            Assert.Equal(46.8m, rm.Shortage);
            Assert.Equal(5, rm.LeadTimeDays);
            Assert.Equal(Due.AddDays(-5), rm.OrderDue);
            Assert.Equal("KG", rm.Uom);
            Assert.Null(rm.PrId);
            Assert.True(rm.IsShort);
            var wo = Assert.Single(rm.Wos);
            Assert.Equal("ITEST-MRP-WO-1", wo.WoNumber);
            Assert.Equal(52.8m, wo.Qty);

            var ok = Row(snap, Ok);
            Assert.Equal(8m, ok.Required);
            Assert.Equal(-92m, ok.Shortage);
            Assert.Null(ok.OrderDue);
            Assert.False(ok.IsShort);

            Assert.DoesNotContain(snap.Materials, m => m.ItemNo == Sub);   // 중간 조립품은 leaf 가 아니다
            Assert.DoesNotContain(snap.Materials, m => m.ItemNo == "ITEST-MRP-INACTIVE");
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void CreateShortagePrs_inserts_draft_pr_and_clears_shortage()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f!);
        try
        {
            var repo = new PpRepository(f!);
            var runId = repo.RunMrp(Actor);

            var created = repo.CreateShortagePrs(runId, new[] { Rm, Ok }, Actor);   // OK 는 부족이 아니라 무시
            var only = Assert.Single(created);
            Assert.Equal(Rm, only.ItemNo);
            Assert.Matches($@"^PR-{DateTime.Today:yyyyMMdd}-\d{{4}}$", only.PrNumber);   // WO-yyyyMMdd-NNN 과 같은 일별 채번

            var pr = repo.ListPurchaseRequests(500).Single(p => p.ItemNo == Rm);
            Assert.Equal(only.PrNumber, pr.PrNumber);
            Assert.Equal("Draft", pr.Status);
            Assert.Equal(46.8m, pr.RequiredQty);
            Assert.Equal(Due.AddDays(-5), pr.RequiredDate);
            Assert.Equal("ITEST-MRP-WO-1", pr.WoNumber);

            var rm = Row(repo.GetLatestMrp()!, Rm);
            Assert.Equal(pr.PrId, rm.PrId);
            Assert.Equal(only.PrNumber, rm.PrNumber);
            Assert.Equal(48.8m, rm.OnOrder);
            Assert.Equal(0m, rm.Shortage);
            Assert.False(rm.IsShort);

            Assert.Empty(repo.CreateShortagePrs(runId, new[] { Rm }, Actor));   // 이미 PR 연결 → 중복 생성 없음

            // 다음 실행은 PO 미전환 PR 을 발주중으로 센다
            repo.RunMrp(Actor);
            Assert.Equal(48.8m, Row(repo.GetLatestMrp()!, Rm).OnOrder);
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void CreateShortagePrs_honours_user_quantity_and_keeps_residual_shortage()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f!);
        try
        {
            var repo = new PpRepository(f!);
            var runId = repo.RunMrp(Actor);

            var created = repo.CreateShortagePrs(runId, new[] { new PpRepository.MrpPrRequest(Rm, 30m) }, Actor);
            Assert.Single(created);

            var pr = repo.ListPurchaseRequests(500).Single(p => p.ItemNo == Rm);
            Assert.Equal(30m, pr.RequiredQty);

            var rm = Row(repo.GetLatestMrp()!, Rm);
            Assert.Equal(pr.PrId, rm.PrId);
            Assert.Equal(32m, rm.OnOrder);      // 2 + 30
            Assert.Equal(16.8m, rm.Shortage);   // 46.8 − 30 잔여 부족
            Assert.False(rm.IsShort);           // PR 이 연결되면 같은 실행에서 재생성 대상이 아니다
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void CreateShortagePrs_rejects_non_positive_quantity()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f!);
        try
        {
            var repo = new PpRepository(f!);
            var runId = repo.RunMrp(Actor);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                repo.CreateShortagePrs(runId, new[] { new PpRepository.MrpPrRequest(Rm, 0m) }, Actor));
            Assert.Null(Row(repo.GetLatestMrp()!, Rm).PrId);
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void ListOpenPrsForItem_returns_only_prs_not_yet_converted_to_po()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f!);
        try
        {
            Exec(f!, """
                INSERT INTO dbo.PP_PurchaseRequest (PrNumber, ItemNo, RequiredQty, RequiredDate, Status, SapPoNumber, CreatedBy)
                VALUES ('ITEST-PR-PO', @RM, 5, '2026-01-01', 'Approved', 'PO-ITEST', @By),
                       ('ITEST-PR-FAIL', @RM, 5, '2026-01-01', 'Failed', NULL, @By),
                       ('ITEST-PR-SENT', @RM, 7, '2026-01-02', 'Sent', NULL, @By),
                       ('ITEST-PR-OTHER', @OK, 5, '2026-01-01', 'Draft', NULL, @By);
                """, ("@RM", Rm), ("@OK", Ok), ("@By", Actor));
            var repo = new PpRepository(f!);
            var runId = repo.RunMrp(Actor);
            Assert.Equal(2m + 7m, Row(repo.GetLatestMrp()!, Rm).OnOrder);   // Open PO 2 + Sent PR 7 (Failed·PO 전환분 제외)
            var created = Assert.Single(repo.CreateShortagePrs(runId, new[] { Rm }, Actor));

            var open = repo.ListOpenPrsForItem(Rm);
            Assert.Equal(new[] { "ITEST-PR-SENT", created.PrNumber }, open.Select(p => p.PrNumber));
            Assert.Equal("ITEST-MRP-WO-1", open[1].WoNumber);
            Assert.Empty(repo.ListOpenPrsForItem("ITEST-MRP-NONE"));
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void RunMrp_with_circular_bom_logs_failed_run_and_throws()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f!);
        try
        {
            Exec(f!, """
                INSERT INTO dbo.MD_Bom (BOMID, ParentItemNo, CompItemNo, BOMLevel, QtyPer, ScrapPct, VersionID, ActiveFlag, CreatedBy)
                VALUES ('ITEST-MRP-B9', @RM, @FG, 3, 1, 0, @Ver, 1, @By);
                """, ("@RM", Rm), ("@FG", Fg), ("@Ver", Ver), ("@By", Actor));
            var repo = new PpRepository(f!);
            Assert.Throws<AMES.Data.Services.MrpCalculator.MrpCycleException>(() => repo.RunMrp(Actor));
            Assert.Equal("Failed", Scalar(f!, "SELECT TOP 1 Status FROM dbo.PP_MRPLog WHERE CreatedBy = @By ORDER BY MrpRunID DESC;", ("@By", Actor)));
            Assert.Equal(0, Scalar(f!, "SELECT COUNT(*) FROM dbo.PP_MRPResult WHERE ItemNo LIKE 'ITEST-MRP-%';"));
        }
        finally { Cleanup(f!); }
    }
}
