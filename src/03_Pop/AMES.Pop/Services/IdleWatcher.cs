namespace AMES.Pop.Services;

public enum IdleState
{
    /// <summary>유휴 감시가 꺼져 있음 (IdleLogoutMinutes ≤ 0).</summary>
    Disabled,
    Active,
    /// <summary>만료 직전 — 화면이 카운트다운 오버레이를 띄운다.</summary>
    Warning,
    Expired,
}

public readonly record struct IdleStatus(IdleState State, int RemainingSeconds);

/// <summary>
/// 마지막 활동 시각만 들고 상태를 계산하는 순수 로직. 타이머도 UI 도 DB 도 없다 —
/// 시각을 인자로 받으므로 경계값을 단위 테스트로 잡을 수 있다.
/// 시간 진행과 화면 전환은 AppRoot 가, 활동 통지는 AppRoot(DOM) 와 ScannerService(시리얼)가 맡는다.
/// </summary>
public sealed class IdleWatcher
{
    // Poke 는 시리얼 리더 스레드에서도 불린다 — UI 스레드의 Evaluate 와 겹치므로 Interlocked.
    private long _lastTicks;

    public IdleWatcher(TimeSpan timeout, TimeSpan warnBefore)
    {
        Timeout = timeout > TimeSpan.Zero ? timeout : TimeSpan.Zero;

        // 경고 창이 유휴 시간보다 길면 로그인하자마자 경고가 뜬다 — 절반으로 접는다.
        WarnBefore = warnBefore <= TimeSpan.Zero ? TimeSpan.Zero
                   : warnBefore >= Timeout      ? new TimeSpan(Timeout.Ticks / 2)
                   : warnBefore;
    }

    public TimeSpan Timeout    { get; }
    public TimeSpan WarnBefore { get; }

    public bool IsEnabled => Timeout > TimeSpan.Zero;

    public void Poke(DateTime now) => Interlocked.Exchange(ref _lastTicks, now.Ticks);

    public IdleStatus Evaluate(DateTime now)
    {
        if (!IsEnabled) return new IdleStatus(IdleState.Disabled, 0);

        var last = Interlocked.Read(ref _lastTicks);

        // Poke 없이 먼저 평가되면(배선 누락·세션 시작 직후) 즉시 만료시키지 않고 여기서 시계를 건다.
        // 로그아웃은 되돌리기 어렵고, 잘못된 즉시 만료는 작업자가 원인을 알 수 없다.
        if (last == 0)
        {
            Poke(now);
            return new IdleStatus(IdleState.Active, (int)Math.Ceiling(Timeout.TotalSeconds));
        }

        var remaining = Timeout - (now - new DateTime(last));
        if (remaining <= TimeSpan.Zero) return new IdleStatus(IdleState.Expired, 0);

        var state = remaining <= WarnBefore ? IdleState.Warning : IdleState.Active;
        return new IdleStatus(state, (int)Math.Ceiling(remaining.TotalSeconds));
    }
}
