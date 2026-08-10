using AMES.Contracts.Dto;
using AMES.Pop.Services;
using Xunit;

namespace AMES.Pop.Tests;

public class LabelDispatcherTests
{
    sealed class FakeSource : IInjLotClaimStore
    {
        public int MaxLotId = 100;
        public List<InjLotDto> NextClaim = new();
        public List<int> Claimed = new();
        public List<int> Released = new();
        public List<(int LotId, string Station)> ReleasedBy = new();
        public List<int> Incremented = new();
        public int LastAfterLotId = -1;
        public bool FailClaim = false;
        public bool FailMaxLotId = false;

        // 선점했던 LOT — 반납되면 다시 대기열로 돌아간다 (실제 DB 의 PrintClaimTS = NULL 효과).
        private readonly Dictionary<int, InjLotDto> _issued = new();

        public int GetMaxLotId(string lineId)
            => FailMaxLotId ? throw new InvalidOperationException("db down") : MaxLotId;

        public List<InjLotDto> ClaimForPrint(string lineId, int afterLotId, string stationId)
        {
            LastAfterLotId = afterLotId;
            if (FailClaim) throw new InvalidOperationException("db down");
            // 실제 저장소의 AND e.LotID > @After 를 흉내낸다 — 이게 없으면
            // 워터마크를 전진시키는 회귀를 이 테스트가 잡지 못한다.
            var batch = NextClaim.Where(l => l.LotId > afterLotId).ToList();
            NextClaim = NextClaim.Where(l => l.LotId <= afterLotId).ToList();
            foreach (var lot in batch) _issued[lot.LotId] = lot;
            Claimed.AddRange(batch.Select(l => l.LotId));
            return batch;
        }

        public void ReleasePrintClaim(int lotId, string stationId)
        {
            ReleasedBy.Add((lotId, stationId));
            Released.Add(lotId);
            if (_issued.TryGetValue(lotId, out var lot)) NextClaim.Add(lot);
        }

        public void IncrementPrintedCount(int lotId) => Incremented.Add(lotId);
    }

    sealed class FakeSink : ILabelSink
    {
        public List<int> Printed = new();
        public HashSet<int> FailFor = new();
        public void Print(InjLotDto lot)
        {
            if (FailFor.Contains(lot.LotId)) throw new IOException("printer offline");
            Printed.Add(lot.LotId);
        }
    }

    static InjLotDto Lot(int id, string status = "RAW") => new()
    {
        LotId = id, LotCode = $"L{id}", ItemNo = "83335-P8000RBQ",
        LineId = "LINE-INJ-01", ConfirmStatus = status, CreatedTS = new DateTime(2026, 8, 3),
    };

    static (LabelDispatcher D, FakeSource S, FakeSink K) Build(int maxFailures = 3)
    {
        var s = new FakeSource();
        var k = new FakeSink();
        var d = new LabelDispatcher(s, k, maxFailures, _ => { });
        d.Start("LINE-INJ-01", "POP-DEV-01");
        return (d, s, k);
    }

    [Fact]
    public void Start_captures_watermark_and_passes_it_as_claim_floor()
    {
        // LotID > @After 필터 자체는 DB 쪽이라 여기서 검증할 수 없다 — Task 10 수동 검증 담당.
        var (d, s, _) = Build();
        s.NextClaim.Add(Lot(101));
        d.Tick();
        Assert.Equal(100, s.LastAfterLotId);   // 시작 시점 MAX(LotID)
    }

    [Fact]
    public void Watermark_failure_is_retried_on_the_next_tick()
    {
        // 로그인 순간의 일시적 DB 장애가 교대 내내 발행을 죽이면 안 된다.
        var s = new FakeSource { FailMaxLotId = true };
        var k = new FakeSink();
        var d = new LabelDispatcher(s, k, maxFailures: 3, _ => { });
        d.Start("LINE-INJ-01", "POP-DEV-01");

        s.NextClaim.Add(Lot(101));
        d.Tick();                        // 워터마크 실패 → 아무것도 안 함
        Assert.Empty(k.Printed);
        Assert.Empty(s.Claimed);

        s.FailMaxLotId = false;          // DB 복구
        d.Tick();
        Assert.Equal(new[] { 101 }, k.Printed);
        Assert.Equal(100, s.LastAfterLotId);
    }

    [Fact]
    public void Watermark_is_captured_once_and_never_reacquired()
    {
        // 매 틱 다시 잡으면 그 사이 도착한 미출력분이 영구 제외된다 —
        // 워터마크는 세션 중 전진하지 않는다는 것이 이 클래스의 불변식.
        var (d, s, k) = Build();
        d.Tick();
        Assert.Equal(100, s.LastAfterLotId);

        s.MaxLotId = 500;                // 그 사이 라인 MAX(LotID) 가 전진했다
        s.NextClaim.Add(Lot(101));
        d.Tick();

        Assert.Equal(100, s.LastAfterLotId);
        Assert.Equal(new[] { 101 }, k.Printed);
    }

