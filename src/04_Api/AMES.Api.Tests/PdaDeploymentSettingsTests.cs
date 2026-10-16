using System.Runtime.CompilerServices;
using System.Text.Json;
using Xunit;

namespace AMES.Api.Tests;

public class PdaDeploymentSettingsTests
{
    [Fact]
    public void Packaged_defaults_target_the_shared_server_not_the_developer_pc()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(SettingsPath()));
        Assert.Equal("http://desktop-poedibs.tail526f68.ts.net:5210",
            json.RootElement.GetProperty("apiBaseUrl").GetString());
    }

    private static string SettingsPath([CallerFilePath] string source = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!,
            "../../05_Pda/AMES.Pda/Resources/Raw/pda-settings.json"));
}
