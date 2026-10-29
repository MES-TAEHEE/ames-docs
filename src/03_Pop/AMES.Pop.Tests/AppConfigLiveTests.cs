using AMES.Pop.Common;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AMES.Pop.Tests;

/// <summary>검사 주기·대기·동기화 주기는 POP 을 다시 켜지 않아도 설정 변경이 바로 보인다(10-29). 없거나 0 이하·숫자 아님은 기본값.</summary>
public sealed class AppConfigLiveTests
{
    private static (AppConfig Config, IConfigurationRoot Root) Make(Dictionary<string, string?> values)
    {
        values["ConnectionStrings:AMES"] = "Server=x;Database=y";
        var root = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return (AppConfig.FromRoot(root), root);
    }

    [Fact]
    public void Defaults_apply_when_the_settings_are_missing_or_invalid()
    {
        var (cfg, _) = Make(new()
        {
            ["PopTerminal:Db:ProbeIntervalSeconds"] = "0",
            ["PopTerminal:Db:ProbeTimeoutSeconds"]  = "abc",
        });
        Assert.Equal(3, cfg.DbProbeIntervalSeconds);
        Assert.Equal(3, cfg.DbProbeTimeoutSeconds);
        Assert.Equal(30, cfg.OfflineRefreshMinutes);
    }

    [Fact]
    public void Changed_settings_are_read_without_restarting()
    {
        var (cfg, root) = Make(new()
        {
            ["PopTerminal:Db:ProbeIntervalSeconds"] = "3",
            ["PopTerminal:Db:ProbeTimeoutSeconds"]  = "3",
            ["PopTerminal:Offline:RefreshMinutes"]  = "30",
        });

        root["PopTerminal:Db:ProbeIntervalSeconds"] = "10";
        root["PopTerminal:Db:ProbeTimeoutSeconds"]  = "5";
        root["PopTerminal:Offline:RefreshMinutes"]  = "15";

        Assert.Equal(10, cfg.DbProbeIntervalSeconds);
        Assert.Equal(5, cfg.DbProbeTimeoutSeconds);
        Assert.Equal(15, cfg.OfflineRefreshMinutes);
    }
}
