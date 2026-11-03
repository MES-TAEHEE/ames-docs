using System.Text.RegularExpressions;
using Xunit;

namespace AMES.Data.Tests;

// Source guards only: never apply migrations, rebuild a database or register jobs.
public sealed class MntMigrationSafetyTests
{
    [Fact]
    public void Legacy_values_are_checked_before_any_schema_change_and_locked_until_drop()
    {
        var sql = Read("dist/migrate_mnt_failure_downtime.sql");
        var guard = sql.IndexOf("THROW 51000", StringComparison.Ordinal);
        Assert.True(guard >= 0);
        foreach (var change in new[] { "ALTER TABLE", "EXEC sys.sp_addextendedproperty", "EXEC sys.sp_dropextendedproperty" })
            Assert.True(sql.IndexOf(change, StringComparison.Ordinal) > guard, change);
        Assert.Contains("COUNT_BIG(*) FROM dbo.MNT_WorkOrder WITH (TABLOCKX, HOLDLOCK) WHERE DowntimeID IS NOT NULL", sql);
        Assert.DoesNotContain("UPDATE dbo.MNT_WorkOrder", sql);
        Assert.DoesNotContain("DELETE FROM dbo.MNT_WorkOrder", sql);
    }

    [Fact]
    public void All_schema_changes_share_one_transaction_and_errors_are_rethrown_after_rollback()
    {
        var sql = Read("dist/migrate_mnt_failure_downtime.sql");
        var transaction = sql[sql.IndexOf("BEGIN TRY", StringComparison.Ordinal)..sql.IndexOf("END CATCH;", StringComparison.Ordinal)];
        Assert.Single(Regex.Matches(transaction, "BEGIN TRANSACTION;"));
        Assert.Single(Regex.Matches(transaction, "COMMIT TRANSACTION;"));
        Assert.DoesNotMatch(@"(?im)^\s*GO\s*$", transaction);
        var commit = transaction.IndexOf("COMMIT TRANSACTION;", StringComparison.Ordinal);
        Assert.True(transaction.LastIndexOf("DROP COLUMN DowntimeID", StringComparison.Ordinal) < commit);
        Assert.True(transaction.LastIndexOf("sp_dropextendedproperty", StringComparison.Ordinal) < commit);
        Assert.Contains("IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;\n    THROW;", transaction.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Existing_transactions_are_rejected_without_rolling_back_the_callers_work()
    {
        var sql = Read("dist/migrate_mnt_failure_downtime.sql");
        var before = sql[..sql.IndexOf("BEGIN TRY", StringComparison.Ordinal)];
        Assert.Contains("IF @@TRANCOUNT <> 0", before);
        Assert.Contains("RAISERROR ('Run this migration without an existing transaction.', 16, 1);", before);
        Assert.Contains("RETURN;", before);
        Assert.DoesNotMatch(@"(?m)^\s*(THROW|ROLLBACK)\b", before);
    }

    [Fact]
    public void Fresh_schemas_do_not_compile_references_to_the_removed_column_and_failure_links_are_preserved()
    {
        var sql = Read("dist/migrate_mnt_failure_downtime.sql");
        Assert.Contains("COL_LENGTH('dbo.MNT_WorkOrder', 'DowntimeID') IS NULL THEN 0 ELSE 1", sql);
        Assert.Contains("N'SELECT @n = COUNT_BIG(*)", sql);
        Assert.Contains("EXEC sys.sp_executesql N'ALTER TABLE dbo.MNT_WorkOrder DROP COLUMN DowntimeID;'", sql);
        Assert.Contains("COL_LENGTH('dbo.PP_LineDowntimeLog', 'FailureID') IS NULL", sql);
        Assert.DoesNotMatch(@"(?i)ALTER\s+TABLE\s+dbo\.MNT_FailureRegister", sql);
        Assert.Contains("Required MNT_WorkOrder or PP_LineDowntimeLog table is missing or not visible.", sql);
    }

    private static string Read(string path, [System.Runtime.CompilerServices.CallerFilePath] string current = "") =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(current)!, "../../..", path)));
}
