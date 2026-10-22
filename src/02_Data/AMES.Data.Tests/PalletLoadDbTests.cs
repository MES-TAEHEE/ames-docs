using AMES.Contracts.Dto;
using AMES.Contracts.Enums;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Data.Services;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

/// <summary>
/// POP 확정 → 팔레트 적재(WH_Inventory) 통합 테스트 — 실제 LOT·팔레트·재고 행을 만들고 끝에 전부 지운다.
/// 적입수량 2 인 코어를 팔레트에 2개 실으면 셋째는 PalletFull, 다른 품번은 PalletMixed, 없는 팔레트는 PalletNotFound.
/// </summary>
[Collection(BackflushDbCollection.Name)]
public class PalletLoadDbTests
{
    const string WriteSkip = "set AMES_TEST_CONN to run — creates and removes LOTs, pallets, inventory rows and transactions";
    const string InjLine = "LINE-INJ-01", ImgLine = "LINE-IMG-01";
    const string CoreA = "ZPLT-CORE-A", CoreB = "ZPLT-CORE-B", FgA = "ZPLT-FG-A";
    const string Pallet1 = "EOS-PLT-9999-9901", Pallet2 = "EOS-PLT-9999-9902", PalletOff = "EOS-PLT-9999-9903";

    static AmesConnectionFactory? WriteFactory()
        => Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null ? null : AmesDevDb.TryFactory();

    static MoldItemDto Map(string item) => new() { MoldCode = "PLT-TEST", ColorCode = "NNB", CavityNo = 1, CavityPos = "LH", ItemNo = item, MoldId = null };

    static void Seed(AmesConnectionFactory f)
    {
        Cleanup(f);
        AmesDevDb.Exec(f, """
            INSERT dbo.MD_Item (ItemNo, ItemName, ItemType, PalletQty, ActiveFlag, CreatedBy) VALUES (@A, N'ZPLT core A', 'SUB', 2, 1, 'ZPLT');
            INSERT dbo.MD_Item (ItemNo, ItemName, ItemType, PalletQty, ActiveFlag, CreatedBy) VALUES (@B, N'ZPLT core B', 'SUB', 2, 1, 'ZPLT');
            INSERT dbo.MD_Item (ItemNo, ItemName, ItemType, RoutingType, PalletQty, ActiveFlag, CreatedBy) VALUES (@F, N'ZPLT fg A', 'ASSY', 'A', 1, 1, 'ZPLT');
            INSERT dbo.MD_Pallet (PalletNo, PalletUse, ActiveFlag, ShippedFlag, CreatedBy, CreatedTS) VALUES (@P1, 'INTERNAL', 1, 0, 'ZPLT', SYSDATETIME());
            INSERT dbo.MD_Pallet (PalletNo, PalletUse, ActiveFlag, ShippedFlag, CreatedBy, CreatedTS) VALUES (@P2, 'INTERNAL', 1, 0, 'ZPLT', SYSDATETIME());
            INSERT dbo.MD_Pallet (PalletNo, PalletUse, ActiveFlag, ShippedFlag, CreatedBy, CreatedTS) VALUES (@P3, 'INTERNAL', 0, 0, 'ZPLT', SYSDATETIME());
            """, ("@A", CoreA), ("@B", CoreB), ("@F", FgA), ("@P1", Pallet1), ("@P2", Pallet2), ("@P3", PalletOff));
    }

