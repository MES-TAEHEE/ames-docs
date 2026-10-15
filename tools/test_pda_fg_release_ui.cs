// Read-only source check: dotnet run --file tools/test_pda_fg_release_ui.cs
#:sdk Microsoft.NET.Sdk.Web
#:property PublishAot=false
static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path="")=>path;
var root=Directory.GetParent(Path.GetDirectoryName(SourcePath())!)!.FullName;
var source=File.ReadAllText(Path.Combine(root,"src/05_Pda/AMES.Pda/Components/Pages/Fg/Fg04FifoPicking.razor"));
var sql=File.ReadAllText(Path.Combine(root,"dist/migrate_fg_shipment_actual.sql"));
void Check(bool condition,string message) { if(!condition)throw new Exception(message); }
Check(source.Contains("fg04-items") && source.Contains("Text=\"OUTBOUND\"") && source.Contains("Text=\"CLEAR\""),"Outbound uses a compact inventory-unit list and clear/confirm actions.");
Check(source.Contains("Customer Required") && source.Contains("FgOutboundUnitReq(barcode, _customerId)"),"Outbound must send a selected or production-matched customer.");
Check(source.Contains("_isBusy || _modalOpen"),"Busy/modal state must prevent repeated scans and submissions.");
Check(sql.Contains("INSERT dbo.FG_Shipment") && sql.Contains("'FG_OUTBOUND',@ShipmentID"),"Actual shipment and inventory transactions must share a shipment link.");
Check(sql.Contains("BEGIN TRANSACTION") && sql.Contains("ROLLBACK TRANSACTION"),"Outbound and return must remain atomic.");
Console.WriteLine("PASS: actual barcode outbound, customer selection, source linking and atomic persistence.");
