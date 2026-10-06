using AMES.Data.Aps;
using AMES.Data.Aps.Contracts;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AMES.Data.Tests.Aps;

/// 골든 픽스처 로더 — bin\…\TestData\Aps\ (csproj 의 TestData\**\*.json 복사 항목).
public static class Fixtures
{
    public static string Root { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "TestData", "Aps");
    public static string Path(string name) => System.IO.Path.Combine(Root, name);
    public static JsonNode Json(string name) => JsonNode.Parse(File.ReadAllText(Path(name)))!;

    public static Settings LoadSettings() =>
        JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path("settings.json")), ApsJson.Options)!;

    public static Dictionary<string, LineInfo> LoadLines() =>
        JsonSerializer.Deserialize<LinesResponse>(File.ReadAllText(Path("lines.json")), ApsJson.Options)!
            .Lines.ToDictionary(l => l.LineCd, StringComparer.OrdinalIgnoreCase);
}
