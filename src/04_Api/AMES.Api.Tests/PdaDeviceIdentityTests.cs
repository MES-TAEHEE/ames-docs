using System.Runtime.CompilerServices;
using AMES.Api.Auth;
using AMES.Pda.Services;
using Xunit;

namespace AMES.Api.Tests;

public sealed class PdaDeviceIdentityTests
{
    [Fact]
    public void Platform_identity_is_stable_case_insensitive_and_fits_the_existing_api()
    {
        var unused = Path.Combine(Path.GetTempPath(), "ames-identity-unused-" + Guid.NewGuid().ToString("N"));
        var terminal = PdaDeviceIdentity.Resolve("a123456789abcdef", unused);
        Assert.Equal(terminal, PdaDeviceIdentity.Resolve(" A123456789ABCDEF ", unused));
        Assert.NotEqual(terminal, PdaDeviceIdentity.Resolve("b123456789abcdef", unused));
        Assert.Matches("^PDA-[0-9A-F]{16}$", terminal);
        Assert.True(LoginInput.ValidContext(terminal, null, null));
        Assert.False(Directory.Exists(unused));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("0000000000000000")]
    [InlineData("9774d56d682e549c")]
    public void Missing_or_shared_platform_ids_get_persistent_independent_installation_ids(string? platformId)
    {
        var root = Directory.CreateTempSubdirectory("ames-pda-identity-");
        try
        {
            var first = Path.Combine(root.FullName, "first");
            var second = Path.Combine(root.FullName, "second");
            var terminal = PdaDeviceIdentity.Resolve(platformId, first);
            Assert.Equal(terminal, PdaDeviceIdentity.Resolve(platformId, first));
            Assert.NotEqual(terminal, PdaDeviceIdentity.Resolve(platformId, second));
            Assert.Matches("^PDA-[0-9A-F]{16}$", terminal);
            Assert.True(LoginInput.ValidContext(terminal, null, null));
        }
        finally { root.Delete(recursive: true); }
    }

    [Fact]
    public void Damaged_fallback_identity_is_replaced_once_then_persisted()
    {
        var root = Directory.CreateTempSubdirectory("ames-pda-identity-");
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "pda-installation-id"), "invalid");
            var terminal = PdaDeviceIdentity.Resolve(null, root.FullName);
            Assert.Equal(terminal, PdaDeviceIdentity.Resolve(null, root.FullName));
            Assert.True(Guid.TryParseExact(File.ReadAllText(Path.Combine(root.FullName, "pda-installation-id")), "N", out _));
        }
        finally { root.Delete(recursive: true); }
    }

    [Fact]
    public void Settings_use_device_identity_and_android_fallback_is_not_restored_to_other_devices()
    {
        var code = Read("src/05_Pda/AMES.Pda/Services/PdaSettings.cs");
        Assert.Contains("Settings.Secure.AndroidId", code);
        Assert.Contains("NoBackupFilesDir", code);
        Assert.Contains("PdaDeviceIdentity.Resolve(androidId, identityDirectory)", code);
        Assert.DoesNotContain("settings.TerminalId", code);
        Assert.DoesNotContain("PDA-DEV-01", code);
    }

    static string Read(string path, [CallerFilePath] string source = "")
        => File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "../../..", path)));
}
