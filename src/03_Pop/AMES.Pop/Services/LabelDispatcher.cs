using AMES.Contracts.Dto;

namespace AMES.Pop.Services;

/// <summary>
/// 사출 라벨 자동 발행 루프. 타이머는 갖지 않는다 — 호출자가 Tick() 을 주기적으로
/// 부르고, 테스트는 시간을 기다리지 않고 직접 부른다.
///
/// 순서는 **카운트 선행**이다: 출력 전에 PrintedCount 를 올리고, 출력이 실패하면 되돌린 뒤 반납한다.
/// 카운트가 1 이상이면 클레임 쿼리(PrintedCount = 0)가 다시 잡지 않으므로, 출력 뒤 카운트 실패가
/// 만들던 "끊김 뒤 같은 LOT 재출력"이 생기지 않는다. 카운트가 안 올라가면 라벨도 나가지 않는다.
///
/// 이 PC 의 발행 원장(<see cref="ILabelLedger"/>)에 있는 LOT 은 다시 뽑지 않고 카운트만 맞춘다.
///
/// 워터마크는 Start() 에서 잡고 로그아웃 때 저장한다(<see cref="Stop"/>). 클레임 쿼리의 LotID > watermark 는
/// 하드 컷오프다 — 워터마크 이하 LOT 은 자동 발행 대상이 아니다(수동 재출력으로 복구).
/// </summary>
internal sealed class LabelDispatcher
{
    // 카운트 확정·되돌리기는 짧게 재시도한다. 총 지연이 틱 주기를 넘지 않도록 작게 유지 —
    // 재진입 게이트가 겹치는 틱을 버리기 때문이다.
    private const int CountAttempts         = 3;
    private const int CountRetryDelayMs     = 100;

    private readonly IInjLotClaimStore _source;
    private readonly ILabelSink        _sink;
    private readonly int               _maxFailures;
    private readonly Action<string>    _log;
    private readonly ILabelWatermarkStore? _watermarks;
    private readonly ILabelLedger?         _ledger;

    private string? _lineId;
    private string  _stationId = string.Empty;
    private string  _employeeNo = string.Empty;
    private int     _watermark;
    private int     _consecutiveFailures;
    private int     _running;            // 재진입 게이트 (0=유휴, 1=실행중)
    private int     _resumeRequested;    // Resume 요청 (0=없음, 1=대기)
    private int     _needsWatermark;     // 1 = 아직 못 잡음, 다음 틱에 재시도

    private volatile bool _isStopped;

    /// <summary>연속 실패로 자동 발행이 멈춘 상태. Resume() 요청으로만 해제된다.</summary>
    public bool IsStopped { get => _isStopped; private set => _isStopped = value; }

    /// <summary>자동 발행이 멈출 때 1회 발생. 배선 코드가 작업자에게 토스트로 알린다.</summary>
    public event Action? OnStopped;

    public LabelDispatcher(IInjLotClaimStore source, ILabelSink sink,
                           int maxFailures, Action<string> log,
                           ILabelWatermarkStore? watermarks = null,
                           ILabelLedger? ledger = null)
    {
        _source      = source;
        _sink        = sink;
        _maxFailures = maxFailures;
        _log         = log;
        _watermarks  = watermarks;
        _ledger      = ledger;
    }

    /// <param name="employeeNo">로그인 사번 — 발행 횟수 갱신의 ModifiedBy.</param>
    public void Start(string lineId, string stationId, string employeeNo)
    {
        _lineId              = lineId;
        _stationId           = stationId;
        _employeeNo          = employeeNo;
        _consecutiveFailures = 0;
        IsStopped            = false;

        // 저장된 워터마크가 있으면 그대로 잇는다 — 로그아웃 동안 생긴 LOT 부터 발행한다(B23).
        var saved = _watermarks?.Load(lineId);
        _watermark = saved ?? 0;
        Volatile.Write(ref _needsWatermark, saved is null ? 1 : 0);

        // 진행 중인 틱이 실패 경로를 끝내며 IsStopped 를 세우면 새 세션이 멈춘 채 시작된다.
        // 다음 틱이 이 요청을 소비해 되돌린다(Resume 과 같은 메커니즘).
        Interlocked.Exchange(ref _resumeRequested, 1);
    }

