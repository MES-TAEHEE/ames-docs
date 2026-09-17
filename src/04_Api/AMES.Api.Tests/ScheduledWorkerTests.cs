using AMES.Api.Workers;
using AMES.Data.Connection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AMES.Api.Tests;

/// <summary>ScheduledWorker 베이스의 스케줄 규칙 — 모든 API Worker 가 공유하므로 여기서 정본으로 고정한다.</summary>
public class ScheduledWorkerTests
{
    public sealed record FakeTarget(string Key, int IntervalMin);
    public sealed record FakeResult(string Key, bool Ok, string? Reason);

    public sealed class FakeWorker : ScheduledWorker<FakeTarget, FakeResult>
    {
        public FakeWorker(IConfiguration? cfg = null) : base(new AmesConnectionFactory("Server=unused"),
            cfg ?? new ConfigurationBuilder().Build(), NullLogger.Instance) { }

        public ScheduledPlan<FakeTarget> Plan = new([], [], true);
        public Exception? PlanFailure;
        public DateTime Clock = new(2026, 9, 17, 9, 0, 0);
        public readonly List<string> Runs = [];
        public readonly List<string> ConfigErrorsRecorded = [];
        public readonly List<Exception> PlanFailuresRecorded = [];
        public Func<FakeTarget, CancellationToken, Task>? OnRun;

        protected override string Code => "FAKE";
        protected override string Name => "Fake";
        protected override DateTime Now => Clock;

        protected override ScheduledPlan<FakeTarget> LoadPlan()
            => PlanFailure is null ? Plan : throw PlanFailure;

        protected override string KeyOf(FakeTarget t) => t.Key;
        protected override int IntervalMinOf(FakeTarget t) => t.IntervalMin;

        protected override async Task<FakeResult> RunTargetAsync(FakeTarget t, CancellationToken ct)
        {
            Runs.Add(t.Key);
            if (OnRun is not null) await OnRun(t, ct);
            return new FakeResult(t.Key, true, null);
        }

        protected override FakeResult RecordConfigError(TargetConfigError e)
        {
            ConfigErrorsRecorded.Add(e.Key);
            return new FakeResult(e.Key, false, "config: " + e.Message);
        }

        protected override FakeResult Skipped(FakeTarget t, string reason) => new(t.Key, false, reason);
        protected override bool IsOk(FakeResult r) => r.Ok;
        protected override void RecordPlanLoadFailure(Exception ex) => PlanFailuresRecorded.Add(ex);

        public Task Tick() => TickAsync(CancellationToken.None);
    }

    static FakeWorker WithTargets(params FakeTarget[] targets)
        => new() { Plan = new ScheduledPlan<FakeTarget>(targets, [], true) };

    [Fact]
    public async Task Tick_runs_every_due_target_once_then_waits_for_its_interval()
    {
        var w = WithTargets(new FakeTarget("A", 30), new FakeTarget("B", 60));

        await w.Tick();
        Assert.Equal(["A", "B"], w.Runs);

        w.Clock = w.Clock.AddMinutes(29);
        await w.Tick();
        Assert.Equal(2, w.Runs.Count);

        w.Clock = w.Clock.AddMinutes(1);
        await w.Tick();
        Assert.Equal(["A", "B", "A"], w.Runs);

        w.Clock = w.Clock.AddMinutes(30);
        await w.Tick();
        Assert.Equal(["A", "B", "A", "A", "B"], w.Runs);
    }

    [Fact]
    public async Task Tick_skips_a_target_whose_interval_is_zero_but_manual_run_still_works()
    {
        var w = WithTargets(new FakeTarget("OFF", 0));

        await w.Tick();
        Assert.Empty(w.Runs);

        var r = Assert.Single(await w.RunNowAsync("OFF", CancellationToken.None));
        Assert.True(r.Ok);
        Assert.Equal(["OFF"], w.Runs);
    }