    static void Cleanup(AmesConnectionFactory f) => AmesDevDb.Exec(f, """
        DELETE t FROM dbo.WH_InventoryTransaction t JOIN dbo.tbl_Lot l ON l.LotCode = t.LotNo COLLATE DATABASE_DEFAULT WHERE l.ItemNo IN (@A, @B, @F);
        DELETE FROM dbo.WH_InventoryTransaction WHERE LotNo IN (@P1, @P2, @P3);
        DELETE w FROM dbo.WH_Inventory w JOIN dbo.tbl_Lot l ON l.LotCode = w.LotNo COLLATE DATABASE_DEFAULT WHERE l.ItemNo IN (@A, @B, @F);
        DELETE FROM dbo.WH_Inventory WHERE LotNo IN (@P1, @P2, @P3) OR PalletNo IN (@P1, @P2, @P3);
        DELETE d FROM dbo.PR_DefectDetail     d JOIN dbo.tbl_Lot l ON l.LotID = d.LotID WHERE l.ItemNo IN (@A, @B, @F);
        DELETE r FROM dbo.PR_ProductionResult r JOIN dbo.tbl_Lot l ON l.LotID = r.LotID WHERE l.ItemNo IN (@A, @B, @F);
        DELETE e FROM dbo.PR_ImgLot           e JOIN dbo.tbl_Lot l ON l.LotID = e.LotID WHERE l.ItemNo IN (@A, @B, @F);
        DELETE e FROM dbo.PR_InjLot           e JOIN dbo.tbl_Lot l ON l.LotID = e.LotID WHERE l.ItemNo IN (@A, @B, @F);
        DELETE FROM dbo.tbl_Lot WHERE ItemNo IN (@A, @B, @F);
        DELETE FROM dbo.MD_Pallet WHERE PalletNo IN (@P1, @P2, @P3) AND CreatedBy = 'ZPLT';
        DELETE FROM dbo.MD_MoldItem WHERE MoldID = 'ZPLT-MOLD';
        DELETE FROM dbo.MD_Mold WHERE MoldID = 'ZPLT-MOLD';
        DELETE FROM dbo.MD_Item WHERE ItemNo IN (@A, @B, @F) AND CreatedBy = 'ZPLT';
        """, ("@A", CoreA), ("@B", CoreB), ("@F", FgA), ("@P1", Pallet1), ("@P2", Pallet2), ("@P3", PalletOff));

    static int Count(AmesConnectionFactory f, string sql, params (string, object)[] ps) => Convert.ToInt32(AmesDevDb.Scalar(f, sql, ps));

