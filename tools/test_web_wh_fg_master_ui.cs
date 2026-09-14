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
