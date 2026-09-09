#:sdk Microsoft.NET.Sdk.Web
#:property PublishAot=false

using System.Collections;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.AspNetCore.Components;

static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
var root = Directory.GetParent(Path.GetDirectoryName(SourcePath())!)!.FullName;
var bin = Environment.GetEnvironmentVariable("AMES_PDA_TEST_BIN")
    ?? Path.Combine(root, "src/05_Pda/AMES.Pda/bin/Debug/net10.0-windows10.0.19041.0/win-x64");
AssemblyLoadContext.Default.Resolving += (_, name) => File.Exists(Path.Combine(bin, name.Name + ".dll"))
    ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bin, name.Name + ".dll")) : null;
var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bin, "AMES.Pda.dll"));
using var client = new HttpClient { BaseAddress = new Uri("http://localhost:5210") };
static void Check(bool valid, string message) { if (!valid) throw new Exception(message); }
async Task<string> Login(string employee)
{
    using var response = await client.PostAsJsonAsync("/api/auth/login", new
    { employeeNo = employee, pin = "0000", terminalId = "PDA-DEV-01", lineId = "LINE-INJ-01", shiftCode = "A" });
    response.EnsureSuccessStatusCode();
    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    var token = json.RootElement.GetProperty("token").GetString()!;
    Check(!string.IsNullOrEmpty(token), employee + " login failed.");
    return token;
}
foreach (var screen in new[] { "qc", "putaway", "inventory", "release", "loading", "return", "adjust" })
{
    using var response = await client.PostAsync($"/api/fg/test/ppt-reset/{screen}", null);
    Check(response.StatusCode == HttpStatusCode.Unauthorized, "Anonymous reset must be denied.");
}
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await Login("TEST"));
using (var detailedReset = await client.PostAsync("/api/fg/test/ppt-reset/release", null))
    Check(detailedReset.IsSuccessStatusCode, "TEST must reset detailed FG scenario data.");
var token = await Login("TEST1");
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
using (var invalid = await client.PostAsync("/api/fg/test/ppt-reset/other", null))
    Check(invalid.StatusCode == HttpStatusCode.BadRequest, "Unknown reset scope must be rejected.");
var authType = assembly.GetType("AMES.Pda.Services.AuthState", true)!;
var auth = Activator.CreateInstance(authType)!;
var session = JsonSerializer.Deserialize(await client.GetStringAsync("/api/auth/me"), authType.GetProperty("Session")!.PropertyType,
    new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
authType.GetMethod("SignIn")!.Invoke(auth, [token, session]);
using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "src/04_Api/AMES.Api/appsettings.json")),
    new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
var connectionString = settings.RootElement.GetProperty("ConnectionStrings").GetProperty("AMES").GetString()!;
var dataAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bin, "AMES.Data.dll"));
var factory = Activator.CreateInstance(dataAssembly.GetType("AMES.Data.Connection.AmesConnectionFactory", true)!, [connectionString])!;
var api = Activator.CreateInstance(assembly.GetType("AMES.Pda.Services.PdaApi", true)!, [client, auth, factory])!;

