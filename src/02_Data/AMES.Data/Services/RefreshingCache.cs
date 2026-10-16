namespace AMES.Data.Services;

/// <summary>
/// 화면이 기다리지 않는 조회 캐시(10-14). 한 번 읽은 값은 실패해도 버리지 않고, 기한이 지나면 지금 값을 바로 돌려준 채
/// 다시 읽기를 백그라운드에서 한 번만 돌린다. 성공하면 <c>ttl</c> 뒤, 실패하면 <c>retry</c> 뒤에 다시 읽고,
/// DB 장애 중(<c>isDown</c>)에는 시도하지 않는다. 첫 적재만 호출한 쪽에서 기다린다.
/// 전에는 실패도 5분 캐시해 이름 사전이 비었고, 장애 중에는 다시 읽기(접속 대기 30초)를 잠금 안에서 해 화면들이 함께 멈췄다.
/// 테스트: RefreshingCacheTests.
/// </summary>
public sealed class RefreshingCache<T> where T : class
{
    readonly Func<T?, T> _load;
    readonly TimeSpan _ttl, _retry;
    readonly Func<DateTime> _utcNow;
    readonly Func<bool> _isDown;
    readonly Action<Action> _background;
    readonly object _firstLoad = new();
    volatile T? _value;
    long _nextTicks;          // 다음 다시 읽기 시각(UTC ticks) — 0 이면 바로
    int _refreshing;

    /// <param name="load">새 값을 읽는다. 인자는 지금 값(없으면 null) — 일부만 실패했을 때 이전 값을 이어 쓰라고 넘긴다.</param>
    /// <param name="isDown">true 면 다시 읽기를 건너뛴다(예: <c>DbHealth.IsDown</c>).</param>
    /// <param name="background">다시 읽기를 돌릴 곳(기본 Task.Run). 테스트는 바로 실행을 넘긴다.</param>
    public RefreshingCache(Func<T?, T> load, TimeSpan ttl, TimeSpan retry,
        Func<bool>? isDown = null, Func<DateTime>? utcNow = null, Action<Action>? background = null)
    {
        _load = load; _ttl = ttl; _retry = retry;
        _isDown = isDown ?? (() => false);
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _background = background ?? (a => Task.Run(a));
    }

    /// <summary>지금 값 — 아직 한 번도 읽지 못했으면 null.</summary>
    public T? Peek => _value;

    /// <summary>값을 돌려주고, 기한이 지났으면 다시 읽기를 건다. 첫 적재만 여기서 기다린다.</summary>
    public T? Get()
    {
        var v = _value;
        if (v is null) return LoadFirst();
        if (Due() && !_isDown() && Interlocked.CompareExchange(ref _refreshing, 1, 0) == 0)
        {
            try { _background(Refresh); }
            catch { Interlocked.Exchange(ref _refreshing, 0); }
        }
        return v;
    }

    /// <summary>다음 Get 에서 다시 읽게 한다 — 값은 그대로 두므로 그동안 화면은 지금 값을 쓴다.</summary>
    public void Invalidate() => Interlocked.Exchange(ref _nextTicks, 0);

    bool Due() => _utcNow().Ticks >= Interlocked.Read(ref _nextTicks);

    T? LoadFirst()
    {
        if (!Due() || _isDown()) return null;
        lock (_firstLoad)
        {
            if (_value is { } loaded) return loaded;
            if (!Due()) return null;          // 앞선 호출이 방금 실패했다 — 재시도 시각까지 기다리지 않고 빈 값
            TryLoad();
            return _value;
        }
    }

    void Refresh()
    {
        try { TryLoad(); }
        finally { Interlocked.Exchange(ref _refreshing, 0); }
    }

    void TryLoad()
    {
        try
        {
            _value = _load(_value);
            Interlocked.Exchange(ref _nextTicks, (_utcNow() + _ttl).Ticks);
        }
        catch
        {
            Interlocked.Exchange(ref _nextTicks, (_utcNow() + _retry).Ticks);
        }
    }
}