    [Fact]
    public void Successful_print_increments_count_and_keeps_claim()
    {
        var (d, s, k) = Build();
        s.NextClaim.Add(Lot(101));
        s.NextClaim.Add(Lot(102));
        d.Tick();

        Assert.Equal(new[] { 101, 102 }, k.Printed);
        Assert.Equal(new[] { 101, 102 }, s.Incremented);
        Assert.Empty(s.Released);
    }

    [Fact]
    public void Failed_print_releases_claim_and_does_not_increment()
    {
        var (d, s, k) = Build();
        k.FailFor.Add(101);
        s.NextClaim.Add(Lot(101));
        s.NextClaim.Add(Lot(102));
        d.Tick();

        Assert.Equal(new[] { 102 }, k.Printed);        // 102 는 계속 진행
        Assert.Equal(new[] { 102 }, s.Incremented);
        Assert.Equal(new[] { 101 }, s.Released);       // 101 만 반납
    }

    [Fact]
    public void Release_passes_own_station_for_ownership_check()
    {
        // 스테일 회수로 다른 터미널이 가져간 LOT 을 뒤늦은 실패가 풀어버리면
        // 그 터미널이 출력 중인 라벨이 재선점된다 — 반납은 소유자만.
        var (d, s, k) = Build();
        k.FailFor.Add(101);
        s.NextClaim.Add(Lot(101));
        d.Tick();

        Assert.Equal(new[] { (101, "POP-DEV-01") }, s.ReleasedBy);
    }

    [Fact]
    public void Ng_blocked_lots_are_printed_too()
    {
        // NG 포함 여부는 SQL 클레임 술어 소관 — 여기서는 디스패처가 상태로
        // 거르지 않는다는 것만 지킨다.
        var (d, s, k) = Build();
        s.NextClaim.Add(Lot(101, "NG_BLOCKED"));
        d.Tick();

        Assert.Equal(new[] { 101 }, k.Printed);
    }

    [Fact]
    public void Failed_lot_is_retried_on_the_next_tick()
    {
        // 이 태스크의 핵심 불변식. 배치에 성공분을 섞는 이유: 워터마크를
        // "성공한 LOT 까지만" 전진시키는 회귀가 가장 그럴듯한 모양인데,
        // 실패분만 있는 배치로는 그걸 잡지 못한다.
        var (d, s, k) = Build();
        k.FailFor.Add(101);
        s.NextClaim.Add(Lot(101));
        s.NextClaim.Add(Lot(102));

        d.Tick();                                      // 101 실패 → 반납, 102 성공
        Assert.Equal(new[] { 102 }, k.Printed);
        Assert.Equal(new[] { 101 }, s.Released);

        k.FailFor.Clear();                             // 프린터 복구
        d.Tick();                                      // 101 이 다시 잡혀 성공
        Assert.Equal(new[] { 102, 101 }, k.Printed);
        Assert.Equal(new[] { 102, 101 }, s.Incremented);
        Assert.Equal(100, s.LastAfterLotId);           // 워터마크는 그대로
    }

    [Fact]
    public void Stops_after_max_consecutive_failures()
    {
        var (d, s, k) = Build(maxFailures: 3);
        k.FailFor.Add(101); k.FailFor.Add(102); k.FailFor.Add(103);
        s.NextClaim.AddRange(new[] { Lot(101), Lot(102), Lot(103) });
        d.Tick();

        Assert.True(d.IsStopped);
        Assert.Equal(3, s.Released.Count);

        s.NextClaim.Add(Lot(104));      // 정지 후에는 클레임조차 하지 않는다
        d.Tick();
        Assert.Empty(k.Printed);
        Assert.DoesNotContain(104, s.Claimed);
    }

    [Fact]
    public void One_success_resets_the_failure_counter()
    {
        // maxFailures 는 실패 횟수와 같아야 한다 — 더 크면 리셋을 지워도 정지하지
        // 않아 테스트가 리셋을 검증하지 못한다.
        var (d, s, k) = Build(maxFailures: 2);
        k.FailFor.Add(101); k.FailFor.Add(103);
        s.NextClaim.AddRange(new[] { Lot(101), Lot(102), Lot(103) });   // 실패·성공·실패
        d.Tick();

        Assert.False(d.IsStopped);      // 연속 아님 → 정지하지 않는다
    }

