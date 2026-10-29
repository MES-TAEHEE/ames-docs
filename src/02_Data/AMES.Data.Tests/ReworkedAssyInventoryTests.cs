using System.Text.RegularExpressions;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

public sealed class ReworkedAssyInventoryTests
{
    [SkippableTheory]
    [InlineData("ASSEMBLY", -1, 1)]
    [InlineData("ASSEMBLY", 0, 1)]
    [InlineData("ASSEMBLY", 1, 0)]
    [InlineData("MATERIAL", -1, 0)]
    [InlineData("INACTIVE", -1, 0)]
    public void Repair_restores_only_missing_or_empty_active_ASSY_stock_without_duplication(string part, int existing, int expected)
    {
        using var conn = FixtureConnection();
        using var cmd = FixtureCommand(conn, part, existing);
        Assert.Equal(expected, Convert.ToInt32(cmd.ExecuteScalar()));
    }

    [SkippableTheory]
    [InlineData(-1)]
    [InlineData(0)]
    public void Restored_stock_is_unpalletized_and_unlocated(int existing)
    {
        using var conn = FixtureConnection();
        using var cmd = FixtureCommand(conn, "ASSEMBLY", existing);
        var batch = cmd.CommandText;
        cmd.CommandText = batch.Replace("SELECT 1;", "SELECT 1; SELECT Qty,ParentLotNo,PalletNo,LocationNo FROM @Stock WHERE LotNo=@L;");
        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read()); Assert.Equal(1, reader.GetInt32(0));
        Assert.True(reader.NextResult()); Assert.True(reader.Read());
        Assert.Equal(1m, reader.GetDecimal(0));
        Assert.True(reader.IsDBNull(1)); Assert.True(reader.IsDBNull(2)); Assert.True(reader.IsDBNull(3));
    }

    [SkippableTheory]
    [InlineData("ASSEMBLY", 2)]
    [InlineData("ASSEMBLY", -2)]
    [InlineData("WRONG", 0)]
    public void Inconsistent_existing_stock_aborts_repair(string part, int qty)
    {
        using var conn = FixtureConnection();
        using var cmd = FixtureCommand(conn, "ASSEMBLY", qty);
        if (part == "WRONG") cmd.CommandText = cmd.CommandText.Replace("VALUES(@L,'PART',@I,", "VALUES(@L,'PART','OTHER',");
        var ex = Assert.Throws<SqlException>(() => cmd.ExecuteScalar());
        Assert.Equal(52730, ex.Number);
    }

    [Fact]
    public void Rework_closes_the_defect_and_commits_only_after_inventory_and_transaction_are_written()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "src/02_Data/AMES.Data/Repositories/ReworkRepository.cs"));
        var confirm = source.IndexOf("SP_POP_RWK_Rework_UpdLotConfirm", StringComparison.Ordinal);
        var restore = source.IndexOf("PalletStockRepository.RestoreReworkedAssy(conn, tx", confirm, StringComparison.Ordinal);
        var close = source.IndexOf("CloseRow(conn, tx", restore, StringComparison.Ordinal);
        var commit = source.IndexOf("tx.Commit()", close, StringComparison.Ordinal);
        Assert.True(confirm < restore && restore < close && close < commit);
        var stock = File.ReadAllText(Path.Combine(Root(), "src/02_Data/AMES.Data/Repositories/PalletStockRepository.cs"));
        Assert.Contains("if (Convert.ToInt32(cmd.ExecuteScalar()) == 0) return;", stock);
        Assert.Contains("WriteTxn(conn, tx, \"IN\", ReworkSourceType", stock);
        Assert.Equal("PROD_REWORK", PalletStockRepository.ReworkSourceType);
    }

    private static SqlCommand FixtureCommand(SqlConnection conn, string part, int qty)
    {
        var sql = PalletStockRepository.RestoreReworkedAssySql.Replace("dbo.WH_Inventory", "@Stock").Replace("dbo.MD_Item", "@Items");
        sql = Regex.Replace(sql, @" WITH\(UPDLOCK,HOLDLOCK\)", "");
        Assert.DoesNotContain("dbo.", sql);
        var cmd = new SqlCommand("""
            DECLARE @Items TABLE(ItemNo varchar(20),ItemType varchar(20),ActiveFlag bit,ItemName nvarchar(200));
            INSERT @Items VALUES('ASSEMBLY','ASSY',1,N'Assembly'),('MATERIAL','MATERIAL',1,N'Material'),('INACTIVE','ASSY',0,N'Inactive');
            DECLARE @Stock TABLE(LotNo nvarchar(50) PRIMARY KEY,UnitType varchar(10),PartNo varchar(20),PartName nvarchar(200),
                ParentLotNo nvarchar(50),PalletNo varchar(50),LocationNo varchar(20),Qty decimal(18,3),ReceivedAt datetime2,CreatedAt datetime2,UpdatedAt datetime2);
            IF @Existing<>-1
                INSERT @Stock(LotNo,UnitType,PartNo,Qty,ParentLotNo,PalletNo,LocationNo)
                VALUES(@L,'PART',@I,@Existing,'OLD-PALLET','OLD-PALLET','OLD-LOC');
            """ + sql, conn);
        cmd.Parameters.AddWithValue("@L", "REPAIRED-LOT");
        cmd.Parameters.AddWithValue("@I", part);
        cmd.Parameters.AddWithValue("@Existing", qty);
        return cmd;
    }

    private static SqlConnection FixtureConnection()
    {
        var connection = Environment.GetEnvironmentVariable("AMES_TEST_CONN");
        Skip.If(string.IsNullOrWhiteSpace(connection), "Table-variable-only repair fixtures; no application rows changed.");
        var conn = new SqlConnection(connection); conn.Open(); return conn;
    }

    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "src/02_Data/AMES.Data"))) return dir.FullName;
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
