#:property PublishAot=false

static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
var root = Directory.GetParent(Path.GetDirectoryName(SourcePath())!)!.FullName;
string Read(string path) => File.ReadAllText(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine("PASS: " + message);
}

var home = Read("src/05_Pda/AMES.Pda/Components/Pages/Home.razor");
var spHome = Read("src/05_Pda/AMES.Pda/Components/Pages/Sp/SpHome.razor");
var whInbound = Read("src/05_Pda/AMES.Pda/Components/Pages/Wh/Wh02PdaInbound.razor");
var inbound = Read("src/05_Pda/AMES.Pda/Components/Pages/Sp/SpInbound.razor");
var inventory = Read("src/05_Pda/AMES.Pda/Components/Pages/Wh/Wh03InventoryStatus.razor");
var inventoryLogic = Read("src/05_Pda/AMES.Pda/Components/Pages/Wh/Wh03InventoryStatus.razor.cs");
var whRelease = Read("src/05_Pda/AMES.Pda/Components/Pages/Wh/Wh07PdaRelease.razor");
var release = Read("src/05_Pda/AMES.Pda/Components/Pages/Sp/SpRelease.razor");
var transactions = Read("src/05_Pda/AMES.Pda/Components/Pages/Wh/Wh08TransactionHistory.razor");
var api = Read("src/05_Pda/AMES.Pda/Services/PdaApi.cs");

Check(home.Contains("Nav.NavigateTo(\"/sp\")") && home.Contains("SPARE PARTS"), "PDA launcher exposes Spare Parts");
foreach (var route in new[] { "/sp/02", "/sp/07", "/sp/03", "/sp/03?tab=adjust", "/sp/08" })
    Check(spHome.Contains(route), $"Spare Parts menu contains {route}");

Check(!whInbound.Contains("@page \"/sp/02\"") && !whRelease.Contains("@page \"/sp/07\""), "SP inbound and release are isolated from WH document flows");
Check(inbound.Contains("@page \"/sp/02\"")
      && inbound.Contains("<label>EOS SP NO</label>")
      && inbound.Contains("APPLICABLE EQUIPMENT")
      && inbound.Contains("SpReceiveAsync")
      && inbound.Contains("SpItemAsync")
      && !inbound.Contains("CKD"), "SP inbound uses one EOS SP No scan flow and hides Local/CKD selection");
Check(release.Contains("@page \"/sp/07\"")
      && release.Contains("<label>EOS SP NO</label>")
      && release.Contains("STORAGE LOCATION")
      && release.Contains("SpReleaseAsync")
      && release.Contains("SpItemAsync"), "SP release scans EOS SP No and shows storage location");
Check(inventory.Contains("@page \"/sp/03\"") && inventory.Contains("WhInventoryAsync(_q, DateFrom, DateTo, ShouldSimulateInventoryApiFailure, AreaScope)"), "Inventory and Adjust use the SP area scope");
Check(inventoryLogic.Contains("SpPptInventorySteps")
      && inventoryLogic.Contains("EOS-SP-K9-269999")
      && inventoryLogic.Contains("SP-EXTRA")
      && inventoryLogic.Contains("PDA-SP-TEST-001"), "SP inventory provides EOS SP No, location, and part scenarios");
Check(transactions.Contains("@page \"/sp/08\"") && transactions.Contains("IsFinishedGoods, AreaScope"), "Transactions use the SP area scope");
Check(api.Contains("public const string SparePartsAreaCode = \"SPARE_PARTS_AREA\"")
      && api.Contains("/api/wh/sp/inventory")
      && api.Contains("SpSaveAdjustQtyAsync")
      && api.Contains("SpMoveLocationAsync")
      && api.Contains("/api/wh/sp/transactions"), "PDA routes SP inventory, adjustment, location, and transactions to the spare-parts ledger");
Check(api.Contains("L.WhCode AS WarehouseCode") && api.Contains("L.AreaCode"), "Direct location query uses normalized warehouse and area columns");

var schema = Read("dist/pda/PDA_SCHEMA.sql");
var seed = Read("dist/pda/PDA_SEED.sql");
var apiEndpoints = Read("src/04_Api/AMES.Api/Endpoints/WhEndpoints.cs");
Check(schema.Contains("CREATE OR ALTER PROCEDURE dbo.SP_PDA_STOCK_MOVE")
      && schema.Contains("UPDATE dbo.MD_SparePart")
      && schema.Contains("INSERT INTO dbo.MNT_SparePartsTxn")
      && schema.Contains("CREATE OR ALTER PROCEDURE dbo.SP_PDA_SIMPLE_TEST_RESET"), "Schema stores spare-parts metadata and resets simple tests");
Check(seed.Contains("EOS-SP-K9-269999")
      && seed.Contains("INSERT INTO dbo.MD_SparePart")
      && seed.Contains("SP-DEMO-V01")
      && seed.Contains("DEMO INDUSTRIAL")
      && !seed.Contains("DECLARE @SpareLots"), "Seed contains the EOS SP No test master without duplicate LOT inventory");
Check(schema.Contains("Maker = N'DEMO INDUSTRIAL'")
      && schema.Contains("SupplierID = 'SP-DEMO-V01'"), "Simple-test reset preserves sample maker and vendor");
Check(apiEndpoints.Contains("MapGet(\"/sp/item\"")
      && apiEndpoints.Contains("MapPost(\"/sp/inbound\"")
      && apiEndpoints.Contains("MapPost(\"/sp/release\"")
      && apiEndpoints.Contains("MapGet(\"/sp/inventory\"")
      && apiEndpoints.Contains("MapPost(\"/sp/adjust/save\"")
      && apiEndpoints.Contains("MapPost(\"/sp/move-location\"")
      && apiEndpoints.Contains("MapGet(\"/sp/transactions\"")
      && apiEndpoints.Contains("FROM dbo.MD_SparePart P")
      && apiEndpoints.Contains("P.PartName, P.PartNo, P.Maker")
      && apiEndpoints.Contains("P.SparePartImage")
      && apiEndpoints.Contains("GetImageDataUrl(rdr, \"SparePartImage\")")
      && apiEndpoints.Contains("L.ZoneCode = P.ZoneCode")
      && !apiEndpoints.Contains("P.StorageLoc")
      && apiEndpoints.Contains("SP_PDA_SIMPLE_TEST_RESET")
      && api.Contains("SpItemAsync")
      && api.Contains("SpReceiveAsync")
      && api.Contains("SpReleaseAsync")
      && api.Contains("SpResetTestAsync"), "API exposes EOS SP No detail, stock movement, and test reset operations");
Check(release.Contains("_lot.ImageDataUrl") && release.Contains("sp-release-image"), "SP release shows the stored image only when available");
Check(schema.Contains("CREATE OR ALTER PROCEDURE dbo.WH_PDA_INVENTORY_STATUS_LIST")
      && schema.Contains("@AreaCode nvarchar(20) = NULL")
      && schema.Contains("@AreaCode IS NULL OR WL.AreaCode = @AreaCode"), "Inventory summary procedure supports an optional area scope");
Check(schema.Contains("CREATE OR ALTER PROCEDURE dbo.WH_PDA_INVENTORY_LOCATION_LIST")
      && schema.Contains("L.WhCode AS WHCD")
      && schema.Contains("L.AreaCode AS AREACD"), "Inventory location procedure returns normalized hierarchy fields");
