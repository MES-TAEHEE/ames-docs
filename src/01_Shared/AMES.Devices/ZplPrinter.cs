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

    /// <summary>
    /// 한 줄 설정값을 출력 대상으로 바꾼다(웹 SYS_Config PALLET_LABEL_PRINTER) — <c>172.29.4.10</c>·<c>172.29.4.10:9100</c>·<c>Tcp:host:port</c> = 네트워크 프린터,
    /// <c>Spooler:프린터 이름</c> = 웹 서버 PC 에 설치된 Windows 프린터. 비었거나 형식이 틀리면 false.
    /// </summary>
    public static bool TryParseTarget(string? value, out ZplPrinterOptions opt)
    {
        opt = new ZplPrinterOptions();
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return false;
        if (v.StartsWith("Spooler:", StringComparison.OrdinalIgnoreCase))
        {
            var name = v["Spooler:".Length..].Trim();
            if (name.Length == 0) return false;
            opt.Mode = "Spooler"; opt.PrinterName = name;
            return true;
        }
        if (v.StartsWith("Tcp:", StringComparison.OrdinalIgnoreCase)) v = v["Tcp:".Length..].Trim();
        var host = v; var port = 9100;
        var colon = v.LastIndexOf(':');
        if (colon >= 0)
        {
            host = v[..colon].Trim();
            if (!int.TryParse(v[(colon + 1)..].Trim(), out port) || port is < 1 or > 65535) return false;
        }
        if (host.Length == 0 || host.Any(c => char.IsWhiteSpace(c) || c == ':')) return false;
        opt.Mode = "Tcp"; opt.Host = host; opt.Port = port;
        return true;
    }

    /// <summary>화면 표시용 — 「172.29.4.10:9100」·「Spooler ZDesigner ZD421」.</summary>
    public string Describe() => string.Equals(Mode, "Spooler", StringComparison.OrdinalIgnoreCase)
        ? $"Spooler {PrinterName}" : string.Equals(Mode, "Tcp", StringComparison.OrdinalIgnoreCase) ? $"{Host}:{Port}" : $"File {OutputDir}";
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
