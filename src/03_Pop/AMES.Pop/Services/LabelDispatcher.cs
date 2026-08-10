using AMES.Contracts.Dto;

namespace AMES.Pop.Services;

/// <summary>
/// 사출 라벨 자동 발행 루프. 타이머는 갖지 않는다 — 호출자가 Tick() 을 주기적으로
/// 부르고, 테스트는 시간을 기다리지 않고 직접 부른다.
///
/// 워터마크는 Start() 후 첫 성공 틱에서 한 번만 잡고 전진시키지 않는다. 전진시키면
/// 출력 실패로 반납한 LOT 이 영구 제외되어 재시도가 불가능해진다.
///
/// 클레임 쿼리의 LotID > watermark 는 **하드 컷오프**다 — 워터마크 이하 LOT 은
/// PrintedCount 가 0 이어도 자동 발행 대상이 아니다. 재시도 판정(PrintedCount = 0
/// AND PrintClaimTS IS NULL)은 워터마크 **위쪽** LOT 에만 적용된다.
///
/// 따라서 워터마크 획득이 DB 장애로 여러 틱 지연되면, 그 사이 생성된 LOT 은
/// 결국 잡힌 워터마크 아래로 들어가 자동 발행에서 빠진다. 의도된 트레이드오프다:
/// 세션 이전 미출력분과 같은 취급(수동 재출력)이고, 대안이었던 "획득 실패 시 세션
/// 포기"는 일시적 DB 장애 한 번에 교대 내내 라벨이 안 나오는 훨씬 나쁜 실패였다.
/// 해당 LOT 들은 미확정 목록에 🖨 버튼과 함께 그대로 남는다.
/// </summary>
internal sealed class LabelDispatcher
{
    // 카운트 확정은 놓치면 중복 라벨이 되므로 짧게 재시도한다. 총 지연이 틱 주기를
    // 넘지 않도록 작게 유지 — 재진입 게이트가 겹치는 틱을 버리기 때문이다.
    private const int IncrementAttempts     = 3;
    private const int IncrementRetryDelayMs = 100;

    private readonly IInjLotClaimStore _source;
    private readonly ILabelSink        _sink;
    private readonly int               _maxFailures;
    private readonly Action<string>    _log;

    private string? _lineId;
    private string  _stationId = string.Empty;
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
                           int maxFailures, Action<string> log)
    {
        _source      = source;
        _sink        = sink;
        _maxFailures = maxFailures;
        _log         = log;
    }

    public void Start(string lineId, string stationId)
    {
        _lineId              = lineId;
        _stationId           = stationId;
        _consecutiveFailures = 0;
        _watermark           = 0;
        IsStopped            = false;
        Volatile.Write(ref _needsWatermark, 1);

        // 진행 중인 틱이 실패 경로를 끝내며 IsStopped 를 세우면 새 세션이 멈춘 채 시작된다.
        // 다음 틱이 이 요청을 소비해 되돌린다(Resume 과 같은 메커니즘).
        Interlocked.Exchange(ref _resumeRequested, 1);
    }

    public void Stop() => _lineId = null;

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
            var stationId = _stationId;
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

            foreach (var lot in batch)
            {
                try { _sink.Print(lot); }
                catch (Exception ex)
                {
                    // 반납을 먼저 — _log 가 던지면 반납이 유실되고 배치 나머지가 중단된다.
                    try { _source.ReleasePrintClaim(lot.LotId, stationId); }
                    catch (Exception rel) { _log($"claim release failed ({lot.LotCode}): {rel.Message}"); }
                    _log($"label print failed ({lot.LotCode}): {ex.Message}");

                    if (++_consecutiveFailures >= _maxFailures)
                    {
                        IsStopped = true;
                        _log($"auto dispatch stopped after {_consecutiveFailures} consecutive failures");
                        OnStopped?.Invoke();
                        return;
                    }
                    continue;
                }

                // 라벨은 이미 나왔다 — 반납하면 다음 틱에 두 장째가 나오므로 반납하지 않는다.
                // DB 장애를 프린터 실패로 세지도 않는다.
                //
                // 주의: 여기서 실패하면 PrintedCount 가 0 으로 남고 클레임은 유지되므로,
                // staleSeconds 후 재선점되어 **같은 라벨이 한 장 더 나간다.** 재시도가 아니라
                // 중복이다. 짧은 재시도로 그 창을 좁힌다 — DB 가 계속 죽어 있으면 결국
                // 중복이 발생하고, 그건 라벨을 아예 잃는 것보다 낫다는 판단이다.
                TryIncrementPrintedCount(lot);
                _consecutiveFailures = 0;
            }
        }
        finally { Interlocked.Exchange(ref _running, 0); }
    }

    /// <summary>
    /// 출력 성공 후 카운트 확정. 실패하면 그 LOT 은 staleSeconds 후 재선점되어
    /// 중복 라벨이 되므로, 놓치는 비용이 큰 만큼 짧게 재시도한다.
    /// </summary>
    private void TryIncrementPrintedCount(InjLotDto lot)
    {
        for (var attempt = 1; attempt <= IncrementAttempts; attempt++)
        {
            try { _source.IncrementPrintedCount(lot.LotId); return; }
            catch (Exception ex)
            {
                if (attempt == IncrementAttempts)
                {
                    _log($"printed but count not incremented after {attempt} attempts " +
                         $"({lot.LotCode}): {ex.Message} — 이 LOT 은 스테일 회수로 한 장 더 나갈 수 있다");
                    return;
                }
                Thread.Sleep(IncrementRetryDelayMs);
            }
        }
    }
}
