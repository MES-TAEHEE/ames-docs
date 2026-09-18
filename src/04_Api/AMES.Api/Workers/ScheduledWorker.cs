using System.Collections.Concurrent;
using AMES.Data.Connection;
using AMES.Data.Repositories;

namespace AMES.Api.Workers;

/// <summary>
/// 한 번의 계획: 실행 대상, 대상별 설정 오류, 스케줄러 전체 중지 여부(수동 실행은 막지 않는다),
/// 그리고 이 Worker 만의 틱 간격·시작 지연(공통코드 — null 이면 appsettings <c>ScheduledWorker</c> 공통 기본값).
/// </summary>
public sealed record ScheduledPlan<TTarget>(
    IReadOnlyList<TTarget> Targets,
    IReadOnlyList<TargetConfigError> Errors,
    bool Enabled,
    int? TickSec = null,
    int? StartupDelaySec = null);

public sealed record TargetConfigError(string Key, string Name, string Message);

/// <summary>
/// 외부 API 연동 Worker 공통 베이스 — API 마다 이 클래스를 상속한 Worker 를 하나씩 둔다.
/// 매 틱 계획을 다시 읽어(캐시 없음, 공통코드를 고치면 다음 틱부터 반영 — 틱 간격 자체도) 주기가 지난 대상만 순차 실행한다.
/// 마지막 실행 시각은 메모리라 Api 재시작 직후엔 모든 대상이 한 번 돈다(의도).
/// Worker 마다 타이머가 따로 돌고 Worker 간 실행 순서 조율은 없다.
/// </summary>
public abstract class ScheduledWorker<TTarget, TResult> : BackgroundService
{
    /// <summary>
    /// Api 에는 역할 모델이 없어 Bearer 세션이면 누구나 수동 실행을 부를 수 있다 —
    /// 반복 호출로 외부 시스템을 두드리는 걸 막는 최소 장치. 스케줄러 틱 실행도 이 시계를 채운다.
    /// </summary>
    public const int ManualCooldownSec = 60;

    private readonly IConfiguration _cfg;
    private readonly ConcurrentDictionary<string, DateTime>      _lastRun = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks   = new(StringComparer.OrdinalIgnoreCase);

    protected ScheduledWorker(AmesConnectionFactory factory, IConfiguration cfg, ILogger log)
    {
        Factory = factory;
        _cfg    = cfg;
        Log     = log;
    }

    protected AmesConnectionFactory Factory { get; }
    protected ILogger Log { get; }

    /// <summary>모니터 행 접두어(<c>{Code}-{키}</c>, <c>{Code}-CONFIG</c>). 접두어 + '-' + 키가 20자를 넘지 않게 Worker 가 키 길이를 제한한다.</summary>
    protected abstract string Code { get; }
    protected abstract string Name { get; }
    /// <summary>모니터 행 CreatedBy/ModifiedBy. 기본은 Code — 대상 실행기가 쓰는 기록자와 맞추려면 재정의한다.</summary>
    protected virtual string Actor => Code;
    protected abstract ScheduledPlan<TTarget> LoadPlan();
    protected abstract string KeyOf(TTarget target);
    /// <summary>0 이하면 그 대상은 스케줄러가 돌리지 않는다(수동 실행은 가능).</summary>
    protected abstract int IntervalMinOf(TTarget target);
    /// <summary>호출자 토큰 취소 외에는 예외를 내지 말고 실패 결과를 돌려준다 — 한 대상 실패가 다른 대상을 막으면 안 된다.</summary>
    protected abstract Task<TResult> RunTargetAsync(TTarget target, CancellationToken ct);
    protected abstract TResult RecordConfigError(TargetConfigError error);
    /// <summary>실행하지 않은 대상의 결과. reason 은 <c>"busy"</c> 또는 <c>"cooldown"</c>.</summary>
    protected abstract TResult Skipped(TTarget target, string reason);
    protected abstract bool IsOk(TResult result);

    protected virtual DateTime Now => DateTime.Now;

    protected virtual void LogResult(TTarget target, TResult result)
    {
        if (IsOk(result)) Log.LogInformation("{Code} {Key}: ok {@Result}", Code, KeyOf(target), result);
        else              Log.LogWarning("{Code} {Key} failed: {@Result}", Code, KeyOf(target), result);
    }

    /// <summary>
    /// SYS-Interfaces 가 유일한 운영 관제 화면이라 계획 로드 실패(DB 접속 등)도 거기 보여야 한다.
    /// 기록 실패는 삼킨다 — 호출자는 원래 예외를 그대로 받는다.
    /// </summary>
    protected virtual void RecordPlanLoadFailure(Exception ex)
    {
        try
        {
            new SysRepository(Factory).UpsertInterfaceMonitor(Code + "-CONFIG", Name + " 설정", "", 0,
                ok: false, recordCount: null, error: "설정 로드 실패: " + ex.GetType().Name + ": " + ex.Message, actor: Actor);
        }
        catch { /* 의도적 무시 */ }
    }

