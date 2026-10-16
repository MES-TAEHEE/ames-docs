using AMES.Pop.Services;
using Xunit;

namespace AMES.Pop.Tests;

public class IdleWatcherTests
{
    private static readonly DateTime T0 = new(2026, 9, 9, 8, 0, 0);

    private static IdleWatcher Watcher(int timeoutMin = 30, int warnSec = 60)
        => new(TimeSpan.FromMinutes(timeoutMin), TimeSpan.FromSeconds(warnSec));

    [Fact]
    public void Active_right_after_activity()
    {
        var w = Watcher();
        w.Poke(T0);

        var s = w.Evaluate(T0.AddMinutes(1));

        Assert.Equal(IdleState.Active, s.State);
    }

    [Fact]
    public void Warns_once_remaining_time_enters_the_warning_window()
    {
        var w = Watcher(timeoutMin: 30, warnSec: 60);
        w.Poke(T0);

        var s = w.Evaluate(T0.AddMinutes(29));

        Assert.Equal(IdleState.Warning, s.State);
        Assert.Equal(60, s.RemainingSeconds);
    }

    [Fact]
    public void Still_active_one_second_before_the_warning_window()
    {
        var w = Watcher(timeoutMin: 30, warnSec: 60);
        w.Poke(T0);

        Assert.Equal(IdleState.Active, w.Evaluate(T0.AddSeconds(29 * 60 - 1)).State);
    }

    [Fact]
    public void Remaining_seconds_count_down_during_the_warning()
    {
        var w = Watcher(timeoutMin: 30, warnSec: 60);
        w.Poke(T0);

        Assert.Equal(15, w.Evaluate(T0.AddSeconds(30 * 60 - 15)).RemainingSeconds);
    }

    [Fact]
    public void Remaining_seconds_round_up_so_the_countdown_never_shows_zero_early()
    {
        var w = Watcher(timeoutMin: 30, warnSec: 60);
        w.Poke(T0);

        Assert.Equal(1, w.Evaluate(T0.AddSeconds(30 * 60 - 0.4)).RemainingSeconds);
    }

    [Fact]
    public void Expires_when_the_timeout_elapses()
    {
        var w = Watcher();
        w.Poke(T0);

        var s = w.Evaluate(T0.AddMinutes(30));

        Assert.Equal(IdleState.Expired, s.State);
        Assert.Equal(0, s.RemainingSeconds);
    }

    [Fact]
    public void Poke_cancels_a_warning_in_progress()
    {
        var w = Watcher();
        w.Poke(T0);
        Assert.Equal(IdleState.Warning, w.Evaluate(T0.AddMinutes(29)).State);

        w.Poke(T0.AddMinutes(29));

        Assert.Equal(IdleState.Active, w.Evaluate(T0.AddMinutes(29.5)).State);
    }

    [Fact]
    public void Zero_timeout_disables_the_watcher()
    {
        var w = Watcher(timeoutMin: 0);
        w.Poke(T0);

        Assert.False(w.IsEnabled);
        Assert.Equal(IdleState.Disabled, w.Evaluate(T0.AddHours(9)).State);
    }

    [Fact]
    public void Warning_window_wider_than_the_timeout_is_clamped_to_half()
    {
        var w = new IdleWatcher(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5));

        Assert.Equal(TimeSpan.FromMinutes(1), w.WarnBefore);
    }

    [Fact]
    public void Negative_warning_window_goes_straight_from_active_to_expired()
    {
        var w = new IdleWatcher(TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(-5));
        w.Poke(T0);

        Assert.Equal(TimeSpan.Zero, w.WarnBefore);
        Assert.Equal(IdleState.Active, w.Evaluate(T0.AddSeconds(30 * 60 - 1)).State);
        Assert.Equal(IdleState.Expired, w.Evaluate(T0.AddMinutes(30)).State);
    }

    [Fact]
    public void Evaluate_before_any_activity_starts_the_clock_instead_of_expiring()
    {
        var w = Watcher();

        Assert.Equal(IdleState.Active, w.Evaluate(T0).State);
        Assert.Equal(IdleState.Expired, w.Evaluate(T0.AddMinutes(30)).State);
    }
}
