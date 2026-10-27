using AMES.Contracts.Dto;
using AMES.Data.Services;
using AMES.InjAgent.Core;
using Xunit;

namespace AMES.InjAgent.Tests;

public class QueuingInjAgentStoreTests : IDisposable
{
    static readonly DateTime Now = new(2026, 10, 27, 8, 0, 0);
    const string Line = "LINE-INJ-02";
    const string Header = "AARI2";   // 2026-10-27 · 라인 I2

    readonly string _dir = Path.Combine(Path.GetTempPath(), "ames-offline-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    OfflineStateFile FileUnderTest => new(Path.Combine(_dir, "offline.bin"));

    static MoldItemDto Map(string pos, int cavity, string item) => new()
    {
        MoldCode = "LQ2DTMDCBK", ColorCode = "BK", CavityNo = cavity, CavityPos = pos, ItemNo = item, MoldId = "M1",
    };

    QueuingInjAgentStore Build(FakeDb db, OfflineStateFile? file = null)
        => new(db, file ?? FileUnderTest, ex => ex is FakeDown, _ => { }, () => Now, downBackoff: TimeSpan.Zero);

    [Fact]
    public void Online_write_goes_straight_to_db_without_queue()
    {
        var db = new FakeDb();
        var store = Build(db);

        var (id, code) = store.CreateRawLot(Line, "EQ", Map("LH", 1, "ITEM-LH"), 55);

        Assert.True(id > 0);
        Assert.Single(db.Lots);
        Assert.Equal(0, store.PendingCount);
        Assert.Equal(code, db.Lots[0].Code);
    }

    [Fact]
    public void Down_shot_is_queued_with_block_number_and_temp_id()
    {
        var db = new FakeDb();
        var store = Build(db);
        store.Sync(new[] { Line });              // 온라인일 때 번호 구간 확보
        db.Down = true;

        var (id, code) = store.CreateRawLot(Line, "EQ", Map("LH", 1, "ITEM-LH"), 55);

        Assert.True(id < 0);                     // 임시 번호
        Assert.Equal(Header + "0001", code);     // 확보한 구간의 첫 번호
        Assert.Equal(1, store.PendingCount);
        Assert.Empty(db.Lots);                   // DB 에는 아무것도 없다
    }

    [Fact]
    public void Inspection_committed_before_connection_drop_is_not_saved_again_on_replay()
    {
        var db = new FakeDb();
        var store = Build(db);
        store.Sync(new[] { Line });
        var (lotId, _) = store.CreateRawLot(Line, "EQ", Map("LH", 1, "ITEM-LH"), 55);
        db.DropAfterSave = true;
        store.SaveInspection(lotId, "EQ", "LH", "OK", "OK", "OK", "OK", false);   // 커밋 후 끊김 → 큐
        Assert.Equal(1, store.PendingCount);

        store.Sync(new[] { Line });                                              // 복구 재생
        Assert.Single(db.Inspections);                                           // 한 번만 저장
        Assert.Equal(0, store.PendingCount);
    }

    [Fact]
    public void Inspection_queued_while_db_was_down_is_saved_once_on_replay()
    {
        var db = new FakeDb();
        var store = Build(db);
        store.Sync(new[] { Line });
        var (lotId, _) = store.CreateRawLot(Line, "EQ", Map("LH", 1, "ITEM-LH"), 55);
        db.Down = true;
        store.SaveInspection(lotId, "EQ", "LH", "OK", "OK", "OK", "OK", false);   // 저장 전에 끊김
        db.Down = false;
        store.Sync(new[] { Line });

        Assert.Single(db.Inspections);
        Assert.Equal(0, store.PendingCount);
    }

    [Fact]
    public void Replay_inserts_queued_lot_with_same_code_and_time_then_maps_inspection()
    {
        var db = new FakeDb();
        var store = Build(db);
        store.Sync(new[] { Line });
        db.Down = true;
        var (tempId, code) = store.CreateRawLot(Line, "EQ", Map("LH", 1, "ITEM-LH"), 55);
        store.SaveInspection(tempId, "EQ", "LH", "OK", "OK", "OK", "OK", false);
        Assert.Equal(2, store.PendingCount);

        db.Down = false;
        store.Sync(new[] { Line });

        Assert.Equal(0, store.PendingCount);
        var lot = Assert.Single(db.Lots);
        Assert.Equal(code, lot.Code);
        Assert.Equal(55, lot.Shot);
        Assert.Equal(Now, lot.At);
        Assert.Equal(lot.Id, Assert.Single(db.Inspections));   // 임시 번호가 실제 LotID 로 바뀌었다
    }

    [Fact]
    public void Replay_skips_lot_already_in_db_by_code()
    {
        var db = new FakeDb();
        var store = Build(db);
        store.Sync(new[] { Line });
        db.Down = true;
        store.CreateRawLot(Line, "EQ", Map("LH", 1, "ITEM-LH"), 55);

        // 이전 재생이 넣고 죽었다고 가정 — 같은 코드가 이미 있다
        db.Down = false;
        db.Lots.Add(new FakeLot(900, Header + "0001", 55, Now));
        store.Sync(new[] { Line });

        Assert.Equal(0, store.PendingCount);
        Assert.Single(db.Lots);                  // 두 번 넣지 않는다
    }

    [Fact]
    public void Queued_work_replays_before_new_online_write()
    {
        var db = new FakeDb();
        var store = Build(db);
        store.Sync(new[] { Line });
        db.Down = true;
        store.CreateRawLot(Line, "EQ", Map("LH", 1, "ITEM-LH"), 55);

        db.Down = false;
        store.CreateRawLot(Line, "EQ", Map("LH", 1, "ITEM-LH"), 56);   // 온라인 호출 — 밀린 55 가 먼저 들어간다

        Assert.Equal(new long[] { 55, 56 }, db.Lots.Select(l => l.Shot).ToArray());
        Assert.Equal(0, store.PendingCount);
    }

    [Fact]
    public void Without_block_the_shot_is_lost_with_clear_error()
    {
        var db = new FakeDb();
        var store = Build(db);                // 구간을 한 번도 받지 못함
        db.Down = true;

        var ex = Assert.Throws<InvalidOperationException>(() => store.CreateRawLot(Line, "EQ", Map("LH", 1, "ITEM-LH"), 55));
        Assert.Contains("구간", ex.Message);
        Assert.Equal(0, store.PendingCount);
    }

    [Fact]
    public void Queue_survives_restart_and_replays_in_new_process()
    {
        var file = FileUnderTest;
        var db = new FakeDb();
        var first = Build(db, file);
        first.Sync(new[] { Line });
        db.Down = true;
        first.CreateRawLot(Line, "EQ", Map("LH", 1, "ITEM-LH"), 55);

        db.Down = false;
        var second = Build(db, file);       // 재시작 — 디스크에서 다시 읽는다
        Assert.Equal(1, second.PendingCount);
        second.Sync(new[] { Line });

        Assert.Equal(0, second.PendingCount);
        Assert.Equal(Header + "0001", Assert.Single(db.Lots).Code);
    }

    [Fact]
    public void Sync_reserves_a_block_when_none_for_today()
    {
        var db = new FakeDb();
        var store = Build(db);

        store.Sync(new[] { Line });

        Assert.Single(db.Reserves);
        Assert.Equal(QueuingInjAgentStore.BlockSize, db.Reserves[0].Count);
    }

    // ── 가짜 DB ───────────────────────────────────────────────────────

    sealed class FakeDown : Exception { }

    sealed record FakeLot(int Id, string Code, long Shot, DateTime At);

    sealed class FakeDb : IInjAgentReplayStore
    {
        public bool Down;
        public readonly List<FakeLot> Lots = new();
        public readonly List<int> Inspections = new();
        public readonly List<LotNoGenerator.LotNoBlock> Reserves = new();
        int _nextId = 100;
        int _nextSeq = 1;

        void Check() { if (Down) throw new FakeDown(); }

        public List<MoldItemDto> GetMoldItems(string moldCode, string colorCode)
        {
            Check();
            return new() { Map("LH", 1, "ITEM-LH"), Map("RH", 2, "ITEM-RH") };
        }

        public (int LotId, string LotCode) CreateRawLot(string lineId, string equipId, MoldItemDto map, long machineShotCount)
        {
            Check();
            var code = Header + (_nextSeq++).ToString("D4");
            var lot = new FakeLot(++_nextId, code, machineShotCount, Now);
            Lots.Add(lot);
            return (lot.Id, lot.Code);
        }

        public (int LotId, string LotCode) CreateRawLotAt(string lineId, string equipId, MoldItemDto map, long machineShotCount,
                                                          string lotCode, DateTime occurredAt)
        {
            Check();
            var lot = new FakeLot(++_nextId, lotCode, machineShotCount, occurredAt);
            Lots.Add(lot);
            return (lot.Id, lot.Code);
        }

        public int? FindLotId(string lotCode)
        {
            Check();
            return Lots.FirstOrDefault(l => l.Code == lotCode)?.Id;
        }

        public LotNoGenerator.LotNoBlock ReserveLotNos(string lineId, DateTime date, int count)
        {
            Check();
            var block = new LotNoGenerator.LotNoBlock(Header, _nextSeq, count);
            _nextSeq += count;
            Reserves.Add(block);
            return block;
        }

        public bool DropAfterSave;   // 커밋은 됐는데 연결이 끊긴 것으로 응답 — 한 번만
        public void SaveInspection(int lotId, string equipId, string cavityPos,
                                   string shortMold, string weldLine, string gas, string weight, bool overallNg)
        {
            Check();
            Inspections.Add(lotId);
            if (DropAfterSave) { DropAfterSave = false; throw new FakeDown(); }
        }

        public void MarkNgBlocked(int lotId) { Check(); }

        public List<InjCondItemDto> GetCondItems(string lineId) { Check(); return new(); }

        public int CountInspections(int lotId, string cavityPos) { Check(); return Inspections.Count(x => x == lotId); }

        public void InsertCondLog(string lineId, string itemCode, long shotSeq, decimal? setValue, decimal? actualValue)
            => Check();
    }
}
