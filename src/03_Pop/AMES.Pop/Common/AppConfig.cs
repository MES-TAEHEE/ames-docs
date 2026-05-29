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
    /// Read from appsettings.json `PopTerminal:ModuleCode`; if missing it's
    /// inferred from the LineId prefix ("LINE-INJ-01" → "INJ" etc.) so existing
    /// configs keep working without an edit.
    /// </summary>
    public string ModuleCode { get; }

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
        ModuleCode = !string.IsNullOrWhiteSpace(explicitModule)
            ? explicitModule.ToUpperInvariant()
            : InferModuleFromLine(LineId);
    }

    /// <summary>Pulls 'INJ' out of 'LINE-INJ-01' etc. Falls back to 'INJ'.</summary>
    private static string InferModuleFromLine(string lineId)
    {
        if (string.IsNullOrEmpty(lineId)) return "INJ";
        var parts = lineId.Split('-');
        return parts.Length >= 2 ? parts[1].ToUpperInvariant() : "INJ";
    }

    private static AppConfig Load()
    {
        var basePath = Path.GetDirectoryName(AppContext.BaseDirectory)!;
        var root = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
            .Build();
        return new AppConfig(root);
    }
}