    [Fact]
    public async Task Disabled_plan_stops_the_scheduler_but_still_records_config_errors_and_allows_manual_runs()
    {
        var w = new FakeWorker
        {
            Plan = new ScheduledPlan<FakeTarget>([new("A", 30)], [new TargetConfigError("BAD", "Bad", "no url")], Enabled: false),
        };

        await w.Tick();
        Assert.Empty(w.Runs);
        Assert.Equal(["BAD"], w.ConfigErrorsRecorded);

        await w.RunNowAsync("A", CancellationToken.None);
        Assert.Equal(["A"], w.Runs);
    }

    [Fact]
    public async Task Config_errors_are_recorded_every_tick_and_reported_by_manual_runs()
    {
        var w = new FakeWorker
        {
            Plan = new ScheduledPlan<FakeTarget>([new("A", 30)], [new TargetConfigError("BAD", "Bad", "no url")], true),
        };

        await w.Tick();
        await w.Tick();
        Assert.Equal(["BAD", "BAD"], w.ConfigErrorsRecorded);

        var single = Assert.Single(await w.RunNowAsync("bad", CancellationToken.None));
        Assert.Equal("config: no url", single.Reason);
        Assert.Single(w.Runs);   // 틱이 A 를 한 번 돌렸을 뿐, 설정 오류 키는 아무것도 실행하지 않는다

        w.Clock = w.Clock.AddMinutes(5);
        var all = await w.RunNowAsync(null, CancellationToken.None);
        Assert.Equal(["BAD", "A"], all.Select(r => r.Key));
    }

