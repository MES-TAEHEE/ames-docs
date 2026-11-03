using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AMES.Api.Tests;

// Configuration/source checks only: never connect to SQL, deploy or modify IIS.
public sealed class WebApiSecretSettingsTests
{
    [Theory]
    [InlineData("src/04_Api/AMES.Api/appsettings.json")]
    [InlineData("src/04_Api/AMES.Api/appsettings.Production.json")]
    [InlineData("src/06_Web/AMES.Web/appsettings.json")]
    [InlineData("src/06_Web/AMES.Web/appsettings.Development.json")]
    [InlineData("src/06_Web/AMES.Web/appsettings.Production.json")]
    public void Checked_in_settings_do_not_contain_database_credentials(string file)
    {
        var text = Read(file);
        using var json = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        Assert.Equal("", json.RootElement.GetProperty("ConnectionStrings").GetProperty("AMES").GetString());
        Assert.DoesNotMatch(@"(?i)(?:password|pwd)\s*=", text);
    }

    [Theory]
    [InlineData("src/04_Api/AMES.Api/appsettings.json")]
    [InlineData("src/04_Api/AMES.Api/appsettings.Production.json")]
    [InlineData("src/06_Web/AMES.Web/appsettings.json")]
    [InlineData("src/06_Web/AMES.Web/appsettings.Development.json")]
    [InlineData("src/06_Web/AMES.Web/appsettings.Production.json")]
    public void Environment_connection_string_overrides_the_empty_json_setting(string file)
    {
        var prefix = "AMES_CONFIG_TEST_" + Guid.NewGuid().ToString("N") + "_";
        var key = prefix + "ConnectionStrings__AMES";
        const string value = "Server=configuration-test;Database=not-opened;Integrated Security=True;";
        Environment.SetEnvironmentVariable(key, value);
        try
        {
            using var json = new MemoryStream(Encoding.UTF8.GetBytes(Read(file)));
            var config = new ConfigurationBuilder().AddJsonStream(json).AddEnvironmentVariables(prefix).Build();
            using var cleanup = (IDisposable)config;
            Assert.Equal(value, config.GetConnectionString("AMES"));
        }
        finally { Environment.SetEnvironmentVariable(key, null); }
    }

    [Theory]
    [InlineData("src/04_Api/AMES.Api/Program.cs", "cs")]
    [InlineData("src/06_Web/AMES.Web/Program.cs", "connectionString")]
    public void Startup_rejects_empty_configuration_with_actionable_environment_variable_name(string file, string variable)
    {
        var source = Read(file);
        Assert.Contains("WebApplication.CreateBuilder(args)", source);
        Assert.Contains("GetConnectionString(\"AMES\")", source);
        Assert.Contains($"string.IsNullOrWhiteSpace({variable})", source);
        Assert.Contains("Set ConnectionStrings__AMES", source);
    }

    [Fact]
    public void Launch_profiles_do_not_override_external_credentials()
    {
        var file = Read("src/06_Web/AMES.Web/Properties/launchSettings.json");
        Assert.DoesNotMatch(@"(?i)(?:password|pwd)\s*=", file);
        Assert.DoesNotContain("\"ConnectionStrings__AMES\"", file);
    }

    [Fact]
    public void Cors_is_closed_by_default_and_uses_only_the_explicit_browser_allowlist()
    {
        using var json = JsonDocument.Parse(Read("src/04_Api/AMES.Api/appsettings.json"));
        Assert.Empty(json.RootElement.GetProperty("Cors").GetProperty("AllowedOrigins").EnumerateArray());
        var source = Read("src/04_Api/AMES.Api/Program.cs");
        Assert.Contains("GetSection(\"Cors:AllowedOrigins\")", source);
        Assert.Contains("if (origins.Length > 0) p.WithOrigins(origins)", source);
        Assert.DoesNotContain("AllowAnyOrigin(", source);
        Assert.DoesNotContain("SetIsOriginAllowed(", source);
    }

    [Theory]
    [InlineData("tools/publish-api.ps1")]
    [InlineData("tools/publish-web.ps1")]
    public void Publishing_has_no_password_defaults_or_connection_string_output_and_checks_before_mutation(string file)
    {
        var source = Read(file);
        Assert.DoesNotMatch(@"(?i)(?:password|pwd)\s*=", source);
        Assert.Contains("[string]$LocalDbConn = $env:ConnectionStrings__AMES", source);
        Assert.DoesNotContain("Get-ActiveConn", source);
        Assert.DoesNotContain("Write-Host \"  $LocalDbConn\"", source);
        Assert.Contains("[string]::IsNullOrWhiteSpace($LocalDbConn)", source);
        Assert.True(source.IndexOf("[string]::IsNullOrWhiteSpace($LocalDbConn)", StringComparison.Ordinal)
            < source.IndexOf("Remove-Item $PkgDir", StringComparison.Ordinal));
    }

    [Fact]
    public void Legacy_route_uninstall_keeps_operator_environment_settings_when_json_has_no_connection()
    {
        var source = Read("tools/db-route.ps1");
        var uninstall = source[source.IndexOf("if ($Uninstall)", StringComparison.Ordinal)..source.IndexOf("if ($Install)", StringComparison.Ordinal)];
        Assert.Contains("[string]::IsNullOrWhiteSpace((Get-DeployedConnString $dir))", uninstall);
        Assert.True(uninstall.IndexOf("continue", StringComparison.Ordinal) < uninstall.IndexOf("Set-PoolVar $pool $null", StringComparison.Ordinal));
    }

    private static string Read(string path, [CallerFilePath] string current = "") =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(current)!, "../../..", path)));
}
