using AMES.Contracts.Dto;
using AMES.Contracts.Enums;
using AMES.Data.Repositories;
using AMES.Data.Services;
using AMES.Pop.Services;
using Xunit;

namespace AMES.Pop.Tests;

// OfflineWork 는 프로세스 전역 상태(사본·큐 파일)를 쓴다 — OfflineAuth 와 같은 컬렉션에서 차례로 돌린다.
[Collection("OfflineAuth")]
public sealed class OfflineWorkTests : IDisposable
{
    private const string Line = "LINE-INJ-01", Station = "ST-INJ-01", Item = "ITEM-A", Sibling = "ITEM-B";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ames-offwork-" + Guid.NewGuid().ToString("N"));
    private readonly string _origDir = OfflineStore.Dir;

    public OfflineWorkTests()
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

    private static OfflineLot Lot(string code, string status = "RAW", string item = Item, string line = Line, DateTime? created = null)
        => new() { LotId = 1, LotCode = code, ItemNo = item, ItemName = "name " + item, LineId = line, ConfirmStatus = status, CreatedTS = created ?? DateTime.Now };

    private static InjItemDailyDto Row(string item, decimal plan = 0, int input = 0, int ng = 0, int final = 0, int pending = 0, bool wo = false)
        => new() { ItemNo = item, ItemName = "name " + item, PlanQty = plan, InputQty = input, NgQty = ng, FinalQty = final, PendingQty = pending, InBop = true, HasOpenWo = wo };

    private static WorkOrderDto Wo(string item, int id = 7, string? mold = null) => new()
    {
        WoId = id, WoNumber = $"WO-{id}", ItemNo = item, ItemName = "name " + item, OrderQty = 100, OpenQty = 100, CompletedQty = 0,
        LineId = Line, MoldId = mold, Status = "Released", StepItemNo = item,
    };

    private static OfflineWorkState State(params OfflineLot[] lots)
    {
        var today = DateTime.Today;
        var s = new OfflineWorkState
        {
            Process = "INJ", TakenAtUtc = DateTime.UtcNow, LineId = Line, StationCode = Station, PastDays = 7, FutureDays = 7,
            Lots = lots.ToList(),
            Summaries = new() { [today.ToString("yyyy-MM-dd")] = new() { Row(Item, plan: 100, input: 4, final: 1, pending: 3, wo: true) } },
            OpenWos = new() { Wo(Item) },
            DefectCodes = new() { new DefectCodeDto { DefectCode = "INJ-D01", DefectName = "미성형" } },
            Pallets = new()
            {
                new PalletStockRepository.OfflinePalletRow("EOS-PLT-2610-0001", true, false, new(StringComparer.OrdinalIgnoreCase)),
                new PalletStockRepository.OfflinePalletRow("EOS-PLT-2610-0002", true, false, new(StringComparer.OrdinalIgnoreCase) { [Item] = 1 }),
                new PalletStockRepository.OfflinePalletRow("EOS-PLT-2610-0003", false, false, new(StringComparer.OrdinalIgnoreCase)),
                new PalletStockRepository.OfflinePalletRow("EOS-PLT-2610-0004", true, false, new(StringComparer.OrdinalIgnoreCase) { ["OTHER"] = 2 }),
            },
            ItemRules = new()
            {
                [Item] = new PalletStockRepository.OfflineItemRule(2, new[] { Item, Sibling }),
                [Sibling] = new PalletStockRepository.OfflineItemRule(2, new[] { Item, Sibling }),   // 형제 관계는 양쪽 모두 담긴다
            },
            Manual = new() { ["|" + Item] = new OfflineManualMold { ItemName = "name " + Item, EquipId = "EQ-1" } },
        };
        OfflineStore.Save("work-state.bin", s);
        OfflineWork.ResetForTests();
        return s;
    }

    private static (InjConfirmOutcome Outcome, string ItemNo, int WoId, bool Unverified) Confirm(string lot, string pallet, string? sel = Item)
        => OfflineWork.Confirm(lot, Line, Station, "E001", "E001", -1, pallet, sel);

