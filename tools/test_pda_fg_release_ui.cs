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
Check(source.Contains("Api.FgInventoryAsync(_slipBarcode)")
    && source.Contains("LooksLikeFgLotBarcode(_slipBarcode)")
    && source.Contains("Please scan the outgoing slip first.")
    && source.Contains("The outgoing slip barcode was not found.")
    && source.Contains("The scanned FG LOT was not found."),
    "A known LOT scanned first must request the outgoing slip without replacing existing not-found alerts.");
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
    && scanCode.Contains("stock.ItemNo") && scanCode.Contains("stock.Qty > remaining")
    && scanCode.Contains("ScannedQty(line) + stock.Qty"),
    "LOT scan must validate the part and accumulate partial LOT quantities client-side.");
Check(scanCode.Contains("OrderBy(x => x.StockTs ?? DateTime.MaxValue)")
    && scanCode.Contains("ThenBy(x => x.StockId)")
    && scanCode.Contains("FIFO Order") && scanCode.Contains("Scan {fifoLot.LotNo} first"),
    "LOT scans must enforce the oldest available stock timestamp per part.");
Check(source[completeStart..].Contains("FgCompleteReleaseAsync"), "Only COMPLETE may persist scanned LOTs.");
var api = File.ReadAllText(Path.Combine(root, "src/04_Api/AMES.Api/Endpoints/FgEndpoints.cs"));
Check(api.Contains("/release/outgoing-slips/{barcode}") && api.Contains("/release/complete")
    && api.Contains("BEGIN TRANSACTION") && api.Contains("ROLLBACK TRANSACTION"),
    "COMPLETE must persist all LOTs in one server transaction.");
Check(api.Contains("OutgoingSlipLineID int '$.OutgoingSlipLineId'")
    && api.Contains("ISNULL(S.ItemNo,'') <> ISNULL(L.ItemNo,'')")
    && api.Contains("StockID int PRIMARY KEY") && api.Contains("SUM(Qty) AS Qty"),
    "COMPLETE must bind multiple scanned LOTs to the matching part line and validate their total quantity.");
Check(api.Contains("ISNULL(Older.StockTS,'9999-12-31') < ISNULL(Chosen.StockTS,'9999-12-31')")
    && api.Contains("A scanned LOT violates FIFO order."),
    "COMPLETE must revalidate FIFO order inside the server transaction.");
var migration = File.ReadAllText(Path.Combine(root, "dist/pda/PDA_SCHEMA.sql"));
Check(migration.Contains("OutgoingSlipNumber") && migration.Contains("CREATE UNIQUE INDEX"),
    "Outgoing-slip barcode migration is required.");
Console.WriteLine("PASS: separated outgoing/part sections, partial LOT accumulation and atomic COMPLETE gate.");
