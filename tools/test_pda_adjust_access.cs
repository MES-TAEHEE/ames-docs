#:sdk Microsoft.NET.Sdk.Web
#:property PublishAot=false

// Read-only integration checks. The isolated test host supplies synthetic sessions;
// no production role, PIN, stock quantity, or API authentication is changed.
using System.Collections;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Transactions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.JSInterop;

static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
var root = Directory.GetParent(Path.GetDirectoryName(SourcePath())!)!.FullName;
var bin = Path.Combine(root, "src/05_Pda/AMES.Pda/bin/Debug/net10.0-windows10.0.19041.0/win-x64");
var apiBin = Path.Combine(root, "src/04_Api/AMES.Api/bin/Debug/net10.0");
AssemblyLoadContext.Default.Resolving += (_, name) => new[] { bin, apiBin }
    .Select(path => Path.Combine(path, name.Name + ".dll")).Where(File.Exists)
    .Select(AssemblyLoadContext.Default.LoadFromAssemblyPath).FirstOrDefault();
var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bin, "AMES.Pda.dll"));
var apiAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(apiBin, "AMES.Api.dll"));
var authType = assembly.GetType("AMES.Pda.Services.AuthState", true)!;
var sessionType = authType.GetProperty("Session")!.PropertyType;
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
void Check(bool condition, string label) { if (!condition) throw new Exception(label); Console.WriteLine("PASS " + label); }

using var live = new HttpClient { BaseAddress = new Uri("http://localhost:5210") };
using var login = await live.PostAsJsonAsync("/api/auth/login", new { employeeNo = "TEST1", pin = "0000", terminalId = "PDA-DEV-01", lineId = "LINE-INJ-01", shiftCode = "A" });
var loginJson = JsonNode.Parse(await login.Content.ReadAsStringAsync())!;
live.DefaultRequestHeaders.Authorization = new("Bearer", loginJson["token"]!.GetValue<string>());
var sessionJson = JsonNode.Parse(await live.GetStringAsync("/api/auth/me"))!;
Check(!sessionJson["isAdmin"]!.GetValue<bool>(), "DB-backed TEST1 session is not an administrator");
foreach (var scope in new[] { "wh", "fg" })
{
    Check((await live.GetAsync($"/api/{scope}/adjust/location?barcode=B0-08-A1")).StatusCode == HttpStatusCode.Forbidden, scope + " live non-admin location blocked");
    Check((await live.GetAsync($"/api/{scope}/adjust/scan?scanText=UNKNOWN")).StatusCode == HttpStatusCode.Forbidden, scope + " live non-admin scan blocked");
    Check((await live.PostAsJsonAsync($"/api/{scope}/adjust/save", new { barcode = "UNKNOWN", deltaQty = 1, reasonCode = "COUNT_DIFF", supervisorPin = "0000" })).StatusCode == HttpStatusCode.Forbidden, scope + " live non-admin save blocked");
}
var ordinarySession = JsonSerializer.Deserialize(sessionJson.ToJsonString(), sessionType, jsonOptions)!;
sessionJson["isAdmin"] = true;
var adminSession = JsonSerializer.Deserialize(sessionJson.ToJsonString(), sessionType, jsonOptions)!;
using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "src/04_Api/AMES.Api/appsettings.json")), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
var data = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bin, "AMES.Data.dll"));
var factory = Activator.CreateInstance(data.GetType("AMES.Data.Connection.AmesConnectionFactory", true)!, settings.RootElement.GetProperty("ConnectionStrings").GetProperty("AMES").GetString()!)!;
var builder = WebApplication.CreateBuilder();
builder.Logging.ClearProviders();
builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Error);
builder.WebHost.UseUrls("http://127.0.0.1:0");
await using var host = builder.Build();
host.Use(async (context, next) => {
    if (context.Request.Headers.Authorization == "Bearer isolated-admin") context.Items["ames-session"] = adminSession;
    await next(context);
});
foreach (var scope in new[] { "Wh", "Fg" })
    apiAssembly.GetType($"AMES.Api.Endpoints.{scope}Endpoints", true)!.GetMethod("Map" + scope)!.Invoke(null, [host, factory]);
