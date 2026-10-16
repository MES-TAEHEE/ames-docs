using AMES.Contracts.Dto;
using AMES.Contracts.Enums;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Data.Services;
using Xunit;

namespace AMES.Data.Tests;

/// <summary>INJ 스캔 확정·재작업 수리가 같은 트랜잭션에서 라인 재고를 빼는지 — 실제 LOT 을 만들고 끝에 전부 지운다.</summary>
[Collection(BackflushDbCollection.Name)]
public class BackflushConfirmTests
{
    const string WriteSkip = "set AMES_TEST_CONN to run — creates and removes a LOT, inventory rows and transactions";
    // D0101-QI000: 복제본 ST-INJ-01 BOP 품번 중 열린 WO 가 없는 코어(레진 21343A0-PIDHA 1008 G) — 확정이 실제 WO 단계를 올리지 않게
    const string Line = "LINE-INJ-01", Prefix = "I1", Core = "D0101-QI000", Resin = "21343A0-PIDHA", TestLot = "BFC-T-L1";

    static AmesConnectionFactory? WriteFactory()
        => Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null ? null : AmesDevDb.TryFactory();

    static MoldItemDto CoreMap() => new() { MoldCode = "BF-TEST", ColorCode = "NNB", CavityNo = 1, CavityPos = "LH", ItemNo = Core, MoldId = null };

    /// <summary>실제 데이터를 건드리지 않을 때만 돈다: BOM 있음 · 그 라인에 열린 WO 없음 · I1 에 그 레진의 실물 LOT·자리표시 없음.</summary>
    static void SkipUnlessIsolated(AmesConnectionFactory f)
    {
        Skip.If(Convert.ToInt32(AmesDevDb.Scalar(f, "SELECT COUNT(*) FROM dbo.MD_Bom WHERE ParentItemNo=@P AND CompItemNo=@C", ("@P", Core), ("@C", Resin))) == 0,
                "replica BOM for the test core is missing");
        Skip.If(Convert.ToInt32(AmesDevDb.Scalar(f, """
            SELECT COUNT(*) FROM dbo.PP_WorkOrderRouting r JOIN dbo.PP_WorkOrder w ON w.WoID = r.WoID
            WHERE r.LineID = @L AND COALESCE(r.ItemNo, w.ItemNo) = @P AND r.Status IN ('Released','In Progress')
            """, ("@L", Line), ("@P", Core))) > 0, "an open WO exists for the test core — the confirm would bump real WO progress");
        Skip.If(Convert.ToInt32(AmesDevDb.Scalar(f, "SELECT COUNT(*) FROM dbo.WH_Inventory WHERE LocationNo=@Loc AND PartNo=@R", ("@Loc", Prefix), ("@R", Resin))) > 0,
                "real line stock or a placeholder for the resin exists at I1 — the test would deduct from or delete it");
    }

    static void Cleanup(AmesConnectionFactory f, int lotId)
    {
        var ph = BackflushRules.PlaceholderLotNo(Prefix, Resin);
        AmesDevDb.Exec(f, """
            DELETE FROM dbo.WH_InventoryTransaction WHERE SourceID = @L AND SourceType IN ('BACKFLUSH','BACKFLUSH_NET');
            DELETE FROM dbo.WH_Inventory WHERE LotNo IN (@T, @Ph);
            DELETE FROM dbo.PR_DefectDetail WHERE LotID = @L;
            DELETE FROM dbo.PR_ProductionResult WHERE LotID = @L;
            DELETE FROM dbo.PR_InjLot WHERE LotID = @L;
            DELETE FROM dbo.tbl_Lot WHERE LotID = @L;
            """, ("@L", lotId), ("@T", TestLot), ("@Ph", ph));
    }

    /// <summary>라인 재고를 소요보다 넉넉히 둔다 — 부족·음수 자리표시 경로는 BackflushRepositoryTests 가 롤백 트랜잭션 안에서 덮는다(마이그레이션 전 복제본에서도 돌게).</summary>
    static void SeedLineStock(AmesConnectionFactory f) => AmesDevDb.Exec(f, """
        INSERT dbo.WH_Inventory (LotNo, UnitType, PartNo, LocationNo, Qty, ReceivedAt, CreatedAt, UpdatedAt)
        VALUES (@T, 'BOX', @R, @Loc, 2000, '2000-01-01', '2000-01-01', '2000-01-01');
        """, ("@T", TestLot), ("@R", Resin), ("@Loc", Prefix));