void CheckDetailedCatalog(string method, string pageType, string sourceField, int expected, string prefix)
{
    var catalog = assembly.GetType("AMES.Pda.Components.FgDetailedScenarioCatalog", true)!;
    var page = assembly.GetType(pageType, true)!;
    var source = (Array)page.GetField(sourceField, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
    var scenarios = (Array)catalog.GetMethod(method, BindingFlags.Static | BindingFlags.Public)!.Invoke(null, [source])!;
    Check(scenarios.Length == expected, $"{prefix} detailed scenario count must be {expected}.");
    for (var index = 0; index < scenarios.Length; index++)
    {
        var scenario = scenarios.GetValue(index)!;
        var type = scenario.GetType();
        Check((string)type.GetProperty("TestCaseId")!.GetValue(scenario)! == $"{prefix}-TC-{index + 1:000}", $"{prefix} test case numbering must be sequential.");
        var sourceStep = (int)type.GetProperty("SourceStep")!.GetValue(scenario)!;
        Check(sourceStep >= 1 && sourceStep <= source.Length, $"{prefix} source step must be valid.");
    }
}
CheckDetailedCatalog("Qc", "AMES.Pda.Components.Pages.Fg.Fg01QcComplete", "PptSteps", 6, "FG001");
CheckDetailedCatalog("PutAway", "AMES.Pda.Components.Pages.Fg.Fg01Stocking", "PptSteps", 13, "FG002");
CheckDetailedCatalog("Inventory", "AMES.Pda.Components.Pages.Fg.Fg02Inventory", "PptSteps", 13, "FG003");
CheckDetailedCatalog("Picking", "AMES.Pda.Components.Pages.Fg.Fg04FifoPicking", "PptSteps", 15, "FG004");
CheckDetailedCatalog("Loading", "AMES.Pda.Components.Pages.Fg.Fg05Loading", "PptSteps", 18, "FG005");
CheckDetailedCatalog("Returns", "AMES.Pda.Components.Pages.Fg.FgRtnReturn", "PptSteps", 11, "FG006");
CheckDetailedCatalog("Adjust", "AMES.Pda.Components.Pages.Wh.Wh03InventoryStatus", "PptFgAdjustSteps", 18, "FG007");
CheckDetailedCatalog("Transactions", "AMES.Pda.Components.Pages.Wh.Wh08TransactionHistory", "FgPptSteps", 13, "FG008");
Driver Page(string name, string route) => new(assembly.GetType("AMES.Pda.Components.Pages." + (name.StartsWith("Wh") ? "Wh." : "Fg.") + name, true)!, auth, api, route);
async Task ResetAll()
{
    foreach (var screen in new[] { "qc", "putaway", "inventory", "release", "loading", "return", "adjust" })
    {
        using var response = await client.PostAsync($"/api/fg/test/ppt-reset/{screen}", null);
        response.EnsureSuccessStatusCode();
    }
}
try
{
    await ResetAll();
    var qc = Page("Fg01QcComplete", "/fg/01");
    await qc.Act("StartPptStep", 1);
    var qcRows = ((IEnumerable)qc.Field("_rows")!).Cast<object>().ToArray();
    var samples = qcRows.Where(x => (string)x.GetType().GetProperty("ItemNo")!.GetValue(x)! == "PPT-FG-QC").ToArray();
    Check(samples.Length == 4, "QC must show four age samples.");
    Check(samples.Select(x => (string)qc.Call("AgeClass", x)!).SequenceEqual(new[] { "waiting-age-10", "waiting-age-5", "waiting-age-1", "" }), "QC age colors and oldest-first order must match.");
    await qc.Act("StartPptStep", 2);
    var put = Page("Fg01Stocking", "/fg/02");
    await put.Act("StartPptStep", 1);
    Check(put.Field("_scan") is null && put.Bool("DisableConfirm"), "Put-Away initial state must block Confirm.");
    await put.Act("ConfirmPutAway");
    Check(put.Text("_modalTitle") == "Put-Away Check", "Missing LOT must block Put-Away.");
    put.Call("DismissModal");
    await put.Act("StartPptStep", 2);
    Check(put.Bool("ReadyForStorage") && !put.Bool("ReadyForLocation"), "QC LOT must require storage.");
    foreach (var kind in new[] { "BOX", "PALLET", "RACK" })
    {
        await put.Act("RunPptValue", kind);
        Check(!put.Bool("ReadyForLocation"), "Container barcode required for " + kind);
        await put.Act("RunPptValue", "CONTAINER");
        Check(put.Bool("ReadyForLocation"), "Container must validate for " + kind);
    }
    await put.Act("RunPptValue", "LOCATION");
    Check(put.Bool("ReadyForLocation") && !put.Bool("RequiresContainer"), "Location Only skips container.");
    await put.Act("StartPptStep", 4);
    await put.Act("RunPptValue", "CONTAINER");
    Check(put.Bool("_highlightLocationScan") && put.Bool("_scrollLocationAfterRender"), "Container must scroll to and highlight location.");
    await put.Act("StartPptStep", 5);
    Check(!put.Bool("DisableConfirm"), "Valid location enables Put-Away.");
    await put.Act("ConfirmPutAway");
    Check(put.Text("_modalTitle") == "Put-Away Complete", "Put-Away must persist.");
    put.Call("DismissModal");
    Check(put.Field("_scan") is null, "Put-Away must reset after OK.");
    await put.Act("ResetPptData");

    var inventory = Page("Fg02Inventory", "/fg/03");
    for (var step=1; step<=5; step++)
    {
        await inventory.Act("StartPptStep", step);
        if (step == 1) Check(inventory.Count("LocationRows") > 0, "Stocked locations must load.");
        if (step == 2) Check(inventory.Count("LocationParts") == 2, "Location must have two parts.");
        if (step == 3 || step == 5) Check(inventory.Count("_partLots") == 2, "Part details must show two LOTs.");
        if (step == 4) Check(inventory.Count("PartRows") == 1, "Same part and location must aggregate to one row.");
    }
    await inventory.Act("RunPptValue", "SEARCH");
    Check(inventory.Count("LocationRows") == 1, "Search must filter one sample location.");
    inventory.Call("ClearSearch");
    Check(inventory.Text("_query") == "", "CLEAR must remove inventory filter.");

    var release = Page("Fg04FifoPicking", "/fg/04");
    await release.Act("RunPptValue", "LOT_FIRST");
    Check(release.Text("_modalTitle") == "Outgoing Slip Required", "LOT without slip must be blocked.");
    await release.Act("RunPptValue", "UNKNOWN_SLIP");
    Check(release.Text("_modalTitle") == "Outgoing Slip Not Found", "Missing slip must show existing error.");
    await release.Act("StartPptStep", 1);
    Check(release.Count("_lines") == 2 && !release.Bool("AllScanned"), "Slip must load two parts without scans.");
    await release.Act("StartPptStep", 2);
    Check(release.Count("_scannedLots") == 1 && !release.Bool("AllScanned"), "10/24 partial must block Complete.");
    await release.Act("CompleteRelease");
    Check(release.Field("_selectedSlip") is not null, "Incomplete release must not save.");
    await release.Act("StartPptStep", 3);
    Check(release.Count("_scannedLots") == 2 && !release.Bool("AllScanned"), "Second part complete must not complete first.");
    await release.Act("StartPptStep", 4);
    Check(release.Bool("AllScanned"), "Three LOTs must complete all parts.");
    await release.Act("RunPptValue", "SLIP");
    Check(release.Count("_scannedLots") == 0 && !release.Bool("AllScanned"), "CANCEL and reload must not restore unsaved scans.");
    await release.Act("StartPptStep", 5);
    Check(release.Text("_modalTitle") == "FIFO Order" && release.Text("_modalMessage").Contains("5011FG260908930001") && release.Text("_modalMessage").Contains("FG-PPT-C1") && release.Count("_scannedLots") == 0, "FIFO must reject later LOT and name first LOT/location.");
    await release.Act("StartPptStep", 6);
    await release.Act("CompleteRelease");
    Check(release.Text("_modalTitle") == "Release Complete" && release.Field("_selectedSlip") is null, "Release must save and reset.");
    using (var ready = await client.GetAsync("/api/fg/loading/order/scan?barcode=FG-PPT-SO-REL"))
        Check(ready.IsSuccessStatusCode, "Completed release must be available for loading.");
    await release.Act("ResetPptData");

    var loading = Page("Fg05Loading", "/fg/05");
    await loading.Act("StartPptStep", 1);
    Check(loading.Field("_truck") is null && !loading.Bool("AllScanned"), "Loading starts with truck.");
    await loading.Act("RunPptValue", "WRONG_ORDER");
    Check(loading.Text("_modalTitle") == "Truck Barcode" && loading.Field("_truck") is null, "Shipment cannot substitute for truck.");
    await loading.Act("StartPptStep", 2);
    Check(loading.Field("_truck") is not null && loading.Field("_order") is null, "Truck verified before order.");
    await loading.Act("StartPptStep", 3);
    Check(loading.Bool("_scrollShipmentAfterRender") && loading.Field("_order") is not null, "Order must trigger shipment scroll.");
    await loading.Act("StartPptStep", 4);
    Check(loading.Count("_scannedStockIds") == 1 && !loading.Bool("AllScanned"), "One stock keeps Confirm blocked.");
    await loading.Act("ConfirmLoading");
    Check(loading.Text("_modalTitle") == "Products Remaining", "Incomplete loading must not save.");
    await loading.Act("StartPptStep", 5);
    Check(loading.Count("_scannedStockIds") == 3 && loading.Bool("AllScanned"), "Three stocks enable Confirm.");
    await loading.Act("StartPptStep", 6);
    Check(loading.Count("_scannedStockIds") == 1 && loading.Text("_modalTitle") == "Already Scanned", "Duplicate stock must not increase count.");
    await loading.Act("StartPptStep", 7);
    await loading.Act("ConfirmLoading");
    Check(loading.Text("_modalTitle") == "Loading Complete" && loading.Text("_modalMessage").Contains("PPT-FG-01") && loading.Field("_truck") is null, "Loading must persist and clear.");
    await loading.Act("ResetPptData");

    var returns = Page("FgRtnReturn", "/fg/06");
    await returns.Act("StartPptStep", 1);
    Check(!returns.Bool("CanSubmit") && returns.Field("_product") is null, "Return starts empty.");
    await returns.Act("StartPptStep", 2);
    Check(returns.Field("_product") is not null && !returns.Bool("CanSubmit"), "Reason required after product scan.");
    await returns.Act("RunPptValue", "NO_REASON");
    Check(returns.Text("_modalTitle") == "Return Reason Required", "Missing return reason must show alert.");
    await returns.Act("RunPptValue", "NOT_SHIPPED");
    Check(returns.Field("_product") is null && returns.Text("_modalMessage").Contains("no completed shipment"), "Unshipped product must be rejected.");
    await returns.Act("StartPptStep", 3);
    Check(returns.Bool("_reasonOpen"), "Reason options must open.");
    await returns.Act("RunPptValue", "REASON");
    Check(returns.Bool("CanSubmit") && returns.Text("_note") == "PPT return note" && !returns.Bool("_reasonOpen"), "Reason and note must prepare Return.");
    await returns.Act("StartPptStep", 4);
    await returns.Act("Submit");
    Check(returns.Text("_modalTitle") == "Return Received" && returns.Field("_product") is null, "Return must save and clear.");
    await returns.Act("RunPptValue", "PRODUCT");
    Check(returns.Field("_product") is null && returns.Text("_modalTitle") == "Return Validation", "Duplicate return must be blocked.");
    await returns.Act("ResetPptData");

    var adjust = Page("Wh03InventoryStatus", "/fg/adjust");
    adjust.Set("_workTab", "Adjust");
    await adjust.Act("StartPptStep", 1);
    Check((decimal)adjust.Property("InventoryBeforeQty")! == 10, "FG Adjust sample must begin at 10 EA.");
    await adjust.Act("RunPptValue", "WH_LOT");
    Check(adjust.Field("_invScan") is null && adjust.Bool("_modalOpen"), "Warehouse material must be blocked in FG Adjust.");
    adjust.Call("DismissModal");
    await adjust.Act("StartPptStep", 2);
    await adjust.Act("RunPptValue", "PLUS");
    Check((decimal)adjust.Property("InventoryAfterQty")! == 13, "FG +3 adjustment preview.");
    await adjust.Act("RunPptValue", "MINUS");
    Check((decimal)adjust.Property("InventoryAfterQty")! == 7, "FG -3 adjustment preview.");
    await adjust.Act("StartPptStep", 3);
    Check(adjust.Text("_invAdjustNote").Length > 0, "FG adjustment Note.");
    await adjust.Act("StartPptStep", 4);
    await adjust.Act("RunPptValue", "REASON");
    await adjust.Act("StartPptStep", 5);
    Check(!adjust.Bool("DisableInventoryAdjust"), "FG whole-number quantity must enable save for TEST1 admin.");
    await adjust.Act("StartPptStep", 6);
    Check(adjust.Bool("_adjustQuantityInvalid") && adjust.Bool("DisableInventoryAdjust"), "FG decimal quantity must block save.");
    await adjust.Act("StartPptStep", 7);
    Check(!adjust.Bool("DisableInventoryAdjust"), "Valid quantity and reason must enable FG save.");
    await adjust.Act("PostInventoryAdjust");
    Check(adjust.Text("_modalTitle") == "Saved" && adjust.Field("_invScan") is null, "FG Adjust must save and clear.");
    using (var saved = JsonDocument.Parse(await client.GetStringAsync("/api/fg/adjust/scan?scanText=5011FG260908960001")))
        Check(saved.RootElement.GetProperty("qty").GetDecimal() == 13, "FG adjusted quantity must persist.");

    Console.WriteLine("PASS: 107 FG detailed scenarios; all 36 FG PPT steps; TEST and TEST1 scoped reset; QC aging; Put-Away storage/confirm; Inventory details; Release partial/FIFO/complete; Loading sequence/duplicate/confirm; Return reason/note/duplicate; admin Adjust quantity/save.");
}
finally { await ResetAll(); }

sealed class TestNavigation : NavigationManager
{
    public TestNavigation(string route) => Initialize("http://localhost/", "http://localhost" + route);
    protected override void NavigateToCore(string uri, bool forceLoad) => Uri = ToAbsoluteUri(uri).ToString();
}
sealed class Driver
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly Type _type;
    private readonly object _page;
    public Driver(Type type, object auth, object api, string route)
    {
        _type = type;
        _page = Activator.CreateInstance(type)!;
        type.GetProperty("Auth", Flags)!.SetValue(_page, auth);
        type.GetProperty("Api", Flags)!.SetValue(_page, api);
        type.GetProperty("Nav", Flags)!.SetValue(_page, new TestNavigation(route));
    }
    public object? Field(string name) => _type.GetField(name, Flags)!.GetValue(_page);
    public object? Property(string name) => _type.GetProperty(name, Flags)!.GetValue(_page);
    public void Set(string name, object value) => _type.GetField(name, Flags)!.SetValue(_page, value);
    public string Text(string name) => (string)Field(name)!;
    public bool Bool(string name) => (bool)(_type.GetField(name, Flags) is { } field ? field.GetValue(_page) : Property(name))!;
    public int Count(string name) => ((IEnumerable)(_type.GetField(name, Flags) is { } field ? field.GetValue(_page) : Property(name))!).Cast<object>().Count();
    public object? Call(string name, params object[] arguments) => _type.GetMethod(name, Flags)!.Invoke(_page, arguments);
    public async Task Act(string name, params object[] arguments) => await (Task)Call(name, arguments)!;
}
