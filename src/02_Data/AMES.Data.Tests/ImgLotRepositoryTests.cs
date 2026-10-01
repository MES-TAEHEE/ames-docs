using AMES.Contracts.Enums;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

/// <summary>
/// IMG Core 스캔 → 완제품 LOT 생성 통합 테스트(AMES_DEV). DB 미기동 시 skip.
/// 코어 ITEST-CORE-A(INJ LOT 품번) ≠ 완제품 ITEST-FG-A(WO·IMG LOT 품번). WO 의 INJ 단계 ItemNo = 코어.
/// </summary>
public class ImgLotRepositoryTests
{
    static readonly string Conn =
        Environment.GetEnvironmentVariable("AMES_TEST_CONN")
        ?? "Server=192.168.1.100,1433;Database=AMES_DEV;User Id=ames_app;Password=!Dev2026;TrustServerCertificate=True;Encrypt=True;Connect Timeout=10;";

    const string Core    = "ITEST-CORE-A";
    const string Fg      = "ITEST-FG-A";
    const string FgB     = "ITEST-FG-B";     // 같은 코어를 쓰는 다른 색상 완제품
    const string ImgLine = "LINE-IMG-01";
    const string InjLine = "LINE-INJ-01";

    static AmesConnectionFactory? TryFactory()
    {
        try { var f = new AmesConnectionFactory(Conn); using var c = f.OpenConnection(); return f; }
        catch { return null; }
    }

    static string NewCoreCode() => "ZC" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();

    static void SeedItems(AmesConnectionFactory f)
    {
        using var conn = f.OpenConnection();
        using var cmd = new SqlCommand("""
            IF NOT EXISTS (SELECT 1 FROM dbo.MD_Item WHERE ItemNo = @C)
                INSERT INTO dbo.MD_Item (ItemNo, ItemName, ItemType, ActiveFlag, CreatedBy) VALUES (@C, N'CORE-ITEST img core', 'SUB', 1, 'ITEST');
            IF NOT EXISTS (SELECT 1 FROM dbo.MD_Item WHERE ItemNo = @F)
                INSERT INTO dbo.MD_Item (ItemNo, ItemName, ItemType, RoutingType, PGN, ALC, MountPos, ActiveFlag, CreatedBy)
                VALUES (@F, N'ITEST img fg', 'ASSY', 'A', 'QTST', 'T001', 'FL', 1, 'ITEST');
            IF NOT EXISTS (SELECT 1 FROM dbo.MD_Item WHERE ItemNo = @B)
                INSERT INTO dbo.MD_Item (ItemNo, ItemName, ItemType, RoutingType, PGN, ALC, MountPos, ActiveFlag, CreatedBy)
                VALUES (@B, N'ITEST img fg B', 'ASSY', 'A', 'QTST', 'T002', 'FL', 1, 'ITEST');
            """, conn);
        cmd.Parameters.AddWithValue("@C", Core); cmd.Parameters.AddWithValue("@F", Fg); cmd.Parameters.AddWithValue("@B", FgB);
        cmd.ExecuteNonQuery();
    }

    static int InsertCore(AmesConnectionFactory f, string lotCode, string status)
    {
        using var conn = f.OpenConnection();
        using var cmd = new SqlCommand("""
            DECLARE @T TABLE (LotID INT);
            INSERT INTO dbo.tbl_Lot (LotCode, ItemNo, LineID, ProcessCode, BatchSize, RemainingQty,
                                     ProducedAt, Status, QualityFlag, CreatedBy, CreatedTS)
            OUTPUT INSERTED.LotID INTO @T
            VALUES (@C, @I, @Line, 'INJ', 1, 1, SYSDATETIME(), @S, 'OK', 'ITEST', SYSDATETIME());
            INSERT INTO dbo.PR_InjLot (LotID, ConfirmStatus, PrintedCount, CreatedBy)
            SELECT LotID, @S, 0, 'ITEST' FROM @T;
            SELECT LotID FROM @T;
            """, conn);
        cmd.Parameters.AddWithValue("@C", lotCode);
        cmd.Parameters.AddWithValue("@I", Core);
        cmd.Parameters.AddWithValue("@Line", InjLine);
        cmd.Parameters.AddWithValue("@S", status);
        return (int)cmd.ExecuteScalar()!;
    }