    [Fact]
    public void Confirming_a_known_raw_lot_succeeds_and_moves_the_counts()
    {
        State(Lot("A91I10001"));
        var r = Confirm("A91I10001", "EOS-PLT-2610-0001");

        Assert.Equal(InjConfirmOutcome.Confirmed, r.Outcome);
        Assert.Equal(Item, r.ItemNo);
        Assert.Equal(7, r.WoId);
        Assert.Equal("CONFIRMED", OfflineWork.FindLot("A91I10001")!.ConfirmStatus);
        Assert.Equal(1, OfflineWork.PendingCount);

        var row = OfflineWork.Summary(DateTime.Today).Single();
        Assert.Equal(4, row.InputQty);      // 입력은 그대로
        Assert.Equal(2, row.PendingQty);    // 확정 대기 3 → 2
        Assert.Equal(2, row.FinalQty);      // 확정 1 → 2
    }

    [Fact]
    public void Same_lot_twice_is_already_confirmed()
    {
        State(Lot("A91I10001"));
        Confirm("A91I10001", "EOS-PLT-2610-0001");
        Assert.Equal(InjConfirmOutcome.AlreadyConfirmed, Confirm("A91I10001", "EOS-PLT-2610-0001").Outcome);
        Assert.Equal(1, OfflineWork.PendingCount);   // 두 번째는 큐에 올리지 않는다
    }

    [Theory]
    [InlineData("NG_BLOCKED", InjConfirmOutcome.NgBlocked)]
    [InlineData("DEFECT", InjConfirmOutcome.InRework)]
    [InlineData("SCRAPPED", InjConfirmOutcome.Scrapped)]
    public void Blocked_statuses_follow_the_online_rules(string status, InjConfirmOutcome expected)
    {
        State(Lot("A91I10002", status));
        Assert.Equal(expected, Confirm("A91I10002", "EOS-PLT-2610-0001").Outcome);
        Assert.Equal(0, OfflineWork.PendingCount);
    }

    [Fact]
    public void Lot_of_another_line_is_refused()
    {
        State(Lot("A91I20001", line: "LINE-INJ-02"));
        Assert.Equal(InjConfirmOutcome.WrongLine, Confirm("A91I20001", "EOS-PLT-2610-0001").Outcome);
    }

    [Fact]
    public void Unknown_lot_is_accepted_with_the_selected_item_and_flagged_unverified()
    {
        State();
        var r = Confirm("A91I99999", "EOS-PLT-2610-0001");
        Assert.Equal(InjConfirmOutcome.Confirmed, r.Outcome);
        Assert.True(r.Unverified);
        Assert.Equal(Item, r.ItemNo);
        Assert.Equal(1, OfflineWork.PendingCount);
    }

    [Fact]
    public void Unknown_lot_without_a_selected_item_or_with_a_bad_shape_is_not_found()
    {
        State();
        Assert.Equal(InjConfirmOutcome.NotFound, Confirm("A91I99999", "EOS-PLT-2610-0001", sel: null).Outcome);
        Assert.Equal(InjConfirmOutcome.NotFound, Confirm("x y", "EOS-PLT-2610-0001").Outcome);
        Assert.Equal(InjConfirmOutcome.NotFound, Confirm("EOS-PLT-2610-0001", "EOS-PLT-2610-0001").Outcome);   // 팔레트 바코드는 LOT 이 아니다
    }

    [Fact]
    public void Pallet_that_is_full_for_the_item_is_refused_and_leaves_the_lot_raw()
    {
        // 적입수량 2 — 0002 에는 이미 1개
        State(Lot("A91I10001"), Lot("A91I10002"));
        Assert.Equal(InjConfirmOutcome.Confirmed, Confirm("A91I10001", "EOS-PLT-2610-0002").Outcome);
        var full = Confirm("A91I10002", "EOS-PLT-2610-0002");
        Assert.Equal(InjConfirmOutcome.PalletFull, full.Outcome);
        Assert.Equal("RAW", OfflineWork.FindLot("A91I10002")!.ConfirmStatus);
        Assert.Equal(1, OfflineWork.PendingCount);
    }

    [Fact]
    public void Inactive_pallet_and_a_pallet_with_another_item_are_refused()
    {
        State(Lot("A91I10001"));
        Assert.Equal(InjConfirmOutcome.PalletUnavailable, Confirm("A91I10001", "EOS-PLT-2610-0003").Outcome);
        Assert.Equal(InjConfirmOutcome.PalletMixed, Confirm("A91I10001", "EOS-PLT-2610-0004").Outcome);
    }

    [Fact]
    public void Pallet_missing_from_the_copy_is_accepted_and_marked_unverified()
    {
        State(Lot("A91I10001"));
        var r = Confirm("A91I10001", "EOS-PLT-2610-9999");
        Assert.Equal(InjConfirmOutcome.Confirmed, r.Outcome);
        Assert.True(r.Unverified);
        Assert.True(OfflineWork.IsUnverifiedPallet("EOS-PLT-2610-9999"));
        Assert.False(OfflineWork.IsKnownPallet("EOS-PLT-2610-0555"));
    }