await host.StartAsync();
using var client = new HttpClient { BaseAddress = new Uri(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
Check((await client.GetAsync("/api/wh/adjust/location?barcode=1F-A-02")).StatusCode == HttpStatusCode.Unauthorized, "anonymous adjustment blocked");
client.DefaultRequestHeaders.Authorization = new("Bearer", "isolated-admin");

var auth = Activator.CreateInstance(authType)!;
authType.GetMethod("SignIn")!.Invoke(auth, ["isolated-admin", adminSession]);
var apiType = assembly.GetType("AMES.Pda.Services.PdaApi", true)!;
var api = Activator.CreateInstance(apiType, client, auth, factory)!;
const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
var output = Path.Combine(root, ".tmp/adjust-check");
Directory.CreateDirectory(output);
async Task Render(string typeName, string route, bool admin, Func<object, Type, Func<string>, Task> inspect)
{
    authType.GetMethod("SignIn")!.Invoke(auth, ["isolated-admin", admin ? adminSession : ordinarySession]);
    var navigation = new TestNavigation(route);
    var services = new ServiceCollection().AddLogging();
    services.AddSingleton(authType, auth); services.AddSingleton(apiType, api);
    services.AddSingleton<NavigationManager>(navigation); services.AddSingleton<IJSRuntime>(new NoJs());
    services.AddSingleton(AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bin, "Radzen.Blazor.dll")).GetType("Radzen.DialogService", true)!);
    await using var provider = services.BuildServiceProvider();
    await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
    Capture.PageType = assembly.GetType(typeName, true)!;
    await renderer.Dispatcher.InvokeAsync(async () => {
        var rendered = await renderer.RenderComponentAsync<Capture>();
        string Html() { typeof(ComponentBase).GetMethod("StateHasChanged", flags)!.Invoke(Capture.Page, null); return rendered.ToHtmlString(); }
        await inspect(Capture.Page!, Capture.PageType, Html);
        if (!admin && route.Contains("adjust")) Check(navigation.Uri.EndsWith(route.StartsWith("/fg") ? "/fg" : "/wh"), "non-admin direct route redirected");
    });
}
foreach (var scope in new[] { "Wh", "Fg" })
{
    await Render($"AMES.Pda.Components.Pages.{scope}.{scope}Home", "/" + scope.ToLower(), false,
        (_, _, html) => { Check(!html().Contains(">Adjust</div>"), scope + " non-admin menu hidden"); return Task.CompletedTask; });
    await Render($"AMES.Pda.Components.Pages.{scope}.{scope}Home", "/" + scope.ToLower(), true,
        (_, _, html) => { Check(html().Contains(">Adjust</div>"), scope + " admin menu visible"); return Task.CompletedTask; });
}
await Render("AMES.Pda.Components.Pages.Wh.Wh03InventoryStatus", "/wh/03?tab=adjust", false, (_, _, _) => Task.CompletedTask);
await Render("AMES.Pda.Components.Pages.Wh.Wh03InventoryStatus", "/fg/adjust", false, (_, _, _) => Task.CompletedTask);
foreach (var (scope, location) in new[] { ("wh", "1F-A-02"), ("fg", "B0-08-A1") })
{
    var route = scope == "wh" ? "/wh/03?tab=adjust" : "/fg/adjust";
    await Render("AMES.Pda.Components.Pages.Wh.Wh03InventoryStatus", route, true, async (page, type, html) => {
        object? Field(string name) => type.GetField(name, flags)!.GetValue(page);
        object? Prop(string name) => type.GetProperty(name, flags)!.GetValue(page);
        async Task Act(string name, params object[] args) { var result = type.GetMethod(name, flags)!.Invoke(page, args); if (result is Task task) await task; }
        type.GetField("_invBarcode", flags)!.SetValue(page, location);
        await Act("ScanInventoryLot");
        var list = Field("_adjustLocation");
        Check(list is not null, scope + " location recognized: " + Field("_invMsg"));
        var stocks = ((IEnumerable)list!.GetType().GetProperty("Items")!.GetValue(list)!).Cast<object>().ToList();
        Check(stocks.Count > 0 && html().Contains("SELECT STOCK"), scope + " DB location stock list rendered");
        await Act("SelectAdjustmentStock", stocks[0]);
        Check(Field("_invScan") is not null, scope + " stock selected and revalidated: " + Field("_invMsg"));
        var before = (decimal)Prop("InventoryBeforeQty")!;
        var stockBarcode = (string)Field("_invBarcode")!;
        await Act("OpenQuantityPad");
        Check((bool)Field("_quantityPadOpen")! && html().Contains("NEW QUANTITY") && html().Contains("inputmode=\"none\"") && html().Contains("readonly"), scope + " quantity keypad opens from readonly input");
        await File.WriteAllTextAsync(Path.Combine(output, scope + "-keypad.html"), "<html><head><meta name='viewport' content='width=device-width,initial-scale=1'><link rel='stylesheet' href='../fg-history/radzen.css'><link rel='stylesheet' href='../../src/05_Pda/AMES.Pda/wwwroot/css/pda.css'></head><body>" + html() + "</body></html>");
        await Act("OnHardwareScanAsync", "999999999");
        Check((string)Field("_invBarcode")! == stockBarcode && Field("_invScan") is not null, scope + " quantity keypad blocks barcode routing");
        while (((string)Field("_quantityPadDraft")!).Length > 0) await Act("DeleteQuantityDigit");
        await Act("PressQuantityDigit", "1");
        await Act("PressQuantityDigit", "2");
        await Act("ConfirmQuantityPad");
        Check(!(bool)Field("_quantityPadOpen")! && (decimal)Prop("InventoryAfterQty")! == 12m, scope + " keypad confirms integer quantity");
        await Act("OnAdjustmentQuantityInput", new ChangeEventArgs { Value = "12" });
        Check((decimal)Prop("InventoryAfterQty")! == 12m && (decimal)Field("_invAdjustDelta")! == 12m - before, scope + " typed integer quantity and delta");
        Check(!(bool)Prop("DisableInventoryAdjust")!, scope + " save enabled without supervisor or PIN");
        Check(!html().Contains("aria-label=\"Supervisor\"") && !html().Contains("aria-label=\"Supervisor PIN\"") && !html().Contains("wh03-qty-readout"), scope + " no supervisor, PIN, or quantity sublabel");
        foreach (var invalid in new[] { "", "-1", "abc", "1.5", "1.0", "1.0001", "1000000000" })
        {
            await Act("OnAdjustmentQuantityInput", new ChangeEventArgs { Value = invalid });
            Check((bool)Field("_adjustQuantityInvalid")! && (bool)Prop("DisableInventoryAdjust")!, scope + " invalid quantity blocked: " + invalid);
        }
        await Act("OnAdjustmentQuantityInput", new ChangeEventArgs { Value = "0" });
        Check(!(bool)Field("_adjustQuantityInvalid")! && (decimal)Prop("InventoryAfterQty")! == 0, scope + " zero stock allowed");
        await Act("StepInventoryAdjust", 1m);
        Check((decimal)Prop("InventoryAfterQty")! == 1, scope + " stepper works after typing");
        var endpoint = apiAssembly.GetType($"AMES.Api.Endpoints.{(scope == "wh" ? "Wh" : "Fg")}Endpoints", true)!;
        var requestType = endpoint.GetNestedType("AdjustSaveReq")!;
        var barcode = (string)stocks[0].GetType().GetProperty("Barcode")!.GetValue(stocks[0])!;
        foreach (var delta in new[] { 1m, 0.5m })
        {
            var request = JsonSerializer.Deserialize(JsonSerializer.Serialize(new { barcode, deltaQty = delta, reasonCode = "COUNT_DIFF", reasonNote = "Rollback-only integer adjustment check" }), requestType, jsonOptions)!;
            using var transaction = new TransactionScope(TransactionScopeOption.RequiresNew, TransactionScopeAsyncFlowOption.Enabled);
            object[] arguments = scope == "wh"
                ? [factory, request, sessionJson["employeeNo"]!.GetValue<string>(), sessionJson["operatorId"]!.GetValue<string>(), false]
                : [factory, request, sessionJson["employeeNo"]!.GetValue<string>(), sessionJson["operatorId"]!.GetValue<string>()];
            var result = endpoint.GetMethod("ExecuteAdjustSave", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, arguments)!;
            var success = (bool)result.GetType().GetProperty("Success")!.GetValue(result)!;
            var message = (string)result.GetType().GetProperty("Message")!.GetValue(result)!;
            Check(delta == 1 ? success : !success && message.Contains("whole number"), scope + " API / procedure without PIN, delta " + delta + ": " + message);
            // Deliberately do not Complete: stock and history changes must roll back.
        }
        await Act("SelectAdjustmentStock", stocks[0]);
        Check((decimal)Prop("InventoryBeforeQty")! == before, scope + " save test rolled back stock changes");
        await File.WriteAllTextAsync(Path.Combine(output, scope + ".html"), "<html><head><meta name='viewport' content='width=device-width,initial-scale=1'><link rel='stylesheet' href='../fg-history/radzen.css'><link rel='stylesheet' href='../../src/05_Pda/AMES.Pda/wwwroot/css/pda.css'></head><body>" + html() + "</body></html>");
        await Act("ClearInventoryWork");
        Check(Field("_adjustLocation") is null && Field("_invScan") is null, scope + " clear removes selection and location list");
    });
}
using var inboundConnection = (System.Data.Common.DbConnection)factory.GetType().GetMethod("OpenConnection")!.Invoke(factory, null)!;
using var inboundQuery = inboundConnection.CreateCommand();
inboundQuery.CommandText = "SELECT TOP 1 P.BoxBarcode FROM dbo.WH_InboundPackage P JOIN dbo.WH_Inventory W ON W.LotID=P.LotID WHERE P.ReceiveType='LOCAL' AND W.OnHandQty>0 AND W.Status NOT IN ('Canceled','Released','Picked') ORDER BY P.BoxBarcode";
var receivedBarcode = inboundQuery.ExecuteScalar() as string;
if (receivedBarcode is not null) await Render("AMES.Pda.Components.Pages.Wh.Wh02PdaInbound", "/wh/02", false, async (page, type, html) => {
    type.GetField("_mode", flags)!.SetValue(page, "LOCAL");
    type.GetField("_barcode", flags)!.SetValue(page, receivedBarcode);
    await (Task)type.GetMethod("Scan", flags)!.Invoke(page, null)!;
    Check((bool)type.GetProperty("AlreadyReceived", flags)!.GetValue(page)!, "Inbound existing receipt detected");
    Check((string)type.GetField("_cls", flags)!.GetValue(page)! == "fail", "Inbound duplicate uses red error status");
    Check(html().Contains("wh02-lot-card received") && html().Contains("wh02-cancel-btn"), "Inbound received card and cancellation button rendered");
    await File.WriteAllTextAsync(Path.Combine(output, "inbound.html"), "<html><head><meta name='viewport' content='width=device-width,initial-scale=1'><link rel='stylesheet' href='../fg-history/radzen.css'><link rel='stylesheet' href='../../src/05_Pda/AMES.Pda/wwwroot/css/pda.css'></head><body>" + html() + "</body></html>");
});
await host.StopAsync();
Console.WriteLine("PASS: Read-only adjustment API, role visibility, location selection, and quantity entry checks. No stock was saved.");

sealed class TestNavigation : NavigationManager { public TestNavigation(string path) => Initialize("http://localhost/", "http://localhost" + path); protected override void NavigateToCore(string uri, bool forceLoad) => Uri = ToAbsoluteUri(uri).ToString(); protected override void NavigateToCore(string uri, NavigationOptions options) => Uri = ToAbsoluteUri(uri).ToString(); }
sealed class NoJs : IJSRuntime { public ValueTask<T> InvokeAsync<T>(string id, object?[]? args) => ValueTask.FromResult(default(T)!); public ValueTask<T> InvokeAsync<T>(string id, CancellationToken cancellation, object?[]? args) => InvokeAsync<T>(id, args); }
sealed class Capture : ComponentBase { public static Type PageType = null!; public static object? Page; protected override void BuildRenderTree(RenderTreeBuilder builder) { builder.OpenComponent(0, PageType); builder.AddComponentReferenceCapture(1, page => Page = page); builder.CloseComponent(); } }