    [Fact]
    public async Task Manual_run_of_an_unknown_key_throws_KeyNotFound()
    {
        var w = WithTargets(new FakeTarget("A", 30));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => w.RunNowAsync("NOPE", CancellationToken.None));
    }

    [Fact]
    public async Task Manual_runs_within_the_cooldown_are_refused_single_throws_and_all_reports_cooldown()
    {
        var w = WithTargets(new FakeTarget("A", 30));
        await w.RunNowAsync("A", CancellationToken.None);

        w.Clock = w.Clock.AddSeconds(ScheduledWorker<FakeTarget, FakeResult>.ManualCooldownSec - 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => w.RunNowAsync("A", CancellationToken.None));
        Assert.Equal("cooldown", Assert.Single(await w.RunNowAsync(null, CancellationToken.None)).Reason);
        Assert.Single(w.Runs);

        w.Clock = w.Clock.AddSeconds(1);
        Assert.True(Assert.Single(await w.RunNowAsync("A", CancellationToken.None)).Ok);
        Assert.Equal(2, w.Runs.Count);
    }

    [Fact]
    public async Task Cooldown_does_not_apply_to_the_scheduler_tick()
    {
        var w = WithTargets(new FakeTarget("A", 1));
        await w.RunNowAsync("A", CancellationToken.None);

        w.Clock = w.Clock.AddMinutes(1);   // 주기는 지났고, 틱은 수동 쿨다운을 보지 않는다
        await w.Tick();
        Assert.Equal(2, w.Runs.Count);
    }

    [Fact]
    public async Task A_target_held_by_a_running_tick_is_busy_single_throws_and_all_reports_busy()
    {
        var w = WithTargets(new FakeTarget("A", 30));
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        w.OnRun = async (_, _) => { started.SetResult(); await release.Task; };

        var tick = w.Tick();
        await started.Task;
        w.Clock = w.Clock.AddSeconds(ScheduledWorker<FakeTarget, FakeResult>.ManualCooldownSec + 1);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => w.RunNowAsync("A", CancellationToken.None));
        Assert.Contains("A", ex.Message);
        Assert.Equal("busy", Assert.Single(await w.RunNowAsync(null, CancellationToken.None)).Reason);

        release.SetResult();
        await tick;
        Assert.Single(w.Runs);
    }

    [Fact]
    public async Task Plan_load_failure_is_recorded_and_rethrown_from_tick_and_manual_run()
    {
        var boom = new InvalidOperationException("db down");
        var w = new FakeWorker { PlanFailure = boom };

        Assert.Same(boom, await Assert.ThrowsAsync<InvalidOperationException>(() => w.Tick()));
        Assert.Same(boom, await Assert.ThrowsAsync<InvalidOperationException>(() => w.RunNowAsync(null, CancellationToken.None)));
        Assert.Equal([boom, boom], w.PlanFailuresRecorded);
    }

    [Fact]
    public async Task Lock_is_released_when_a_run_is_cancelled()
    {
        var w = WithTargets(new FakeTarget("A", 30));
        using var cts = new CancellationTokenSource();
        w.OnRun = (_, ct) => { cts.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => w.RunNowAsync("A", cts.Token));

        w.OnRun = null;
        w.Clock = w.Clock.AddSeconds(ScheduledWorker<FakeTarget, FakeResult>.ManualCooldownSec + 1);
        Assert.True(Assert.Single(await w.RunNowAsync("A", CancellationToken.None)).Ok);
    }

    static IConfiguration Cfg(params (string Key, string Value)[] kv)
        => new ConfigurationBuilder().AddInMemoryCollection(kv.Select(x => new KeyValuePair<string, string?>(x.Key, x.Value))).Build();

    static ScheduledPlan<FakeTarget> Timing(int? tick, int? delay) => new([], [], true, tick, delay);

    [Fact]
    public void Timing_uses_built_in_defaults_without_settings()
    {
        var w = new FakeWorker();
        Assert.Equal(TimeSpan.FromSeconds(ScheduledWorkerSettings.DefaultTickSec), w.TickOf(null));
        Assert.Equal(TimeSpan.FromSeconds(ScheduledWorkerSettings.DefaultStartupDelaySec), w.StartupDelayOf(null));
        Assert.Equal(TimeSpan.FromSeconds(60), w.TickOf(Timing(null, null)));
    }

    [Fact]
    public void Timing_uses_appsettings_common_defaults()
    {
        var w = new FakeWorker(Cfg(("ScheduledWorker:TickSec", "15"), ("ScheduledWorker:StartupDelaySec", "0")));
        Assert.Equal(TimeSpan.FromSeconds(15), w.TickOf(Timing(null, null)));
        Assert.Equal(TimeSpan.Zero, w.StartupDelayOf(Timing(null, null)));
    }

    [Fact]
    public void Timing_worker_value_from_the_plan_overrides_the_common_default()
    {
        var w = new FakeWorker(Cfg(("ScheduledWorker:TickSec", "15"), ("ScheduledWorker:StartupDelaySec", "5")));
        Assert.Equal(TimeSpan.FromSeconds(120), w.TickOf(Timing(120, 30)));
        Assert.Equal(TimeSpan.FromSeconds(30), w.StartupDelayOf(Timing(120, 30)));
    }

    [Fact]
    public void Timing_ignores_out_of_range_values_and_falls_back()
    {
        var w = new FakeWorker(Cfg(("ScheduledWorker:TickSec", "0"), ("ScheduledWorker:StartupDelaySec", "-1")));
        Assert.Equal(TimeSpan.FromSeconds(60), w.TickOf(Timing(0, -5)));
        Assert.Equal(TimeSpan.FromSeconds(10), w.StartupDelayOf(Timing(0, -5)));

        w = new FakeWorker(Cfg(("ScheduledWorker:TickSec", "20")));
        Assert.Equal(TimeSpan.FromSeconds(20), w.TickOf(Timing(-1, null)));
    }

    [Theory]
    [InlineData(null, null, 60)]
    [InlineData(null, "90", 90)]
    [InlineData(30, "90", 30)]
    [InlineData(0, "90", 90)]
    [InlineData(null, "0", 60)]
    [InlineData(null, "abc", 60)]
    public void Timeout_worker_value_then_common_default_then_built_in(int? worker, string? common, int expected)
    {
        var cfg = common is null ? Cfg() : Cfg(("ScheduledWorker:TimeoutSec", common));
        Assert.Equal(expected, ScheduledWorkerSettings.TimeoutSec(worker, cfg));
    }

    [Fact]
    public async Task Tick_returns_the_plan_it_loaded_so_the_next_delay_follows_it()
    {
        var w = new FakeWorker { Plan = Timing(7, null) };
        var plan = await w.TickAsync(CancellationToken.None);
        Assert.Equal(TimeSpan.FromSeconds(7), w.TickOf(plan));
    }
}