    internal TimeSpan TickOf(ScheduledPlan<TTarget>? plan)
        => TimeSpan.FromSeconds(ScheduledWorkerSettings.TickSec(plan?.TickSec, _cfg));

    internal TimeSpan StartupDelayOf(ScheduledPlan<TTarget>? plan)
        => TimeSpan.FromSeconds(ScheduledWorkerSettings.StartupDelaySec(plan?.StartupDelaySec, _cfg));

    /// <summary>
    /// 틱 간격이 공통코드라 고정 타이머를 쓰지 않고 틱이 끝날 때마다 그 틱에서 읽은 계획으로 다음 대기 시간을 정한다 —
    /// 틱 시작 간격은 (실행 시간 + 틱 간격)이다. 계획 로드가 실패하면 직전 계획의 값을 쓴다.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        ScheduledPlan<TTarget>? plan = null;
        // 시작 지연을 정하려고 한 번 읽을 뿐이다 — 실패 기록은 첫 틱이 한다.
        try { plan = LoadPlan(); }
        catch (Exception ex) { Log.LogWarning(ex, "{Code} startup plan load failed, using default timing", Code); }

        try { await Task.Delay(StartupDelayOf(plan), ct); } catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try { plan = await TickAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Log.LogError(ex, "{Code} tick failed", Code); }

            try { await Task.Delay(TickOf(plan), ct); } catch (OperationCanceledException) { break; }
        }
    }

    internal async Task<ScheduledPlan<TTarget>> TickAsync(CancellationToken ct)
    {
        var plan = LoadPlanRecordingFailure();
        foreach (var e in plan.Errors)
            RecordConfigError(e);

        if (!plan.Enabled) return plan;

        var now = Now;
        foreach (var t in plan.Targets)
        {
            var interval = IntervalMinOf(t);
            if (interval <= 0) continue;
            if (_lastRun.TryGetValue(KeyOf(t), out var last) && last.AddMinutes(interval) > now) continue;
            await RunOneAsync(t, ct, manual: false, throwIfBusy: false);
        }
        return plan;
    }

    /// <summary>
    /// 수동 실행 — 주기는 무시한다. key 가 null 이면 설정 오류 결과 + 모든 대상.
    /// 전체 실행은 바쁘거나 쿨다운 중인 대상을 결과에 busy/cooldown 으로 담고 나머지는 계속 돈다 —
    /// 한 대상이 틱과 겹쳤다고 이미 끝난 대상들의 결과까지 버리지 않는다.
    /// 단건 실행은 미등록 키에 KeyNotFoundException, 진행 중·쿨다운에 InvalidOperationException 을 던진다.
    /// </summary>
    public async Task<IReadOnlyList<TResult>> RunNowAsync(string? key, CancellationToken ct)
    {
        var plan    = LoadPlanRecordingFailure();
        var results = new List<TResult>();

        if (key is not null)
        {
            var err = plan.Errors.FirstOrDefault(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (err is not null) return [RecordConfigError(err)];

            var target = plan.Targets.FirstOrDefault(t => KeyOf(t).Equals(key, StringComparison.OrdinalIgnoreCase));
            if (target is null)
                throw new KeyNotFoundException($"{Code} 대상 '{key}' 가 없거나 비활성입니다.");

            results.Add(await RunOneAsync(target, ct, manual: true, throwIfBusy: true));
            return results;
        }

        foreach (var e in plan.Errors) results.Add(RecordConfigError(e));
        foreach (var t in plan.Targets) results.Add(await RunOneAsync(t, ct, manual: true, throwIfBusy: false));
        return results;
    }

    private async Task<TResult> RunOneAsync(TTarget target, CancellationToken ct, bool manual, bool throwIfBusy)
    {
        var key = KeyOf(target);

        if (manual && _lastRun.TryGetValue(key, out var lastRun)
            && (Now - lastRun).TotalSeconds < ManualCooldownSec)
        {
            if (throwIfBusy)
                throw new InvalidOperationException($"'{key}' 는 {ManualCooldownSec}초 이내에 실행됐습니다. 잠시 후 다시 시도하세요.");
            return Skipped(target, "cooldown");
        }

        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, ct))
        {
            if (throwIfBusy) throw new InvalidOperationException($"'{key}' 는 이미 실행 중입니다.");
            return Skipped(target, "busy");
        }
        try
        {
            _lastRun[key] = Now;
            var result = await RunTargetAsync(target, ct);
            LogResult(target, result);
            return result;
        }
        finally { gate.Release(); }
    }

    private ScheduledPlan<TTarget> LoadPlanRecordingFailure()
    {
        try { return LoadPlan(); }
        catch (Exception ex)
        {
            RecordPlanLoadFailure(ex);
            throw;
        }
    }
}
