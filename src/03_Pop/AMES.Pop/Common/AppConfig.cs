using Microsoft.Extensions.Configuration;

namespace AMES.Pop.Common;

/// <summary>
/// Loads appsettings.json once at startup.
/// Process-wide singleton — fetched via AppConfig.Current.
/// </summary>
public sealed class AppConfig
{
    public string ConnectionString { get; }
    public string DefaultShift { get; }

    /// <summary>화면 표시 언어: "ko"(기본) 또는 "en". PopLang 초기값.</summary>
    public string Language { get; }

    /// <summary>라벨 ZPL 프린터 설정 — Mode: Tcp(네트워크) · Spooler(Windows 큐, USB) · File(기본값).</summary>
    public string PrinterMode      { get; }
    public string PrinterHost      { get; }
    public int    PrinterPort      { get; }
    public string PrinterOutputDir { get; }
    /// <summary>Spooler 모드 전용 — Windows 에 등록된 프린터 이름.</summary>
    public string PrinterName      { get; }

    /// <summary>라벨 자동 발행 폴링 주기(ms). 0 이하면 자동 발행 비활성.</summary>
    public int PrinterPollMs { get; }

    /// <summary>연속 출력 실패가 이 횟수에 도달하면 자동 발행을 멈춘다.</summary>
    public int PrinterMaxFailures { get; }

    /// <summary>시리얼 스캐너 COM 포트. 비어 있으면 시리얼 스캐너 비활성(HID 만).</summary>
    public string ScannerPortName { get; }

    /// <summary>포트 열기 실패·끊김 후 재시도 간격(ms).</summary>
    public int ScannerReconnectMs { get; }

    private static readonly Lazy<AppConfig> _instance = new(Load);
    public static AppConfig Current => _instance.Value;

    private AppConfig(IConfigurationRoot root)
    {
        ConnectionString = root.GetConnectionString("AMES")
            ?? throw new InvalidOperationException("ConnectionStrings:AMES is missing in appsettings.json");
        DefaultShift = root["PopTerminal:DefaultShift"] ?? "DAY";

        Language     = (root["PopTerminal:Language"] ?? "ko").ToLowerInvariant() == "en" ? "en" : "ko";

        PrinterMode      = root["PopTerminal:Printer:Mode"]      ?? "File";
        PrinterHost      = root["PopTerminal:Printer:Host"]      ?? "127.0.0.1";
        PrinterPort      = int.TryParse(root["PopTerminal:Printer:Port"], out var pp) ? pp : 9100;
        PrinterOutputDir = root["PopTerminal:Printer:OutputDir"] ?? "labels";
        PrinterName      = root["PopTerminal:Printer:Name"]      ?? "";
        PrinterPollMs      = int.TryParse(root["PopTerminal:Printer:PollMs"], out var pms) ? pms : 1000;
        PrinterMaxFailures = int.TryParse(root["PopTerminal:Printer:MaxFailures"], out var pmf) ? pmf : 3;

        ScannerPortName    = (root["PopTerminal:Scanner:PortName"] ?? string.Empty).Trim();
        ScannerReconnectMs = int.TryParse(root["PopTerminal:Scanner:ReconnectMs"], out var srm) && srm > 0 ? srm : 3000;
    }

    private static AppConfig Load()
    {
        var basePath = Path.GetDirectoryName(AppContext.BaseDirectory)!;

        // Same environment split as AMES.Web: base appsettings.json holds terminal
        // settings, and appsettings.{env}.json overrides the connection string.
        // launchSettings sets DOTNET_ENVIRONMENT=Development for `dotnet run`;
        // a deployed terminal has no env var and falls back to Production.
        var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Production";

        var root = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile($"appsettings.{env}.json", optional: true, reloadOnChange: false)
            .Build();
        return new AppConfig(root);
    }
}
