// Run after a Windows PDA build: dotnet run --file tools/test_pda_fg_release_ui.cs
#:sdk Microsoft.NET.Sdk.Web
#:property PublishAot=false

static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
var root = Directory.GetParent(Path.GetDirectoryName(SourcePath())!)!.FullName;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

var source = File.ReadAllText(Path.Combine(root, "src/05_Pda/AMES.Pda/Components/Pages/Fg/Fg04FifoPicking.razor"));
Check(source.Contains("Text=\"CANCEL\"") && source.Contains("Text=\"COMPLETE\""), "Action labels must be CANCEL and COMPLETE.");
Check(source.Contains("Disabled=\"@(_isBusy || !AllScanned)\""), "COMPLETE must use the AllScanned disabled gate.");
Check(source.Contains("_lines.All(x => ScannedQty(x) >= x.RequiredQty)"),
    "COMPLETE must remain disabled until every part quantity is scanned.");
Check(System.Text.RegularExpressions.Regex.Matches(source, "<RadzenTextBox[^>]*pda-scan-input").Count == 1
    && !source.Contains("Text=\"SCAN\"") && !source.Contains("wh07-scan-panel"),
    "FG Release must keep one barcode input and no SCAN button.");
Check(source.Contains("OUTGOING SLIP BARCODE") && !source.Contains("SHIPMENT ORDER") && !source.Contains("ShipOrder"),
    "FG Release must use outgoing-slip terminology only.");
Check(source.Contains("LooksLikeFgLotBarcode(_slipBarcode)")
    && !source.Contains("Api.FgInventoryAsync(_slipBarcode)")
    && source.Contains("Please scan the outgoing slip first.")
    && source.Contains("The outgoing slip barcode was not found.")
    && source.Contains("Picking Service Error"),
    "A LOT scanned first must request the outgoing slip while service failures stay visible.");
Check(source.Contains("Only RELEASED outgoing slips can be picked")
    && source.Contains("string.Equals(_selectedSlip.Status, \"Released\""),
    "Only a released outgoing slip may enter picking.");
var plannedStart = source.IndexOf("@foreach (var line in _lines)", StringComparison.Ordinal);
var actionsStart = source.IndexOf("<div class=\"wh07-action-spacer\">", StringComparison.Ordinal);
Check(plannedStart >= 0 && actionsStart > plannedStart && !source[plannedStart..actionsStart].Contains("LOT NO"),
    "Outgoing-slip lines must not reveal a preassigned LOT.");
Check(!source.Contains("SCANNED LOTS") && source.Contains("fg04-section-head outgoing")
    && source.Contains("fg04-section-head parts"),
    "Duplicate scanned-LOT cards must be removed and outgoing/part sections separated.");
var scanStart = source.IndexOf("private async Task ScanLot()", StringComparison.Ordinal);
var completeStart = source.IndexOf("private async Task CompleteRelease()", StringComparison.Ordinal);
Check(scanStart >= 0 && completeStart > scanStart, "Expected scan and complete methods.");
var scanCode = source[scanStart..completeStart];
Check(!scanCode.Contains("FgCompleteReleaseAsync") && scanCode.Contains("_scannedLots.Add")
    && scanCode.Contains("FgReleaseLotScanAsync") && !scanCode.Contains("FgInventoryAsync")
    && scanCode.Contains("ScannedQty(line) + stock.Qty"),
    "LOT scan must use the dedicated server validation and accumulate accepted quantities client-side.");
Check(source[completeStart..].Contains("FgCompleteReleaseAsync"), "Only COMPLETE may persist scanned LOTs.");
var api = File.ReadAllText(Path.Combine(root, "src/04_Api/AMES.Api/Endpoints/FgEndpoints.cs"));
Check(api.Contains("/release/outgoing-slips/{barcode}") && api.Contains("/release/lot/scan")
    && api.Contains("dbo.FG_PDA_PICKING_SCAN") && api.Contains("dbo.FG_PDA_PICKING_COMPLETE"),
    "Picking scan and complete must use the database procedures.");
var migration = File.ReadAllText(Path.Combine(root, "dist/pda/PDA_SCHEMA.sql"));
Check(migration.Contains("CREATE TABLE dbo.FG_PickingDetail")
    && migration.Contains("CREATE OR ALTER PROCEDURE dbo.FG_PDA_PICKING_SCAN")
    && migration.Contains("CREATE OR ALTER PROCEDURE dbo.FG_PDA_PICKING_COMPLETE")
    && migration.Contains("FROM dbo.FG_ShipmentOrder WITH(UPDLOCK,HOLDLOCK)")
    && migration.Contains("ISNULL(S.HoldFlag,0)=1")
    && migration.Contains("This FG LOT belongs to a different customer.")
    && migration.Contains("Only RELEASED outgoing slips can be completed.")
    && migration.Contains("INSERT dbo.FG_PickingDetail"),
    "Schema must enforce release status, hold/customer checks, concurrency and LOT detail persistence.");
var scanProcedure = migration[migration.IndexOf("CREATE OR ALTER PROCEDURE dbo.FG_PDA_PICKING_SCAN", StringComparison.Ordinal)..
    migration.IndexOf("CREATE OR ALTER PROCEDURE dbo.FG_PDA_PICKING_COMPLETE", StringComparison.Ordinal)];
Check(!scanProcedure.Contains("SELECT TOP 100")
    && scanProcedure.Contains("ORDER BY ISNULL(F.StockTS,'9999-12-31'),F.StockID"),
    "Picking FIFO must inspect the complete eligible inventory queue.");
Check(migration.Contains("CREATE OR ALTER PROCEDURE dbo.FG_PDA_LOADING_ORDER_SCAN")
    && migration.Contains("FROM dbo.FG_PickingDetail D")
    && migration.Contains("No completed picking detail exists for this shipment order."),
    "Truck loading must use the persisted picking details.");
Console.WriteLine("PASS: separated outgoing/part sections, partial LOT accumulation and atomic COMPLETE gate.");