    [Fact]
    public void Inspect_reports_the_loaded_count_for_a_known_pallet()
    {
        State();
        var st = OfflineWork.Inspect("EOS-PLT-2610-0002", Item);
        Assert.Equal(PalletLoadOutcome.Ok, st.Outcome);
        Assert.Equal(2, st.PalletQty);
        Assert.Equal(1, st.State.CountOf(Item));
    }

    [Fact]
    public void Sibling_item_may_share_a_pallet_but_counts_separately()
    {
        State(Lot("A91I10001", item: Sibling));
        var r = Confirm("A91I10001", "EOS-PLT-2610-0002", sel: Sibling);
        // 0002 에는 ITEM-A 1개 — ITEM-B 는 형제라 함께 실린다
        Assert.Equal(InjConfirmOutcome.Confirmed, r.Outcome);
    }

    [Fact]
    public void Defect_on_a_raw_lot_moves_pending_to_ng()
    {
        State(Lot("A91I10001"));
        var r = OfflineWork.RegisterDefect("A91I10001", Line, Station, "E001", "E001", -1, "INJ-D01", Item);
        Assert.Equal(DefectRegisterOutcome.Registered, r.Outcome);
        Assert.Equal("DEFECT", OfflineWork.FindLot("A91I10001")!.ConfirmStatus);

        var row = OfflineWork.Summary(DateTime.Today).Single();
        Assert.Equal(2, row.PendingQty);
        Assert.Equal(1, row.NgQty);
        Assert.Equal(1, row.FinalQty);
    }

    [Fact]
    public void Defect_after_an_offline_confirm_takes_the_lot_off_the_pallet()
    {
        State(Lot("A91I10001"));
        Confirm("A91I10001", "EOS-PLT-2610-0001");
        Assert.Equal(1, OfflineWork.Inspect("EOS-PLT-2610-0001", Item).State.CountOf(Item));

        var r = OfflineWork.RegisterDefect("A91I10001", Line, Station, "E001", "E001", -1, "INJ-D01", Item);
        Assert.Equal(DefectRegisterOutcome.Registered, r.Outcome);
        Assert.Equal(0, OfflineWork.Inspect("EOS-PLT-2610-0001", Item).State.CountOf(Item));
        // 확정 → 불량: 확정 +1 −1 = 변화 없음, NG +1
        var row = OfflineWork.Summary(DateTime.Today).Single();
        Assert.Equal(1, row.FinalQty);
        Assert.Equal(1, row.NgQty);
    }

    [Theory]
    [InlineData("DEFECT", DefectRegisterOutcome.AlreadyInRework)]
    [InlineData("SCRAPPED", DefectRegisterOutcome.Scrapped)]
    public void Defect_on_a_lot_already_out_of_play_follows_the_online_rules(string status, DefectRegisterOutcome expected)
    {
        State(Lot("A91I10003", status));
        Assert.Equal(expected, OfflineWork.RegisterDefect("A91I10003", Line, Station, "E001", "E001", -1, "INJ-D01", Item).Outcome);
        Assert.Equal(0, OfflineWork.PendingCount);
    }

    [Fact]
    public void Manual_lot_takes_the_next_reserved_number_and_shows_up_as_pending()
    {
        var s = State();
        var now = DbClock_Now();
        var head = $"{LotNoGenerator.EncodeYear(now.Year)}{LotNoGenerator.EncodeMonth(now.Month)}{LotNoGenerator.EncodeDay(now.Day)}I1";
        s.LotBlocks.Add(new OfflineLotBlock { Header = head, Day = now.Date, Next = 21, End = 22 });
        OfflineStore.Save("work-state.bin", s);
        OfflineWork.ResetForTests();

        var a = OfflineWork.CreateManualLot(Line, Station, "E001", "E001", -1, Item, null, 0);
        var b = OfflineWork.CreateManualLot(Line, Station, "E001", "E001", -1, Item, null, 0);
        Assert.Equal(head + "0021", a.LotCode);
        Assert.Equal(head + "0022", b.LotCode);
        Assert.NotEqual(a.LotId, b.LotId);
        Assert.Throws<OfflineLotNumbersExhaustedException>(() =>
            OfflineWork.CreateManualLot(Line, Station, "E001", "E001", -1, Item, null, 0));

        var row = OfflineWork.Summary(DateTime.Today).Single();
        Assert.Equal(6, row.InputQty);     // 4 + 2
        Assert.Equal(5, row.PendingQty);   // 3 + 2
        Assert.Equal(2, OfflineWork.BuildScreen(Line, Station, Item, DateTime.Today)!.RawLots.Count(l => l.LotCode.StartsWith(head)));
    }

