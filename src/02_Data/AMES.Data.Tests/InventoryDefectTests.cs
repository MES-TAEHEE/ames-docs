using AMES.Data.Repositories;
using AMES.Data.Connection;
using Xunit;

namespace AMES.Data.Tests;

public sealed class InventoryDefectTests
{
    private static string Root([System.Runtime.CompilerServices.CallerFilePath]string path="")
        =>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!,"../../.."));
    private static string Read(string path)=>File.ReadAllText(Path.Combine(Root(),path));
    [Theory]
    [InlineData("")][InlineData("  ")]
    public void Reject_requires_a_reason(string reason)=>Assert.Throws<ArgumentException>(()=>FinishedGoodsRepository.ValidateDefectReason(reason));
    [Fact]
    public void Reason_is_trimmed_and_bounded()
    {
        Assert.Equal("Crack",FinishedGoodsRepository.ValidateDefectReason(" Crack "));
        Assert.Throws<ArgumentException>(()=>FinishedGoodsRepository.ValidateDefectReason(new string('X',501)));
    }

    [Theory]
    [InlineData(0,10,3)][InlineData(-1,10,3)][InlineData(11,10,3)]
    [InlineData(1.0001,10,3)][InlineData(0.5,10,0)]
    public void Defect_quantity_must_fit_stock_and_unit_precision(decimal quantity,decimal stock,int digits)
        =>Assert.Throws<ArgumentException>(()=>FinishedGoodsRepository.ValidateDefectQuantity(quantity,stock,digits));

    [Theory]
    [InlineData(1,1,0)][InlineData(10,10,0)][InlineData(1.25,1.25,3)]
    public void Full_lot_defect_quantities_are_supported(decimal quantity,decimal stock,int digits)
        =>Assert.Equal(quantity,FinishedGoodsRepository.ValidateDefectQuantity(quantity,stock,digits));

    [Fact]
    public void All_items_require_the_whole_lot_without_quantity_input()
    {
        Assert.Throws<ArgumentException>(()=>FinishedGoodsRepository.ValidateDefectQuantity(1,10,0));
        Assert.Equal(10,FinishedGoodsRepository.ValidateDefectQuantity(10,10,0));
    }
    [Fact]
    public void Rejection_is_authorized_and_stock_is_checked_before_any_write()
    {
        var source=Read("src/02_Data/AMES.Data/Repositories/FinishedGoodsRepository.Defects.cs");
        var write=source.IndexOf("UPDATE dbo.WH_Inventory",StringComparison.Ordinal);
        foreach(var check in new[]{"P.ScreenCode='FG-DEFECT'","P.PermissionLevel LIKE '%A%'","@Qty<>@Expected","ShipmentID IS NULL","@Defect>@Qty","ROUND(@Defect,@Digits)"})
            Assert.True(source.IndexOf(check,StringComparison.Ordinal)<write);
        Assert.Contains("Qty=@After",source);Assert.Contains("@After=@Qty-@Defect",source);
        Assert.Contains("@Qty,-@Defect,@After,'DEFECT','INV_DEFECT'",source);
        Assert.Contains("ISNULL(W.LocationNo,'')=ISNULL(@Location,'')",source);
        Assert.DoesNotContain("ItemType='ASSY'",source);
        Assert.Contains("IF @Defect<>@Qty",source);Assert.DoesNotContain("dbo.tbl_Lot",source);
        Assert.Contains("INSERT dbo.SYS_AuditLog",source);Assert.Contains("tx.Commit()",source);
        Assert.DoesNotContain("UPDATE dbo.tbl_Lot",source);Assert.DoesNotContain("UPDATE dbo.PR_ProductionResult",source);
        Assert.Contains("This LOT was rejected as defective",PdaFinishedGoodsSql.PutAway);
        Assert.Contains("SourceType IN('FG_DEFECT','INV_DEFECT') AND QtyAfter=0",PdaFinishedGoodsSql.PutAway);
        var lookup=Read("src/06_Web/AMES.Web/Components/Shared/InventoryLookup.razor");
        Assert.DoesNotContain("RadzenNumeric",lookup);
        Assert.Contains("MarkInventoryDefective",lookup);
        Assert.Contains("_eligibleLots.Remove",lookup);
        Assert.Contains("ValidateDefectQuantity(lot.Qty,lot.Qty",lookup);
        Assert.DoesNotContain("_defectQty",lookup);
        var rowAction=lookup.Split('\n').Single(line=>line.Contains("Click=\"@(()=>OpenDefect(row))\""));
        Assert.DoesNotContain("Text=",rowAction);Assert.Contains("Icon=\"report_problem\"",rowAction);
        Assert.Contains("aria-label=",rowAction);
    }

    [Fact]
    public void Location_creation_and_fake_inventory_status_are_not_shown()
    {
        var location=Read("src/06_Web/AMES.Web/Components/Pages/Md/Fd/Location.razor");
        Assert.DoesNotContain("OpenCreate",location);Assert.DoesNotContain("OpenLineLocations",location);
        var lookup=Read("src/06_Web/AMES.Web/Components/Shared/InventoryLookup.razor");
        Assert.DoesNotContain("Col.Status",lookup);Assert.Contains("RecheckApproveAsync",lookup);
        Assert.DoesNotContain("Reject LOT",lookup);
        Assert.Contains("WH.Inventory.DefectAction",lookup);
        Assert.Contains("@if(_eligibleLots.Contains(row.LotNo",lookup);
    }

    [Fact]
    public void Lot_defect_is_in_Warehouse_and_all_master_items_are_shown_by_default()
    {
        var page=Read("src/06_Web/AMES.Web/Components/Pages/Wh/Inventory.razor");
        Assert.Contains("AllowLotDefect=\"true\"",page);
        Assert.DoesNotContain("IncludeFinishedGoods",page);
        Assert.Contains("includeFinishedGoods: true",page);
        Assert.Contains("InventorySearchRows.FromMaster(MD.ListItems(), stock)",page);
        Assert.Contains("SCM.ListItemVendors()",page);
        Assert.DoesNotContain("AllowLotDefect=\"true\"",Read("src/06_Web/AMES.Web/Components/Pages/Fg/Inventory.razor"));
        Assert.Equal("wh/inventory/defect",FinishedGoodsRepository.DefectPermissionHref);
        var repository=Read("src/02_Data/AMES.Data/Repositories/WarehouseRepository.cs");
        Assert.Contains("bool includeFinishedGoods = false",repository);
        Assert.Contains("@IncludeFinishedGoods = 1 OR",repository);
    }

    [Fact]
    public void Location_hierarchy_cards_wrap_from_the_left_and_keep_navigation_and_editing()
    {
        var page=Read("src/06_Web/AMES.Web/Components/Shared/LocationMapExplorer.razor");
        Assert.Contains("location-hierarchy-cards",page);
        Assert.Contains("SelectStageRow(row)",page);
        Assert.Contains("if (_selectingFloor) SelectFloor(row.Key)",page);
        Assert.Contains("else SelectCard(row.Key)",page);
        Assert.Contains("@RenderMap()",page);
        Assert.DoesNotContain("location-stage-grid",page);
        Assert.Contains("class=\"location-stage-card\"",page);
        Assert.Contains("DeleteCard(row.Key)",page);
        Assert.Contains("OpenDialog(SelectionKind)",page);
        var css=Read("src/06_Web/AMES.Web/Components/Shared/LocationMapExplorer.razor.css");
        Assert.Contains("flex-wrap: wrap",css);
        Assert.Contains("justify-content: flex-start",css);
        Assert.Contains("align-content: flex-start",css);
        Assert.Contains("aspect-ratio: 1",css);
    }

    [Fact]
    public void Defect_quantity_is_shown_in_both_inventory_grids_without_inflating_available_stock()
    {
        var lookup=Read("src/06_Web/AMES.Web/Components/Shared/InventoryLookup.razor");
        Assert.Equal(2,lookup.Split("Property=\"DefectQty\"").Length-1);
        Assert.Contains("group.Sum(x => x.DefectQty)",lookup);
        Assert.Contains("FilteredInventory.Sum(x => x.Qty)",lookup);
        Assert.Contains("includeDefects: true",Read("src/06_Web/AMES.Web/Components/Pages/Wh/Inventory.razor"));
        var map=Read("src/06_Web/AMES.Web/Components/Shared/LocationMapDatabase.cs");
        Assert.Contains("includeDefects: true",map);
        Assert.Contains("x.Sum(y => y.Qty)",map);
        Assert.Contains("x.Sum(y => y.DefectQty)",map);
        Assert.Contains("Icon=\"task_alt\"",lookup);
        Assert.Contains("else if(row.DefectQty <= 0)",lookup);
        Assert.Contains("row.Qty, row.DefectQty",lookup);
        Assert.Contains("Property=\"DefectQty\"",Read("src/06_Web/AMES.Web/Components/Shared/LocationMapExplorer.razor"));
    }

    [Fact]
    public void Location_print_is_icon_only_and_defects_are_red_in_detail_rows_only()
    {
        var page=Read("src/06_Web/AMES.Web/Components/Shared/LocationMapExplorer.razor");
        var print=page.Split('\n').Single(line=>line.Contains("Click=\"OpenLocationBarcode\""));
        Assert.Contains("Icon=\"print\"",print);Assert.Contains("aria-label=",print);
        Assert.DoesNotContain("Text=",print);
        Assert.DoesNotContain("NG @",page);Assert.DoesNotContain("has-defects",page);
        Assert.DoesNotContain("LocationQuantities(true)",page);
        Assert.Contains("<span class=\"location-defect-qty\">@UomFmt.QtyUnit(row.DefectQty, row.Unit)</span>",page);
        var css=Read("src/06_Web/AMES.Web/Components/Shared/LocationMapExplorer.razor.css");
        Assert.Contains(".location-defect-qty { color: var(--ames-tx-red-400); }",css);
    }

    [SkippableFact]
    public void Defect_history_is_read_once_per_lot_location_and_zero_stock_remains_visible_without_writes()
    {
        var connection=Environment.GetEnvironmentVariable("AMES_TEST_CONN");
        Skip.If(string.IsNullOrWhiteSpace(connection),"Set AMES_TEST_CONN for read-only defect quantity verification.");
        var factory=new AmesConnectionFactory(connection!);
        var warehouse=new WarehouseRepository(factory);
        var normal=warehouse.ListUnifiedInventory(false,includeFinishedGoods:true);
        var withDefects=warehouse.ListUnifiedInventory(false,includeFinishedGoods:true,includeDefects:true);
        Assert.All(normal,row=>{Assert.True(row.Qty>0);Assert.Equal(0,row.DefectQty);});
        Assert.Equal(normal.Sum(row=>row.Qty),withDefects.Sum(row=>row.Qty));
        Assert.Equal(withDefects.Count,withDefects.Select(row=>(row.LotNo,row.PartNo,row.LocationNo)).Distinct().Count());
        using var conn=factory.OpenConnection();
        using var cmd=new Microsoft.Data.SqlClient.SqlCommand("""
            SELECT T.LotNo,T.PartNo,ISNULL(T.LocationNo,''),SUM(-T.QtyChange) FROM dbo.WH_InventoryTransaction T
            WHERE T.SourceType IN ('INV_DEFECT','FG_DEFECT') AND T.QtyChange<0 AND T.PartNo IS NOT NULL
            GROUP BY T.LotNo,T.PartNo,ISNULL(T.LocationNo,'');
            """,conn);
        using var reader=cmd.ExecuteReader();
        var total=0m;
        while(reader.Read())
        {
            var row=Assert.Single(withDefects,row=>row.LotNo==reader.GetString(0) && row.PartNo==reader.GetString(1) && row.LocationNo==reader.GetString(2));
            Assert.Equal(reader.GetDecimal(3),row.DefectQty);
            total+=reader.GetDecimal(3);
        }
        Assert.Equal(total,withDefects.Sum(row=>row.DefectQty));
    }

    [Fact]
    public void Excel_export_requires_confirmation_before_building_or_downloading_the_filtered_snapshot()
    {
        var lookup=Read("src/06_Web/AMES.Web/Components/Shared/InventoryLookup.razor");
        var export=lookup[lookup.IndexOf("private async Task ExportXlsx()",StringComparison.Ordinal)..];
        Assert.Contains("var rows = SummaryRows",export);
        Assert.Contains("if (rows.Count == 0) return",export);
        Assert.Contains("WH.Inventory.ExportConfirm",export);
        Assert.Contains("}) != true) return",export);
        Assert.Contains("var data = rows.Select",export);
        Assert.True(export.IndexOf("Dialog.Confirm",StringComparison.Ordinal)<export.IndexOf("XlsxWriter.Build",StringComparison.Ordinal));
        Assert.True(export.IndexOf("Dialog.Confirm",StringComparison.Ordinal)<export.IndexOf("amesDownload",StringComparison.Ordinal));
    }

    [SkippableFact]
    public void Inventory_filters_and_eligible_lots_load_without_writes()
    {
        var connection=Environment.GetEnvironmentVariable("AMES_TEST_CONN");
        Skip.If(string.IsNullOrWhiteSpace(connection),"Set AMES_TEST_CONN for read-only inventory verification.");
        var factory=new AmesConnectionFactory(connection!);
        var warehouse=new WarehouseRepository(factory);
        Assert.DoesNotContain(warehouse.ListUnifiedInventory(false),row=>row.AreaCode=="FG_AREA");
        Assert.All(warehouse.ListUnifiedInventory(true),row=>Assert.Equal("FG_AREA",row.AreaCode));
        warehouse.ListUnifiedInventory(false,includeFinishedGoods:true);
        var lots=new FinishedGoodsRepository(factory).ListDefectLots();
        using var conn=factory.OpenConnection();
        using var cmd=new Microsoft.Data.SqlClient.SqlCommand("""
            SELECT W.LotNo FROM dbo.WH_Inventory W
            WHERE W.Qty>0 AND NULLIF(LTRIM(RTRIM(W.PartNo)),'') IS NOT NULL
              AND NOT EXISTS(SELECT 1 FROM dbo.WH_Inventory C WHERE C.ParentLotNo=W.LotNo);
            """,conn);
        using var reader=cmd.ExecuteReader();
        var expected=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while(reader.Read())expected.Add(reader.GetString(0));
        Assert.True(expected.SetEquals(lots));
    }

}
