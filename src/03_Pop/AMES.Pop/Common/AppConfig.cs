using Microsoft.Extensions.Configuration;

namespace AMES.Pop.Common;

/// <summary>
/// Loads appsettings.json once at startup.
/// Process-wide singleton — fetched via AppConfig.Current.
/// </summary>
public sealed class AppConfig
{
    public string ConnectionString { get; }
    public string StationId { get; }
    public string LineId { get; }
    public string DefaultShift { get; }

    /// <summary>
    /// Process code this terminal belongs to: INJ / IMG / PNT / QC.
    /// Read solely from appsettings.json `PopTerminal:ModuleCode` — required.
    /// </summary>
    public string ModuleCode { get; }

    /// <summary>화면 표시 언어: "ko"(기본) 또는 "en". PopLang 초기값.</summary>
    public string Language { get; }

    /// <summary>라벨 재출력용 ZPL 프린터 설정 (없으면 File 모드 기본값).</summary>
    public string PrinterMode      { get; }
    public string PrinterHost      { get; }
    public int    PrinterPort      { get; }
    public string PrinterOutputDir { get; }

    private static readonly Lazy<AppConfig> _instance = new(Load);
    public static AppConfig Current => _instance.Value;

    private AppConfig(IConfigurationRoot root)
    {
        ConnectionString = root.GetConnectionString("AMES")
            ?? throw new InvalidOperationException("ConnectionStrings:AMES is missing in appsettings.json");
        StationId    = root["PopTerminal:StationId"]    ?? "POP-UNKNOWN";
        LineId       = root["PopTerminal:LineId"]       ?? "UNASSIGNED";
        DefaultShift = root["PopTerminal:DefaultShift"] ?? "DAY";

        var explicitModule = root["PopTerminal:ModuleCode"];
        if (string.IsNullOrWhiteSpace(explicitModule))
            throw new InvalidOperationException("PopTerminal:ModuleCode is missing in appsettings.json");
        ModuleCode = explicitModule.ToUpperInvariant();

        Language     = (root["PopTerminal:Language"] ?? "ko").ToLowerInvariant() == "en" ? "en" : "ko";

        PrinterMode      = root["PopTerminal:Printer:Mode"]      ?? "File";
        PrinterHost      = root["PopTerminal:Printer:Host"]      ?? "127.0.0.1";
        PrinterPort      = int.TryParse(root["PopTerminal:Printer:Port"], out var pp) ? pp : 9100;
        PrinterOutputDir = root["PopTerminal:Printer:OutputDir"] ?? "labels";
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