    [Fact]
    public void Manual_lot_without_a_number_block_for_today_is_refused()
    {
        State();
        Assert.Throws<OfflineLotNumbersExhaustedException>(() =>
            OfflineWork.CreateManualLot(Line, Station, "E001", "E001", -1, Item, null, 0));
    }

    [Fact]
    public void A_manual_lot_can_be_confirmed_offline_and_printing_is_remembered()
    {
        var s = State();
        var now = DbClock_Now();
        var head = $"{LotNoGenerator.EncodeYear(now.Year)}{LotNoGenerator.EncodeMonth(now.Month)}{LotNoGenerator.EncodeDay(now.Day)}I1";
        s.LotBlocks.Add(new OfflineLotBlock { Header = head, Day = now.Date, Next = 1, End = 5 });
        OfflineStore.Save("work-state.bin", s);
        OfflineWork.ResetForTests();

        var lot = OfflineWork.CreateManualLot(Line, Station, "E001", "E001", -1, Item, null, 0);
        OfflineWork.MarkPrinted(lot.LotCode);
        Assert.Equal(1, OfflineWork.FindLot(lot.LotCode)!.PrintedCount);

        Assert.Equal(InjConfirmOutcome.Confirmed, Confirm(lot.LotCode, "EOS-PLT-2610-0001").Outcome);
        Assert.Equal(2, OfflineWork.PendingCount);   // ManualLot + Confirm, 발생 순서대로
    }

    [Fact]
    public void Queue_and_state_survive_a_restart()
    {
        State(Lot("A91I10001"));
        Confirm("A91I10001", "EOS-PLT-2610-0001");
        OfflineWork.ResetForTests();   // 프로세스 재시작처럼 메모리만 버린다

        Assert.Equal(1, OfflineWork.PendingCount);
        Assert.Equal("CONFIRMED", OfflineWork.FindLot("A91I10001")!.ConfirmStatus);
    }

    [Fact]
    public void Screen_is_built_from_the_copy_and_picks_the_first_item_with_an_open_wo()
    {
        State(Lot("A91I10001"), Lot("A91I10002", "NG_BLOCKED"));
        var scr = OfflineWork.BuildScreen(Line, Station, null, DateTime.Today)!;
        Assert.Equal(Item, scr.SelItemNo);
        Assert.Equal(7, scr.ActiveWo!.WoId);
        Assert.Equal(1, scr.RawCount);
        Assert.Equal(1, scr.NgCount);
        Assert.Equal(2, scr.RawLots.Count);
    }

    [Fact]
    public void Screen_is_unavailable_for_another_station_or_without_a_copy()
    {
        Assert.Null(OfflineWork.BuildScreen(Line, Station, null, DateTime.Today));
        State();
        Assert.Null(OfflineWork.BuildScreen(Line, "ST-OTHER", null, DateTime.Today));
        Assert.NotNull(OfflineWork.BuildScreen(Line, Station, null, DateTime.Today));
    }

    [Fact]
    public void Copy_older_than_the_limit_is_not_used()
    {
        var s = State();
        s.TakenAtUtc = DateTime.UtcNow - OfflineAuth.MaxAge - TimeSpan.FromHours(1);
        OfflineStore.Save("work-state.bin", s);
        OfflineWork.ResetForTests();
        Assert.False(OfflineWork.HasData(Line, Station));
    }

    [Fact]
    public void Past_and_future_days_come_from_the_copy_without_local_effects()
    {
        var s = State();
        s.Summaries[DateTime.Today.AddDays(1).ToString("yyyy-MM-dd")] = new() { Row(Item, plan: 55) };
        OfflineStore.Save("work-state.bin", s);
        OfflineWork.ResetForTests();
        Assert.Equal(55, OfflineWork.Summary(DateTime.Today.AddDays(1)).Single().PlanQty);
        Assert.Empty(OfflineWork.Summary(DateTime.Today.AddDays(30)));
    }

    // 사본 만들 때와 같은 시계 — DbClock 은 설정 안 된 테스트 프로세스에서 호스트 시각이다.
    private static DateTime DbClock_Now() => AMES.Data.Services.DbClock.Now;
}
