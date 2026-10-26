using AMES.Pop.Common;
using Xunit;

namespace AMES.Pop.Tests;

public class AppConfigOutputDirTests
{
    private const string BaseDir = @"C:\Ames\Pop";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_setting_uses_labels_next_to_the_executable(string? configured)
    {
        Assert.Equal(Path.Combine(BaseDir, "labels"), AppConfig.ResolveOutputDir(configured, BaseDir));
    }

    [Fact]
    public void Relative_setting_is_resolved_against_the_executable_folder()
    {
        Assert.Equal(Path.Combine(BaseDir, "logs", "pop"), AppConfig.ResolveOutputDir(@"logs\pop", BaseDir));
    }

    [Fact]
    public void Absolute_setting_is_used_as_is()
    {
        var absolute = Path.Combine(Path.GetTempPath(), "ames-out");
        Assert.Equal(absolute, AppConfig.ResolveOutputDir(absolute, BaseDir));
    }

    [Fact]
    public void Environment_variables_are_expanded_before_resolving()
    {
        const string name = "AMES_TEST_OUTPUT_ROOT";
        var root = Path.Combine(Path.GetTempPath(), "ames-env");
        Environment.SetEnvironmentVariable(name, root);
        try
        {
            Assert.Equal(Path.Combine(root, "labels"), AppConfig.ResolveOutputDir($"%{name}%\\labels", BaseDir));
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }
}
