using AMES.Contracts.Enums;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Data.Services;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

/// <summary>
/// PNT(도장) 원천 LOT 통합 테스트(AMES_DEV). DB 미기동 시 skip.
/// 코어 스캔이 없다 — 라벨 발행 버튼(CreateRawLot)이 좌측 선택 완제품으로 RAW LOT 을 만들고 판정 OK 가 확정한다.
/// 완제품 PTEST-FG-A(라우팅 B, BOM 자식 = 코어 PTEST-CORE-A(InjFlag 1) 1 EA + 자재 PTEST-RESIN 50 G).
/// 재고를 건드리는 차감 테스트는 AMES_TEST_CONN 지정 시에만 돈다(BackflushConfirmTests 와 같은 직렬 컬렉션).
/// </summary>
[Collection(BackflushDbCollection.Name)]
public class PntLotRepositoryTests
{
    const string Fg      = "PTEST-FG-A";
    const string Core    = "PTEST-CORE-A";
    const string Resin   = "PTEST-RESIN";
    const string PntLine = "LINE-PNT-01";
    const string Prefix  = "P1";
    const string InjLine = "LINE-INJ-01";
    const string Version = "PTEST-PNT-V";
    const string WriteSkip = "set AMES_TEST_CONN to run — moves line stock (placeholder rows)";

    static AmesConnectionFactory? TryFactory() => AmesDevDb.TryFactory();
    static AmesConnectionFactory? WriteFactory()
        => Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null ? null : AmesDevDb.TryFactory();

    static string NewCoreCode() => "ZP" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();

    static void SeedItems(AmesConnectionFactory f) => AmesDevDb.Exec(f, """
        IF NOT EXISTS (SELECT 1 FROM dbo.MD_Item WHERE ItemNo = @F)
            INSERT INTO dbo.MD_Item (ItemNo, ItemName, ItemType, RoutingType, PGN, ALC, MountPos, ActiveFlag, CreatedBy)
            VALUES (@F, N'PTEST painted fg', 'ASSY', 'B', 'QTST', 'T101', 'FL', 1, 'PTEST');
        IF NOT EXISTS (SELECT 1 FROM dbo.MD_Item WHERE ItemNo = @C)
            INSERT INTO dbo.MD_Item (ItemNo, ItemName, ItemType, InjFlag, ActiveFlag, CreatedBy)
            VALUES (@C, N'CORE-PTEST pnt core', 'SUB', 1, 1, 'PTEST');
        IF NOT EXISTS (SELECT 1 FROM dbo.MD_Item WHERE ItemNo = @R)
            INSERT INTO dbo.MD_Item (ItemNo, ItemName, ItemType, DefaultUOM, ActiveFlag, CreatedBy)
            VALUES (@R, N'PTEST resin', 'MATERIAL', 'G', 1, 'PTEST');
        IF NOT EXISTS (SELECT 1 FROM dbo.MD_BomVersion WHERE VersionID = @V)
        BEGIN
            INSERT INTO dbo.MD_BomVersion (VersionID, RootItemNo, VersionNo, EffFrom, EffTo, Status, CreatedBy)
            VALUES (@V, @F, 'V1', DATEADD(day,-10,CAST(GETDATE() AS date)), NULL, 'APPROVED', 'PTEST');
            INSERT INTO dbo.MD_Bom (BOMID, ParentItemNo, CompItemNo, BOMLevel, QtyPer, UOM, VersionID, ActiveFlag, CreatedBy)
            VALUES ('PTEST-PNT-B1', @F, @C, 1, 1,  'EA', @V, 1, 'PTEST'),
                   ('PTEST-PNT-B2', @F, @R, 1, 50, 'G',  @V, 1, 'PTEST');
        END
        """, ("@F", Fg), ("@C", Core), ("@R", Resin), ("@V", Version));

