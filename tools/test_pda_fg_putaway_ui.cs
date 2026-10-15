// Read-only source check; this tool never changes database state.
var root = Directory.GetCurrentDirectory();
string Read(string file) => File.ReadAllText(Path.Combine(root, file));
void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
var ui = Read("src/05_Pda/AMES.Pda/Components/Pages/Fg/Fg01Stocking.razor");
var api = Read("src/04_Api/AMES.Api/Endpoints/FgEndpoints.cs");
var sql = Read("dist/migrate_fg_putaway_transactions.sql");
Check(ui.Contains("_scan.LotNo, _selectedLocation.LocationId"), "Confirmation must send only LOT and location.");
Check(!api.Contains("dbo.FG_PutAway"), "API must not use the retired table.");
Check(api.Contains("dbo.FG_PDA_PUTAWAY_COMPLETE"), "Put-away must use the atomic inventory/transaction procedure.");
Check(!api.Contains("ResolvePalletSplit"), "Do not invent pallet quantities for location put-away.");
Check(sql.Contains("CASE WHEN @StoredQty IS NULL THEN 'IN' ELSE 'MOVE' END"), "Existing unlocated inventory must move without another receipt.");
Check(sql.Contains("This FG LOT is already stocked"), "Already stocked LOTs must not be duplicated.");
Check(ui.Contains("Load Capacity") && ui.Contains("Area") && ui.Contains("Aisle"), "Preserve location details.");
Console.WriteLine("PASS: FG LOT/location put-away uses a single atomic inventory and transaction path.");
