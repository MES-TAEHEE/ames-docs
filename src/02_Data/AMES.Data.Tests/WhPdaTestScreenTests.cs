using AMES.Data.Repositories;
using Xunit;

namespace AMES.Data.Tests;

public sealed class WhPdaTestScreenTests
{
    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string path="")=>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!,"../../.."));
    private static string Read(string path)=>File.ReadAllText(Path.Combine(Root(),path));
    private static WarehouseInboundTestRepository.StorageRow Row(string barcode,string? location=null)
        =>new("LOCAL",barcode,barcode,"PART","Part",20,"EA","DN-1",null,location,"CASE-1");
    [Fact]
    public void A_box_scan_shows_only_that_box_and_requires_relocation_if_stored()
    {
        var scan=WarehouseInboundTestRepository.BuildStorageSelection("BOX-1",[Row("BOX-1","MA01A1F1"),Row("BOX-2")])!;
        Assert.Equal("BOX",scan.Kind);Assert.Single(scan.Rows);Assert.True(scan.RequiresRelocation);Assert.Equal(["BOX-1"],scan.Selected);
    }
    [Fact]
    public void A_case_selects_all_pending_boxes_but_not_stored_boxes()
    {
        var scan=WarehouseInboundTestRepository.BuildStorageSelection("CASE-1",[Row("BOX-1"),Row("BOX-2","MA01A1F1")])!;
        Assert.Equal("CASE",scan.Kind);Assert.Equal(2,scan.Rows.Count);Assert.Equal(["BOX-1"],scan.Selected);Assert.False(scan.RequiresRelocation);
    }
    [Fact]
    public void A_delivery_note_is_view_only_and_an_unknown_barcode_has_no_selection()
    {
        var rows=new List<WarehouseInboundTestRepository.StorageRow>{Row("BOX-1"),Row("BOX-2")};
        var scan=WarehouseInboundTestRepository.BuildStorageSelection("DN-1",rows)!;
        Assert.Equal("DELIVERY_NOTE",scan.Kind);Assert.Empty(scan.Selected);
        Assert.Null(WarehouseInboundTestRepository.BuildStorageSelection("NO-MATCH",rows));
    }
    [Fact]
    public void Web_test_matches_PDA_workflow_and_never_enables_row_click_selection()
    {
        var page=Read("src/06_Web/AMES.Web/Components/Pages/Wh/InboundTest.razor");
        Assert.Contains("WarehouseInboundTester",page);Assert.Contains("WarehousePutAwayTester",page);
        Assert.Contains("UserId=\"@_userId\"",page);Assert.Contains("Actor=\"@_actor\"",page);
        var inbound=Read("src/06_Web/AMES.Web/Components/Shared/WarehouseInboundTester.razor");
        Assert.Contains("CANCEL INCOMING",inbound);Assert.Contains("SCAN REQUIRED",inbound);Assert.Contains("BULK RECEIVE",inbound);
        Assert.Contains("_scanned.Add(box.Barcode)",inbound);Assert.Contains("!CanWrite",inbound);
        var storage=Read("src/06_Web/AMES.Web/Components/Shared/WarehousePutAwayTester.razor");
        Assert.Contains("ALREADY PUT AWAY",storage);Assert.Contains("AT @row.LocationNo",storage);
        Assert.Contains("LOCATION BARCODE SCAN",storage);Assert.Contains("VIEW ONLY",storage);
        Assert.DoesNotContain("@onclick",storage);Assert.Contains("_selected=selection.Selected.ToHashSet",storage);
        var css=Read("src/06_Web/AMES.Web/Components/Pages/Wh/InboundTest.razor.css");
        Assert.Contains("width:430px",css);Assert.Contains("overflow-y:auto",css);
    }
    [Fact]
    public void Web_test_opens_on_two_PDA_menu_tiles_with_back_navigation()
    {
        var page=Read("src/06_Web/AMES.Web/Components/Pages/Wh/InboundTest.razor");
        Assert.Contains("string _screen=\"\"",page);
        Assert.DoesNotContain("RadzenSelectBar",page);
        Assert.Contains("pda-test-menu-tile inbound",page);Assert.Contains("pda-test-menu-tile putaway",page);
        Assert.Equal(2,page.Split("OnBack=\"ShowMenu\"").Length-1);
        Assert.Contains("void ShowMenu()=>_screen=\"\"",page);
        foreach(var component in new[]{"WarehouseInboundTester","WarehousePutAwayTester"})
        {
            var source=Read($"src/06_Web/AMES.Web/Components/Shared/{component}.razor");
            var back=source.Split('\n').Single(line=>line.Contains("Icon=\"arrow_back\""));
            Assert.Contains("OnBack.InvokeAsync()",back);Assert.Contains("Disabled=\"_busy\"",back);
        }
        Assert.Contains("aspect-ratio:1",Read("src/06_Web/AMES.Web/Components/Pages/Wh/InboundTest.razor.css"));
    }
    [Fact]
    public void Web_putaway_uses_PDA_SQL_and_rechecks_permissions_and_stock_before_atomic_save()
    {
        var source=Read("src/02_Data/AMES.Data/Repositories/WarehouseInboundTestRepository.cs");
        Assert.Contains("PdaWarehouseSql.PutAwayInventory",source);Assert.Contains("PdaWarehouseSql.InboundMove",source);
        Assert.Contains("PdaWarehouseSql.InboundCancel",source);Assert.Contains("RequireAccess(userId,receive:true);CheckActor(actor)",source);
        Assert.Contains("IsolationLevel.Serializable",source);Assert.Contains("WITH(UPDLOCK,HOLDLOCK)",source);Assert.Contains("tx.Commit()",source);
        Assert.Contains("reader[\"YN\"].ToString()==\"N\"",source);
        Assert.Contains("PdaWarehouseSql.PutAwayInventory",Read("src/04_Api/AMES.Api/Endpoints/WhEndpoints.cs"));
    }
    [Fact]
    public void Damage_and_defect_are_not_quantity_adjustment_reasons()
    {
        var page=Read("src/05_Pda/AMES.Pda/Components/Pages/Wh/Wh03InventoryStatus.razor");
        Assert.Contains("is not (\"DAMAGED\" or \"DEFECT\" or \"DEFECTIVE\")",page);
        Assert.DoesNotContain("_invAdjustReason = \"DAMAGED\"",page);
        var sql=PdaWarehouseSql.AdjustSave;
        Assert.Contains("@Reason IN (N'DAMAGED', N'DEFECT', N'DEFECTIVE')",sql);
        Assert.True(sql.IndexOf("THROW 51521",StringComparison.Ordinal)<sql.IndexOf("UPDATE dbo.WH_Inventory",StringComparison.Ordinal));
        Assert.Contains("COUNT_DIFF",page);
    }

    [SkippableFact]
    public void Received_box_and_location_load_from_actual_schema_without_writes()
    {
        var connection=Environment.GetEnvironmentVariable("AMES_TEST_CONN");
        Skip.If(string.IsNullOrWhiteSpace(connection),"Set AMES_TEST_CONN for read-only PDA web verification.");
        var factory=new AMES.Data.Connection.AmesConnectionFactory(connection!);
        using var conn=factory.OpenConnection();
        using var userCmd=new Microsoft.Data.SqlClient.SqlCommand("SELECT Id FROM dbo.AspNetUsers WHERE NormalizedUserName='ADMIN@AMES.LOCAL'",conn);
        var user=userCmd.ExecuteScalar() as string;
        Skip.If(user is null,"Administrator account is not present.");
        var repo=new WarehouseInboundTestRepository(factory);
        Assert.True(repo.CanRead(user!));
        using var cmd=new Microsoft.Data.SqlClient.SqlCommand("""
            SELECT TOP(1) W.LotNo FROM dbo.WH_Inventory W
            JOIN dbo.SCM_DeliveryBox B ON B.BoxNumber=W.LotNo AND B.ActiveFlag=1
            JOIN dbo.SCM_DeliveryLine L ON L.DeliveryLineID=B.DeliveryLineID
            JOIN dbo.SCM_Delivery D ON D.DeliveryID=L.DeliveryID
            WHERE W.Qty>0 AND D.Status IN('Shipped','Received') ORDER BY W.LotNo;
            """,conn);
        var barcode=cmd.ExecuteScalar() as string;
        Skip.If(barcode is null,"No received portal box is present.");
        var inbound=repo.Scan(user!,"LOCAL",barcode!);
        Assert.Equal("LOT",inbound.Kind);Assert.True(Assert.Single(inbound.Rows).Received);
        var storage=repo.ScanPutAway(user!,barcode!);
        Assert.Equal("BOX",storage.Kind);Assert.Equal(barcode,Assert.Single(storage.Rows).Barcode);
        using var locationCmd=new Microsoft.Data.SqlClient.SqlCommand("""
            SELECT TOP(1) LocationID FROM dbo.MD_Location WHERE COALESCE(ActiveFlag,1)=1
                AND COALESCE(LocationType,'') NOT IN('BLOCKED','HOLD','QUARANTINE','INACTIVE') ORDER BY LocationID;
            """,conn);
        var location=locationCmd.ExecuteScalar() as string;
        if(location is not null)Assert.Equal(location,repo.ScanPutAwayLocation(user!,location).LocationNo);
    }
}
