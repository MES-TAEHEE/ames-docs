using System.Net;
using System.Net.Sockets;
using System.Text;
using AMES.InjAgent.Core;

namespace AMES.InjAgent.Plc;

/// <summary>
/// 취출로봇 FEnet 소켓 클라이언트. 원본 AxFEnet.cs 의 소켓/캐시 부분을
/// FEnetFrames(순수함수) 위에 얹은 것. 오류 시 소켓을 버리고
/// 다음 EnsureConnected 에서 재접속한다 (원본의 "예외 삼킴 후 미복구" 결함 개선).
/// </summary>
public sealed class FEnetClient : IRobotLink, IDisposable
{
    public const int BaseWord = 5000;
    public const int WordCount = 48;

    private readonly IPAddress _addr;
    private readonly int _port;
    private readonly object _sync = new();
    private Socket? _sock;
    private readonly ushort[] _block = new ushort[WordCount];
    private bool _blockValid;
    private volatile bool _connected;
    private bool _disposed;
    private DateTime _lastConnectAttempt = DateTime.MinValue;

    public string Model { get; private set; } = "XGB";
    public bool Connected => _connected;

    static FEnetClient() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public FEnetClient(string ip, int port = 2004)
    {
        _addr = IPAddress.Parse(ip);
        _port = port;
    }

    public bool EnsureConnected()
    {
        lock (_sync)
        {
            if (_disposed) return false;
            if (_sock?.Connected == true) return true;
            if ((DateTime.UtcNow - _lastConnectAttempt).TotalMilliseconds < 1000) return false; // 백오프
            _lastConnectAttempt = DateTime.UtcNow;
            CloseLocked();
            var sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                var task = sock.ConnectAsync(new IPEndPoint(_addr, _port));
                if (!task.Wait(3000) || !sock.Connected) { sock.Close(); return false; }
                sock.ReceiveTimeout = 3000;
                sock.SendTimeout = 3000;
                _sock = sock;
                if (!SelectCpuLocked()) { CloseLocked(); return false; }
                _connected = true;
                return true;
            }
            catch
            {
                try { sock.Close(); } catch { }
                CloseLocked();
                return false;
            }
        }
    }

    bool SelectCpuLocked()
    {
        var rsp = ExchangeLocked(FEnetFrames.BuildSelectCpu());
        if (rsp is null) return false;
        Model = FEnetFrames.IsXgtSelectResponse(rsp) ? "XGT" : "XGB";
        return true;
    }

    public bool RefreshBlock()
    {
        lock (_sync)
        {
            if (_sock?.Connected != true) { _blockValid = false; return false; }
            var frame = FEnetFrames.BuildRead(FEnetFrames.ContinuousDevice(BaseWord), WordCount * 2);
            var rsp = ExchangeLocked(frame);
            if (rsp is null || !FEnetFrames.TryParseReadResponse(rsp, out var data, out _)
                || data.Length < WordCount * 2)
            {
                _blockValid = false;
                CloseLocked();
                return false;
            }
            for (int i = 0; i < WordCount; i++)
                _block[i] = BitConverter.ToUInt16(data, i * 2);   // 응답 워드는 LE (원본 ReadBlock 동일)
            _blockValid = true;
            return true;
        }
    }

    public int ReadBit(int word, int bit)
    {
        lock (_sync)
        {
            int idx = word - BaseWord;
            if (!_blockValid || idx < 0 || idx >= WordCount || bit is < 0 or > 15) return -1;
            return (_block[idx] >> bit) & 1;
        }
    }

    public bool WriteBit(int point, bool on)
    {
        string device = Model == "XGB"
            ? FEnetFrames.BitDeviceXgb(BaseWord, point)
            : FEnetFrames.BitDeviceXgt(BaseWord, point);
        var frame = FEnetFrames.BuildWrite(device, FEnetFrames.DataType.Bit, new[] { (byte)(on ? 1 : 0) });
        return Write(frame);
    }

    public bool WriteString(int wordAddr, string value)
    {
        if (string.IsNullOrEmpty(value)) return true;    // 빈 값은 전송 생략 (원본 동일)
        var bytes = Encoding.GetEncoding(949).GetBytes(value);
        var frame = FEnetFrames.BuildWrite(FEnetFrames.ContinuousDevice(wordAddr), FEnetFrames.DataType.Continue, bytes);
        return Write(frame);
    }

    bool Write(byte[] frame)
    {
        lock (_sync)
        {
            if (_sock?.Connected != true) return false;
            var rsp = ExchangeLocked(frame);
            if (rsp is null || !FEnetFrames.TryParseWriteResponse(rsp, out _))
            {
                CloseLocked();
                return false;
            }
            return true;
        }
    }

    byte[]? ExchangeLocked(byte[] frame)
    {
        try
        {
            _sock!.Send(frame);
            var buffer = new byte[1024];
            int n = _sock.Receive(buffer);
            if (n <= 0) return null;
            return buffer[..n];
        }
        catch
        {
            return null;
        }
    }

    public void Disconnect() { lock (_sync) CloseLocked(); }

    void CloseLocked()
    {
        _connected = false;
        try { _sock?.Shutdown(SocketShutdown.Both); } catch { /* 소켓이 이미 죽은 경우 */ }
        try { _sock?.Close(); } catch { }
        _sock = null;
        _blockValid = false;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            CloseLocked();
        }
    }
}