    [Fact]
    public void Resume_request_is_consumed_by_the_next_tick()
    {
        var (d, s, k) = Build(maxFailures: 1);
        k.FailFor.Add(101);
        s.NextClaim.Add(Lot(101));
        d.Tick();
        Assert.True(d.IsStopped);

        d.Resume();
        Assert.True(d.IsStopped);       // 아직 — 진행 중인 틱과 경쟁하지 않으려고 미룬다

        k.FailFor.Clear();
        s.NextClaim.Add(Lot(102));
        d.Tick();                        // 여기서 소비된다
        Assert.False(d.IsStopped);
        // 101 은 반납되어 대기열로 돌아왔으므로 102 와 함께 다시 나간다.
        Assert.Equal(new[] { 101, 102 }, k.Printed);
    }

    [Fact]
    public void Resume_resets_the_failure_counter()
    {
        // 리셋이 없으면 Resume 후 단 한 번의 실패로 다시 멈춘다.
        var (d, s, k) = Build(maxFailures: 2);
        k.FailFor.Add(101); k.FailFor.Add(102);
        s.NextClaim.AddRange(new[] { Lot(101), Lot(102) });
        d.Tick();
        Assert.True(d.IsStopped);

        d.Resume();
        k.FailFor.Clear();
        k.FailFor.Add(101);              // 재개 후 1회만 실패
        d.Tick();
        Assert.False(d.IsStopped);       // 카운터가 리셋됐다면 1회로는 안 멈춘다
    }

    [Fact]
    public void Db_failure_does_not_stop_dispatch()
    {
        var (d, s, k) = Build(maxFailures: 1);
        s.FailClaim = true;
        d.Tick();
        Assert.False(d.IsStopped);      // 일시적 DB 장애는 정지 사유가 아니다

        s.FailClaim = false;
        s.NextClaim.Add(Lot(101));
        d.Tick();
        Assert.Equal(new[] { 101 }, k.Printed);
    }

    [Fact]
    public void Stop_clears_line_so_tick_does_nothing()
    {
        var (d, s, k) = Build();
        d.Stop();
        s.NextClaim.Add(Lot(101));
        d.Tick();
        Assert.Empty(k.Printed);
        Assert.Empty(s.Claimed);
    }

    [Fact]
    public void Stop_does_not_clear_the_failure_halt()
    {
        // Stop() 은 세션 종료, IsStopped 는 실패로 인한 자동 정지 — 서로 다른 개념이다.
        var (d, s, k) = Build(maxFailures: 1);
        k.FailFor.Add(101);
        s.NextClaim.Add(Lot(101));
        d.Tick();
        Assert.True(d.IsStopped);

        d.Stop();
        Assert.True(d.IsStopped);
    }

    [Fact]
    public void Stopping_abandons_the_rest_of_the_batch()
    {
        var (d, s, k) = Build(maxFailures: 1);
        var raised = 0;
        d.OnStopped += () => raised++;
        k.FailFor.Add(101); k.FailFor.Add(102);
        s.NextClaim.AddRange(new[] { Lot(101), Lot(102) });
        d.Tick();

        Assert.Equal(1, raised);
        Assert.Equal(new[] { 101 }, s.Released);   // 102 는 손대지 않았다
    }

    [Fact]
    public void Stopping_raises_event_once()
    {
        var (d, s, k) = Build(maxFailures: 1);
        var raised = 0;
        d.OnStopped += () => raised++;

        k.FailFor.Add(101);
        s.NextClaim.Add(Lot(101));
        d.Tick();
        d.Tick();                       // 이미 정지 — 다시 발생하지 않는다

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Tick_is_not_reentrant()
    {
        // 폴링 주기(1초) < TCP 타임아웃(2초) 이라 틱이 겹칠 수 있다.
        var s = new FakeSource();
        var k = new ReentrantSink();
        var d = new LabelDispatcher(s, k, maxFailures: 3, _ => { });
        d.Start("LINE-INJ-01", "POP-DEV-01");
        k.Dispatcher = d;
        k.Source = s;

        s.NextClaim.Add(Lot(101));
        d.Tick();

        Assert.Equal(1, k.PrintCalls);          // 중첩 호출이 한 번 더 뽑지 않았다
        Assert.Single(s.Claimed);               // 중첩 틱은 클레임도 하지 않았다
    }

    sealed class ReentrantSink : ILabelSink
    {
        public LabelDispatcher? Dispatcher;
        public FakeSource? Source;
        public int PrintCalls;
        public void Print(InjLotDto lot)
        {
            PrintCalls++;
            // 출력 중에 새 LOT 이 도착한 상황. 이걸 넣지 않으면 바깥 틱이 대기열을
            // 이미 비운 뒤라 중첩 틱은 게이트가 없어도 할 일이 없어 — 테스트가
            // 게이트를 검증하지 못하고 항상 통과한다.
            if (PrintCalls == 1) Source?.NextClaim.Add(Lot(102));
            Dispatcher?.Tick();                 // 출력 도중 다음 틱이 들어온 상황
        }
    }
}
