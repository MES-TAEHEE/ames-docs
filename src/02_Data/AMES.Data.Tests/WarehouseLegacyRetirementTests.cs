using Xunit;

namespace AMES.Data.Tests;

public sealed class WarehouseLegacyRetirementTests
{
    static string Read(string path, [System.Runtime.CompilerServices.CallerFilePath] string file="")
        => File.ReadAllText(Path.Combine(Path.GetDirectoryName(file)!, "../../..", path));

    [Fact]
    public void Fresh_schema_and_export_do_not_recreate_retired_release_schedule()
    {
        Assert.DoesNotContain("CREATE TABLE [dbo].[WH_ReleaseSchedule]",Read("dist/AMES_Schema.sql"));
        Assert.Contains("name.Equals(\"WH_ReleaseSchedule\", StringComparison.OrdinalIgnoreCase)",Read("tools/schema_from_db/Program.cs"));
        Assert.Contains("CREATE TABLE [dbo].[WH_PickSlip]",Read("dist/AMES_Schema.sql"));
    }

    [Fact]
    public void Retirement_defaults_to_preview_and_requires_empty_unreferenced_legacy_table()
    {
        var sql=Read("dist/retire_wh_release_schedule.sql");
        Assert.Contains("DECLARE @Apply bit=0",sql);
        Assert.Contains("IF @Apply=0",sql);
        Assert.Contains("IF OBJECT_ID(N'dbo.WH_PickSlip',N'U') IS NULL",sql);
        Assert.Contains("WITH(TABLOCKX,HOLDLOCK)",sql);
        Assert.Contains("IF @Rows<>0",sql);
        Assert.Contains("sys.foreign_keys",sql);
        Assert.Contains("sys.sql_expression_dependencies",sql);
        Assert.Contains("ROLLBACK TRANSACTION",sql);
        Assert.DoesNotContain("DELETE FROM",sql,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE dbo.WH_PickSlip",sql,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Retired_demo_seeder_cannot_connect_or_write_to_any_database()
    {
        var source=Read("tools/seed_wh_demo/Program.cs");
        Assert.Contains("return 1;",source);
        Assert.DoesNotContain("SqlConnection",source);
        Assert.DoesNotContain("DELETE FROM",source);
        Assert.DoesNotContain("INSERT INTO",source);
    }
}
