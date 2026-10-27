using System.Text.RegularExpressions;
using Xunit;

namespace AMES.Data.Tests;

// Read-only source checks: never execute a rebuild or contact a database.
public sealed class RebuildSeedPolicyTests
{
    [Theory]
    [InlineData("seed_inj_img_master_dev.sql")]
    [InlineData("seed_pnt_dev.sql")]
    [InlineData("seed_pp_customer_order_dev.sql")]
    [InlineData("seed_md_worker_dev.sql")]
    [InlineData("seed_aps_dev.sql")]
    [InlineData("seed_demand_plan_dev.sql")]
    [InlineData("seed_portal_dev.sql")]
    [InlineData("pda/PDA_SEED.sql")]
    public void Every_development_seed_is_behind_explicit_opt_in(string file)
    {
        var script = ReadScript();
        var files = Regex.Match(script, @"(?ms)^FILES=\((.*?)^\)").Groups[1].Value;
        Assert.Contains(file, files);
        var classification = Regex.Match(script, @"(?ms)^is_development_seed\(\) \{.*?^\}").Value;
        Assert.Contains("seed_*_dev.sql|pda/PDA_SEED.sql) return 0", classification);
        Assert.True(file == "pda/PDA_SEED.sql" || file.StartsWith("seed_") && file.EndsWith("_dev.sql"));

        var loop = script[script.IndexOf("for f in \"${FILES[@]}\"; do", StringComparison.Ordinal)..];
        Assert.Contains("if is_development_seed \"$f\"; then", loop);
        Assert.Contains("${AMES_DEVELOPMENT_SEEDS:-0}", loop);
        Assert.Contains("!= \"1\"", loop);
        Assert.True(loop.IndexOf("continue", StringComparison.Ordinal) < loop.IndexOf("EXEC sys.sp_set_session_context", StringComparison.Ordinal));
        Assert.Contains("@key=N'AMES_DEVELOPMENT_SEED', @value=1", loop);
    }

    [Fact]
    public void Core_schema_master_data_and_job_order_are_preserved_without_stale_instructions()
    {
        var script = ReadScript();
        var header = script[..script.IndexOf("set -euo pipefail", StringComparison.Ordinal)];
        Assert.DoesNotContain("dist/rebuild_db.ps1", header);
        Assert.DoesNotContain("시드 12개", header);
        Assert.Contains("AMES_DEVELOPMENT_SEEDS=1", header);
        Assert.Contains("--force", header);
        Assert.Contains("AMES_Schema.sql", script);
        Assert.Contains("seed_md_item_bom_master_list.sql", script);
        Assert.Contains("seed_md_mold_master.sql", script);
        Assert.Contains("COLLATE Korean_Wansung_CI_AS", script);
        Assert.Contains("run_file setup_job_mnt_oeelog_fill.sql", script);
    }

    private static string ReadScript([System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "../../../dist/rebuild_db.sh")));
}
