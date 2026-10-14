using AMES.Data.Services;
using Xunit;

namespace AMES.Data.Tests;

// 10-14 — 화면이 기다리지 않는 조회 캐시(행위자 이름 사전): 실패해도 이전 값 유지·짧은 재시도·장애 중 건너뜀·백그라운드 한 번만
public class RefreshingCacheTests
{
    static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5), Retry = TimeSpan.FromSeconds(30);

    sealed class Rig
    {
        public DateTime Now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
        public bool Down;
        public Func<string?, string> Load = _ => "v1";
        public int Calls;
        public readonly List<Action> Pending = new();   // 백그라운드에 걸린 다시 읽기 — 테스트가 직접 실행
        public RefreshingCache<string> Make() => new(prev => { Calls++; return Load(prev); }, Ttl, Retry,
            isDown: () => Down, utcNow: () => Now, background: a => Pending.Add(a));
        public void RunPending() { var a = Pending.ToList(); Pending.Clear(); a.ForEach(x => x()); }
    }

    [Fact]
    public void First_get_loads_synchronously_and_then_serves_from_cache()
    {
        var r = new Rig(); var c = r.Make();
        Assert.Equal("v1", c.Get());
        r.Now += TimeSpan.FromMinutes(4);
        Assert.Equal("v1", c.Get());
        Assert.Equal(1, r.Calls);
        Assert.Empty(r.Pending);
    }

    [Fact]
    public void Failed_first_load_is_retried_after_30_seconds_not_5_minutes()
    {
        var r = new Rig { Load = _ => throw new InvalidOperationException("db") }; var c = r.Make();
        Assert.Null(c.Get());
        r.Now += TimeSpan.FromSeconds(10);
        Assert.Null(c.Get());
        Assert.Equal(1, r.Calls);                      // 재시도 시각 전에는 다시 부르지 않는다(매 화면마다 30초 대기 방지)

        r.Load = _ => "v1";
        r.Now += TimeSpan.FromSeconds(25);
        Assert.Equal("v1", c.Get());
        Assert.Equal(2, r.Calls);
    }

    [Fact]
    public void Stale_value_is_returned_immediately_and_refreshed_once_in_background()
    {
        var r = new Rig(); var c = r.Make();
        c.Get();
        r.Load = _ => "v2";
        r.Now += Ttl;

        Assert.Equal("v1", c.Get());                   // 기다리지 않고 지금 값
        Assert.Equal("v1", c.Get());
        Assert.Single(r.Pending);                      // 다시 읽기는 한 번만 걸린다
        r.RunPending();
        Assert.Equal("v2", c.Get());
        Assert.Equal(2, r.Calls);
    }

    [Fact]
    public void Failed_refresh_keeps_previous_value_and_retries_after_30_seconds()
    {
        var r = new Rig(); var c = r.Make();
        c.Get();
        r.Load = _ => throw new InvalidOperationException("db");
        r.Now += Ttl;
        c.Get(); r.RunPending();
        Assert.Equal("v1", c.Get());                   // 실패해도 이전 사전을 버리지 않는다
        Assert.Empty(r.Pending);

        r.Now += TimeSpan.FromSeconds(29);
        c.Get();
        Assert.Empty(r.Pending);
        r.Load = _ => "v2";
        r.Now += TimeSpan.FromSeconds(1);
        c.Get(); r.RunPending();
        Assert.Equal("v2", c.Get());
    }

    [Fact]
    public void No_attempt_while_the_database_is_down()
    {
        var r = new Rig { Down = true }; var c = r.Make();
        Assert.Null(c.Get());
        Assert.Equal(0, r.Calls);                      // 첫 적재도 장애 중에는 시도하지 않는다

        r.Down = false;
        Assert.Equal("v1", c.Get());
        r.Down = true;
        r.Now += Ttl;
        Assert.Equal("v1", c.Get());
        Assert.Empty(r.Pending);
        r.Down = false;
        c.Get();
        Assert.Single(r.Pending);
    }

    [Fact]
    public void Invalidate_keeps_the_value_and_refreshes_on_next_get()
    {
        var r = new Rig(); var c = r.Make();
        c.Get();
        r.Load = prev => prev + "+new";                // 이전 값을 넘겨받는다
        c.Invalidate();
        Assert.Equal("v1", c.Get());
        r.RunPending();
        Assert.Equal("v1+new", c.Get());
        Assert.Equal("v1+new", c.Peek);
    }

    [Fact]
    public void Concurrent_first_gets_load_once()
    {
        var calls = 0;
        var c = new RefreshingCache<string>(_ => { Interlocked.Increment(ref calls); Thread.Sleep(50); return "v"; }, Ttl, Retry);
        var results = new string?[8];
        Parallel.For(0, results.Length, i => results[i] = c.Get());
        Assert.All(results, v => Assert.Equal("v", v));
        Assert.Equal(1, calls);
    }
}
