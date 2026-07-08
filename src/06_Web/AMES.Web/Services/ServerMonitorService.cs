using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using AMES.Data.Repositories;

namespace AMES.Web.Services;

/// <summary>
/// SYS-010 실시간 서버 정보. 호스트 CPU/메모리/디스크/네트워크는 Windows API를 직접 호출하고
/// (별도 NuGet 불필요), DB/REST API 상태는 실제 핑으로 측정한다. 상태는 스냅샷 간 델타로
/// 계산하므로 싱글턴으로 등록해 이전 샘플을 보관해야 한다.
/// </summary>
public sealed class ServerMonitorService
{
    private readonly SysRepository _sys;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;

    private (long Idle, long Kernel, long User)? _prevCpu;
    private (long Bytes, DateTime At)? _prevNet;

    public ServerMonitorService(SysRepository sys, IHttpClientFactory httpFactory, IConfiguration config)
    {
        _sys = sys;
        _httpFactory = httpFactory;
        _config = config;
    }

    public sealed record ServiceStatusRow(string Name, string Host, string Status, int ResponseMs);

    public sealed record ServerSnapshot(
        int CpuPct, int Cores,
        int MemPct, long MemUsedGb, long MemTotalGb,
        int DiskPct, long DiskUsedGb, long DiskTotalGb,
        int NetPct, double NetMbps, int NetSpeedMbps,
        List<ServiceStatusRow> Services);

    public async Task<ServerSnapshot> GetSnapshotAsync()
    {
        var (cpuPct, cores) = SampleCpu();
        var (memPct, memUsedGb, memTotalGb) = SampleMemory();
        var (diskPct, diskUsedGb, diskTotalGb) = SampleDisk();
        var (netPct, netMbps, netSpeedMbps) = SampleNetwork();
        var services = await BuildServicesAsync();

        return new ServerSnapshot(
            cpuPct, cores,
            memPct, memUsedGb, memTotalGb,
            diskPct, diskUsedGb, diskTotalGb,
            netPct, netMbps, netSpeedMbps,
            services);
    }

    private async Task<List<ServiceStatusRow>> BuildServicesAsync()
    {
        var list = new List<ServiceStatusRow>
        {
            new("Web Portal (AMES.Web)", Environment.MachineName, "OK", 0)
        };

        var (dbOk, dbMs, dbHost) = _sys.PingDatabase();
        list.Add(new("Database (SQL Server)", dbHost ?? "-", dbOk ? "OK" : "DOWN", (int)dbMs));

        var apiBase = _config["Services:ApiBaseUrl"];
        if (string.IsNullOrWhiteSpace(apiBase))
        {
            list.Add(new("REST API (AMES.Api)", "-", "WARN", 0));
        }
        else
        {
            var (apiOk, apiMs) = await PingApiAsync(apiBase);
            var host = Uri.TryCreate(apiBase, UriKind.Absolute, out var u) ? u.Host : apiBase;
            list.Add(new("REST API (AMES.Api)", host, apiOk ? "OK" : "DOWN", apiMs));
        }

        var k = _sys.GetHealth();
        var ifTotal = k.InterfacesOk + k.InterfacesDown;
        list.Add(new($"External Interfaces ({k.InterfacesOk}/{ifTotal})", "-",
            k.InterfacesDown == 0 ? "OK" : "WARN", 0));
        list.Add(new("Notification Delivery", "-",
            k.NotifFailedLast24h == 0 ? "OK" : "WARN", 0));

        return list;
    }

    private async Task<(bool Ok, int Ms)> PingApiAsync(string baseUrl)
    {
        try
        {
            var client = _httpFactory.CreateClient();
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(2);
            var sw = Stopwatch.StartNew();
            var resp = await client.GetAsync("/api/health");
            sw.Stop();
            return (resp.IsSuccessStatusCode, (int)sw.ElapsedMilliseconds);
        }
        catch
        {
            return (false, 0);
        }
    }

    // ── Host metrics (Windows) ──────────────────────────────────────────
    private (int Pct, int Cores) SampleCpu()
    {
        var cores = Environment.ProcessorCount;
        if (!OperatingSystem.IsWindows() || !GetSystemTimes(out var idleFt, out var kernelFt, out var userFt))
            return (-1, cores);

        var idle = ToLong(idleFt);
        var kernel = ToLong(kernelFt);
        var user = ToLong(userFt);

        if (_prevCpu is { } prev)
        {
            var idleDelta = idle - prev.Idle;
            var totalDelta = (kernel - prev.Kernel) + (user - prev.User);
            _prevCpu = (idle, kernel, user);
            if (totalDelta <= 0) return (0, cores);
            var busy = totalDelta - idleDelta;
            return ((int)Math.Clamp(Math.Round(100.0 * busy / totalDelta), 0, 100), cores);
        }

        _prevCpu = (idle, kernel, user);
        return (0, cores);
    }

    private static (int Pct, long UsedGb, long TotalGb) SampleMemory()
    {
        if (!OperatingSystem.IsWindows())
            return (-1, 0, 0);

        var stat = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref stat))
            return (-1, 0, 0);

        var totalGb = (long)(stat.ullTotalPhys / (1024 * 1024 * 1024));
        var availGb = (long)(stat.ullAvailPhys / (1024 * 1024 * 1024));
        return ((int)stat.dwMemoryLoad, totalGb - availGb, totalGb);
    }

    private static (int Pct, long UsedGb, long TotalGb) SampleDisk()
    {
        try
        {
            var root = Path.GetPathRoot(AppContext.BaseDirectory) ?? Path.GetPathRoot(Environment.CurrentDirectory);
            if (string.IsNullOrEmpty(root)) return (0, 0, 0);
            var drive = new DriveInfo(root);
            var totalGb = drive.TotalSize / (1024L * 1024 * 1024);
            var freeGb = drive.TotalFreeSpace / (1024L * 1024 * 1024);
            var usedGb = totalGb - freeGb;
            var pct = totalGb > 0 ? (int)Math.Round(100.0 * usedGb / totalGb) : 0;
            return (pct, usedGb, totalGb);
        }
        catch
        {
            return (0, 0, 0);
        }
    }

    private (int Pct, double Mbps, int SpeedMbps) SampleNetwork()
    {
        var nic = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                     && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .OrderByDescending(n => n.Speed)
            .FirstOrDefault();
        if (nic is null) return (0, 0, 0);

        var stats = nic.GetIPv4Statistics();
        var bytes = stats.BytesSent + stats.BytesReceived;
        var now = DateTime.UtcNow;
        double mbps = 0;
        if (_prevNet is { } prev)
        {
            var elapsedSec = (now - prev.At).TotalSeconds;
            if (elapsedSec > 0)
                mbps = (bytes - prev.Bytes) * 8.0 / 1_000_000.0 / elapsedSec;
        }
        _prevNet = (bytes, now);

        var speedMbps = (int)(nic.Speed / 1_000_000);
        var pct = speedMbps > 0 ? (int)Math.Clamp(Math.Round(100.0 * mbps / speedMbps), 0, 100) : 0;
        return (pct, Math.Max(0, mbps), speedMbps);
    }

    private static long ToLong(FILETIME ft) => ((long)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}
