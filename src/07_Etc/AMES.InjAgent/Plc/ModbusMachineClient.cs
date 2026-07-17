using System.Net.Sockets;
using AMES.InjAgent.Core;
using NModbus;

namespace AMES.InjAgent.Plc;

/// <summary>
/// 사출기 Modbus TCP 클라이언트 (NModbus). unit id 1 고정 (원본 동일).
/// 읽기 실패 시 연결을 버리고 예외를 올린다 → 폴러가 로그 후 다음 tick 에 재접속.
/// </summary>
public sealed class ModbusMachineClient : IInjectionMachine, IDisposable
{
    private const byte UnitId = 1;
    private const ushort ShotCountAddress = 5000;
    private const ushort MoldCodeAddress = 5330;

    private readonly string _ip;
    private readonly int _port;
    private readonly object _sync = new();
    private TcpClient? _tcp;
    private IModbusMaster? _master;
    private volatile bool _connected;
    private bool _disposed;
    private DateTime _lastConnectAttempt = DateTime.MinValue;

    public ModbusMachineClient(string ip, int port = 502)
    {
        _ip = ip;
        _port = port;
    }

    public bool Connected => _connected;

    public bool EnsureConnected()
    {
        lock (_sync)
        {
            if (_disposed) return false;
            if (_tcp?.Connected == true) return true;
            if ((DateTime.UtcNow - _lastConnectAttempt).TotalMilliseconds < 1000) return false; // 백오프
            _lastConnectAttempt = DateTime.UtcNow;
            CleanupLocked();
            var tcp = new TcpClient();
            try
            {
                if (!tcp.ConnectAsync(_ip, _port).Wait(1000)) { tcp.Close(); return false; }
                tcp.ReceiveTimeout = 1000;
                _master = new ModbusFactory().CreateMaster(tcp);
                _master.Transport.ReadTimeout = 1000;
                _master.Transport.WriteTimeout = 1000;
                _tcp = tcp;
                _connected = true;
                return true;
            }
            catch
            {
                try { tcp.Close(); } catch { }
                CleanupLocked();
                return false;
            }
        }
    }

    public long   ReadShotCount()      => PlcCodec.ToInt64(Read(ShotCountAddress, 4));
    public string ReadMoldCode()       => PlcCodec.ToAscii(Read(MoldCodeAddress, 6));
    public long   ReadLong(int addr)   => PlcCodec.ToInt64(Read((ushort)addr, 4));
    public float  ReadFloat(int addr)  => PlcCodec.ToFloat(Read((ushort)addr, 2));

    ushort[] Read(ushort address, ushort count)
    {
        lock (_sync)
        {
            if (_master is null) throw new InvalidOperationException("modbus not connected");
            try
            {
                return _master.ReadHoldingRegisters(UnitId, address, count);
            }
            catch
            {
                CleanupLocked();
                throw;
            }
        }
    }

    public void Disconnect() { lock (_sync) CleanupLocked(); }

    void CleanupLocked()
    {
        _connected = false;
        try { _master?.Dispose(); } catch { }
        try { _tcp?.Close(); } catch { }
        _master = null;
        _tcp = null;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            CleanupLocked();
        }
    }
}
