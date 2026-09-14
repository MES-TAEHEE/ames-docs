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
      && inbound.Contains("EOS SP NO")
      && inbound.Contains("APPLICABLE EQUIPMENT")
      && inbound.Contains("WhReceiveInboundAsync(new(\"LOCAL\"")
      && !inbound.Contains("CKD"), "SP inbound uses one LOT scan flow and hides Local/CKD selection");
Check(release.Contains("@page \"/sp/07\"")
      && release.Contains("EOS SP NO")
      && release.Contains("STORAGE LOCATION")
      && release.Contains("WhDirectOutgoingAsync"), "SP release uses one LOT scan flow and shows storage location");
Check(inventory.Contains("@page \"/sp/03\"") && inventory.Contains("WhInventoryAsync(_q, DateFrom, DateTo, ShouldSimulateInventoryApiFailure, AreaScope)"), "Inventory and Adjust use the SP area scope");
Check(inventoryLogic.Contains("SpPptInventorySteps")
      && inventoryLogic.Contains("EOS-SP-A1-260001")
      && inventoryLogic.Contains("SP-CAB1-03")
      && inventoryLogic.Contains("PRCDTP7HLQK15"), "SP inventory provides simple LOT, location, and part scenarios");
Check(transactions.Contains("@page \"/sp/08\"") && transactions.Contains("IsFinishedGoods, AreaScope"), "Transactions use the SP area scope");
Check(api.Contains("public const string SparePartsAreaCode = \"SPARE_PARTS_AREA\"")
      && api.Contains("FilterByAreaAsync")
      && api.Contains("EnsureArea"), "PDA API filters lists and rejects cross-area locations");
Check(api.Contains("L.WhCode AS WarehouseCode") && api.Contains("L.AreaCode"), "Direct location query uses normalized warehouse and area columns");

var schema = Read("dist/pda/PDA_SCHEMA.sql");
var seed = Read("dist/pda/PDA_SEED.sql");
var apiEndpoints = Read("src/04_Api/AMES.Api/Endpoints/WhEndpoints.cs");
Check(schema.Contains("SparePartNo nvarchar(80)")
      && schema.Contains("ApplicableEquipment nvarchar(80)")
      && schema.Contains("MakerName nvarchar(80)")
      && schema.Contains("CREATE OR ALTER PROCEDURE dbo.SP_PDA_SIMPLE_TEST_RESET"), "Schema stores spare-parts metadata and resets simple tests");
Check(seed.Contains("EOS-SP-A1-260001")
      && seed.Contains("EOS-SP-I1-260006")
      && seed.Contains("EOS-SP-A1-260009")
      && seed.Contains("SP-CAB1-03")
      && seed.Contains("SP-C1-03"), "Seed contains workbook inventory and a pending inbound test LOT");
Check(apiEndpoints.Contains("MapGet(\"/sp/lot\"")
      && apiEndpoints.Contains("SP_PDA_SIMPLE_TEST_RESET")
      && api.Contains("SpLotAsync")
      && api.Contains("SpResetTestAsync"), "API exposes SP LOT detail and test reset operations");
Check(schema.Contains("CREATE OR ALTER PROCEDURE dbo.WH_PDA_INVENTORY_STATUS_LIST")
      && schema.Contains("@AreaCode nvarchar(20) = NULL")
      && schema.Contains("@AreaCode IS NULL OR WL.AreaCode = @AreaCode"), "Inventory summary procedure supports an optional area scope");
Check(schema.Contains("CREATE OR ALTER PROCEDURE dbo.WH_PDA_INVENTORY_LOCATION_LIST")
      && schema.Contains("L.WhCode AS WHCD")
      && schema.Contains("L.AreaCode AS AREACD"), "Inventory location procedure returns normalized hierarchy fields");
