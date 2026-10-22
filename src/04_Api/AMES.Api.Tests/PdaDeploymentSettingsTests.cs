using System.Runtime.CompilerServices;
using System.Text.Json;
using AMES.Api.Auth;
using AMES.Api.Endpoints;
using AMES.Pda.Services;
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

    [Fact]
    public void Packaged_settings_do_not_assign_a_production_line_or_shift()
    {
        using var json=JsonDocument.Parse(File.ReadAllText(SettingsPath()));
        Assert.False(json.RootElement.TryGetProperty("lineId",out _));
        Assert.False(json.RootElement.TryGetProperty("shiftCode",out _));
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("terminalId").GetString()));
    }

    [Fact]
    public void Both_login_requests_omit_line_and_shift_and_match_the_api_contract()
    {
        var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var pinJson=JsonSerializer.Serialize(new PdaApi.LoginReq("EMP-01","test-only","PDA-01"),options);
        var badgeJson=JsonSerializer.Serialize(new PdaApi.BarcodeLoginReq("test-badge","PDA-01"),options);
        foreach(var text in new[]{pinJson,badgeJson})
        {
            using var json=JsonDocument.Parse(text);
            Assert.False(json.RootElement.TryGetProperty("lineId",out _));
            Assert.False(json.RootElement.TryGetProperty("shiftCode",out _));
        }
        var pin=JsonSerializer.Deserialize<AuthEndpoints.LoginDto>(pinJson,options)!;
        var badge=JsonSerializer.Deserialize<AuthEndpoints.BarcodeLoginDto>(badgeJson,options)!;
        Assert.True(LoginInput.ValidContext(pin.TerminalId,pin.LineId,pin.ShiftCode));
        Assert.True(LoginInput.ValidContext(badge.TerminalId,badge.LineId,badge.ShiftCode));
        Assert.Null(pin.LineId); Assert.Null(pin.ShiftCode);
        Assert.Null(badge.LineId); Assert.Null(badge.ShiftCode);
    }

    private static string SettingsPath([CallerFilePath] string source = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!,
            "../../05_Pda/AMES.Pda/Resources/Raw/pda-settings.json"));
}
