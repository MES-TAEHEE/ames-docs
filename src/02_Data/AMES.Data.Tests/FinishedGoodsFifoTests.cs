using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

public sealed class FinishedGoodsFifoTests
{
    [SkippableTheory]
    [InlineData("available", true)]
    [InlineData("same-date", false)]
    [InlineData("newer", false)]
    [InlineData("other-customer", false)]
    [InlineData("unknown-customer", false)]
    [InlineData("unlocated", false)]
    [InlineData("non-fg", false)]
    [InlineData("primary", false)]
    [InlineData("inactive", false)]
    [InlineData("inspection", false)]
    [InlineData("rejected", false)]
    [InlineData("defective", false)]
    [InlineData("reserved", false)]
    [InlineData("shipped-load", true)]
    [InlineData("invalid-sibling", false)]
    [InlineData("mixed-customer", false)]
    [InlineData("pnt", true)]
    [InlineData("no-pallet", true)]
    [InlineData("unrelated-cycle", true)]
    [InlineData("candidate-cycle", false)]
    public void Isolated_candidates_exclude_ineligible_units_and_compare_same_customer_part(string variant, bool expected)
    {
        using var conn = Open();
        using var cmd = new SqlCommand(Fixture + Variants[variant] + Isolate(FinishedGoodsFifo.CandidatesSql), conn);
        cmd.Parameters.AddWithValue("@Barcode", "NEW-PALLET");
        cmd.Parameters.AddWithValue("@Customer", "C1");
        cmd.Parameters.AddWithValue("@DeliveryId", DBNull.Value);
        using var r = cmd.ExecuteReader();
        Assert.Equal(expected, r.Read());
        if (!expected) return;
        Assert.Equal("PART", r["PartNo"]);
        Assert.Equal("OLD-LOT", r["LotNo"]);
        Assert.Equal("FG-1", r["LocationNo"]);
        Assert.Equal(variant == "no-pallet" ? DBNull.Value : "OLD-PALLET", r["PalletNo"]);
        Assert.False(r.Read());
    }

    [SkippableFact]
    public void Oldest_available_lot_is_recommended_once_per_part_and_selected_unit_is_excluded()
    {
        using var conn = Open();
        using var cmd = new SqlCommand(Fixture + """
            INSERT @Inventory VALUES('OLDER',NULL,'PART','FG-1',1,'20260901');
            INSERT @Lots VALUES(3,'OLDER','CONFIRMED'); INSERT @Img VALUES(3,'CODE1');
            """ + Isolate(FinishedGoodsFifo.CandidatesSql), conn);
        cmd.Parameters.AddWithValue("@Barcode", "NEW-PALLET");
        cmd.Parameters.AddWithValue("@Customer", "C1");
        cmd.Parameters.AddWithValue("@DeliveryId", DBNull.Value);
        using var r = cmd.ExecuteReader();
        Assert.True(r.Read()); Assert.Equal("OLDER", r["LotNo"]); Assert.False(r.Read());
    }

    [SkippableFact]
    public void Direct_lot_uses_selected_customer_only_for_unknown_selected_stock_not_unknown_candidates()
    {
        using var conn = Open();
        using var cmd = new SqlCommand(Fixture + "DELETE @Img WHERE LotID=1;" + Isolate(FinishedGoodsFifo.CandidatesSql), conn);
        cmd.Parameters.AddWithValue("@Barcode", "NEW-LOT");
        cmd.Parameters.AddWithValue("@Customer", "C1");
        cmd.Parameters.AddWithValue("@DeliveryId", DBNull.Value);
        using var r = cmd.ExecuteReader(); Assert.True(r.Read()); Assert.Equal("OLD-LOT", r["LotNo"]);
    }

