using AMES.Data.Connection;
using AMES.Data.Repositories;
using Xunit;

namespace AMES.Data.Tests;

public sealed class LinePrefixTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("I")]
    [InlineData("I12")]
    [InlineData("I-")]
    [InlineData("한글")]
    [InlineData("I\n1")]
    public void Invalid_prefix_is_rejected_before_database_access_on_create_and_edit(string prefix)
    {
        var repo = new MasterDataRepository(new AmesConnectionFactory("invalid connection string"));
        Assert.Contains("Line Prefix", Assert.Throws<ArgumentException>(() => repo.InsertLine(
            "LINE-1", "Line", "Line", "EOS", "WC-1", null, null, false, "ACTIVE", "TEST", prefix)).Message);
        Assert.Contains("Line Prefix", Assert.Throws<ArgumentException>(() => repo.UpdateLine(
            "LINE-1", "Line", "Line", "EOS", "WC-1", null, null, false, "ACTIVE", "TEST", prefix)).Message);
    }

    [Fact]
    public void Prefix_replaces_rfid_in_the_grid_and_is_loaded_and_saved_by_both_forms()
    {
        var page = Read("src/06_Web/AMES.Web/Components/Pages/Md/Rp/Line.razor");
        var markup = page[..page.IndexOf("@code {", StringComparison.Ordinal)];
        Assert.Contains("Property=\"LotPrefix\" Title=\"Line Prefix\"", markup);
        Assert.DoesNotContain("Rfid", markup);
        Assert.Contains("Name=\"CreateLinePrefix\" @bind-Value=\"_f.LotPrefix\" MaxLength=\"2\"", markup);
        Assert.Contains("Name=\"EditLinePrefix\" @bind-Value=\"_f.LotPrefix\" MaxLength=\"2\"", markup);
        Assert.Contains("LotPrefix       = r.LotPrefix?.Trim() ?? \"\"", page);
        Assert.Equal(2, page.Split("lotPrefix: _f.LotPrefix").Length - 1);
        Assert.Equal(2, page.Split("if (!ValidateLinePrefix()) return;").Length - 1);
        Assert.Contains("RfidEnabledFlag = r.RfidEnabledFlag", page);
    }

    [Fact]
    public void Prefix_uses_the_existing_column_and_parameter_without_renaming_location_ids()
    {
        var repo = Read("src/02_Data/AMES.Data/Repositories/MasterDataRepository.cs");
        var writes = repo[repo.IndexOf("public void InsertLine(", StringComparison.Ordinal)..repo.IndexOf("private static string? NormalizeLinePrefix", StringComparison.Ordinal)];
        Assert.Equal(2, writes.Split("lotPrefix = NormalizeLinePrefix(lotPrefix)").Length - 1);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(writes, "SqlDbType.VarChar, +2\\)\\.Value = \\(object\\?\\)lotPrefix").Count);
        Assert.Equal(2, writes.Split("Line Prefix is already assigned to another line.").Length - 1);
        Assert.Contains("LotPrefix=COALESCE(@Prefix,LotPrefix)", writes);
        Assert.Contains("using var tx = conn.BeginTransaction()", writes);
        Assert.Contains("tx.Commit()", writes);
        Assert.DoesNotContain("SET LocationID=", writes);
    }

    private static string Read(string path, [System.Runtime.CompilerServices.CallerFilePath] string current = "")
        => File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(current)!, "../../..", path)));
}