    [SkippableFact]
    public void Inj_confirm_backflushes_line_stock_once()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        SkipUnlessIsolated(f!);
        var repo = new InjLotRepository(f!);
        var (lotId, lotCode) = repo.CreateRawLot(Line, "INJ-650-01", CoreMap(), 990015);
        try
        {
            SeedLineStock(f!);

            var (outcome, _, _, _) = repo.ConfirmByLotCode(lotCode, Line, "E-BFT", null, "E-BFT", out var bf);

            Assert.Equal(InjConfirmOutcome.Confirmed, outcome);
            Assert.Null(bf.Error); Assert.False(bf.Skipped); Assert.Empty(bf.Shortages);
            Assert.Equal(992m, (decimal)AmesDevDb.Scalar(f!, "SELECT Qty FROM dbo.WH_Inventory WHERE LotNo=@T", ("@T", TestLot))!);   // 2000 − 1008
            Assert.Equal(1, (int)AmesDevDb.Scalar(f!, "SELECT COUNT(*) FROM dbo.WH_InventoryTransaction WHERE SourceType='BACKFLUSH' AND SourceID=@L", ("@L", lotId))!);

            // 재확정은 AlreadyConfirmed — 차감도 다시 일어나지 않는다
            var (again, _, _, _) = repo.ConfirmByLotCode(lotCode, Line, "E-BFT", null, "E-BFT", out var bf2);
            Assert.Equal(InjConfirmOutcome.AlreadyConfirmed, again);
            Assert.True(bf2.Skipped);
        }
        finally { Cleanup(f!, lotId); }
    }

    [SkippableFact]
    public void Rework_backflushes_lot_line_once()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        SkipUnlessIsolated(f!);
        var defectCode = AmesDevDb.Scalar(f!, "SELECT TOP 1 DefectCode FROM dbo.MD_DefectCode ORDER BY DefectCode") as string;
        var cause      = AmesDevDb.Scalar(f!, "SELECT TOP 1 CauseCode FROM dbo.MD_DefectCause ORDER BY CauseCode") as string;
        Skip.If(defectCode is null || cause is null, "replica has no defect code / cause");
        var inj = new InjLotRepository(f!);
        var rwk = new ReworkRepository(f!);
        var (lotId, lotCode) = inj.CreateRawLot(Line, "INJ-650-01", CoreMap(), 990016);
        try
        {
            SeedLineStock(f!);
            var reg = inj.RegisterDefect(lotCode, Line, defectCode!, "E-BFT", null, "E-BFT");   // RAW → DEFECT, 확정된 적 없음
            Assert.Equal(DefectRegisterOutcome.Registered, reg.Outcome);

            Assert.Equal(ReworkOutcome.Done, rwk.Rework(reg.DefectId, cause!, null, "LINE-RWK-01", "E-BFT", null, "E-BFT", out var bf));
            Assert.Null(bf.Error); Assert.False(bf.Skipped); Assert.Empty(bf.Shortages);
            // 재작업 라인(LINE-RWK-01, LotPrefix 없음)이 아니라 LOT 의 원래 라인 I1 재고에서 빠진다
            Assert.Equal(992m, (decimal)AmesDevDb.Scalar(f!, "SELECT Qty FROM dbo.WH_Inventory WHERE LotNo=@T", ("@T", TestLot))!);
            Assert.Equal(1, (int)AmesDevDb.Scalar(f!, "SELECT COUNT(*) FROM dbo.WH_InventoryTransaction WHERE SourceType='BACKFLUSH' AND SourceID=@L", ("@L", lotId))!);
            Assert.Equal(Prefix, (string)AmesDevDb.Scalar(f!, "SELECT TOP 1 LocationNo FROM dbo.WH_InventoryTransaction WHERE SourceType='BACKFLUSH' AND SourceID=@L", ("@L", lotId))!);
        }
        finally { Cleanup(f!, lotId); }
    }

    [SkippableFact]
    public void Rework_does_not_charge_a_lot_that_was_confirmed_before_rollout()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        SkipUnlessIsolated(f!);
        var defectCode = AmesDevDb.Scalar(f!, "SELECT TOP 1 DefectCode FROM dbo.MD_DefectCode ORDER BY DefectCode") as string;
        var cause      = AmesDevDb.Scalar(f!, "SELECT TOP 1 CauseCode FROM dbo.MD_DefectCause ORDER BY CauseCode") as string;
        Skip.If(defectCode is null || cause is null, "replica has no defect code / cause");
        var inj = new InjLotRepository(f!);
        var rwk = new ReworkRepository(f!);
        var (lotId, lotCode) = inj.CreateRawLot(Line, "INJ-650-01", CoreMap(), 990017);
        try
        {
            SeedLineStock(f!);
            Assert.Equal(InjConfirmOutcome.Confirmed, inj.ConfirmByLotCode(lotCode, Line, "E-BFT", null, "E-BFT").Outcome);
            // 배포 전에 확정된 LOT 처럼 — 차감 기록이 없다
            AmesDevDb.Exec(f!, "DELETE FROM dbo.WH_InventoryTransaction WHERE SourceID = @L AND SourceType = 'BACKFLUSH'", ("@L", lotId));
            var reg = inj.RegisterDefect(lotCode, Line, defectCode!, "E-BFT", null, "E-BFT");   // CONFIRMED → DEFECT (역분개)
            Assert.Equal(DefectRegisterOutcome.Registered, reg.Outcome);

            Assert.Equal(ReworkOutcome.Done, rwk.Rework(reg.DefectId, cause!, null, "LINE-RWK-01", "E-BFT", null, "E-BFT", out var bf));

            Assert.True(bf.Skipped, "a lot that had been CONFIRMED before must not be charged again at rework");
            Assert.Equal(992m, (decimal)AmesDevDb.Scalar(f!, "SELECT Qty FROM dbo.WH_Inventory WHERE LotNo=@T", ("@T", TestLot))!);
            Assert.Equal(0, (int)AmesDevDb.Scalar(f!, "SELECT COUNT(*) FROM dbo.WH_InventoryTransaction WHERE SourceType='BACKFLUSH' AND SourceID=@L", ("@L", lotId))!);
        }
        finally { Cleanup(f!, lotId); }
    }
}