    [SkippableTheory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Completion_checks_all_loaded_roots_once_and_never_recommends_reserved_stock(bool olderAlreadyLoaded, bool expected)
    {
        using var conn = Open();
        using var cmd = new SqlCommand(Fixture + "INSERT @Loads VALUES('NEW-LOT',NULL,1,'NEW-PALLET');" +
            (olderAlreadyLoaded ? "INSERT @Loads VALUES('OLD-LOT',NULL,2,'OLD-PALLET');" : "") +
            Isolate(FinishedGoodsFifo.CandidatesSql), conn);
        cmd.Parameters.AddWithValue("@Barcode", "");
        cmd.Parameters.AddWithValue("@Customer", "C1");
        cmd.Parameters.AddWithValue("@DeliveryId", 1);
        using var r = cmd.ExecuteReader(); Assert.Equal(expected, r.Read());
        if (expected) Assert.Equal("OLD-LOT", r["LotNo"]);
    }

    static string Isolate(string sql)
    {
        foreach (var (table, variable) in new[] {("WH_Inventory","Inventory"),("MD_Location","Locations"),
            ("FG_CustomerReturn","Returns"),("tbl_Lot","Lots"),("PR_ImgLot","Img"),("PR_PntLot","Pnt"),
            ("MD_Customer","Customers"),("FG_DeliveryLoad","Loads")}) sql = sql.Replace($"dbo.{table}", $"@{variable}");
        Assert.DoesNotContain("dbo.", sql, StringComparison.OrdinalIgnoreCase);
        return sql.Replace(" WITH(READCOMMITTED)", "");
    }

    static SqlConnection Open()
    {
        var value = Environment.GetEnvironmentVariable("AMES_TEST_CONN");
        Skip.If(string.IsNullOrWhiteSpace(value), "Table-variable fixtures only; set AMES_TEST_CONN explicitly.");
        var conn = new SqlConnection(value); conn.Open(); return conn;
    }

    static readonly Dictionary<string,string> Variants = new()
    {
        ["available"]="",
        ["same-date"]="UPDATE @Inventory SET ReceivedAt='20261020' WHERE LotNo='OLD-LOT';",
        ["newer"]="UPDATE @Inventory SET ReceivedAt='20261021' WHERE LotNo='OLD-LOT';",
        ["other-customer"]="UPDATE @Img SET CustomerCode='CODE2' WHERE LotID=2;",
        ["unknown-customer"]="DELETE @Img WHERE LotID=2;",
        ["unlocated"]="UPDATE @Inventory SET LocationNo=NULL WHERE LotNo='OLD-LOT';",
        ["non-fg"]="UPDATE @Locations SET LocationType='WH' WHERE LocationID='FG-1';",
        ["primary"]="UPDATE @Locations SET LocationLevel=1 WHERE LocationID='FG-1';",
        ["inactive"]="UPDATE @Locations SET ActiveFlag=0 WHERE LocationID='FG-1';",
        ["inspection"]="INSERT @Returns VALUES('OLD-LOT','Inspecting');",
        ["rejected"]="INSERT @Returns VALUES('OLD-LOT','Rejected');",
        ["defective"]="UPDATE @Lots SET Status='DEFECT' WHERE LotID=2;",
        ["reserved"]="INSERT @Loads VALUES('OLD-LOT',NULL,1,'OLD-PALLET');",
        ["shipped-load"]="INSERT @Loads VALUES('OLD-LOT',1,1,'OLD-PALLET');",
        ["invalid-sibling"]="INSERT @Inventory VALUES('SIBLING','OLD-PALLET','OTHER',NULL,1,'20261001');",
        ["mixed-customer"]="INSERT @Inventory VALUES('SIBLING','OLD-PALLET','OTHER','FG-1',1,'20261001'); INSERT @Lots VALUES(3,'SIBLING','CONFIRMED'); INSERT @Img VALUES(3,'CODE2');",
        ["pnt"]="DELETE @Img WHERE LotID=2; INSERT @Pnt VALUES(2,'CODE1');",
        ["no-pallet"]="UPDATE @Inventory SET ParentLotNo=NULL WHERE LotNo='OLD-LOT'; DELETE @Inventory WHERE LotNo='OLD-PALLET';",
        ["unrelated-cycle"]="INSERT @Inventory VALUES('CYCLE-1','CYCLE-2',NULL,NULL,0,NULL),('CYCLE-2','CYCLE-1',NULL,NULL,0,NULL),('CYCLE-LEAF','CYCLE-1','OTHER','FG-1',1,'20260101');",
        ["candidate-cycle"]="UPDATE @Inventory SET ParentLotNo='CYCLE' WHERE LotNo='OLD-PALLET'; INSERT @Inventory VALUES('CYCLE','OLD-PALLET',NULL,NULL,0,NULL);"
    };

    const string Fixture = """
        DECLARE @Inventory TABLE(LotNo nvarchar(50),ParentLotNo nvarchar(50),PartNo varchar(20),LocationNo varchar(20),Qty decimal(18,3),ReceivedAt datetime2);
        DECLARE @Locations TABLE(LocationID varchar(20),LocationLevel int,ActiveFlag bit,LocationType varchar(10));
        DECLARE @Returns TABLE(LotNo nvarchar(50),Status varchar(20));
        DECLARE @Lots TABLE(LotID int,LotCode varchar(50),Status varchar(20));
        DECLARE @Img TABLE(LotID int,CustomerCode varchar(20));
        DECLARE @Pnt TABLE(LotID int,CustomerCode varchar(20));
        DECLARE @Customers TABLE(CustomerID varchar(20),CustomerCode varchar(20));
        DECLARE @Loads TABLE(LotNo nvarchar(50),ShipmentID int,DeliveryID int,RootLotNo nvarchar(50));
        INSERT @Customers VALUES('C1','CODE1'),('C2','CODE2');
        INSERT @Locations VALUES('FG-1',2,1,'FG');
        INSERT @Inventory VALUES('NEW-PALLET',NULL,NULL,NULL,1,'20261020'),('NEW-LOT','NEW-PALLET','PART','FG-1',1,'20261020'),
            ('OLD-PALLET',NULL,NULL,NULL,1,'20261001'),('OLD-LOT','OLD-PALLET','PART','FG-1',1,'20261001');
        INSERT @Lots VALUES(1,'NEW-LOT','CONFIRMED'),(2,'OLD-LOT','CONFIRMED');
        INSERT @Img VALUES(1,'CODE1'),(2,'CODE1');
        """;
}
