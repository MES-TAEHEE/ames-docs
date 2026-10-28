using System.Data;
using System.Reflection;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

public sealed class CustomerReturnInspectionTests
{
    [Fact]
    public void General_location_write_cannot_bypass_finished_goods_validation()
    {
        var sql = PdaWarehouseSql.InboundMove;
        Assert.Contains("ItemNo=@PartNo AND ItemType='ASSY'", sql);
        Assert.Contains("UPPER(LocationType)='FG'", sql);
        Assert.Contains("Finished goods require the ASSY validation flow", sql);
        Assert.True(sql.IndexOf("Finished goods require", StringComparison.Ordinal)
            < sql.IndexOf("UPDATE dbo.WH_Inventory", StringComparison.Ordinal));
    }

    static string InspectionSql => (string)typeof(FinishedGoodsRepository)
        .GetField("ReturnInspectionSql", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;

    [Theory]
    [InlineData("Open", true)]
    [InlineData("Inspecting", true)]
    [InlineData("INSPECTING", true)]
    [InlineData("Closed", false)]
    [InlineData("Rejected", false)]
    [InlineData(null, false)]
    public void Only_pending_returns_can_be_inspected(string? status, bool pending) =>
        Assert.Equal(pending, FinishedGoodsRepository.IsReturnAwaitingInspection(status));

    [Fact]
    public void Defective_returns_require_a_reason_and_notes_are_not_truncated()
    {
        Assert.Throws<ArgumentException>(() => FinishedGoodsRepository.ValidateReturnInspectionNote(false, " "));
        Assert.Throws<ArgumentException>(() => FinishedGoodsRepository.ValidateReturnInspectionNote(true, new string('x', 501)));
        Assert.Equal("damaged", FinishedGoodsRepository.ValidateReturnInspectionNote(false, " damaged "));
        Assert.Equal("", FinishedGoodsRepository.ValidateReturnInspectionNote(true, null));
    }

    [Fact]
    public void Receipt_and_every_FG_write_path_enforce_inspection()
    {
        Assert.Contains("'Inspecting',@Now", PdaFinishedGoodsSql.ReturnReceive);
        Assert.Contains("InventoryStatus='INSPECTING'", PdaFinishedGoodsSql.ReturnReceive);
        Assert.Contains("FG_SHIPPING_ORDERS", PdaFinishedGoodsSql.ReturnReceive);
        foreach (var sql in new[] { PdaFinishedGoodsSql.OutboundScan, PdaFinishedGoodsSql.OutboundComplete,
            PdaFinishedGoodsSql.PutAway, PdaFinishedGoodsSql.AdjustSave, PdaWarehouseSql.InboundMove,
            WarehouseRepository.WarehouseBoxPredicate, PdaStockScope.Inventory(PdaWarehouseSql.Inventory, "FG") })
        {
            Assert.Contains("dbo.FG_CustomerReturn", sql);
            Assert.Contains("'Open','Inspecting'", sql);
        }
    }

    // SQL table variables only: all referenced application tables are replaced before execution.
    static string IsolatedSql => InspectionSql
        .Replace("dbo.AspNetUserRoles", "@UserRoles").Replace("dbo.SYS_RolePermission", "@Permissions")
        .Replace("dbo.SYS_Screen", "@Screens").Replace("dbo.AspNetUsers", "@Users")
        .Replace("dbo.SYS_UserProfile", "@Profiles").Replace("dbo.FG_CustomerReturn", "@Returns")
        .Replace("dbo.WH_InventoryTransaction", "@Transactions").Replace("dbo.WH_Inventory", "@Inventory")
        .Replace("dbo.FG_DeliveryLoad", "@Loads").Replace("dbo.tbl_Lot", "@Lots").Replace("dbo.SYS_AuditLog", "@Audit")
        .Replace(" WITH(UPDLOCK,HOLDLOCK)", "").Replace(" WITH(HOLDLOCK)", "");

    const string Fixture = """
        DECLARE @UserRoles TABLE(UserId nvarchar(450),RoleId nvarchar(450));
        DECLARE @Permissions TABLE(RoleID nvarchar(450),ModuleCode varchar(10),ScreenCode varchar(20),PermissionLevel varchar(10));
        DECLARE @Screens TABLE(ScreenCode varchar(20),ModuleCode varchar(10),PermissionCriteria varchar(10));
        DECLARE @Users TABLE(Id nvarchar(450),LockoutEnd datetimeoffset);
        DECLARE @Profiles TABLE(UserID nvarchar(450),AccountStatus varchar(20));
        DECLARE @Returns TABLE(ReturnID int,LotNo nvarchar(50),PartNo varchar(20),ReturnQty decimal(18,3),LotID int,
            Status varchar(15),ClosedAt datetime2,ClosedBy varchar(20),ModifiedTS datetime2,ModifiedBy varchar(20));
        DECLARE @Inventory TABLE(LotNo nvarchar(50),PartNo varchar(20),Qty decimal(18,3),LocationNo varchar(50),UpdatedAt datetime2);
        DECLARE @Loads TABLE(LotNo nvarchar(50),ShipmentID int);
        DECLARE @Lots TABLE(LotID int,RemainingQty decimal(18,3),InventoryStatus varchar(30),ModifiedTS datetime2,ModifiedBy varchar(20));
        DECLARE @Transactions TABLE(TransactionTime datetime2,TransactionType varchar(10),PartNo varchar(20),LocationNo varchar(50),LotNo nvarchar(50),
            QtyBefore decimal(18,3),QtyChange decimal(18,3),QtyAfter decimal(18,3),ReasonCode varchar(30),SourceType varchar(30),SourceID int,
            OperatorID varchar(20),Note nvarchar(500),CreatedBy varchar(20),CreatedTS datetime2);
        DECLARE @Audit TABLE(EventTS datetime2,ActorUserID nvarchar(450),ModuleCode varchar(10),ScreenCode varchar(20),ActionType varchar(20),
            TargetEntity varchar(50),TargetID varchar(40),BeforeValueJSON nvarchar(max),AfterValueJSON nvarchar(max),Result varchar(10),Note nvarchar(500),CreatedBy varchar(20),CreatedTS datetime2);
        INSERT @UserRoles VALUES(N'inspector',N'role');
        INSERT @Permissions VALUES(N'role','WEB','FG-DEFECT','R_A');
        INSERT @Screens VALUES('FG-DEFECT','WEB','_X_');
        INSERT @Users VALUES(N'inspector',NULL);
        INSERT @Returns(ReturnID,LotNo,PartNo,ReturnQty,LotID,Status) VALUES(1,N'LOT-RETURN','PART-1',5,1,'Inspecting');
        INSERT @Inventory(LotNo,PartNo,Qty) VALUES(N'LOT-RETURN','PART-1',5);
        INSERT @Lots(LotID,RemainingQty,InventoryStatus) VALUES(1,0,'INSPECTING');
        """;

    [SkippableTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Disposition_preserves_or_rejects_quantity_and_records_actor_atomically(bool accepted)
    {
        using var conn = OpenTestConnection();
        var sql = IsolatedSql;
        Assert.DoesNotContain("dbo.", sql, StringComparison.OrdinalIgnoreCase);
        using var cmd = new SqlCommand(Fixture + sql + """
            SELECT I.Qty,R.Status,R.ClosedBy,L.RemainingQty,T.QtyBefore,T.QtyChange,T.QtyAfter,
                (SELECT COUNT(*) FROM @Audit) AuditCount
            FROM @Inventory I CROSS JOIN @Returns R CROSS JOIN @Lots L CROSS JOIN @Transactions T;
            """, conn);
        Parameters(cmd, accepted);
        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(accepted ? 5m : 0m, reader.GetDecimal(0));
        Assert.Equal(accepted ? "Closed" : "Rejected", reader.GetString(1));
        Assert.Equal("INSPECTOR01", reader.GetString(2));
        Assert.Equal(accepted ? 5m : 0m, reader.GetDecimal(3));
        Assert.Equal(5m, reader.GetDecimal(4));
        Assert.Equal(accepted ? 0m : -5m, reader.GetDecimal(5));
        Assert.Equal(accepted ? 5m : 0m, reader.GetDecimal(6));
        Assert.Equal(1, reader.GetInt32(7));
        Assert.False(reader.Read());
    }

    [SkippableTheory]
    [InlineData("UPDATE @Returns SET Status='Closed';", 52611)]
    [InlineData("UPDATE @Inventory SET Qty=4;", 52612)]
    [InlineData("INSERT @Loads VALUES(N'LOT-RETURN',NULL);", 52613)]
    [InlineData("DELETE @Permissions;", 52610)]
    public void Stale_or_unauthorized_inspections_are_rejected(string mutation, int error)
    {
        using var conn = OpenTestConnection();
        var sql = Fixture + mutation + IsolatedSql;
        Assert.DoesNotContain("dbo.", sql, StringComparison.OrdinalIgnoreCase);
        using var cmd = new SqlCommand(sql, conn);
        Parameters(cmd, true);
        Assert.Equal(error, Assert.Throws<SqlException>(() => cmd.ExecuteNonQuery()).Number);
    }

    static SqlConnection OpenTestConnection()
    {
        var cs = Environment.GetEnvironmentVariable("AMES_TEST_CONN");
        Skip.If(string.IsNullOrWhiteSpace(cs), "Set AMES_TEST_CONN for SQL table-variable-only tests; no application rows are written.");
        var conn = new SqlConnection(cs); conn.Open(); return conn;
    }

    [SkippableFact]
    public void Real_schema_batches_compile_without_executing_any_stock_changes()
    {
        using var conn = OpenTestConnection();
        using (var noexec = new SqlCommand("SET NOEXEC ON;", conn)) noexec.ExecuteNonQuery();
        try
        {
            foreach (var sql in new[] {
                "DECLARE @Return int,@Accepted bit,@Note nvarchar(500),@User nvarchar(450),@Actor varchar(20);" + InspectionSql,
                "DECLARE @Barcode nvarchar(50),@ReturnReason varchar(60),@Note nvarchar(500),@OperatorID varchar(20);" + PdaFinishedGoodsSql.ReturnReceive,
                "DECLARE @Barcode nvarchar(50),@CustomerID varchar(20),@OperatorID varchar(20),@ShippingDeliveryID int;" + PdaFinishedGoodsSql.OutboundComplete,
                "DECLARE @Barcode nvarchar(50),@Location varchar(50),@OperatorID varchar(20);" + PdaFinishedGoodsSql.PutAway })
            {
                using var cmd = new SqlCommand(sql, conn);
                cmd.ExecuteNonQuery();
            }
        }
        finally { using var reset = new SqlCommand("SET NOEXEC OFF;", conn); reset.ExecuteNonQuery(); }
    }

    [SkippableFact]
    public void Existing_return_and_inventory_read_queries_accept_the_inspection_projection()
    {
        var cs = Environment.GetEnvironmentVariable("AMES_TEST_CONN");
        Skip.If(string.IsNullOrWhiteSpace(cs), "Set AMES_TEST_CONN for read-only schema verification.");
        var factory = new AMES.Data.Connection.AmesConnectionFactory(cs!);
        new FinishedGoodsRepository(factory).ListReturns(search: "INSPECTION-SCHEMA-CHECK-NO-ROW");
        new WarehouseRepository(factory).ListUnifiedInventory(false, true, true);
        new RptRepository(factory).ListInventorySku();
        new RptRepository(factory).ListInventory();
    }

    static void Parameters(SqlCommand cmd, bool accepted)
    {
        cmd.Parameters.Add("@Return", SqlDbType.Int).Value = 1;
        cmd.Parameters.Add("@Accepted", SqlDbType.Bit).Value = accepted;
        cmd.Parameters.Add("@User", SqlDbType.NVarChar, 450).Value = "inspector";
        cmd.Parameters.Add("@Actor", SqlDbType.VarChar, 20).Value = "INSPECTOR01";
        cmd.Parameters.Add("@Note", SqlDbType.NVarChar, 500).Value = "Inspection completed";
    }
}