    /// <summary>
    /// 로그아웃. 현재 최대 LotID 를 라인 워터마크로 남긴다 — 다음 로그인 때 이 뒤에 생긴 LOT 이 발행 대상이다.
    /// 저장에 실패하면 다음 로그인은 기존처럼 로그인 시점 최대값으로 잡는다(과거분 폭주 방지를 택한 fallback).
    /// 로그아웃 직전에 미출력이었던 LOT 은 이 워터마크 아래라 자동 발행에서 빠지고 미확정 목록의 재출력으로 복구한다.
    /// </summary>
    public void Stop()
    {
        var line = _lineId;
        _lineId = null;
        if (line is null || _watermarks is null) return;
        // UI 스레드에서 도는 로그아웃이다 — DB 가 끊긴 것으로 알려져 있으면 접속 대기로 화면을 붙잡지 않는다.
        if (AMES.Data.Connection.DbHealth.IsDown) { _log("watermark not saved at logout: DB down"); return; }
        try { _watermarks.Save(line, _source.GetMaxLotId(line)); }
        catch (Exception ex) { _log($"watermark not saved at logout: {ex.Message}"); }
    }

    /// <summary>
    /// 작업자가 프린터를 고쳤다는 신호 (수동 재출력 성공) — 자동 발행 재개 요청.
    /// 여기서 바로 IsStopped 를 내리지 않는다: 진행 중인 틱이 실패 경로를 끝내며
    /// 다시 세우면 요청이 조용히 사라진다. 다음 틱이 이 플래그를 소비한다(≤1초).
    /// </summary>
    public void Resume() => Interlocked.Exchange(ref _resumeRequested, 1);

