namespace AMES.InjAgent.Core;

/// <summary>
/// 호기 1대의 폴링 실행 수명주기. Start/Stop 은 UI 스레드에서 호출되며 멱등.
/// Stop 은 루프 취소를 최대 2초 기다린 뒤 상태를 초기화하고 소켓을 해제한다 (재시작 가능).
/// 2초 초과 시(소켓 타임아웃 중) 루프는 취소 플래그로 다음 tick 에 스스로 종료된다.
/// </summary>
public sealed class PollerRunner : IDisposable
{
    private readonly MachinePoller _poller;
    private readonly int _pollingMs;
    private readonly Action<string> _log;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private readonly object _gate = new();

    public PollerRunner(MachinePoller poller, int pollingMs, Action<string> log)
    {
        _poller = poller;
        _pollingMs = pollingMs;
        _log = log;
    }

    public MachinePoller Poller => _poller;
    public bool IsRunning => _loop is { IsCompleted: false };

    public void Start()
    {
        lock (_gate)
        {
            if (IsRunning) return;
            _poller.ResetForRestart();                 // fresh baseline — 정지 중 카운터 변화가 유령 샷이 되지 않게
            var cts = new CancellationTokenSource();
            _cts = cts;
            _loop = Task.Run(async () =>
            {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_pollingMs));
                try
                {
                    while (await timer.WaitForNextTickAsync(cts.Token).ConfigureAwait(false))
                    {
                        try { _poller.PollOnce(); }
                        catch (Exception ex) { _log($"[{_poller.Status.EquipId}] Polling error: {ex.Message}"); }
                    }
                }
                catch (OperationCanceledException) { /* 정상 종료 */ }
            });
        }
        _log($"[{_poller.Status.EquipId}] Polling started");
    }

    public void Stop()
    {
        CancellationTokenSource cts;
        Task? loop;
        lock (_gate)
        {
            if (_cts is null) return;
            cts = _cts;
            loop = _loop;
            _cts = null;
        }
        cts.Cancel();
        bool finished = true;
        try { finished = loop?.Wait(2000) ?? true; }
        catch (AggregateException) { finished = true; }

        if (finished)
        {
            lock (_gate)
            {
                if (ReferenceEquals(_loop, loop)) _loop = null;
            }
            cts.Dispose();
            ReleaseSockets();
            _log($"[{_poller.Status.EquipId}] Polling stopped — sockets released");
            return;
        }

        // 소켓 I/O 에 막힌 PollOnce 가 아직 끝나지 않았다. 도는 중에는 상태·소켓을 건드리지 않고,
        // 루프가 끝난 뒤에 정리한다(_loop 를 유지해 IsRunning=true 로 재시작을 막는다).
        loop!.ContinueWith(_ =>
        {
            lock (_gate)
            {
                cts.Dispose();
                if (!ReferenceEquals(_loop, loop)) return;   // 그사이 다시 시작했다 — 새 세션을 건드리지 않는다
                _loop = null;
                ReleaseSockets();
            }
        }, TaskScheduler.Default);
        _log($"[{_poller.Status.EquipId}] Stop requested — waiting for in-flight poll to finish (START available after it ends)");
    }

    void ReleaseSockets()
    {
        _poller.ResetForRestart();
        _poller.DisconnectClients();
    }

    public void Dispose()
    {
        Stop();
        _poller.Dispose();
    }
}
