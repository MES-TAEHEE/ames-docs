using System.Text.RegularExpressions;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

public sealed class FgScenarioReviewSqlTests
{
    [SkippableTheory]
    [InlineData("LOT-OK", null, false)]
    [InlineData("LOT-MISSING", "LOT-MISSING", false)]
    [InlineData("LOT-WH", "LOT-WH", false)]
    [InlineData("LOT-INSPECT", "LOT-INSPECT", true)]
    [InlineData("LOT-OPEN", "LOT-OPEN", true)]
    [InlineData("LOT-APPROVED", null, false)]
    [InlineData("LOT-REJECTED", null, false)]
    [InlineData("PALLET", "Z-HELD", true)]
    public void Isolated_outbound_precheck_prioritizes_inspection_over_other_leaf_location_errors(
        string barcode, string? expectedLot, bool expectedHold)
    {
        using var conn = OpenFixtureConnection();
        var sql = PdaFinishedGoodsSql.OutboundPrecheck
            .Replace("dbo.WH_Inventory", "@Stock").Replace("dbo.MD_Location", "@Locations")
            .Replace("dbo.FG_CustomerReturn", "@Returns");
        sql = Regex.Replace(sql, @" WITH\((?:UPDLOCK,)?HOLDLOCK\)", "");
        Assert.DoesNotContain("dbo.", sql, StringComparison.OrdinalIgnoreCase);
        using var cmd = new SqlCommand("""
            DECLARE @Stock TABLE(LotNo varchar(50),ParentLotNo varchar(50),UnitType varchar(10),PartNo varchar(20),LocationNo varchar(20),Qty decimal(18,3));
            DECLARE @Locations TABLE(LocationID varchar(20),LocationType varchar(10));
            DECLARE @Returns TABLE(LotNo varchar(50),Status varchar(20));
            INSERT @Locations VALUES('FG-1','FG'),('WH-1','WH');
            INSERT @Stock VALUES
              ('LOT-OK',NULL,'LOT','PART','FG-1',1),
              ('LOT-MISSING',NULL,'LOT','PART',NULL,1),
              ('LOT-WH',NULL,'LOT','PART','WH-1',1),
              ('LOT-INSPECT',NULL,'LOT','PART',NULL,1),
              ('LOT-OPEN',NULL,'LOT','PART',NULL,1),
              ('LOT-APPROVED',NULL,'LOT','PART','FG-1',1),
              ('LOT-REJECTED',NULL,'LOT','PART','FG-1',1),
              ('PALLET',NULL,'PALLET',NULL,NULL,2),
              ('A-MISSING','PALLET','LOT','PART',NULL,1),
              ('Z-HELD','PALLET','LOT','PART',NULL,1),
              ('0-ZERO-HELD','PALLET','LOT','PART',NULL,0);
            INSERT @Returns VALUES('LOT-INSPECT','Inspecting'),('LOT-OPEN','Open'),
              ('LOT-APPROVED','Approved'),('LOT-REJECTED','Rejected'),('Z-HELD','Inspecting'),('0-ZERO-HELD','Open');
            """ + sql, conn);
        cmd.Parameters.AddWithValue("@Barcode", barcode);
        using var reader = cmd.ExecuteReader();
        Assert.Equal(expectedLot is not null, reader.Read());
        if (expectedLot is null) return;
        Assert.Equal(expectedLot, reader["LotNo"]);
        Assert.Equal(expectedHold, reader["InspectionPending"]);
        Assert.False(reader.Read());
    }

    [SkippableTheory]
    [InlineData("FG_DEFECT", 0, "LOT", true)]
    [InlineData("INV_DEFECT", 0, "LOT", true)]
    [InlineData("INV_DEFECT", 2, "LOT", false)]
    [InlineData("FG_OUTBOUND", 0, "LOT", false)]
    [InlineData("FG_DEFECT", 0, "OTHER", false)]
    public void Putaway_scan_uses_the_same_full_defect_condition_as_save(string sourceType, int qtyAfter, string lot, bool expected)
    {
        using var conn = OpenFixtureConnection();
        var source = File.ReadAllText(Path.Combine(Root(), "src/04_Api/AMES.Api/Endpoints/FgEndpoints.cs"));
        var match = Regex.Match(source, @"CASE WHEN EXISTS\(SELECT 1 FROM dbo.WH_InventoryTransaction T\s+WHERE T.LotNo.*?AS RejectedAsDefective", RegexOptions.Singleline);
        Assert.True(match.Success);
        var expression = match.Value.Replace("dbo.WH_InventoryTransaction", "@Transactions").Replace("L.LotCode", "@LotNo");
        Assert.DoesNotContain("dbo.", expression, StringComparison.OrdinalIgnoreCase);
        using var cmd = new SqlCommand("""
            DECLARE @Transactions TABLE(LotNo varchar(50),SourceType varchar(20),QtyAfter decimal(18,3));
            INSERT @Transactions VALUES(@Lot,@SourceType,@QtyAfter);
            SELECT
            """ + " " + expression, conn);
        cmd.Parameters.AddWithValue("@LotNo", "LOT");
        cmd.Parameters.AddWithValue("@Lot", lot);
        cmd.Parameters.AddWithValue("@SourceType", sourceType);
        cmd.Parameters.AddWithValue("@QtyAfter", qtyAfter);
        Assert.Equal(expected ? 1 : 0, (int)cmd.ExecuteScalar()!);
        Assert.Contains("SourceType IN('FG_DEFECT','INV_DEFECT') AND QtyAfter=0", PdaFinishedGoodsSql.PutAway);
    }

    private static SqlConnection OpenFixtureConnection()
    {
        var connection = Environment.GetEnvironmentVariable("AMES_TEST_CONN");
        Skip.If(string.IsNullOrWhiteSpace(connection), "SQL fixtures use table variables only; set AMES_TEST_CONN explicitly.");
        var conn = new SqlConnection(connection);
        conn.Open();
        return conn;
    }

    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "src/04_Api/AMES.Api"))) return dir.FullName;
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
