using AMES.Contracts.Dto;
using AMES.Contracts.Enums;
using AMES.Data.Repositories;
using AMES.Data.Services;
using AMES.Pop.Services;
using Xunit;

namespace AMES.Pop.Tests;

// IMG·PNT 오프라인 엔진 — INJ 와 같은 엔진 위에서 Core 스캔·완제품 라벨 발행·판정이 로컬 사본으로 도는지.
[Collection("OfflineAuth")]
public sealed class OfflineFinishedTests : IDisposable
{
    private const string ImgLine = "LINE-IMG-01", PntLine = "LINE-PNT-01", Station = "ST-1";
    private const string Fin = "FIN-A", FinG = "FIN-G", CoreA = "CORE-A", CoreD = "CORE-D";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ames-offfin-" + Guid.NewGuid().ToString("N"));
    private readonly string _origDir = OfflineStore.Dir;

    public OfflineFinishedTests()
    {
        OfflineStore.Dir = _dir;
        OfflineWork.ResetForTests();
        OfflineAuth.ResetForTests();
    }

    public void Dispose()
    {
        OfflineStore.Dir = _origDir;
        OfflineWork.ResetForTests();
        OfflineAuth.ResetForTests();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static string Head()
    {
        var now = DbClock.Now;
        return $"{LotNoGenerator.EncodeYear(now.Year)}{LotNoGenerator.EncodeMonth(now.Month)}{LotNoGenerator.EncodeDay(now.Day)}W1";
    }

    private static OfflineLabel Label(string name, string? customer = "CUS-ITEM") => new()
    {
        Exists = true, ItemName = name, Pgn = "PGN1", Alc = "ALC1", MountPos = "FL", CustomerCode = customer, EquipId = "EQ-W1",
    };

    private static OfflineWorkState Img(int block = 5, params OfflineLot[] lots)
    {
        var s = new OfflineWorkState
        {
            Process = "IMG", TakenAtUtc = DateTime.UtcNow, LineId = ImgLine, StationCode = Station, PastDays = 7, FutureDays = 7,
            Lots = lots.ToList(),
            Summaries = new() { [DateTime.Today.ToString("yyyy-MM-dd")] = new() { new InjItemDailyDto { ItemNo = Fin, ItemName = "name " + Fin, PlanQty = 50, InputQty = 0, NgQty = 0, FinalQty = 0, PendingQty = 0, InBop = true, HasOpenWo = true } } },
            OpenWos = new() { new WorkOrderDto { WoId = 9, WoNumber = "WO-9", ItemNo = Fin, ItemName = "name " + Fin, OrderQty = 50, OpenQty = 50, CompletedQty = 0, LineId = ImgLine, Status = "Released", StepItemNo = Fin } },
            DefectCodes = new() { new DefectCodeDto { DefectCode = "IMG-D01", DefectName = "주름" } },
            Pallets = new() { new PalletStockRepository.OfflinePalletRow("EOS-PLT-2610-0001", true, false, new(StringComparer.OrdinalIgnoreCase)) },
            ItemRules = new() { [Fin] = new PalletStockRepository.OfflineItemRule(2, new[] { Fin }) },
            Labels = new() { [Fin] = Label("name " + Fin, "CUS-ITEM"), [FinG] = Label("name " + FinG, "CUS-G") },
            Cores = new() { new OfflineCore { LotCode = "A91I10001", ItemNo = CoreA }, new OfflineCore { LotCode = "A91I10002", ItemNo = CoreD } },
            CoreMap = new() { new OfflineCoreMap { WoId = 9, FinishedItem = Fin, CoreItem = CoreA, CustomerCode = "CUS-WO" } },
            BomCores = new() { [FinG] = new OfflineBomCore { Outcome = "Core", CoreItemNo = CoreD }, [Fin] = new OfflineBomCore { Outcome = "Core", CoreItemNo = CoreA } },
        };
        s.LotPrefix = Head()[3..5];
        s.LotBlocks.Add(new OfflineLotBlock(DateTime.Today, Head(), block + 1, 9999));   // 온라인에서 받아 둔 구간 — 다음은 block+1
        OfflineStore.Save("work-state.bin", s);
        OfflineWork.ResetForTests();
        return s;
    }

    private static OfflineLot FinLot(string code, string status = "RAW", string item = Fin, string line = ImgLine)
        => new() { LotId = 1, LotCode = code, ItemNo = item, ItemName = "name " + item, LineId = line, ConfirmStatus = status, CreatedTS = DateTime.Now };

    private static (ImgCoreOutcome Outcome, OfflineLot? Lot, string? UsedBy, string? ItemNo, bool Unverified) Core(string code, string? selected = null)
        => OfflineWork.CreateFromCore(code, ImgLine, Station, "E001", "E001", -1, selected);

    // ── IMG Core 스캔 ───────────────────────────────────────────────────

    [Fact]
    public void Core_scan_with_an_open_wo_makes_the_wos_finished_item_with_its_customer()
    {
        Img();
        var r = Core("A91I10001");

        Assert.Equal(ImgCoreOutcome.Created, r.Outcome);
        Assert.Equal(Fin, r.ItemNo);
        Assert.False(r.Unverified);
        Assert.Equal(Head() + "0006", r.Lot!.LotCode);   // 라인의 마지막 순번 5 다음
        Assert.Equal("A91I10001", r.Lot.CoreLotCode);
        Assert.Equal("CUS-WO", r.Lot.CustomerCode);   // WO 의 수주처 — 라벨 V 토큰
        Assert.Equal("PGN1", r.Lot.Pgn);
        Assert.Equal("RAW", r.Lot.ConfirmStatus);
        Assert.Equal(1, OfflineWork.PendingCount);

        var row = OfflineWork.Summary(DateTime.Today).Single();
        Assert.Equal(1, row.InputQty);
        Assert.Equal(1, row.PendingQty);
    }

    [Fact]
    public void A_core_can_be_used_only_once_even_offline()
    {
        Img();
        var first = Core("A91I10001");
        var second = Core("A91I10001");
        Assert.Equal(ImgCoreOutcome.CoreUsed, second.Outcome);
        Assert.Equal(first.Lot!.LotCode, second.UsedBy);
        Assert.Equal(1, OfflineWork.PendingCount);
    }

    [Fact]
    public void Numbers_continue_after_the_last_number_and_stop_at_9999()
    {
        Img(block: 9998);
        var first = Core("A91I10001");
        Assert.Equal(ImgCoreOutcome.Created, first.Outcome);
        Assert.EndsWith("9999", first.Lot!.LotCode);
        Assert.Throws<OfflineLotNumbersExhaustedException>(() => Core("A91I10002", FinG));
    }

    [Fact]
    public void Offline_label_numbers_continue_from_the_line_cursor_in_order()
    {
        Img(block: 7);
        var a = OfflineWork.CreateFinishedRaw(ImgLine, Station, "op", "E1", 1, Fin);
        var b = OfflineWork.CreateFinishedRaw(ImgLine, Station, "op", "E1", 1, Fin);
        Assert.Equal(Head() + "0008", a.LotCode);
        Assert.Equal(Head() + "0009", b.LotCode);
    }

    [Fact]
    public void Online_printing_does_not_move_the_reserved_block()
    {
        Img(block: 3);
        var next = OfflineWork.CreateFinishedRaw(ImgLine, Station, "op", "E1", 1, Fin);
        Assert.Equal(Head() + "0004", next.LotCode);   // 구간은 온라인 출력과 무관하게 받은 그대로 이어진다
    }

    [Fact]
    public void Without_an_open_wo_the_selected_item_must_match_the_boms_core()
    {
        Img();
        // CORE-D 는 WO 가 없다. FIN-G 의 BOM 코어가 CORE-D 이므로 맞다
        var ok = Core("A91I10002", FinG);
        Assert.Equal(ImgCoreOutcome.Created, ok.Outcome);
        Assert.Equal(FinG, ok.Lot!.ItemNo);
        Assert.Equal("CUS-G", ok.Lot.CustomerCode);   // WO 가 없으니 품번의 열린 WO 기준 수주처(사본의 라벨 정보)
    }

    [Fact]
    public void Selected_item_whose_bom_core_differs_from_the_scanned_core_is_refused()
    {
        Img();
        // CORE-A 를 찍었는데 FIN-G(BOM 코어 CORE-D)를 골랐다 — WO 도 FIN-G 와 안 맞는다
        var r = Core("A91I10001", FinG);
        Assert.Equal(ImgCoreOutcome.CoreMismatch, r.Outcome);
        Assert.Equal(0, OfflineWork.PendingCount);
    }

    [Fact]
    public void Without_a_wo_and_without_a_selection_the_finished_item_is_unknown()
    {
        Img();
        var r = Core("A91I10002");
        Assert.Equal(ImgCoreOutcome.NoFinishedItem, r.Outcome);
        Assert.Equal(CoreD, r.ItemNo);
    }

    [Fact]
    public void An_unknown_core_is_accepted_only_with_a_selected_item_and_flagged_unverified()
    {
        Img();
        Assert.Equal(ImgCoreOutcome.NotFound, Core("A91I99999").Outcome);
        Assert.Equal(ImgCoreOutcome.NotFound, Core("EOS-PLT-2610-0001", Fin).Outcome);   // 팔레트 바코드는 Core 가 아니다

        var r = Core("A91I99999", Fin);
        Assert.Equal(ImgCoreOutcome.Created, r.Outcome);
        Assert.True(r.Unverified);
        Assert.Equal(Fin, r.Lot!.ItemNo);
    }

    // ── 완제품 라벨 발행·판정 ───────────────────────────────────────────

    [Fact]
    public void Issuing_a_label_without_a_core_uses_the_label_info_and_remembers_printing()
    {
        Img();
        var lot = OfflineWork.CreateFinishedRaw(ImgLine, Station, "E001", "E001", -1, Fin);
        Assert.Equal("CUS-ITEM", lot.CustomerCode);
        Assert.Equal("ALC1", lot.Alc);
        Assert.Null(lot.CoreLotCode);
        OfflineWork.MarkPrinted(lot.LotCode);
        Assert.Equal(1, OfflineWork.FindLot(lot.LotCode)!.PrintedCount);
        Assert.Equal(1, OfflineWork.PendingCount);
    }

    [Fact]
    public void A_finished_lot_made_offline_can_be_judged_ok_and_the_counts_follow()
    {
        Img();
        var lot = Core("A91I10001").Lot!;
        var ok = OfflineWork.Confirm(lot.LotCode, ImgLine, Station, "E001", "E001", -1, "EOS-PLT-2610-0001", lot.ItemNo);

        Assert.Equal(InjConfirmOutcome.Confirmed, ok.Outcome);
        Assert.Equal(ImgConfirmOutcome.Confirmed, OfflineWork.ToImg(ok.Outcome));
        Assert.Equal(9, ok.WoId);
        Assert.Equal(2, OfflineWork.PendingCount);   // CreateFromCore + Confirm 발생 순서대로

        var row = OfflineWork.Summary(DateTime.Today).Single();
        Assert.Equal(1, row.InputQty);
        Assert.Equal(0, row.PendingQty);
        Assert.Equal(1, row.FinalQty);
    }

    [Fact]
    public void Judging_ng_registers_a_defect_and_takes_the_lot_off_the_pallet()
    {
        Img();
        var lot = Core("A91I10001").Lot!;
        OfflineWork.Confirm(lot.LotCode, ImgLine, Station, "E001", "E001", -1, "EOS-PLT-2610-0001", lot.ItemNo);
        Assert.Equal(1, OfflineWork.Inspect("EOS-PLT-2610-0001", Fin).State.CountOf(Fin));

        var ng = OfflineWork.RegisterDefect(lot.LotCode, ImgLine, Station, "E001", "E001", -1, "IMG-D01", lot.ItemNo);
        Assert.Equal(DefectRegisterOutcome.Registered, ng.Outcome);
        Assert.Equal(0, OfflineWork.Inspect("EOS-PLT-2610-0001", Fin).State.CountOf(Fin));
        var row = OfflineWork.Summary(DateTime.Today).Single();
        Assert.Equal(0, row.FinalQty);
        Assert.Equal(1, row.NgQty);
    }

    [Fact]
    public void Img_confirm_outcomes_map_by_name_and_ng_blocked_has_no_equivalent()
    {
        Assert.Equal(ImgConfirmOutcome.PalletFull, OfflineWork.ToImg(InjConfirmOutcome.PalletFull));
        Assert.Equal(ImgConfirmOutcome.AlreadyConfirmed, OfflineWork.ToImg(InjConfirmOutcome.AlreadyConfirmed));
        Assert.Equal(ImgConfirmOutcome.NotFound, OfflineWork.ToImg(InjConfirmOutcome.NgBlocked));
    }

    [Fact]
    public void Screen_shows_todays_lots_and_the_raw_count_among_them()
    {
        Img(5, FinLot("OLD00001", "RAW"));
        // 사본의 LOT 은 CreatedTS 가 지금이라 오늘 목록에 들어간다
        var made = Core("A91I10001").Lot!;
        var scr = OfflineWork.BuildScreen(ImgLine, Station, Fin, DateTime.Today)!;
        Assert.Equal(2, scr.TodayLots.Count);
        Assert.Equal(2, scr.RawCount);
        Assert.Equal(made.LotCode, scr.TodayLots[0].LotCode);   // 최신순
    }

    // ── 온라인 중 덧붙이기 ──────────────────────────────────────────────

    [Fact]
    public void Absorb_adds_new_lots_updates_statuses_and_refreshes_todays_summary()
    {
        Img(5, FinLot("OLD00001", "RAW"));
        var todaySummary = new List<InjItemDailyDto> { new() { ItemNo = Fin, ItemName = "x", PlanQty = 50, InputQty = 9, NgQty = 1, FinalQty = 5, PendingQty = 3, InBop = true, HasOpenWo = true } };

        OfflineWork.Absorb(ImgLine, Station,
            new[] { FinLot("OLD00001", "CONFIRMED"), FinLot("NEW00002", "RAW") }, todaySummary);

        Assert.Equal("CONFIRMED", OfflineWork.FindLot("OLD00001")!.ConfirmStatus);
        Assert.NotNull(OfflineWork.FindLot("NEW00002"));
        Assert.Equal(9, OfflineWork.Summary(DateTime.Today).Single().InputQty);
    }

    [Fact]
    public void Absorb_is_skipped_while_offline_work_is_pending_and_for_another_line()
    {
        Img(5, FinLot("OLD00001", "RAW"));
        Core("A91I10001");   // 반영 못 한 작업이 있다
        OfflineWork.Absorb(ImgLine, Station, new[] { FinLot("NEW00002", "RAW") }, null);
        Assert.Null(OfflineWork.FindLot("NEW00002"));

        OfflineWork.ResetForTests();
        Img(5);
        OfflineWork.Absorb("LINE-IMG-02", Station, new[] { FinLot("NEW00003", "RAW", line: "LINE-IMG-02") }, null);
        Assert.Null(OfflineWork.FindLot("NEW00003"));
    }

    [Fact]
    public void Absorbed_changes_reach_the_disk_only_when_flushed()
    {
        Img(5, FinLot("OLD00001", "RAW"));
        OfflineWork.Absorb(ImgLine, Station, new[] { FinLot("NEW00002", "RAW") }, null);
        OfflineWork.FlushIfDirty(force: true);
        OfflineWork.ResetForTests();   // 재시작
        Assert.NotNull(OfflineWork.FindLot("NEW00002"));
    }

    // ── 결번 최소화 ─────────────────────────────────────────────────────
}
