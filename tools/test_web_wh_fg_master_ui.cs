#:property PublishAot=false

static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
var root = Directory.GetParent(Path.GetDirectoryName(SourcePath())!)!.FullName;
string Read(string path) => File.ReadAllText(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine("PASS: " + message);
}

var inventory = Read("src/06_Web/AMES.Web/Components/Shared/InventoryLookup.razor");
Check(inventory.Contains("ames-kpis") && inventory.Contains("master-list-toolbar"), "WH/FG Inventory uses the Product Item Master layout");
Check(inventory.Contains("ames-grid-fixed") && inventory.Contains("PageSizeOptions"), "WH/FG Inventory uses the master grid paging pattern");
Check(inventory.Contains("inventory-detail-drawer"), "Inventory right-side detail drawer remains available");

foreach (var file in new[]
{
    "src/06_Web/AMES.Web/Components/Pages/Fg/CustomerReturns.razor",
    "src/06_Web/AMES.Web/Components/Pages/Fg/Shipments.razor",
    "src/06_Web/AMES.Web/Components/Pages/Fg/History.razor",
    "src/06_Web/AMES.Web/Components/Pages/Wh/LogHistory.razor",
})
{
    var page = Read(file);
    Check(page.Contains("ames-kpis") && page.Contains("master-list-toolbar") && page.Contains("ames-grid-fixed"), Path.GetFileName(file) + " uses the Product Item Master pattern");
}

var layout = Read("src/06_Web/AMES.Web/Components/Layout/MainLayout.razor");
Check(layout.Contains("ames-sec-wh") && layout.Contains("ames-sec-fg"), "WH and FG sections receive fixed-grid layout scope");

var screens = Read("src/06_Web/AMES.Web/Components/Pages/Sys/Screens.razor");
var nav = Read("src/06_Web/AMES.Web/Components/Layout/NavMenu.razor");
var catalog = Read("src/06_Web/AMES.Web/Services/MenuCatalog.cs");
var header = Read("src/06_Web/AMES.Web/Components/Layout/AmesPageHeader.razor");
var repo = Read("src/02_Data/AMES.Data/Repositories/SysRepository.cs");
Check(screens.Contains("ScreenCatalog.Notify()") && nav.Contains("ScreenCatalog.Changed += OnScreensChanged") &&
      header.Contains("ScreenCatalog.Changed += OnScreensChanged"), "Screen Master saves refresh open menus and headers");
Check(header.Contains("Sys.ListScreens(\"WEB\")") && catalog.Contains("s.LidLabel ?? s.ScreenCode") &&
      catalog.Contains("s.IsVisible"), "Menu and page header use visible Screen Master entries");
Check(screens.Contains("lid = code") && repo.Contains("BEGIN TRANSACTION;") &&
      repo.Contains("UPDATE dbo.SYS_RolePermission"), "Screen code edits retain labels and role permissions");

var shipment = Read("src/06_Web/AMES.Web/Components/Pages/Fg/Shipments.razor");
var shipmentCss = Read("src/06_Web/AMES.Web/Components/Pages/Fg/Shipments.razor.css");
Check(!shipment.Contains("<table") && shipment.Contains("shipment-items-grid"), "Shipment details use Radzen instead of hand-built tables");
Check(shipmentCss.Contains("max-height:280px") && shipmentCss.Contains("height:auto !important"), "Nested shipment details remain content-sized and scroll when long");

var picking = Read("src/06_Web/AMES.Web/Components/Pages/Wh/PickingOrders.razor");
Check(picking.Contains("ames-kpis") && picking.Contains("master-list-toolbar") && picking.Contains("ames-grid-fixed") && picking.Contains("RadzenSelectBar"), "Picking Orders follows the MD/SYS list and tab patterns");

var orders = Read("src/06_Web/AMES.Web/Components/Pages/Fg/ShippingOrders.razor");
Check(orders.Contains("RadzenSelectBar") && orders.Contains("master-list-toolbar") && orders.Contains("ames-grid-fixed") && !orders.Contains("<details") && !orders.Contains("<button"), "Shipment Orders uses standard Radzen navigation, grid expansion and buttons");
var create = Read("src/06_Web/AMES.Web/Components/Shared/ShipmentOrderCreate.razor");
var planPage=Read("src/06_Web/AMES.Web/Components/Pages/Fg/ShipmentPlan.razor");
Check(planPage.Contains("TItem=\"ShippingOrderDto\"") && planPage.Contains("<Template Context=\"order\"><ShipmentOrderLines"),"Shipment Plan expands saved orders into their part lists.");
Check(planPage.Contains("TrailerLoadDraft.FromOrders(_view)") && planPage.Contains("Dialog.OpenAsync<ShipmentTrailerDialog>") && !planPage.Contains("ListShipmentPlanOrders"),"Truck preview opens on demand using saved shipment orders, not raw SOs.");
Check(planPage.Contains("Click=\"OpenTrailerPreview\"") && planPage.Contains("ames-grid-fixed") && planPage.Contains("--ames-page-rows") && !planPage.Contains("Embedded=\"true\"") && !planPage.Contains("<ShipmentTrailerDialog "),"Shipment Plan defaults to a full-height table; trailers open only from the preview button.");
Check(orders.Contains("SHIPMENT ORDERS CREATED") && orders.Contains("Open Shipment Plan") && !orders.Contains("_creating=false"),"Creation confirms the exact saved orders in a popup without switching to the list tab.");
Check(create.Contains("ames-kpis") && create.Contains("master-list-toolbar") && create.Contains("ames-grid-fixed") && create.Contains("Immediate=\"true\"") && create.Contains("EstimateShippingPallets"), "Standard shipment creation preserves immediate quantity/pallet calculations");

foreach(var path in new[]{
    "src/06_Web/AMES.Web/Components/Shared/LineLocationEditor.razor",
    "src/06_Web/AMES.Web/Components/Shared/InventoryLookup.razor",
    "src/06_Web/AMES.Web/Components/Shared/LocationMapExplorer.razor",
    "src/06_Web/AMES.Web/Components/Pages/Fg/ShipmentTrailerDialog.razor"})
{
    var text=Read(path);
    Check(text.Contains("RadzenDataGrid") && !text.Contains("<table"), Path.GetFileName(path)+" uses Radzen for interactive data tables");
}
var lineEditor=Read("src/06_Web/AMES.Web/Components/Shared/LineLocationEditor.razor");
Check(lineEditor.Contains("RadzenRow") && lineEditor.Contains("RadzenColumn") && lineEditor.Contains("RadzenLabel") && !lineEditor.Contains("<label>"), "Line location forms use the shared master form layout");
Check(Read("AGENTS.md").Contains("Radzen") && Read("AGENTS.md").Contains("Claude"), "Project instructions preserve the required design-system source and Radzen policy");