    [SkippableFact]
    public void Inj_confirm_puts_the_lot_on_the_scanned_pallet_and_stops_at_pallet_qty()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        Seed(f!);
        var repo = new InjLotRepository(f!);
        try
        {
            var (lot1, code1) = repo.CreateRawLot(InjLine, "INJ-01-01-1800", Map(CoreA), 990101);
            var (lot2, code2) = repo.CreateRawLot(InjLine, "INJ-01-01-1800", Map(CoreA), 990102);
            var (lot3, code3) = repo.CreateRawLot(InjLine, "INJ-01-01-1800", Map(CoreA), 990103);
            var (_,    codeB) = repo.CreateRawLot(InjLine, "INJ-01-01-1800", Map(CoreB), 990104);

            // 없는 팔레트·사용 안 함 팔레트는 확정 자체가 거부되고 LOT 은 RAW 로 남는다
            Assert.Equal(InjConfirmOutcome.PalletNotFound,    repo.ConfirmByLotCode(code1, InjLine, "E-PLT", null, "E-PLT", "EOS-PLT-9999-0000", out _).Outcome);
            Assert.Equal(InjConfirmOutcome.PalletUnavailable, repo.ConfirmByLotCode(code1, InjLine, "E-PLT", null, "E-PLT", PalletOff, out _).Outcome);
            Assert.Equal("RAW", (string)AmesDevDb.Scalar(f!, "SELECT Status FROM dbo.tbl_Lot WHERE LotID=@L", ("@L", lot1))!);

            Assert.Equal(InjConfirmOutcome.Confirmed, repo.ConfirmByLotCode(code1, InjLine, "E-PLT", null, "E-PLT", Pallet1, out _).Outcome);

            // 제품 행: 1 EA, 부모·팔레트 = 팔레트 번호, 위치 없음(PDA FG 입고가 배정) + 팔레트 머리 행 + IN 거래
            using (var conn = f!.OpenConnection())
            using (var cmd = new SqlCommand("SELECT UnitType, ParentLotNo, PartNo, PalletNo, LocationNo, Qty FROM dbo.WH_Inventory WHERE LotNo=@L", conn))
            {
                cmd.Parameters.AddWithValue("@L", code1);
                using var r = cmd.ExecuteReader(); Assert.True(r.Read());
                Assert.Equal("PART", r["UnitType"]); Assert.Equal(Pallet1, r["ParentLotNo"]); Assert.Equal(CoreA, r["PartNo"]);
                Assert.Equal(Pallet1, r["PalletNo"]); Assert.Equal(DBNull.Value, r["LocationNo"]); Assert.Equal(1m, r["Qty"]);
            }
            Assert.Equal(1, Count(f!, "SELECT COUNT(*) FROM dbo.WH_Inventory WHERE LotNo=@P AND UnitType='PALLET' AND Qty=0", ("@P", Pallet1)));
            Assert.Equal(1, Count(f!, "SELECT COUNT(*) FROM dbo.WH_InventoryTransaction WHERE LotNo=@L AND TransactionType='IN' AND SourceType='PROD_CONFIRM' AND SourceID=@Id AND QtyChange=1",
                                  ("@L", code1), ("@Id", lot1)));

            // 둘째까지 싣고 셋째는 가득 참 — 셋째 LOT 은 RAW, 재고 행·실적 없음
            Assert.Equal(InjConfirmOutcome.Confirmed,  repo.ConfirmByLotCode(code2, InjLine, "E-PLT", null, "E-PLT", Pallet1, out _).Outcome);
            Assert.Equal(InjConfirmOutcome.PalletFull, repo.ConfirmByLotCode(code3, InjLine, "E-PLT", null, "E-PLT", Pallet1, out _).Outcome);
            Assert.Equal("RAW", (string)AmesDevDb.Scalar(f!, "SELECT Status FROM dbo.tbl_Lot WHERE LotID=@L", ("@L", lot3))!);
            Assert.Equal(0, Count(f!, "SELECT COUNT(*) FROM dbo.WH_Inventory WHERE LotNo=@L", ("@L", code3)));
            Assert.Equal(0, Count(f!, "SELECT COUNT(*) FROM dbo.PR_ProductionResult WHERE LotID=@L", ("@L", lot3)));

            // 형제가 아닌 다른 품번(금형 마스터에 없음)은 혼적 거부, 새 팔레트에는 실린다. 머리 행은 팔레트당 하나뿐
            Assert.Equal(InjConfirmOutcome.PalletMixed, repo.ConfirmByLotCode(codeB, InjLine, "E-PLT", null, "E-PLT", Pallet1, out _).Outcome);
            Assert.Equal(InjConfirmOutcome.Confirmed,   repo.ConfirmByLotCode(code3, InjLine, "E-PLT", null, "E-PLT", Pallet2, out _).Outcome);
            Assert.Equal(1, Count(f!, "SELECT COUNT(*) FROM dbo.WH_Inventory WHERE LotNo=@P", ("@P", Pallet1)));

            // 화면 칩용 조회 — 1번 팔레트 2 / 2 가득 참, 2번 팔레트 1 / 2
            var stock = new PalletStockRepository(f!);
            var s1 = stock.Inspect(Pallet1, CoreA);
            Assert.Equal(2, s1.State.CountOf(CoreA)); Assert.Equal(2, s1.PalletQty);
            Assert.Equal(PalletLoadOutcome.Full, s1.Outcome);
            Assert.Equal(PalletLoadOutcome.Ok, stock.Inspect(Pallet2, CoreA).Outcome);
            Assert.Equal(PalletLoadOutcome.NotFound, stock.Inspect("EOS-PLT-9999-0000", CoreA).Outcome);
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void Pallet_is_optional_at_repository_level_so_other_callers_confirm_without_inventory()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        Seed(f!);
        var repo = new InjLotRepository(f!);
        try
        {
            var (_, code) = repo.CreateRawLot(InjLine, "INJ-01-01-1800", Map(CoreA), 990105);
            Assert.Equal(InjConfirmOutcome.Confirmed, repo.ConfirmByLotCode(code, InjLine, "E-PLT", null, "E-PLT").Outcome);
            Assert.Equal(0, Count(f!, "SELECT COUNT(*) FROM dbo.WH_Inventory WHERE LotNo=@L", ("@L", code)));
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void Img_core_scan_takes_the_core_off_its_pallet_and_ok_puts_the_finished_lot_on_the_scanned_pallet()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        Seed(f!);
        var inj = new InjLotRepository(f!);
        var img = new ImgLotRepository(f!);
        try
        {
            var (_, coreCode) = inj.CreateRawLot(InjLine, "INJ-01-01-1800", Map(CoreA), 990106);
            Assert.Equal(InjConfirmOutcome.Confirmed, inj.ConfirmByLotCode(coreCode, InjLine, "E-PLT", null, "E-PLT", Pallet1, out _).Outcome);
            Assert.Equal(1, new PalletStockRepository(f!).Inspect(Pallet1, CoreA).State.CountOf(CoreA));

            // 코어 스캔 = 코어가 팔레트에서 나와 완제품이 됐다 → 코어 재고 행은 지우고(0 으로 남기지 않음, 2026-10-22 사용자 결정) OUT 거래만 남긴다, 팔레트는 다시 빈다
            var made = img.CreateFromCore(coreCode, ImgLine, "E-PLT", FgA);
            Assert.Equal(ImgCoreOutcome.Created, made.Outcome);
            Assert.Equal(0, Count(f!, "SELECT COUNT(*) FROM dbo.WH_Inventory WHERE LotNo=@L", ("@L", coreCode)));
            Assert.Equal(1, Count(f!, "SELECT COUNT(*) FROM dbo.WH_InventoryTransaction WHERE LotNo=@L AND TransactionType='OUT' AND SourceType='IMG_CORE' AND SourceID=@Id AND QtyChange=-1 AND QtyBefore=1 AND QtyAfter=0",
                                  ("@L", coreCode), ("@Id", made.Lot!.LotId)));
            Assert.Equal(0, new PalletStockRepository(f!).Inspect(Pallet1, CoreA).State.CountOf(CoreA));

            // 완제품 OK — 적입수량 1 인 2번 팔레트: 하나 실리고 둘째는 가득 참
            Assert.Equal(ImgConfirmOutcome.Confirmed, img.ConfirmByLotCode(made.Lot.LotCode, ImgLine, "E-PLT", null, "E-PLT", Pallet2, out _).Outcome);
            Assert.Equal(1, Count(f!, "SELECT COUNT(*) FROM dbo.WH_Inventory WHERE LotNo=@L AND ParentLotNo=@P AND PalletNo=@P AND PartNo=@F AND Qty=1",
                                  ("@L", made.Lot.LotCode), ("@P", Pallet2), ("@F", FgA)));
            var second = img.CreateRawLot(ImgLine, FgA, "E-PLT");
            Assert.Equal(ImgConfirmOutcome.PalletFull, img.ConfirmByLotCode(second.LotCode, ImgLine, "E-PLT", null, "E-PLT", Pallet2, out _).Outcome);
            Assert.Equal("RAW", (string)AmesDevDb.Scalar(f!, "SELECT ConfirmStatus FROM dbo.PR_ImgLot WHERE LotID=@L", ("@L", second.LotId))!);
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void Siblings_from_the_same_mold_and_color_share_a_pallet_with_per_item_capacity()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        Seed(f!);
        // 같은 금형·같은 색상의 LH/RH — APS 형제 기준(금형 × 색상)과 같다
        AmesDevDb.Exec(f!, """
            INSERT dbo.MD_Mold (MoldID, MoldName, Status, CreatedBy) VALUES ('ZPLT-MOLD', N'ZPLT test mold', 'AVAILABLE', 'ZPLT');
            INSERT dbo.MD_MoldItem (MoldID, ItemNo, Color, CavitySeq, CavityPos, CavityCount, ActiveFlag, CreatedBy)
            VALUES ('ZPLT-MOLD', @A, 'CBK', 1, 'LH', 2, 1, 'ZPLT'), ('ZPLT-MOLD', @B, 'CBK', 2, 'RH', 2, 1, 'ZPLT');
            """, ("@A", CoreA), ("@B", CoreB));
        var repo = new InjLotRepository(f!);
        try
        {
            var (_, a1) = repo.CreateRawLot(InjLine, "INJ-01-01-1800", Map(CoreA), 990201);
            var (_, a2) = repo.CreateRawLot(InjLine, "INJ-01-01-1800", Map(CoreA), 990202);
            var (_, a3) = repo.CreateRawLot(InjLine, "INJ-01-01-1800", Map(CoreA), 990203);
            var (_, b1) = repo.CreateRawLot(InjLine, "INJ-01-01-1800", Map(CoreB), 990204);
            var (_, b2) = repo.CreateRawLot(InjLine, "INJ-01-01-1800", Map(CoreB), 990205);
            var (_, b3) = repo.CreateRawLot(InjLine, "INJ-01-01-1800", Map(CoreB), 990206);

            // LH 2개로 LH 는 가득 — RH 는 형제라 같은 팔레트에 자기 적입수량(2)만큼 더 실린다
            Assert.Equal(InjConfirmOutcome.Confirmed,  repo.ConfirmByLotCode(a1, InjLine, "E-PLT", null, "E-PLT", Pallet1, out _).Outcome);
            Assert.Equal(InjConfirmOutcome.Confirmed,  repo.ConfirmByLotCode(a2, InjLine, "E-PLT", null, "E-PLT", Pallet1, out _).Outcome);
            Assert.Equal(InjConfirmOutcome.PalletFull, repo.ConfirmByLotCode(a3, InjLine, "E-PLT", null, "E-PLT", Pallet1, out _).Outcome);
            Assert.Equal(InjConfirmOutcome.Confirmed,  repo.ConfirmByLotCode(b1, InjLine, "E-PLT", null, "E-PLT", Pallet1, out _).Outcome);
            Assert.Equal(InjConfirmOutcome.Confirmed,  repo.ConfirmByLotCode(b2, InjLine, "E-PLT", null, "E-PLT", Pallet1, out _).Outcome);
            Assert.Equal(InjConfirmOutcome.PalletFull, repo.ConfirmByLotCode(b3, InjLine, "E-PLT", null, "E-PLT", Pallet1, out _).Outcome);

            var st = new PalletStockRepository(f!).Inspect(Pallet1, CoreB);
            Assert.Equal(2, st.State.CountOf(CoreA)); Assert.Equal(2, st.State.CountOf(CoreB));
            Assert.True(st.IsFull);
            Assert.Equal($"{CoreB} 2 · {CoreA} 2 / 2", st.CapacityText);
            Assert.Equal(4, Count(f!, "SELECT COUNT(*) FROM dbo.WH_Inventory WHERE ParentLotNo=@P AND Qty=1", ("@P", Pallet1)));

            // 형제가 아닌 완제품 품번은 여전히 혼적 거부
            var fg = new ImgLotRepository(f!).CreateRawLot(ImgLine, FgA, "E-PLT");
            Assert.Equal(ImgConfirmOutcome.PalletMixed, new ImgLotRepository(f!).ConfirmByLotCode(fg.LotCode, ImgLine, "E-PLT", null, "E-PLT", Pallet1, out _).Outcome);
        }
        finally { Cleanup(f!); }
    }

    /// <summary>
    /// 불량 등록은 팔레트에 실린 LOT 의 재고 행을 지우고 OUT(POP_DEFECT)만 남긴다 — 팔레트 적재 수가 줄어 다음 LOT 이 실린다.
    /// 폐기는 안전망: 불량 등록이 이미 뺐으면 아무것도 쓰지 않고, 행이 남아 있던 LOT(이 기능 전에 등록된 LOT)은 지우며 위치가 있었으면 알린다.
    /// 실린 적 없는 RAW LOT 의 불량 등록은 재고를 건드리지 않는다.
    /// </summary>
    [SkippableFact]
    public void Defect_registration_takes_the_lot_off_its_pallet_and_scrap_is_the_safety_net()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        Seed(f!);
        var inj = new InjLotRepository(f!);
        var rwk = new ReworkRepository(f!);
        var stock = new PalletStockRepository(f!);
        try
        {
            var (lot1, code1) = inj.CreateRawLot(InjLine, "INJ-01-01-1800", Map(CoreA), 990301);
            var (lot2, code2) = inj.CreateRawLot(InjLine, "INJ-01-01-1800", Map(CoreA), 990302);
            var (_,    code3) = inj.CreateRawLot(InjLine, "INJ-01-01-1800", Map(CoreA), 990303);
            var (_,    code4) = inj.CreateRawLot(InjLine, "INJ-01-01-1800", Map(CoreA), 990304);
            Assert.Equal(InjConfirmOutcome.Confirmed,  inj.ConfirmByLotCode(code1, InjLine, "E-PLT", null, "E-PLT", Pallet1, out _).Outcome);
            Assert.Equal(InjConfirmOutcome.Confirmed,  inj.ConfirmByLotCode(code2, InjLine, "E-PLT", null, "E-PLT", Pallet1, out _).Outcome);
            Assert.Equal(InjConfirmOutcome.PalletFull, inj.ConfirmByLotCode(code3, InjLine, "E-PLT", null, "E-PLT", Pallet1, out _).Outcome);

            // 불량 등록 → 재고 행 삭제 + OUT POP_DEFECT(위치 없음 = 정상 경로, 경고 없음), 팔레트는 1 / 2 로 돌아가 셋째가 실린다
            var reg1 = inj.RegisterDefect(code1, InjLine, "INJ-D01", "E-PLT", null, "E-PLT", out var u1);
            Assert.Equal(DefectRegisterOutcome.Registered, reg1.Outcome);
            Assert.True(u1.Removed); Assert.Equal(Pallet1, u1.PalletNo); Assert.Null(u1.LocationNo); Assert.False(u1.WasLocated);
            Assert.Equal(0, Count(f!, "SELECT COUNT(*) FROM dbo.WH_Inventory WHERE LotNo=@L", ("@L", code1)));
            Assert.Equal(1, Count(f!, "SELECT COUNT(*) FROM dbo.WH_InventoryTransaction WHERE LotNo=@L AND TransactionType='OUT' AND SourceType='POP_DEFECT' AND SourceID=@Id AND QtyBefore=1 AND QtyChange=-1 AND QtyAfter=0 AND LocationNo IS NULL",
                                  ("@L", code1), ("@Id", lot1)));
            Assert.Equal(1, stock.Inspect(Pallet1, CoreA).State.CountOf(CoreA));
            Assert.Equal(InjConfirmOutcome.Confirmed, inj.ConfirmByLotCode(code3, InjLine, "E-PLT", null, "E-PLT", Pallet1, out _).Outcome);

            // 폐기: 불량 등록이 이미 뺐으므로 안전망은 아무것도 쓰지 않는다
            Assert.Equal(ReworkOutcome.Done, rwk.Scrap(reg1.DefectId, "ITEST-CAUSE", null, "E-PLT", "E-PLT", out var u2));
            Assert.False(u2.Removed);
            Assert.Equal(0, Count(f!, "SELECT COUNT(*) FROM dbo.WH_InventoryTransaction WHERE LotNo=@L AND SourceType='POP_SCRAP'", ("@L", code1)));

            // 안전망: 이 기능 전에 등록돼 행이 남은 LOT 을 흉내 — 불량 등록 뒤 재고 행을 위치까지 배정된 채 되살려 두고 폐기하면 지우고 위치를 알린다
            var reg2 = inj.RegisterDefect(code2, InjLine, "INJ-D01", "E-PLT", null, "E-PLT", out var u2b);
            Assert.True(u2b.Removed);
            AmesDevDb.Exec(f!, """
                INSERT dbo.WH_Inventory (LotNo, UnitType, ParentLotNo, PartNo, PalletNo, LocationNo, Qty, ReceivedAt, CreatedAt, UpdatedAt)
                VALUES (@L, 'PART', @P, @I, @P, 'ZPLT-LOC', 1, SYSDATETIME(), SYSDATETIME(), SYSDATETIME());
                """, ("@L", code2), ("@P", Pallet1), ("@I", CoreA));
            Assert.Equal(ReworkOutcome.Done, rwk.Scrap(reg2.DefectId, "ITEST-CAUSE", null, "E-PLT", "E-PLT", out var u3));
            Assert.True(u3.Removed); Assert.Equal("ZPLT-LOC", u3.LocationNo); Assert.True(u3.WasLocated);
            Assert.Equal(0, Count(f!, "SELECT COUNT(*) FROM dbo.WH_Inventory WHERE LotNo=@L", ("@L", code2)));
            Assert.Equal(1, Count(f!, "SELECT COUNT(*) FROM dbo.WH_InventoryTransaction WHERE LotNo=@L AND TransactionType='OUT' AND SourceType='POP_SCRAP' AND SourceID=@Id AND QtyChange=-1 AND LocationNo='ZPLT-LOC'",
                                  ("@L", code2), ("@Id", lot2)));
            Assert.Equal("SCRAPPED", (string)AmesDevDb.Scalar(f!, "SELECT ConfirmStatus FROM dbo.PR_InjLot WHERE LotID=@L", ("@L", lot2))!);

            // 실린 적 없는 RAW LOT — 재고도 거래도 없다. 팔레트 머리 행은 그대로
            var reg4 = inj.RegisterDefect(code4, InjLine, "INJ-D01", "E-PLT", null, "E-PLT", out var u4);
            Assert.Equal(DefectRegisterOutcome.Registered, reg4.Outcome);
            Assert.False(u4.Removed);
            Assert.Equal(0, Count(f!, "SELECT COUNT(*) FROM dbo.WH_InventoryTransaction WHERE LotNo=@L", ("@L", code4)));
            Assert.Equal(1, Count(f!, "SELECT COUNT(*) FROM dbo.WH_Inventory WHERE LotNo=@P AND UnitType='PALLET'", ("@P", Pallet1)));
        }
        finally { Cleanup(f!); }
    }
}