    /// <summary>완제품 WO: INJ 단계(ItemNo = 코어, Closed) + PNT 단계(In Progress). 수주처 CUS-SAV 를 달면 라벨 V 토큰이 SAV.</summary>
    static int InsertPntWo(AmesConnectionFactory f, string? soCustomerId = null)
    {
        using var conn = f.OpenConnection();
        using var cmd = new SqlCommand("""
            DECLARE @W TABLE (WoID INT);
            DECLARE @So INT = NULL;
            IF @Cust IS NOT NULL
            BEGIN
                INSERT INTO dbo.PP_CustomerOrder (SoNumber, SoLineNo, CustomerID, ItemNo, OrderQty, RequestedDeliveryDate, Status, CreatedBy)
                VALUES (@No, 1, @Cust, @I, 100, CAST(GETDATE() AS date), 'Confirmed', 'PTEST');
                SET @So = SCOPE_IDENTITY();
            END
            INSERT INTO dbo.PP_WorkOrder (WoNumber, SoID, ItemNo, OrderQty, CompletedQty, Status, Priority, CreatedBy, CreatedTS)
            OUTPUT INSERTED.WoID INTO @W
            VALUES (@No, @So, @I, 100, 0, 'In Progress', 5, 'PTEST', SYSDATETIME());
            INSERT INTO dbo.PP_WorkOrderRouting (WoID, StepSeq, ProcessCode, LineID, ItemNo, Status, CompletedQty, CreatedBy)
            SELECT WoID, 1, 'INJ', @InjLine, @C, 'Closed', 100, 'PTEST' FROM @W
            UNION ALL
            SELECT WoID, 2, 'PNT', @Line, NULL, 'In Progress', 0, 'PTEST' FROM @W;
            SELECT WoID FROM @W;
            """, conn);
        cmd.Parameters.AddWithValue("@No", "WO-PTC-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant());
        cmd.Parameters.AddWithValue("@I", Fg);
        cmd.Parameters.AddWithValue("@C", Core);
        cmd.Parameters.AddWithValue("@Line", PntLine);
        cmd.Parameters.AddWithValue("@InjLine", InjLine);
        cmd.Parameters.AddWithValue("@Cust", (object?)soCustomerId ?? DBNull.Value);
        return (int)cmd.ExecuteScalar()!;
    }

    /// <summary>INJ 코어 LOT — PNT 조회가 다른 공정 LOT 을 돌려주지 않는지 시험할 때만 쓴다.</summary>
    static string InsertInjCore(AmesConnectionFactory f)
    {
        var code = NewCoreCode();
        AmesDevDb.Exec(f, """
            DECLARE @T TABLE (LotID INT);
            INSERT INTO dbo.tbl_Lot (LotCode, ItemNo, LineID, ProcessCode, BatchSize, RemainingQty, ProducedAt, Status, QualityFlag, CreatedBy, CreatedTS)
            OUTPUT INSERTED.LotID INTO @T
            VALUES (@C, @I, @Line, 'INJ', 1, 1, SYSDATETIME(), 'CONFIRMED', 'OK', 'PTEST', SYSDATETIME());
            INSERT INTO dbo.PR_InjLot (LotID, ConfirmStatus, PrintedCount, CreatedBy) SELECT LotID, 'CONFIRMED', 0, 'PTEST' FROM @T;
            """, ("@C", code), ("@I", Core), ("@Line", InjLine));
        return code;
    }

    static T Scalar<T>(AmesConnectionFactory f, string sql, params (string, object)[] ps)
        => (T)Convert.ChangeType(AmesDevDb.Scalar(f, sql, ps)!, typeof(T));

    static void Cleanup(AmesConnectionFactory f) => AmesDevDb.Exec(f, """
        DELETE t FROM dbo.WH_InventoryTransaction t JOIN dbo.tbl_Lot l ON l.LotID = t.SourceID
            WHERE t.SourceType IN ('BACKFLUSH','BACKFLUSH_NET','PROD_CONFIRM') AND l.ItemNo IN (@F, @C, @R);
        DELETE FROM dbo.WH_Inventory WHERE LotNo IN (@PhC, @PhR);
        DELETE i FROM dbo.WH_Inventory i JOIN dbo.tbl_Lot l ON l.LotCode = i.LotNo WHERE l.ItemNo IN (@F, @C);
        DELETE d FROM dbo.PR_DefectDetail     d JOIN dbo.tbl_Lot l ON l.LotID = d.LotID WHERE l.ItemNo IN (@F, @C);
        DELETE r FROM dbo.PR_ProductionResult r JOIN dbo.tbl_Lot l ON l.LotID = r.LotID WHERE l.ItemNo IN (@F, @C);
        DELETE e FROM dbo.PR_PntLot           e JOIN dbo.tbl_Lot l ON l.LotID = e.LotID WHERE l.ItemNo IN (@F, @C);
        DELETE e FROM dbo.PR_InjLot           e JOIN dbo.tbl_Lot l ON l.LotID = e.LotID WHERE l.ItemNo IN (@F, @C);
        DELETE FROM dbo.tbl_Lot WHERE ItemNo IN (@F, @C);
        DELETE r FROM dbo.PP_WorkOrderRouting r JOIN dbo.PP_WorkOrder w ON w.WoID = r.WoID WHERE w.ItemNo = @F;
        DELETE FROM dbo.PP_WorkOrder     WHERE ItemNo = @F;
        DELETE FROM dbo.PP_CustomerOrder WHERE ItemNo = @F;
        DELETE FROM dbo.MD_Bom        WHERE VersionID = @V;
        DELETE FROM dbo.MD_BomVersion WHERE VersionID = @V;
        DELETE FROM dbo.MD_Item WHERE ItemNo IN (@F, @C, @R) AND CreatedBy = 'PTEST';
        """, ("@F", Fg), ("@C", Core), ("@R", Resin), ("@V", Version),
             ("@PhC", BackflushRules.PlaceholderLotNo(Prefix, Core)), ("@PhR", BackflushRules.PlaceholderLotNo(Prefix, Resin)));

    [SkippableFact]
    public void CreateRawLot_makes_raw_pnt_lot_with_customer_and_label_fields()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f!); SeedItems(f!);
        try
        {
            InsertPntWo(f!, soCustomerId: "CUS-SAV");
            var lot = new PntLotRepository(f!).CreateRawLot(PntLine, Fg, "E-PTEST");

            Assert.Equal("RAW", lot.ConfirmStatus);
            Assert.Equal(Fg, lot.ItemNo);
            Assert.Equal(("QTST", "T101", "FL"), (lot.Pgn, lot.Alc, lot.MountPos));
            Assert.Equal("SAV", lot.CustomerCode);
            Assert.Null(lot.CoreLotCode);
            Assert.Equal(Prefix, lot.LotCode.Substring(3, 2));
            Assert.Equal("PNT", Scalar<string>(f!, "SELECT ProcessCode FROM dbo.tbl_Lot WHERE LotCode = @C", ("@C", lot.LotCode)));
            Assert.Null(AmesDevDb.Scalar(f!, "SELECT ParentLotID FROM dbo.tbl_Lot WHERE LotCode = @C", ("@C", lot.LotCode)) as int?);
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void GetByLotCode_returns_only_pnt_lots()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f!); SeedItems(f!);
        try
        {
            var repo = new PntLotRepository(f!);
            var pnt = repo.CreateRawLot(PntLine, Fg, "E-PTEST");
            var inj = InsertInjCore(f!);

            Assert.NotNull(repo.GetByLotCode(pnt.LotCode));
            Assert.Null(repo.GetByLotCode(inj));
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void Confirm_records_result_and_bumps_step()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f!); SeedItems(f!);
        try
        {
            var woId = InsertPntWo(f!);
            var repo = new PntLotRepository(f!);
            var lot = repo.CreateRawLot(PntLine, Fg, "E-PTEST");

            var ok = repo.ConfirmByLotCode(lot.LotCode, PntLine, "E-PTEST", null, "E-PTEST");

            Assert.Equal((ImgConfirmOutcome.Confirmed, Fg, woId), (ok.Outcome, ok.ItemNo, ok.WoId));
            Assert.Equal("CONFIRMED", repo.GetByLotCode(lot.LotCode)!.ConfirmStatus);
            Assert.Equal(1, Scalar<int>(f!, "SELECT COUNT(*) FROM dbo.PR_ProductionResult r JOIN dbo.tbl_Lot l ON l.LotID = r.LotID WHERE l.LotCode = @C AND r.ProcessCode = 'PNT' AND r.GoodQty = 1", ("@C", lot.LotCode)));
            Assert.Equal(1m, Scalar<decimal>(f!, "SELECT CompletedQty FROM dbo.PP_WorkOrderRouting WHERE WoID = @W AND StepSeq = 2", ("@W", woId)));
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void Confirm_without_wo_still_confirms_with_null_wo()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f!); SeedItems(f!);
        try
        {
            var repo = new PntLotRepository(f!);
            var lot = repo.CreateRawLot(PntLine, Fg, "E-PTEST");
            Assert.Null(lot.CustomerCode);   // 열린 WO 가 없어 수주처도 없다 — 라벨 V 토큰이 빈다

            var ok = repo.ConfirmByLotCode(lot.LotCode, PntLine, "E-PTEST", null, "E-PTEST");

            Assert.Equal((ImgConfirmOutcome.Confirmed, 0), (ok.Outcome, ok.WoId));
            Assert.Equal(1, Scalar<int>(f!, "SELECT COUNT(*) FROM dbo.PR_ProductionResult r JOIN dbo.tbl_Lot l ON l.LotID = r.LotID WHERE l.LotCode = @C AND r.WoID IS NULL", ("@C", lot.LotCode)));
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void Confirm_rejects_lot_of_another_line()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f!); SeedItems(f!);
        try
        {
            var repo = new PntLotRepository(f!);
            var lot = repo.CreateRawLot(PntLine, Fg, "E-PTEST");

            Assert.Equal(ImgConfirmOutcome.WrongLine, repo.ConfirmByLotCode(lot.LotCode, "LINE-PNT-02", "E-PTEST", null, "E-PTEST").Outcome);
            Assert.Equal("RAW", repo.GetByLotCode(lot.LotCode)!.ConfirmStatus);
        }
        finally { Cleanup(f!); }
    }

    /// <summary>PNT 확정은 MATERIAL 뿐 아니라 사출품 자식(InjFlag)도 뺀다 — 라인 재고가 없으니 자리표시가 음수로 내려간다.</summary>
    [SkippableFact]
    public void Confirm_backflushes_injection_child_into_placeholder()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        Cleanup(f!); SeedItems(f!);
        try
        {
            var repo = new PntLotRepository(f!);
            var lot = repo.CreateRawLot(PntLine, Fg, "E-PTEST");

            var ok = repo.ConfirmByLotCode(lot.LotCode, PntLine, "E-PTEST", null, "E-PTEST", null, out var backflush);

            Assert.Equal(ImgConfirmOutcome.Confirmed, ok.Outcome);
            Assert.Null(backflush.Error);
            Assert.Equal(new[] { (Core, 1m), (Resin, 50m) }, backflush.Shortages.Select(s => (s.PartNo, s.Qty)).ToArray());
            Assert.Equal(-1m,  Scalar<decimal>(f!, "SELECT Qty FROM dbo.WH_Inventory WHERE LotNo = @L", ("@L", BackflushRules.PlaceholderLotNo(Prefix, Core))));
            Assert.Equal(-50m, Scalar<decimal>(f!, "SELECT Qty FROM dbo.WH_Inventory WHERE LotNo = @L", ("@L", BackflushRules.PlaceholderLotNo(Prefix, Resin))));

            Assert.Equal(ImgConfirmOutcome.AlreadyConfirmed, repo.ConfirmByLotCode(lot.LotCode, PntLine, "E-PTEST", null, "E-PTEST", null, out _).Outcome);
            Assert.Equal(-1m,  Scalar<decimal>(f!, "SELECT Qty FROM dbo.WH_Inventory WHERE LotNo = @L", ("@L", BackflushRules.PlaceholderLotNo(Prefix, Core))));
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void RegisterDefect_moves_raw_lot_to_defect_without_result()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f!); SeedItems(f!);
        try
        {
            var repo = new PntLotRepository(f!);
            var lot = repo.CreateRawLot(PntLine, Fg, "E-PTEST");

            var ng = repo.RegisterDefect(lot.LotCode, PntLine, "PNT-D01", "E-PTEST", null, "E-PTEST");

            Assert.Equal(DefectRegisterOutcome.Registered, ng.Outcome);
            Assert.Equal("DEFECT", repo.GetByLotCode(lot.LotCode)!.ConfirmStatus);
            Assert.Equal(0, Scalar<int>(f!, "SELECT COUNT(*) FROM dbo.PR_ProductionResult r JOIN dbo.tbl_Lot l ON l.LotID = r.LotID WHERE l.LotCode = @C", ("@C", lot.LotCode)));
            Assert.Equal("PNT", Scalar<string>(f!, "SELECT ProcessCode FROM dbo.PR_DefectDetail WHERE DefectID = @D", ("@D", ng.DefectId)));
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void Rework_of_defect_pnt_lot_confirms_via_rwk_branch()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f!); SeedItems(f!);
        try
        {
            var repo = new PntLotRepository(f!);
            var lot = repo.CreateRawLot(PntLine, Fg, "E-PTEST");
            var ng  = repo.RegisterDefect(lot.LotCode, PntLine, "PNT-D01", "E-PTEST", null, "E-PTEST");

            var rwk = new ReworkRepository(f!).Rework(ng.DefectId, "ITEST-CAUSE", null, "LINE-RWK-01", "E-PTEST", null, "E-PTEST");

            Assert.Equal(ReworkOutcome.Done, rwk);
            Assert.Equal("CONFIRMED", repo.GetByLotCode(lot.LotCode)!.ConfirmStatus);
        }
        finally { Cleanup(f!); }
    }

    /// <summary>확정된 적 없는 PNT LOT 의 수리 양품은 PNT 규칙(사출품 자식 포함)으로 차감한다 — ReworkRepository 가 LOT 의 ProcessCode 로 판정.</summary>
    [SkippableFact]
    public void Rework_of_never_confirmed_pnt_lot_backflushes_injection_child()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        Cleanup(f!); SeedItems(f!);
        try
        {
            var repo = new PntLotRepository(f!);
            var lot = repo.CreateRawLot(PntLine, Fg, "E-PTEST");
            var ng  = repo.RegisterDefect(lot.LotCode, PntLine, "PNT-D01", "E-PTEST", null, "E-PTEST");

            var rwk = new ReworkRepository(f!).Rework(ng.DefectId, "ITEST-CAUSE", null, "LINE-RWK-01", "E-PTEST", null, "E-PTEST", out var backflush);

            Assert.Equal(ReworkOutcome.Done, rwk);
            Assert.Null(backflush.Error);
            Assert.Contains(backflush.Shortages, s => s.PartNo == Core && s.Qty == 1m);
            Assert.Equal(-1m, Scalar<decimal>(f!, "SELECT Qty FROM dbo.WH_Inventory WHERE LotNo = @L", ("@L", BackflushRules.PlaceholderLotNo(Prefix, Core))));
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void DailySummary_holds_input_equals_final_plus_ng_plus_pending()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f!); SeedItems(f!);
        try
        {
            var repo = new PntLotRepository(f!);
            var a = repo.CreateRawLot(PntLine, Fg, "E-PTEST");
            var b = repo.CreateRawLot(PntLine, Fg, "E-PTEST");
            repo.CreateRawLot(PntLine, Fg, "E-PTEST");
            repo.ConfirmByLotCode(a.LotCode, PntLine, "E-PTEST", null, "E-PTEST");
            repo.RegisterDefect(b.LotCode, PntLine, "PNT-D01", "E-PTEST", null, "E-PTEST");

            var row = repo.GetDailyItemSummary(PntLine, "ST-PNT-01", DbClock.Today).Single(x => x.ItemNo == Fg);

            Assert.Equal((3, 1, 1, 1, false), (row.InputQty, row.FinalQty, row.NgQty, row.PendingQty, row.InBop));
        }
        finally { Cleanup(f!); }
    }
}
