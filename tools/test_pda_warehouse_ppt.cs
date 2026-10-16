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
var bin = Path.Combine(root, "src/05_Pda/AMES.Pda/bin/Debug/net10.0-windows10.0.19041.0/win-x64");
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
foreach (var screen in new[] { "release", "inventory", "adjust", "history" })
{
    using var response = await client.PostAsync($"/api/wh/test/ppt-reset/{screen}", null);
    Check(response.StatusCode == HttpStatusCode.Unauthorized, "Anonymous reset must be denied.");
}
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await Login("TEST"));
using (var forbidden = await client.PostAsync("/api/wh/test/ppt-reset/release", null))
    Check(forbidden.StatusCode == HttpStatusCode.Forbidden, "TEST must not reset TEST1 data.");
var token = await Login("TEST1");
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
using (var invalid = await client.PostAsync("/api/wh/test/ppt-reset/other", null))
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
Driver Page(string name, string route) => new(assembly.GetType("AMES.Pda.Components.Pages.Wh." + name, true)!, auth, api, route);
async Task ResetAll()
{
    foreach (var screen in new[] { "release", "inventory", "adjust", "history" })
    {
        using var response = await client.PostAsync($"/api/wh/test/ppt-reset/{screen}", null);
        response.EnsureSuccessStatusCode();
    }
}
try
{
    await ResetAll();
    var release = Page("Wh07PdaRelease", "/wh/07");
    await release.Act("StartPptStep", 2);
    Check(release.Count("_lines") == 2, "Release must load two requested parts.");
    await release.Act("RunPptValue", "FIFO");
    Check(release.Count("_pendingPicks") == 0 && release.Text("_modalMessage").Contains("5011LL260908810001"), "FIFO must name the first LOT without accepting the second.");
    release.Call("DismissModal");
    await release.Act("StartPptStep", 3);
    await release.Act("RunPptValue", "LOT1");
    Check(release.Count("_pendingPicks") == 1 && !release.Bool("CanRelease"), "Partial scan must block Release.");
    await release.Act("RunPptValue", "LOT1");
    Check(release.Count("_pendingPicks") == 1 && release.Bool("_modalOpen"), "Duplicate LOT must not be counted.");
    release.Call("DismissModal");
    await release.Act("RunPptValue", "LOT2");
    await release.Act("RunPptValue", "LOT3");
    Check(release.Bool("CanCompleteRelease") && !release.Bool("CanRelease"), "Complete scans still require outgoing type.");
    foreach (var outgoing in new[] { "PRODUCTION", "OTHER", "DEFECT" })
    {
        await release.Act("RunPptValue", outgoing);
        Check(release.Bool("CanRelease") && release.Text("_outgoingType") == outgoing, "Outgoing type must enable Release.");
    }
    await release.Act("StartPptStep", 6);
    await release.Act("ReleaseAsync");
    Check(release.Text("_modalTitle") == "Release Complete" && release.Count("_pendingPicks") == 0, "Release must save and clear the work.");
    release.Call("DismissModal");
    await release.Act("ResetPptData");
    release.Call("DismissModal");
    await release.Act("RunPptValue", "DIRECT");
    Check(release.Field("_directLot") is not null && release.Count("_lines") == 0, "Standalone LOT must enter direct outgoing.");

    var inventory = Page("Wh03InventoryStatus", "/wh/03");
    for (var step = 1; step <= 5; step++)
    {
        await inventory.Act("StartPptStep", step);
        if (step == 1) Check(inventory.Count("VisibleInventoryLocations") > 0, "Inventory locations must load.");
        if (step == 2) Check(inventory.Count("SelectedLocationParts") > 0, "Location must show its parts.");
        if (step == 3) Check(inventory.Count("_selectedLocationLots") == 2, "Location/Part must show both sample LOTs.");
        if (step == 4) Check(inventory.Count("BrowsePartRows") == 1, "Part search must show the selected part's location.");
        if (step == 5) Check(inventory.Count("_browsePartLots") == 2, "Part detail must show both LOTs behind the 50 EA total.");
    }

    var adjust = Page("Wh03InventoryStatus", "/wh/03?tab=adjust");
    adjust.Set("_workTab", "Adjust");
    await adjust.Act("StartPptStep", 2);
    Check((decimal)adjust.Property("InventoryBeforeQty")! == 10, "Adjust must start with 10 EA.");
    await adjust.Act("RunPptValue", "PLUS");
    Check((decimal)adjust.Property("InventoryAfterQty")! == 13, "Plus sample must show 13 EA.");
    await adjust.Act("RunPptValue", "MINUS");
    Check((decimal)adjust.Property("InventoryAfterQty")! == 7, "Minus sample must show 7 EA.");
    await adjust.Act("StartPptStep", 3);
    Check(adjust.Text("_invAdjustNote").Length > 0, "Adjustment note must be populated.");
    await adjust.Act("StartPptStep", 5);
    Check(!adjust.Bool("DisableInventoryAdjust"), "Whole-number quantity must enable save for TEST1 admin.");
    await adjust.Act("StartPptStep", 6);
    Check(adjust.Bool("_adjustQuantityInvalid") && adjust.Bool("DisableInventoryAdjust"), "Decimal quantity must block save.");
    await adjust.Act("StartPptStep", 7);
    Check(!adjust.Bool("DisableInventoryAdjust"), "Valid quantity and reason must enable save.");
    await adjust.Act("PostInventoryAdjust");
    Check(adjust.Text("_modalTitle") == "Saved" && adjust.Field("_invScan") is null, "Adjust must save and clear.");
    using (var saved = JsonDocument.Parse(await client.GetStringAsync("/api/wh/adjust/scan?scanText=5011LL260908830001")))
        Check(saved.RootElement.GetProperty("qty").GetDecimal() == 13, "DB adjustment must persist 13 EA.");

    var history = Page("Wh08TransactionHistory", "/wh/08");
    await history.Act("StartPptStep", 1);
    Check(history.Count("FilteredRows") == 3, "History must contain three sample transactions.");
    foreach (var kind in new[] { "IN", "OUT", "ADJ" })
    {
        await history.Act("RunPptValue", kind);
        Check(history.Count("FilteredRows") == 1, "Type filter must show one sample row.");
    }
    await history.Act("RunPptValue", "YESTERDAY");
    Check(history.Count("FilteredRows") == 0, "Yesterday must not include today's samples.");
    await history.Act("RunPptValue", "UNKNOWN");
    Check(history.Count("FilteredRows") == 0, "Unknown barcode must have no history.");
    await history.Act("StartPptStep", 5);
    var detail = history.Field("_detailRow")!;
    Check((decimal)detail.GetType().GetProperty("BeforeQty")!.GetValue(detail)! == 16
        && (decimal)detail.GetType().GetProperty("AfterQty")!.GetValue(detail)! == 18, "Detail quantities must match the adjustment.");
    history.Call("CloseDetail");
    Check(history.Field("_detailRow") is null, "CLOSE must dismiss detail.");
    Console.WriteLine("PASS: TEST1 reset authorization; Release FIFO and completion; Inventory five views; admin Adjust quantity/save; Transactions dates/types/detail.");
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