    public void Tick()
    {
        if (_lineId is null) return;

        // 폴링 주기(1초)보다 TCP 연결 타임아웃(2초)이 길어 틱이 겹칠 수 있다.
        if (Interlocked.Exchange(ref _running, 1) == 1) return;
        try
        {
            // Resume 요청은 틱 안에서 소비한다 — 바깥에서 IsStopped 를 내리면
            // 진행 중이던 틱의 실패 경로가 그 위에 다시 써버린다.
            if (Interlocked.Exchange(ref _resumeRequested, 0) == 1)
            {
                _consecutiveFailures = 0;
                IsStopped            = false;
            }
            if (IsStopped) return;

            // 세션 값을 틱 시작 시점에 고정한다. 로그아웃→재로그인이 진행 중인 틱과
            // 겹치면(프린터 타임아웃 2초) 아래 반납이 새 세션의 StationId 로 나가
            // ReleasePrintClaim 의 소유권 검증이 무력화된다.
            var lineId    = _lineId;
            var stationId  = _stationId;
            var employeeNo = _employeeNo;
            if (lineId is null) return;

            // 워터마크를 못 잡으면 과거분이 쏟아지므로 발행하지 않는다. 다만 여기서
            // 포기하면 로그인 순간의 일시적 DB 장애 한 번에 교대 내내 라벨이 안 나온다.
            if (Volatile.Read(ref _needsWatermark) == 1)
            {
                try
                {
                    _watermark = _source.GetMaxLotId(lineId);
                    Volatile.Write(ref _needsWatermark, 0);
                }
                catch (Exception ex)
                {
                    _log($"watermark init failed, retrying next tick: {ex.Message}");
                    return;
                }
            }
            var watermark = _watermark;

            List<InjLotDto> batch;
            try { batch = _source.ClaimForPrint(lineId, watermark, stationId); }
            catch (Exception ex)
            {
                // DB 일시 장애는 정지 사유가 아니다 — 다음 틱에 재시도.
                _log($"claim failed: {ex.Message}");
                return;
            }

            for (var i = 0; i < batch.Count; i++)
            {
                var lot = batch[i];

                // 이 PC 가 이미 뽑은 LOT — 라벨은 다시 뽑지 않고 카운트만 맞춘다.
                if (_ledger?.Contains(lot.LotCode) == true)
                {
                    if (TryCount(lot, employeeNo, "sync"))
                    {
                        _log($"already printed on this PC, count synced ({lot.LotCode})");
                        continue;
                    }
                    ReleaseFrom(batch, i, stationId);
                    return;
                }

                // 카운트 선행 — 올리지 못하면 출력하지 않는다. DB 장애는 프린터 실패로 세지 않는다.
                if (!TryCount(lot, employeeNo, "before print"))
                {
                    ReleaseFrom(batch, i, stationId);
                    _log($"count failed before print — label not printed, retry next tick ({lot.LotCode})");
                    return;
                }

                try { _sink.Print(lot); }
                catch (Exception ex)
                {
                    // 출력이 안 됐으니 카운트를 되돌린다 — 되돌리지 못하면 이 LOT 은 수동 재출력으로 복구한다.
                    if (!TryDecrement(lot, employeeNo))
                        _log($"print failed and count not reverted ({lot.LotCode}) — 수동 재출력이 필요하다");
                    ReleaseOne(lot, stationId);
                    _log($"label print failed ({lot.LotCode}): {ex.Message}");

                    if (++_consecutiveFailures >= _maxFailures)
                    {
                        ReleaseFrom(batch, i + 1, stationId);
                        IsStopped = true;
                        _log($"auto dispatch stopped after {_consecutiveFailures} consecutive failures");
                        OnStopped?.Invoke();
                        return;
                    }
                    continue;
                }

                _ledger?.Record(lot.LotCode);
                _consecutiveFailures = 0;
            }
        }
        finally { Interlocked.Exchange(ref _running, 0); }
    }

    /// <summary>카운트 +1 — 재시도 후에도 실패하면 false.</summary>
    private bool TryCount(InjLotDto lot, string employeeNo, string when)
    {
        for (var attempt = 1; attempt <= CountAttempts; attempt++)
        {
            try { _source.IncrementPrintedCount(lot.LotId, employeeNo); return true; }
            catch (Exception ex)
            {
                if (attempt == CountAttempts)
                {
                    _log($"count increment failed {when} after {attempt} attempts ({lot.LotCode}): {ex.Message}");
                    return false;
                }
                Thread.Sleep(CountRetryDelayMs);
            }
        }
        return false;
    }

    /// <summary>카운트 -1 (출력 실패 되돌리기) — 재시도 후에도 실패하면 false.</summary>
    private bool TryDecrement(InjLotDto lot, string employeeNo)
    {
        for (var attempt = 1; attempt <= CountAttempts; attempt++)
        {
            try { _source.DecrementPrintedCount(lot.LotId, employeeNo); return true; }
            catch (Exception ex)
            {
                if (attempt == CountAttempts)
                {
                    _log($"count decrement failed after {attempt} attempts ({lot.LotCode}): {ex.Message}");
                    return false;
                }
                Thread.Sleep(CountRetryDelayMs);
            }
        }
        return false;
    }

    private void ReleaseOne(InjLotDto lot, string stationId)
    {
        // 반납을 먼저 — 로그가 던져도 반납은 끝나 있어야 한다.
        try { _source.ReleasePrintClaim(lot.LotId, stationId); }
        catch (Exception rel) { _log($"claim release failed ({lot.LotCode}): {rel.Message}"); }
    }

    private void ReleaseFrom(List<InjLotDto> batch, int from, string stationId)
    {
        for (var j = from; j < batch.Count; j++) ReleaseOne(batch[j], stationId);
    }
}
