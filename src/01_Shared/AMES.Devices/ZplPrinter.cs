using System.Net.Sockets;
using System.Text;

namespace AMES.Devices;

public sealed class ZplPrinterOptions
{
    /// <summary>"Tcp"(Zebra 네트워크 프린터) · "Spooler"(Windows 프린터 큐 — USB 연결) · "File"(테스트용 .zpl 저장).</summary>
    public string Mode        { get; set; } = "File";
    public string Host        { get; set; } = "127.0.0.1";
    public int    Port        { get; set; } = 9100;
    public string OutputDir   { get; set; } = "labels";
    /// <summary>Spooler 모드 전용 — Windows 에 등록된 프린터 이름.</summary>
    public string PrinterName { get; set; } = "";
}

/// <summary>ZPL 문자열을 설정된 대상으로 내보낸다. 실패 시 예외 — 호출자가 로깅.</summary>
public sealed class ZplPrinter
{
    private readonly ZplPrinterOptions _opt;
    public ZplPrinter(ZplPrinterOptions opt) => _opt = opt;

    public void Print(string zpl, string labelName)
    {
        if (string.Equals(_opt.Mode, "Tcp", StringComparison.OrdinalIgnoreCase))
        {
            using var tcp = new TcpClient();
            if (!tcp.ConnectAsync(_opt.Host, _opt.Port).Wait(2000))
                throw new TimeoutException($"printer connect timeout {_opt.Host}:{_opt.Port}");
            tcp.SendTimeout = 3000;
            tcp.ReceiveTimeout = 3000;
            using var stream = tcp.GetStream();
            var bytes = Encoding.UTF8.GetBytes(zpl);
            stream.Write(bytes, 0, bytes.Length);
            return;
        }

        if (string.Equals(_opt.Mode, "Spooler", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(_opt.PrinterName))
                throw new InvalidOperationException("Spooler mode requires PrinterName");
            RawSpooler.Send(_opt.PrinterName, Encoding.UTF8.GetBytes(zpl), labelName);
            return;
        }

        Directory.CreateDirectory(_opt.OutputDir);
        var safe = string.Concat(labelName.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        // 같은 LotCode 재출력 시 덮어쓰기 — 최신 내용이 남는 것이 의도된 동작.
        File.WriteAllText(Path.Combine(_opt.OutputDir, safe + ".zpl"), zpl, Encoding.UTF8);
    }
}
