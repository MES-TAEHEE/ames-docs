// Run with: dotnet run --file tools/test_pda_fg_loading_ui.cs
static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
var root = Directory.GetParent(Path.GetDirectoryName(SourcePath())!)!.FullName;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

var page = File.ReadAllText(Path.Combine(root, "src/05_Pda/AMES.Pda/Components/Pages/Fg/Fg05Loading.razor"));
Check(System.Text.RegularExpressions.Regex.Matches(page, "<RadzenTextBox").Count == 1
    && page.Contains("BARCODE SCAN") && page.Contains("fg05-barcode-input")
    && !page.Contains("fg05-truck-input") && !page.Contains("fg05-order-input") && !page.Contains("fg05-product-input"),
    "Truck Loading must show one full-width barcode scanner only.");
Check(page.Contains("private Task ScanCurrent() => _truck is null")
    && page.Contains(": _order is null") && page.Contains("? ScanOrder()") && page.Contains(": ScanProduct()"),
    "The shared scanner must follow Truck -> Shipment Order -> Product.");
Check(page.Contains("Disabled=\"@(_isBusy || !AllScanned)\""),
    "Confirm Load must remain disabled until every product is scanned.");
Check(page.Contains("Text=\"CONFIRM\"") && !page.Contains("Text=\"CONFIRM LOAD\""),
    "The final action must be labeled CONFIRM.");
Check(page.Contains("fg05-scan-guide") && page.Contains("@ScanPrompt") && page.Contains("@ScanFormat")
    && !page.Contains("ScanPlaceholder"),
    "The next barcode type must be shown outside the input placeholder.");
Check(page.IndexOf("<div class=\"fg05-scan-guide\">", StringComparison.Ordinal)
        < page.IndexOf("<div class=\"pda-bd wh07-body fg05-body\">", StringComparison.Ordinal)
    && page.Contains("fg05-item-primary") && page.Contains("fg05-item-secondary")
    && page.Contains("fg05-row-location") && page.Contains("fg05-row-unit")
    && !page.Contains("@(scanned ? \"CHECKED\" : \"PENDING\")")
    && page.Contains("STOCK BARCODE") && !page.Contains("FG LOT / STOCK BARCODE"),
    "NEXT SCAN must be separate and products must render as readable two-line stock rows.");
Check(page.Contains("_scrollShipmentAfterRender = true;")
    && page.Contains("pdaScan.scrollTo\", \"#fg05-shipment-info\", 0")
    && page.Contains("id=\"fg05-shipment-info\""),
    "Shipment information must scroll directly below the sticky NEXT SCAN guide.");
Check(page.Contains("barcode.StartsWith(\"TRUCK:\", StringComparison.OrdinalIgnoreCase)")
    && page.Contains("_truckBarcode = \"\";")
    && page.Contains("Expected TRUCK:<license plate>"),
    "The PDA must reject and clear shipment-order text scanned in the truck stage.");
Check(page.Contains("fg05-detail-rows") && page.Contains("fg05-item-card {(scanned ? \"scanned\" : \"open\")}"),
    "Shipment and product information must use row cards with scanned state.");
Check(!page.Contains("FG-005") && !page.Contains("wh07-subtitle"),
    "Truck Loading must not show a screen code or navigation subtitle.");

var css = File.ReadAllText(Path.Combine(root, "src/05_Pda/AMES.Pda/wwwroot/css/pda.css"));
Check(css.Contains(".fg05-item-card.scanned.rz-card")
    && css.Contains("background: #123c2a !important")
    && css.Contains("border-left: 1px solid #22c55e !important"),
    "Scanned product cards must use the squared green Release-style state.");
Check(css.Contains(".pda-shell.wh.fg05-shell *::after")
    && css.Contains(".fg05-scan-panel.scan-only")
    && css.Contains("box-sizing: border-box !important")
    && css.Contains(".fg05-barcode-input.wh07-input.rz-textbox")
    && css.Contains(".fg05-shell .wh07-release-bar .rz-button")
    && css.Contains("position: relative !important")
    && css.Contains("flex: 0 0 auto !important")
    && css.Contains(".fg05-item-primary,")
    && css.Contains("min-height: 58px !important")
    && css.Contains("font-size: 11px !important"),
    "Every Truck Loading element must be squared and the scanner must fill the card width.");
var scanJs = File.ReadAllText(Path.Combine(root, "src/05_Pda/AMES.Pda/wwwroot/js/pda-scan.js"));
Check(scanJs.Contains("function scrollTo(selector, topOffset)")
    && scanJs.Contains("Number.isFinite(topOffset)"),
    "The shared scroll helper must support positioning content below a sticky guide.");
var api = File.ReadAllText(Path.Combine(root, "src/04_Api/AMES.Api/Endpoints/FgEndpoints.cs"));
Check(api.Contains("Expected TRUCK:<license plate>")
    && api.Contains(".Equals(\"TRUCK\", StringComparison.OrdinalIgnoreCase)")
    && api.Contains("new LoadingTruckRow($\"TRUCK:{truck}\", truck"),
    "Shipment orders must not be accepted as truck barcodes.");
Check(api.Contains("dbo.FG_PDA_LOADING_ORDER_SCAN")
    && api.Contains("dbo.FG_PDA_LOADING_STOCK_SCAN")
    && api.Contains("dbo.FG_PDA_LOADING_COMPLETE")
    && !api.Contains("body.StockIds.Count > 100"),
    "Truck Loading must use server procedures without a 100-stock ceiling.");
var seed = File.ReadAllText(Path.Combine(root, "dist/pda/PDA_SEED.sql"));
Check(seed.Contains("FG-DEMO-STK-005") && seed.Contains("FG-DEMO-STK-009")
    && seed.Contains("FG-DEMO-STK-010") && seed.Contains("'PICKED',   'FG-PICK-DEMO-002'")
    && seed.Contains("INSERT dbo.FG_PickingDetail"),
    "The loading demo shipment must contain three independently scannable stock rows.");
var schema = File.ReadAllText(Path.Combine(root, "dist/pda/PDA_SCHEMA.sql"));
Check(schema.Contains("CREATE OR ALTER PROCEDURE dbo.FG_PDA_LOADING_ORDER_SCAN")
    && schema.Contains("CREATE OR ALTER PROCEDURE dbo.FG_PDA_LOADING_STOCK_SCAN")
    && schema.Contains("CREATE OR ALTER PROCEDURE dbo.FG_PDA_LOADING_COMPLETE")
    && schema.Contains("D.Qty,S.Qty")
    && schema.Contains("ISNULL(S.HoldFlag,0)")
    && schema.Contains("@Status<>'PICKED'")
    && schema.Contains("DepartureTS,PalletsLoadedJSON")
    && schema.Contains("UX_FG_LoadingConfirm_LoadingNumber")
    && schema.Contains("FK_FG_LoadingConfirm_Pick"),
    "Truck Loading must preserve picking quantity, revalidate held stock, and enforce loading integrity.");
Console.WriteLine("PASS: one scanner, Truck -> Shipment Order -> Product, squared list UI.");
