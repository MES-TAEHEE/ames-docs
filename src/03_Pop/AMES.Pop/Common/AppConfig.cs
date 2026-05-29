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

    private static readonly Lazy<AppConfig> _instance = new(Load);
    public static AppConfig Current => _instance.Value;

    private AppConfig(IConfigurationRoot root)
    {
        ConnectionString = root.GetConnectionString("AMES")
            ?? throw new InvalidOperationException("ConnectionStrings:AMES is missing in appsettings.json");
        StationId    = root["PopTerminal:StationId"]    ?? "POP-UNKNOWN";
        LineId       = root["PopTerminal:LineId"]       ?? "UNASSIGNED";
        DefaultShift = root["PopTerminal:DefaultShift"] ?? "DAY";
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
