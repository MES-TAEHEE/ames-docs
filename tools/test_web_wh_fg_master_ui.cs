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
Check(picking.Contains("ames-kpis") && picking.Contains("master-list-toolbar") && picking.Contains("ames-grid-fixed"), "Picking Orders follows the MD/SYS list pattern");
var moldReference = Read("src/06_Web/AMES.Web/Components/Pages/Md/Re/Mold.razor");
var notificationReference = Read("src/06_Web/AMES.Web/Components/Pages/Sys/Notifications.razor");
Check(new[] { picking, moldReference, notificationReference }.All(page => page.Contains("<AmesTabs>") && page.Contains("<AmesTab Title=")) && !picking.Contains("RadzenSelectBar"), "Picking Orders reuses the actual MD/SYS shared tabs, not a selection bar");
Check(picking.IndexOf("<AmesTab Title=\"Picking Orders\"", StringComparison.Ordinal) < picking.IndexOf("<AmesTab Title=\"Line Stock\"", StringComparison.Ordinal) && picking.Contains("WH.ListLineStock()") && picking.Contains("WH.ListPickingSlipHeaders(_search)"), "Picking Orders opens on its list and retains the line-minimum stock view");

var orders = Read("src/06_Web/AMES.Web/Components/Pages/Fg/ShippingOrders.razor");
Check(!orders.Contains("RadzenSelectBar") && !orders.Contains("shipping-view-tabs") && !orders.Contains("_creating=true,_") && orders.Contains("master-list-toolbar") && orders.Contains("ames-grid-fixed") && !orders.Contains("<details") && !orders.Contains("<button"), "Shipment Orders defaults to the list without a Create/list navigation split");
Check(orders.Contains("Click=\"OpenCreate\"") && orders.Contains("ButtonStyle=\"ButtonStyle.Primary\"") && orders.Contains("aria-label=\"Create shipment order\"") && orders.Contains("ames-modal ames-modal-fullwide") && orders.Contains("Closed=\"CloseCreate\"") && orders.Contains("if(!_createBusy)_creating=false"), "Shipment creation uses the standard toolbar button and modal, with busy-close protection");
Check(!orders.Contains("ShippingTrailer") && !orders.Contains("@bind-Value=\"_trailer\"") && orders.Contains("Fg.CreateShippingDelivery(ids,null,_actor)"), "DCR creation uses selected shipment orders without a trailer-number input");
var printPreview = Read("src/06_Web/AMES.Web/Components/Shared/ShippingOrderPrintPreview.razor");
var printCss = Read("src/06_Web/AMES.Web/Components/Shared/ShippingOrderPrintPreview.razor.css");
Check(printPreview.Contains("if(!string.IsNullOrWhiteSpace(Delivery?.TrailerNo))"), "DCR print omits an empty trailer-number label");
Check(orders.Contains("<ShippingOrderPrintPreview") && printPreview.Contains("A4 (210 × 297 mm)") && printPreview.Contains("fg-print-area") && printPreview.Contains("fg-dcr-paper") && printPreview.Contains("data-no-busy") && printCss.Contains("page:fg-dcr") && Read("src/06_Web/AMES.Web/wwwroot/app.css").Contains("@page fg-dcr { size: A4 portrait; margin: 0; }"), "Order and DCR printing use the existing A4 document-preview and print-page rules");
var create = Read("src/06_Web/AMES.Web/Components/Shared/ShipmentOrderCreate.razor");
var planPage=Read("src/06_Web/AMES.Web/Components/Pages/Fg/ShipmentPlan.razor");
Check(planPage.Contains("TItem=\"ShipmentPlanRows.Delivery\"") && planPage.Contains("ShipmentPlanRows.FromOrders") && planPage.Contains("foreach(var order in delivery.Orders)") && planPage.Contains("<ShipmentOrderLines Lines=\"order.Lines\""),"Shipment Plan shows one DCR per row and expands only its saved orders and parts.");
Check(planPage.Contains("TrailerLoadDraft.FromOrders(_view.SelectMany(d=>d.Orders))") && planPage.Contains("Dialog.OpenAsync<ShipmentTrailerDialog>") && !planPage.Contains("ListShipmentPlanOrders"),"Truck preview uses only the orders assigned to the displayed DCRs, not unassigned orders or raw SOs.");
Check(orders.Contains("&dcr={Uri.EscapeDataString(no)}") && planPage.Contains("Name=\"dcr\"") && planPage.Contains("<ShippingOrderPrintPreview"), "DCR creation opens its Shipment Plan entry with the same A4 print preview");
Check(planPage.Contains("Click=\"OpenTrailerPreview\"") && planPage.Contains("ames-grid-fixed") && planPage.Contains("--ames-page-rows") && !planPage.Contains("Embedded=\"true\"") && !planPage.Contains("<ShipmentTrailerDialog "),"Shipment Plan defaults to a full-height table; trailers open only from the preview button.");
Check(orders.Contains("SHIPMENT ORDERS CREATED") && orders.Contains("create a DCR to include them in Shipment Plan") && orders.Contains("_creating=false;"),"Order creation confirms saved orders; only creating their DCR sends them to Shipment Plan.");
Check(create.Contains("Closed.InvokeAsync()") && create.Contains("BusyChanged.InvokeAsync(true)") && create.Contains("BusyChanged.InvokeAsync(false)") && create.Contains("Disabled=\"_busy\""), "Shipment creation keeps the standard Cancel action and blocks closing during save");
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
