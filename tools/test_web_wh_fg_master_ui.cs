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
var header = Read("src/06_Web/AMES.Web/Components/Layout/AmesPageHeader.razor");
var repo = Read("src/02_Data/AMES.Data/Repositories/SysRepository.cs");
Check(screens.Contains("ScreenCatalog.Notify()") && nav.Contains("ScreenCatalog.Changed += OnScreensChanged") &&
      header.Contains("ScreenCatalog.Changed += OnScreensChanged"), "Screen Master saves refresh open menus and headers");
Check(header.Contains("Sys.ListScreens(\"WEB\")") && nav.Contains("s.LidLabel ?? s.ScreenCode") &&
      nav.Contains("s.IsVisible"), "Menu and page header use visible Screen Master entries");
Check(screens.Contains("lid = code") && repo.Contains("BEGIN TRANSACTION;") &&
      repo.Contains("UPDATE dbo.SYS_RolePermission"), "Screen code edits retain labels and role permissions");
