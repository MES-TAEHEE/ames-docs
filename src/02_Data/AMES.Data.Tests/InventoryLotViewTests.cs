using AMES.Data.Repositories;
using AMES.Web.Components.Shared;
using AMES.Web.Services;
using Xunit;

namespace AMES.Data.Tests;

public sealed class InventoryLotViewTests
{
    private static readonly DateTime Old = new(2026, 11, 1);
    private static readonly DateTime Recent = new(2026, 11, 20);
    private static ScmRepository.ItemVendorRow Vendor(string part, string code)
        => new(part, code, "Vendor name must not be displayed", true, "SUPPLIER", "test", Old);

    [Fact]
    public void Classification_follows_item_master_not_the_current_location()
    {
        LocationMapStock[] stock = [
            new("FG-1", "RESIN", "Resin", "M1", 100, "G", ItemType:"MATERIAL"),
            new("WH-1", "SUB", "Sub", "S1", 1, "EA", ItemType:" sub "),
            new("", "ASSY", "Assy", "A1", 1, "EA", ItemType:"ASSY")
        ];
        Assert.Equal("M1", Assert.Single(InventorySearchRows.Lots(stock, [], false)).LotNo);
        Assert.Equal(new[]{"A1", "S1"}, InventorySearchRows.Lots(stock, [], true).Select(x => x.LotNo));
    }

    [Fact]
    public void Each_part_is_ordered_by_oldest_receipt_with_unknown_dates_last()
    {
        LocationMapStock[] stock = [
            new("L1", "P2", "", "Z", 1, "EA", ReceivedAt:Old),
            new("L1", "P1", "", "A-unknown", 1, "EA"),
            new("L1", "P1", "", "Z-recent", 1, "EA", ReceivedAt:Recent),
            new("L1", "P1", "", "B-old", 1, "EA", ReceivedAt:Old),
            new("L1", "P1", "", "A-old", 1, "EA", ReceivedAt:Old)
        ];
        Assert.Equal(new[]{"A-old", "B-old", "Z-recent", "A-unknown", "Z"},
            InventorySearchRows.Lots(stock, [], false).Select(x => x.LotNo));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("0000")]
    public void Floor_uses_display_location_0000_without_changing_the_inventory(string? location)
    {
        var original = new LocationMapStock(location!, "P1", "Material", "LOT1", 100.5m, "G");
        var row = Assert.Single(InventorySearchRows.Lots([original], [], false));
        Assert.True(row.IsFloor);
        Assert.Equal("0000", row.LocationNo);
        Assert.Equal(100.5m, row.FloorQty);
        Assert.Equal(0, row.StoredQty);
        Assert.Equal(location, original.LocationNo);
        Assert.Single(InventorySearchRows.Filter([original], [], "0000", "", "", "", "", ""));
    }

    [Fact]
    public void Floor_is_first_within_each_part_then_receipts_are_oldest_first()
    {
        LocationMapStock[] stock = [
            new("A1", "P1", "", "STORED-OLD", 1, "EA", ReceivedAt:Old),
            new("A1", "P1", "", "STORED-RECENT", 1, "EA", ReceivedAt:Recent),
            new("", "P1", "", "FLOOR-RECENT", 1, "EA", ReceivedAt:Recent),
            new("0000", "P1", "", "FLOOR-OLD", 1, "EA", ReceivedAt:Old),
            new("", "P2", "", "P2-FLOOR", 1, "EA", ReceivedAt:Old)
        ];
        Assert.Equal(new[]{"FLOOR-OLD", "FLOOR-RECENT", "STORED-OLD", "STORED-RECENT", "P2-FLOOR"},
            InventorySearchRows.Lots(stock, [], false).Select(x => x.LotNo));
    }

    [Fact]
    public void Inventory_pages_are_separate_and_the_shipment_tabs_keep_their_own_read_permissions()
    {
        static string Read(string path, [System.Runtime.CompilerServices.CallerFilePath] string file = "")
            => File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../..", path)));
        var workspace = Read("src/06_Web/AMES.Web/Components/Pages/Fg/ShipmentManagement.razor");
        Assert.Contains("@page \"/fg/shipping-orders\"", workspace);
        Assert.Contains("@page \"/fg/shipment-plan\"", workspace);
        Assert.Contains("Permissions.IsVisible(\"fg/shipping-orders\")", workspace);
        Assert.Contains("Permissions.IsVisible(\"fg/shipment-plan\")", workspace);
        Assert.Contains("@if (_showOrders)", workspace);
        Assert.Contains("@if (_showPlan)", workspace);
        Assert.Contains("<ShippingOrders Embedded=\"true\"", workspace);
        Assert.Contains("<ShipmentPlan Embedded=\"true\"", workspace);
        var inventory = Read("src/06_Web/AMES.Web/Components/Shared/InventoryLookup.razor");
        Assert.DoesNotContain("<AmesTabs", inventory);
        Assert.Contains("IsFinishedGoods => FinishedGoodsOnly", inventory);
        var catalog = Read("src/06_Web/AMES.Web/Services/MenuCatalog.cs");
        foreach(var (route,code) in new[]{("fg/inventory","FG-01"),("fg/shipments","FG-03"),("fg/customer-returns","FG-04")})
            Assert.Contains($"\"{route}\" => \"{code}\"", catalog);
        Assert.DoesNotContain("path.Equals(\"fg/inventory\"", catalog);
    }

    [Fact]
    public void Zero_floor_rows_and_historical_defects_do_not_return_to_the_stock_list()
    {
        LocationMapStock[] stock = [
            new("", "P1", "", "ZERO", 0, "G", DefectQty:30),
            new("", "P1", "", "NEGATIVE", -1, "G"),
            new("", "P1", "", null, 0, "G"),
            new("A1", "P1", "", "STORED", 25.5m, "G", DefectQty:10)
        ];
        var row = Assert.Single(InventorySearchRows.Lots(stock, [], false));
        Assert.Equal("STORED", row.LotNo);
        Assert.Equal(25.5m, row.StoredQty);
        Assert.Equal(0, row.FloorQty);
    }

    [Fact]
    public void Receipt_vendor_code_takes_precedence_over_part_vendor_mapping()
    {
        ScmRepository.ItemVendorRow[] vendors = [Vendor("P1", "V001"), Vendor("P1", "V002")];
        LocationMapStock[] stock = [
            new("A1", "P1", "", "V1-LOT", 10, "G", VendorCode:"V001"),
            new("A1", "P1", "", "V2-LOT", 20, "G", VendorCode:"V002"),
            new("A1", "P2", "", "UNLINKED", 30, "G")
        ];
        var filtered = InventorySearchRows.Filter(stock, vendors, "", "", "", "v002", "", "");
        var row = Assert.Single(InventorySearchRows.Lots(filtered, vendors, false));
        Assert.Equal("V2-LOT", row.LotNo);
        Assert.Equal("V002", row.VendorCodes);
    }

    [Fact]
    public void Missing_receipt_vendor_falls_back_to_distinct_codes_without_names()
    {
        var row = Assert.Single(InventorySearchRows.Lots(
            [new("A1", "P1", "", "LOT", 1, "EA")],
            [Vendor("P1", "V002"), Vendor("P1", "V001"), Vendor("P1", "V001")], false));
        Assert.Equal("V001, V002", row.VendorCodes);
    }
}
