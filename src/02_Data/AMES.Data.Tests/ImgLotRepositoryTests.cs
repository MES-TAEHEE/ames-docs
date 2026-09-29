using AMES.Contracts.Enums;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

/// <summary>
/// IMG Core 스캔 → 완제품 LOT 생성 통합 테스트(AMES_DEV). DB 미기동 시 skip.
/// 테스트 전용 품번 ITEST-CORE-A 로 Core·WO 를 만들고 끝나면 그 품번 행을 전부 지운다.
/// </summary>
public class ImgLotRepositoryTests
{
    static readonly string Conn =
        Environment.GetEnvironmentVariable("AMES_TEST_CONN")
        ?? "Server=192.168.1.100,1433;Database=AMES_DEV;User Id=ames_app;Password=!Dev2026;TrustServerCertificate=True;Encrypt=True;Connect Timeout=10;";

    const string Item    = "ITEST-CORE-A";
    const string ImgLine = "LINE-IMG-01";
    const string InjLine = "LINE-INJ-01";

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

    static string NewCoreCode() => "ZC" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();

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
        cmd.Parameters.AddWithValue("@I", Item);
        cmd.Parameters.AddWithValue("@Line", InjLine);
        cmd.Parameters.AddWithValue("@S", status);
        return (int)cmd.ExecuteScalar()!;
    }

    static void InsertImgWo(AmesConnectionFactory f)
    {
        using var conn = f.OpenConnection();
        using var cmd = new SqlCommand("""
            DECLARE @W TABLE (WoID INT);
            INSERT INTO dbo.PP_WorkOrder (WoNumber, ItemNo, OrderQty, CompletedQty, Status, CreatedBy, CreatedTS)
            OUTPUT INSERTED.WoID INTO @W
            VALUES (@No, @I, 100, 0, 'In Progress', 'ITEST', SYSDATETIME());
            INSERT INTO dbo.PP_WorkOrderRouting (WoID, StepSeq, ProcessCode, LineID, Status, CompletedQty, CreatedBy)
            SELECT WoID, 2, 'IMG', @Line, 'In Progress', 0, 'ITEST' FROM @W;
            """, conn);
        cmd.Parameters.AddWithValue("@No", "WO-ITC-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant());
        cmd.Parameters.AddWithValue("@I", Item);
        cmd.Parameters.AddWithValue("@Line", ImgLine);
        cmd.ExecuteNonQuery();
    }

    static int ImgLotCount(AmesConnectionFactory f)
    {
        using var conn = f.OpenConnection();
        using var cmd = new SqlCommand(
            "SELECT COUNT(*) FROM dbo.tbl_Lot WHERE ItemNo = @I AND ProcessCode = 'IMG';", conn);
        cmd.Parameters.AddWithValue("@I", Item);
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
            DELETE d FROM dbo.PR_DefectDetail     d JOIN dbo.tbl_Lot l ON l.LotID = d.LotID WHERE l.ItemNo = @I;
            DELETE r FROM dbo.PR_ProductionResult r JOIN dbo.tbl_Lot l ON l.LotID = r.LotID WHERE l.ItemNo = @I;
            DELETE e FROM dbo.PR_ImgLot           e JOIN dbo.tbl_Lot l ON l.LotID = e.LotID WHERE l.ItemNo = @I;
            DELETE e FROM dbo.PR_InjLot           e JOIN dbo.tbl_Lot l ON l.LotID = e.LotID WHERE l.ItemNo = @I;
            DELETE FROM dbo.tbl_Lot WHERE ItemNo = @I;
            DELETE r FROM dbo.PP_WorkOrderRouting r JOIN dbo.PP_WorkOrder w ON w.WoID = r.WoID WHERE w.ItemNo = @I;
            DELETE FROM dbo.PP_WorkOrder WHERE ItemNo = @I;
            """, conn);
        cmd.Parameters.AddWithValue("@I", Item);
        cmd.ExecuteNonQuery();
    }

    [SkippableFact]
    public void CreateFromCore_confirmed_core_creates_linked_raw_lot()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f!);
        try
        {
            InsertImgWo(f!);
            var core = NewCoreCode();
            var coreId = InsertCore(f!, core, "CONFIRMED");
            var repo = new ImgLotRepository(f!);

            var (outcome, lot, usedBy, itemNo) = repo.CreateFromCore(core, ImgLine, "E-ITEST");

            Assert.Equal(ImgCoreOutcome.Created, outcome);
            Assert.NotNull(lot);
            Assert.Null(usedBy);
            Assert.Equal(Item, itemNo);
            Assert.Equal(Item, lot!.ItemNo);
            Assert.Equal("RAW", lot.ConfirmStatus);
            Assert.Equal(core, lot.CoreLotCode);
            Assert.Equal(coreId, ParentOf(f!, lot.LotCode));
            Assert.Equal(core, repo.GetByLotCode(lot.LotCode)!.CoreLotCode);
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void CreateFromCore_same_core_twice_returns_used_with_first_lot()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f!);
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
            Assert.Equal(1, ImgLotCount(f!));
        }
        finally { Cleanup(f!); }
    }

    static void AssertRejected(string coreStatus, ImgCoreOutcome expected)
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f!);
        try
        {
            InsertImgWo(f!);
            var core = NewCoreCode();
            InsertCore(f!, core, coreStatus);

            var (outcome, lot, _, _) = new ImgLotRepository(f!).CreateFromCore(core, ImgLine, "E-ITEST");

            Assert.Equal(expected, outcome);
            Assert.Null(lot);
            Assert.Equal(0, ImgLotCount(f!));
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact] public void CreateFromCore_rejects_raw_core()      => AssertRejected("RAW",      ImgCoreOutcome.CoreUnconfirmed);
    [SkippableFact] public void CreateFromCore_rejects_defect_core()   => AssertRejected("DEFECT",   ImgCoreOutcome.CoreDefect);
    [SkippableFact] public void CreateFromCore_rejects_scrapped_core() => AssertRejected("SCRAPPED", ImgCoreOutcome.CoreScrapped);

    [SkippableFact]
    public void CreateFromCore_without_open_wo_creates_nothing()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f!);
        try
        {
            var core = NewCoreCode();
            InsertCore(f!, core, "CONFIRMED");

            var (outcome, lot, _, itemNo) = new ImgLotRepository(f!).CreateFromCore(core, ImgLine, "E-ITEST");

            Assert.Equal(ImgCoreOutcome.NoWoForItem, outcome);
            Assert.Null(lot);
            Assert.Equal(Item, itemNo);
            Assert.Equal(0, ImgLotCount(f!));
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
        Cleanup(f!);
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
            Assert.Equal(1, ImgLotCount(f!));
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void Lots_from_core_can_be_judged_ok_and_ng()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f!);
        try
        {
            InsertImgWo(f!);
            var coreA = NewCoreCode(); InsertCore(f!, coreA, "CONFIRMED");
            var coreB = NewCoreCode(); InsertCore(f!, coreB, "CONFIRMED");
            var repo = new ImgLotRepository(f!);
            var okLot = repo.CreateFromCore(coreA, ImgLine, "E-ITEST").Lot!;
            var ngLot = repo.CreateFromCore(coreB, ImgLine, "E-ITEST").Lot!;

            var ok = repo.ConfirmByLotCode(okLot.LotCode, ImgLine, "E-ITEST", null, "E-ITEST");
            var ng = repo.RegisterDefect(ngLot.LotCode, ImgLine, "IMG-D01", "E-ITEST", null, "E-ITEST");

            Assert.Equal(ImgConfirmOutcome.Confirmed, ok.Outcome);
            Assert.Equal(DefectRegisterOutcome.Registered, ng.Outcome);
            Assert.Equal("CONFIRMED", repo.GetByLotCode(okLot.LotCode)!.ConfirmStatus);
            Assert.Equal("DEFECT",    repo.GetByLotCode(ngLot.LotCode)!.ConfirmStatus);
        }
        finally { Cleanup(f!); }
    }
}
