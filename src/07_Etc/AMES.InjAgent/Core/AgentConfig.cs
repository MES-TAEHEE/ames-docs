using AMES.Devices;
using Microsoft.Extensions.Configuration;

namespace AMES.InjAgent.Core;

public sealed class MachineConfig
{
    public string EquipId    { get; set; } = string.Empty;
    public string LineId     { get; set; } = string.Empty;
    public string ModbusIp   { get; set; } = string.Empty;
    public int    ModbusPort { get; set; } = 502;
    public string FenetIp    { get; set; } = string.Empty;
    public int    FenetPort  { get; set; } = 2004;
}

/// <summary>appsettings.json 1회 로드 (Pop AppConfig 와 같은 패턴).</summary>
public sealed class AgentConfig
{
    public string              ConnectionString { get; }
    public int                 PollingMs        { get; }
    public List<MachineConfig> Machines         { get; }
    public ZplPrinterOptions   Printer          { get; }

    private static readonly Lazy<AgentConfig> _instance = new(Load);
    public static AgentConfig Current => _instance.Value;

    private AgentConfig(IConfigurationRoot root)
    {
        ConnectionString = root.GetConnectionString("AMES")
            ?? throw new InvalidOperationException("ConnectionStrings:AMES is missing in appsettings.json");
        PollingMs = int.TryParse(root["Agent:PollingMs"], out var ms) ? ms : 100;

        Machines = new List<MachineConfig>();
        foreach (var m in root.GetSection("Agent:Machines").GetChildren())
        {
            Machines.Add(new MachineConfig
            {
                EquipId    = m["EquipId"]  ?? throw new InvalidOperationException("Machines[].EquipId missing"),
                LineId     = m["LineId"]   ?? throw new InvalidOperationException("Machines[].LineId missing"),
                ModbusIp   = m["ModbusIp"] ?? throw new InvalidOperationException("Machines[].ModbusIp missing"),
                ModbusPort = int.TryParse(m["ModbusPort"], out var mp) ? mp : 502,
                FenetIp    = m["FenetIp"]  ?? throw new InvalidOperationException("Machines[].FenetIp missing"),
                FenetPort  = int.TryParse(m["FenetPort"], out var fp) ? fp : 2004,
            });
        }
        if (Machines.Count == 0)
            throw new InvalidOperationException("Agent:Machines is empty in appsettings.json");

        Printer = new ZplPrinterOptions
        {
            Mode      = root["Agent:Printer:Mode"]      ?? "File",
            Host      = root["Agent:Printer:Host"]      ?? "127.0.0.1",
            Port      = int.TryParse(root["Agent:Printer:Port"], out var pp) ? pp : 9100,
            OutputDir = root["Agent:Printer:OutputDir"] ?? "labels",
        };
    }

    private static AgentConfig Load()
    {
        var basePath = Path.GetDirectoryName(AppContext.BaseDirectory)!;
        var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Production";
        var root = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile($"appsettings.{env}.json", optional: true, reloadOnChange: false)
            .Build();
        return new AgentConfig(root);
    }
}
