#:project ../src/06_Web/AMES.Web/AMES.Web.csproj
#:property PublishAot=false

// Pure inventory projection and Excel tests. No host, database, or stock writes.
using AMES.Data.Repositories;
using AMES.Web.Components.Shared;
using AMES.Web.Services;
using System.IO.Compression;
using System.Xml.Linq;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine("PASS: " + message);
}
static MasterDataRepository.ItemRow Item(string no, string type, string category) =>
    new(no, "Master " + no, type, category, false, null, "KG", null, null, null, null, null,
        null, null, null, null, null, false, true, null, null, null, null);
MasterDataRepository.ItemRow[] items = [Item("FABRIC", "MATERIAL", "FABRIC"), Item("RESIN", "MATERIAL", "RESIN"), Item("ASSY", "ASSY", "TRIM")];
LocationMapStock[] stock = [
    new("A", "FABRIC", "Outdated name", "LOT-1", 8.5m, "EA", "EOS", "EOS", "MAT", "Material", 1.5m),
    new("B", "FABRIC", "Outdated name", "LOT-2", 0, "EA", "EOS", "EOS", "MAT", "Material", 2),
    new("A", "UNKNOWN", "Not in master", "LOT-X", 99, "EA")
];
ScmRepository.ItemVendorRow[] vendors = [new("FABRIC", "V1", "Fabric vendor", true, "", "", default),
    new("FABRIC", "V2", "Second vendor", true, "", "", default), new("RESIN", "V2", "Resin vendor", true, "", "", default)];
var rows = InventorySearchRows.FromMaster(items, stock);
Check(rows.Select(x => x.PartNo).Distinct().Order().SequenceEqual(items.Select(x => x.ItemNo).Order()), "Only Item Master products appear, including zero-stock products.");
Check(rows.All(x => x.PartName == "Master " + x.PartNo && x.Unit == "KG"), "Names and units come from Item Master.");
Check(rows.Where(x => x.PartNo == "RESIN").Single() is { Qty: 0, DefectQty: 0, LotNo: null, LocationNo: "" }, "No-stock products do not create fake LOTs or locations.");
Check(rows.Sum(x => x.Qty) == 8.5m && rows.Sum(x => x.DefectQty) == 3.5m, "Available and defective quantities remain separate.");
var fabric = InventorySearchRows.Filter(rows, vendors, "", "", "", "V1", "MATERIAL", "FABRIC");
Check(fabric.Count == 2 && fabric.Sum(x => x.Qty) == 8.5m && fabric.Sum(x => x.DefectQty) == 3.5m, "Vendor/type/category filtering does not multiply inventory for multiple supplier mappings.");
Check(InventorySearchRows.Filter(rows, vendors, "resin", "", "", "V2", "", "").Single().PartNo == "RESIN", "A vendor's zero-stock item remains searchable.");
Check(InventorySearchRows.Filter(rows, vendors, "LOT-1", "EOS", "MAT", "V1", "", "").Single().Qty == 8.5m, "LOT and warehouse/area filters are applied to the same data.");
Check(!InventorySearchRows.Filter(rows, vendors, "", "", "", "V1", "", "RESIN").Any(), "Unrelated supplier products are excluded.");
var bytes = XlsxWriter.Build("Inventory", ["Part", "Available", "Defective"], fabric.GroupBy(x => x.PartNo)
    .Select(g => (IReadOnlyList<object?>)new object?[] { g.Key, g.Sum(x => x.Qty), g.Sum(x => x.DefectQty) }));
using var zip = new ZipArchive(new MemoryStream(bytes));
using var sheet = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
var xml = XDocument.Load(sheet);
XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
var cells = xml.Descendants(ns + "c").ToDictionary(x => (string)x.Attribute("r")!);
Check(cells["B2"].Element(ns + "v")!.Value == "8.5" && cells["C2"].Element(ns + "v")!.Value == "3.5", "Excel contains independent numeric available/defect cells, not formatted text.");
Check(xml.Descendants(ns + "row").Count() == 2, "Excel exports the filtered group once, not every supplier or page.");

if (Environment.GetEnvironmentVariable("AMES_TEST_CONN") is { Length: > 0 } connection)
{
    var factory = new AMES.Data.Connection.AmesConnectionFactory(connection);
    var md = new MasterDataRepository(factory);
    var warehouse = new WarehouseRepository(factory);
    var actualItems = md.ListItems();
    var actualStock = warehouse.ListUnifiedInventory(false, includeFinishedGoods: true, includeDefects: true);
    var actualRows = InventorySearchRows.FromMaster(actualItems, actualStock.Select(x => new LocationMapStock(
        x.LocationNo, x.PartNo, x.PartName, x.LotNo, x.Qty, x.Unit, x.WarehouseCode, x.WarehouseName, x.AreaCode, x.AreaName, x.DefectQty)));
    var actualVendors = new ScmRepository(factory).ListItemVendors();
    Check(actualRows.Select(x => x.PartNo).Distinct().Order().SequenceEqual(actualItems.Select(x => x.ItemNo).Order()), "Actual DB: every master item is shown and unknown stock products are excluded.");
    var actualParts = actualItems.Select(x => x.ItemNo).ToHashSet(StringComparer.OrdinalIgnoreCase);
    Check(actualRows.Sum(x => x.Qty) == actualStock.Where(x => actualParts.Contains(x.PartNo)).Sum(x => x.Qty)
        && actualRows.Sum(x => x.DefectQty) == actualStock.Where(x => actualParts.Contains(x.PartNo)).Sum(x => x.DefectQty), "Actual DB: catalog projection preserves both quantities exactly.");
    foreach (var vendor in actualVendors.Select(x => x.VendorID).Distinct())
    {
        var vendorRows = InventorySearchRows.Filter(actualRows, actualVendors, "", "", "", vendor, "", "");
        var vendorParts = actualVendors.Where(x => x.VendorID == vendor).Select(x => x.ItemNo).ToHashSet();
        Check(vendorRows.All(x => vendorParts.Contains(x.PartNo)), $"Actual DB: supplier {vendor} filters only linked products.");
    }
    var map = LocationMapDatabase.Load(md, warehouse, false);
    Check(map.Locations.Sum(x => x.DefectQty) == map.Inventory.Where(x => map.Locations.Any(l => l.LocationNo == x.LocationNo)).Sum(x => x.DefectQty), "Actual DB: location cells and detail rows agree on defective quantity.");
    Console.WriteLine($"READ ONLY: {actualItems.Count} master items, {actualRows.Count(x => x.LotNo is null)} items without stock, {map.Locations.Count} location cells.");
}