    /// <summary>완제품 WO: INJ 단계(ItemNo = 코어, Closed) + IMG 단계(In Progress). priority 로 선택 순서를 시험한다.</summary>
    static int InsertImgWo(AmesConnectionFactory f, int priority = 5, string? soCustomerId = null, string finishedItem = Fg)
    {
        using var conn = f.OpenConnection();
        using var cmd = new SqlCommand("""
            DECLARE @W TABLE (WoID INT);
            DECLARE @So INT = NULL;
            IF @Cust IS NOT NULL
            BEGIN
                INSERT INTO dbo.PP_CustomerOrder (SoNumber, SoLineNo, CustomerID, ItemNo, OrderQty, RequestedDeliveryDate, Status, CreatedBy)
                VALUES (@No, 1, @Cust, @I, 100, CAST(GETDATE() AS date), 'Confirmed', 'ITEST');
                SET @So = SCOPE_IDENTITY();
            END
            INSERT INTO dbo.PP_WorkOrder (WoNumber, SoID, ItemNo, OrderQty, CompletedQty, Status, Priority, CreatedBy, CreatedTS)
            OUTPUT INSERTED.WoID INTO @W
            VALUES (@No, @So, @I, 100, 0, 'In Progress', @P, 'ITEST', SYSDATETIME());
            INSERT INTO dbo.PP_WorkOrderRouting (WoID, StepSeq, ProcessCode, LineID, ItemNo, Status, CompletedQty, CreatedBy)
            SELECT WoID, 1, 'INJ', @InjLine, @C, 'Closed', 100, 'ITEST' FROM @W
            UNION ALL
            SELECT WoID, 2, 'IMG', @Line, NULL, 'In Progress', 0, 'ITEST' FROM @W;
            SELECT WoID FROM @W;
            """, conn);
        cmd.Parameters.AddWithValue("@No", "WO-ITC-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant());
        cmd.Parameters.AddWithValue("@I", finishedItem);
        cmd.Parameters.AddWithValue("@C", Core);
        cmd.Parameters.AddWithValue("@Line", ImgLine);
        cmd.Parameters.AddWithValue("@InjLine", InjLine);
        cmd.Parameters.AddWithValue("@P", priority);
        cmd.Parameters.AddWithValue("@Cust", (object?)soCustomerId ?? DBNull.Value);
        return (int)cmd.ExecuteScalar()!;
    }

    static int ImgLotCount(AmesConnectionFactory f, string itemNo)
    {
        using var conn = f.OpenConnection();
        using var cmd = new SqlCommand("SELECT COUNT(*) FROM dbo.tbl_Lot WHERE ItemNo = @I AND ProcessCode = 'IMG';", conn);
        cmd.Parameters.AddWithValue("@I", itemNo);
        return (int)cmd.ExecuteScalar()!;
    }

    static int? ParentOf(AmesConnectionFactory f, string imgLotCode)
    {
        using var conn = f.OpenConnection();
        using var cmd = new SqlCommand("SELECT ParentLotID FROM dbo.tbl_Lot WHERE LotCode = @C;", conn);
        cmd.Parameters.AddWithValue("@C", imgLotCode);
        return cmd.ExecuteScalar() as int?;
    }

    static void Cleanup(AmesConnectionFactory f)
    {
        using var conn = f.OpenConnection();
        using var cmd = new SqlCommand("""
            DELETE d FROM dbo.PR_DefectDetail     d JOIN dbo.tbl_Lot l ON l.LotID = d.LotID WHERE l.ItemNo IN (@C, @F, @B);
            DELETE r FROM dbo.PR_ProductionResult r JOIN dbo.tbl_Lot l ON l.LotID = r.LotID WHERE l.ItemNo IN (@C, @F, @B);
            DELETE e FROM dbo.PR_ImgLot           e JOIN dbo.tbl_Lot l ON l.LotID = e.LotID WHERE l.ItemNo IN (@C, @F, @B);
            DELETE e FROM dbo.PR_InjLot           e JOIN dbo.tbl_Lot l ON l.LotID = e.LotID WHERE l.ItemNo IN (@C, @F, @B);
            DELETE FROM dbo.tbl_Lot WHERE ItemNo IN (@C, @F, @B);
            DELETE r FROM dbo.PP_WorkOrderRouting r JOIN dbo.PP_WorkOrder w ON w.WoID = r.WoID WHERE w.ItemNo IN (@C, @F, @B);
            DELETE FROM dbo.PP_WorkOrder     WHERE ItemNo IN (@C, @F, @B);
            DELETE FROM dbo.PP_CustomerOrder WHERE ItemNo IN (@C, @F, @B);
            DELETE FROM dbo.MD_Item WHERE ItemNo IN (@C, @F, @B) AND CreatedBy = 'ITEST';
            """, conn);
        cmd.Parameters.AddWithValue("@C", Core); cmd.Parameters.AddWithValue("@F", Fg); cmd.Parameters.AddWithValue("@B", FgB);
        cmd.ExecuteNonQuery();
    }

    [SkippableFact]
    public void CreateFromCore_confirmed_core_creates_finished_lot_linked_to_core()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f!); SeedItems(f!);
        try
        {
            InsertImgWo(f!, soCustomerId: "CUS-SAV");
            var core = NewCoreCode();
            var coreId = InsertCore(f!, core, "CONFIRMED");
            var repo = new ImgLotRepository(f!);

            var (outcome, lot, usedBy, itemNo) = repo.CreateFromCore(core, ImgLine, "E-ITEST");

            Assert.Equal(ImgCoreOutcome.Created, outcome);
            Assert.NotNull(lot);
            Assert.Null(usedBy);
            Assert.Equal(Fg, itemNo);                       // 완제품 = WO 품번, 코어 품번이 아니다
            Assert.Equal(Fg, lot!.ItemNo);
            Assert.Equal(("QTST", "T001", "FL"), (lot.Pgn, lot.Alc, lot.MountPos));   // 완제품 속성
            Assert.Equal("SAV", lot.CustomerCode);          // 그 WO 의 수주처
            Assert.Equal("RAW", lot.ConfirmStatus);
            Assert.Equal(core, lot.CoreLotCode);
            Assert.Equal(coreId, ParentOf(f!, lot.LotCode));
            Assert.Equal(core, repo.GetByLotCode(lot.LotCode)!.CoreLotCode);
            Assert.Equal(0, ImgLotCount(f!, Core));
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void CreateFromCore_picks_wo_by_open_step_order()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f!); SeedItems(f!);
        try
        {
            var later = InsertImgWo(f!, priority: 5);
            var first = InsertImgWo(f!, priority: 1);       // Priority 가 낮은 숫자가 먼저
            var core = NewCoreCode(); InsertCore(f!, core, "CONFIRMED");
            var repo = new ImgLotRepository(f!);

            var lot = repo.CreateFromCore(core, ImgLine, "E-ITEST").Lot!;
            var ok  = repo.ConfirmByLotCode(lot.LotCode, ImgLine, "E-ITEST", null, "E-ITEST");

            Assert.Equal(ImgConfirmOutcome.Confirmed, ok.Outcome);
            Assert.Equal(first, ok.WoId);
            Assert.NotEqual(later, ok.WoId);
        }
        finally { Cleanup(f!); }
    }

    /// <summary>
    /// 색상 변형 완제품들이 같은 코어를 쓴다(마스터 리스트 코어 35종 중 24종). IMG-MAIN 좌측에서 고른 완제품이 있으면
    /// 그 완제품의 열린 WO 만 받고, 그 WO 가 없으면 다른 색으로 흘리지 않고 NoWoForItem.
    /// </summary>
    [SkippableFact]
    public void CreateFromCore_uses_selected_finished_item_when_core_is_shared()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f!); SeedItems(f!);
        try
        {
            var woA = InsertImgWo(f!, priority: 1);                     // 순서 규칙으로는 A 가 먼저
            var woB = InsertImgWo(f!, priority: 5, finishedItem: FgB);
            var repo = new ImgLotRepository(f!);

            var core1 = NewCoreCode(); InsertCore(f!, core1, "CONFIRMED");
            var picked = repo.CreateFromCore(core1, ImgLine, "E-ITEST", FgB);
            Assert.Equal(ImgCoreOutcome.Created, picked.Outcome);
            Assert.Equal((FgB, FgB, "T002"), (picked.ItemNo, picked.Lot!.ItemNo, picked.Lot.Alc));
            Assert.Equal(woB, repo.ConfirmByLotCode(picked.Lot.LotCode, ImgLine, "E-ITEST", null, "E-ITEST").WoId);

            var core2 = NewCoreCode(); InsertCore(f!, core2, "CONFIRMED");
            var none = repo.CreateFromCore(core2, ImgLine, "E-ITEST", "ITEST-FG-NONE");
            Assert.Equal((ImgCoreOutcome.NoWoForItem, Core), (none.Outcome, none.ItemNo));
            Assert.Null(none.Lot);

            var core3 = NewCoreCode(); InsertCore(f!, core3, "CONFIRMED");
            var byOrder = repo.CreateFromCore(core3, ImgLine, "E-ITEST", null);   // 선택 없음 → 순서 규칙
            Assert.Equal(Fg, byOrder.ItemNo);
            Assert.Equal(woA, repo.ConfirmByLotCode(byOrder.Lot!.LotCode, ImgLine, "E-ITEST", null, "E-ITEST").WoId);
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void CreateFromCore_same_core_twice_returns_used_with_first_lot()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f!); SeedItems(f!);
        try
        {
            InsertImgWo(f!);
            var core = NewCoreCode();
            InsertCore(f!, core, "CONFIRMED");
            var repo = new ImgLotRepository(f!);

            var first = repo.CreateFromCore(core, ImgLine, "E-ITEST");
            var second = repo.CreateFromCore(core, ImgLine, "E-ITEST");

            Assert.Equal(ImgCoreOutcome.CoreUsed, second.Outcome);
            Assert.Null(second.Lot);
            Assert.Equal(first.Lot!.LotCode, second.UsedByLotCode);
            Assert.Equal(1, ImgLotCount(f!, Fg));
        }
        finally { Cleanup(f!); }
    }

    static void AssertRejected(string coreStatus, ImgCoreOutcome expected)
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f!); SeedItems(f!);
        try
        {
            InsertImgWo(f!);
            var core = NewCoreCode();
            InsertCore(f!, core, coreStatus);

            var (outcome, lot, _, _) = new ImgLotRepository(f!).CreateFromCore(core, ImgLine, "E-ITEST");

            Assert.Equal(expected, outcome);
            Assert.Null(lot);
            Assert.Equal(0, ImgLotCount(f!, Fg));
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact] public void CreateFromCore_rejects_raw_core()      => AssertRejected("RAW",      ImgCoreOutcome.CoreUnconfirmed);
    [SkippableFact] public void CreateFromCore_rejects_defect_core()   => AssertRejected("DEFECT",   ImgCoreOutcome.CoreDefect);
    [SkippableFact] public void CreateFromCore_rejects_scrapped_core() => AssertRejected("SCRAPPED", ImgCoreOutcome.CoreScrapped);

    [SkippableFact]
    public void CreateFromCore_without_open_wo_creates_nothing_and_reports_core_item()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f!); SeedItems(f!);
        try
        {
            var core = NewCoreCode();
            InsertCore(f!, core, "CONFIRMED");

            var (outcome, lot, _, itemNo) = new ImgLotRepository(f!).CreateFromCore(core, ImgLine, "E-ITEST");

            Assert.Equal(ImgCoreOutcome.NoWoForItem, outcome);
            Assert.Null(lot);
            Assert.Equal(Core, itemNo);                     // WO 가 없으면 완제품을 모른다 — 코어 품번을 돌려준다
            Assert.Equal(0, ImgLotCount(f!, Fg));
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void CreateFromCore_unknown_code_is_not_found()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var (outcome, lot, _, _) = new ImgLotRepository(f!).CreateFromCore("ZZNOSUCH1", ImgLine, "E-ITEST");
        Assert.Equal(ImgCoreOutcome.NotFound, outcome);
        Assert.Null(lot);
    }

    [SkippableFact]
    public void CreateFromCore_img_lot_code_is_not_a_core()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f!); SeedItems(f!);
        try
        {
            InsertImgWo(f!);
            var core = NewCoreCode();
            InsertCore(f!, core, "CONFIRMED");
            var repo = new ImgLotRepository(f!);
            var created = repo.CreateFromCore(core, ImgLine, "E-ITEST").Lot!;

            var (outcome, lot, _, _) = repo.CreateFromCore(created.LotCode, ImgLine, "E-ITEST");

            Assert.Equal(ImgCoreOutcome.NotFound, outcome);
            Assert.Null(lot);
            Assert.Equal(1, ImgLotCount(f!, Fg));
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void Today_lots_show_core_for_scanned_lots_and_none_for_the_fallback_button()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f!); SeedItems(f!);
        try
        {
            InsertImgWo(f!);
            var core = NewCoreCode();
            InsertCore(f!, core, "CONFIRMED");
            var repo = new ImgLotRepository(f!);
            var fromCore = repo.CreateFromCore(core, ImgLine, "E-ITEST").Lot!;
            var fallback = repo.CreateRawLot(ImgLine, Fg, "E-ITEST");

            var today = repo.GetTodayLots(ImgLine);

            Assert.Null(fallback.CoreLotCode);
            Assert.Null(ParentOf(f!, fallback.LotCode));
            Assert.Null(today.Single(x => x.LotCode == fallback.LotCode).CoreLotCode);
            Assert.Equal(core, today.Single(x => x.LotCode == fromCore.LotCode).CoreLotCode);
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void Lots_from_core_can_be_judged_ok_and_ng()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f!); SeedItems(f!);
        try
        {
            var woId = InsertImgWo(f!);
            var coreA = NewCoreCode(); InsertCore(f!, coreA, "CONFIRMED");
            var coreB = NewCoreCode(); InsertCore(f!, coreB, "CONFIRMED");
            var repo = new ImgLotRepository(f!);
            var okLot = repo.CreateFromCore(coreA, ImgLine, "E-ITEST").Lot!;
            var ngLot = repo.CreateFromCore(coreB, ImgLine, "E-ITEST").Lot!;

            var ok = repo.ConfirmByLotCode(okLot.LotCode, ImgLine, "E-ITEST", null, "E-ITEST");
            var ng = repo.RegisterDefect(ngLot.LotCode, ImgLine, "IMG-D01", "E-ITEST", null, "E-ITEST");

            Assert.Equal(ImgConfirmOutcome.Confirmed, ok.Outcome);
            Assert.Equal((Fg, woId), (ok.ItemNo, ok.WoId));
            Assert.Equal(DefectRegisterOutcome.Registered, ng.Outcome);
            Assert.Equal("CONFIRMED", repo.GetByLotCode(okLot.LotCode)!.ConfirmStatus);
            Assert.Equal("DEFECT",    repo.GetByLotCode(ngLot.LotCode)!.ConfirmStatus);
        }
        finally { Cleanup(f!); }
    }
}
