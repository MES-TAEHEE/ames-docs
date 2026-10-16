using System.IO.Ports;

namespace AMES.Pop.Services;

/// <summary>
/// COM 포트 하나를 백그라운드 스레드로 읽어 프레임마다 콜백한다.
/// 열기 실패·끊김은 reconnectMs 간격으로 무한 재시도 — 무인 터미널이라 사람이 재시작해 줄 수 없다.
///
/// 파라미터는 코드 고정이다. USB CDC 는 보레이트·패리티를 무시하므로 설정에 노출하면
/// "바꿔야 하는 값" 처럼 보여 현장이 헷갈린다. DTR/RTS 는 일부 CDC 드라이버가
/// 올라와야 데이터를 흘려보내므로 켠다.
/// </summary>
internal sealed class SerialScannerReader : IDisposable
{
    // 케이블이 뽑히면 Windows 의 SerialPort.Read 는 예외 없이 타임아웃만 반복하는 경우가
    // 있다. 타임아웃이 이만큼 이어지면 포트 목록에 아직 있는지 확인해 끊김을 잡아낸다.
    private const int ReadTimeoutMs       = 500;
    private const int TimeoutsBeforeProbe = 10;

    private readonly string         _portName;
    private readonly int            _reconnectMs;
    private readonly Action<string> _onFrame;
    private readonly Action<string> _log;
    private readonly CancellationTokenSource _cts = new();

    private Thread?       _thread;
    private volatile bool _connected;

    public bool IsConnected => _connected;

    /// <summary>연결 상태가 바뀔 때만. 리더 스레드에서 발생.</summary>
    public event Action<bool>? ConnectionChanged;

    public SerialScannerReader(string portName, int reconnectMs, Action<string> onFrame, Action<string> log)
    {
        _portName    = portName;
        _reconnectMs = reconnectMs;
        _onFrame     = onFrame;
        _log         = log;
    }

    public void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(Loop) { IsBackground = true, Name = "SerialScanner" };
        _thread.Start();
    }

    private void Loop()
    {
        var token = _cts.Token;
        var buf   = new byte[256];
        string? lastError = null;

        while (!token.IsCancellationRequested)
        {
            SerialPort? port = null;
            try
            {
                port = new SerialPort(_portName, 9600, Parity.None, 8, StopBits.One)
                {
                    Handshake   = Handshake.None,
                    DtrEnable   = true,
                    RtsEnable   = true,
                    ReadTimeout = ReadTimeoutMs,
                };
                port.Open();
                port.DiscardInBuffer();
                lastError = null;
                _log($"opened {_portName}");
                SetConnected(true);

                var parser   = new ScanFrameParser();
                var overflow = 0;
                var timeouts = 0;
                while (!token.IsCancellationRequested)
                {
                    int n;
                    try { n = port.Read(buf, 0, buf.Length); }
                    catch (TimeoutException)
                    {
                        if (++timeouts >= TimeoutsBeforeProbe)
                        {
                            timeouts = 0;
                            if (!PortExists()) throw new IOException($"{_portName} disappeared");
                        }
                        continue;
                    }
                    timeouts = 0;
                    if (n <= 0) continue;

                    foreach (var frame in parser.Feed(buf.AsSpan(0, n)))
                    {
                        try { _onFrame(frame); }
                        catch (Exception ex) { _log($"frame handler failed: {ex.Message}"); }
                    }
                    if (parser.OverflowCount != overflow)
                    {
                        overflow = parser.OverflowCount;
                        _log($"frame over {ScanFrameParser.MaxFrameBytes} bytes discarded (total {overflow})");
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 같은 오류를 재시도마다 찍으면 로그가 넘친다 — 메시지가 바뀔 때만.
                if (ex.Message != lastError)
                {
                    lastError = ex.Message;
                    _log($"{_portName}: {ex.Message}");
                }
            }
            finally
            {
                SetConnected(false);
                try { port?.Dispose(); } catch { /* 이미 끊긴 장치는 닫기도 실패한다 */ }
            }

            if (token.IsCancellationRequested) break;
            token.WaitHandle.WaitOne(_reconnectMs);
        }
    }

    private bool PortExists()
        => SerialPort.GetPortNames().Any(p => string.Equals(p, _portName, StringComparison.OrdinalIgnoreCase));

    private void SetConnected(bool on)
    {
        if (_connected == on) return;
        _connected = on;
        try { ConnectionChanged?.Invoke(on); }
        catch (Exception ex) { _log($"connection subscriber failed: {ex.Message}"); }
    }

    public void Dispose()
    {
        _cts.Cancel();
        // ReadTimeout 이 500ms 라 루프는 그 안에 취소를 본다. 종료를 오래 잡지 않는다.
        _thread?.Join(1500);
        _cts.Dispose();
    }
}
