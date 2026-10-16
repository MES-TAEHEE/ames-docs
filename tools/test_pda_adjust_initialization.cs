#:sdk Microsoft.NET.Sdk.Web
#:property PublishAot=false

// Read-only rendered-component regression check; no live API or database needed.
// dotnet run --file tools/test_pda_adjust_initialization.cs -- <optional PDA bin directory>
using System.Collections;
using System.Net;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

#pragma warning disable ASP0000 // Each case intentionally renders in an isolated service provider.

static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string p = "") => p;
var root = Directory.GetParent(Path.GetDirectoryName(SourcePath())!)!.FullName;
var bin = args.FirstOrDefault() ?? Path.Combine(root, "src/05_Pda/AMES.Pda/bin/Debug/net10.0-windows10.0.19041.0/win-x64");
bin = Path.GetFullPath(bin);
AssemblyLoadContext.Default.Resolving += (_, n) => File.Exists(Path.Combine(bin, n.Name + ".dll"))
    ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bin, n.Name + ".dll")) : null;
var asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bin, "AMES.Pda.dll"));
var authType = asm.GetType("AMES.Pda.Services.AuthState", true)!;
var apiType = asm.GetType("AMES.Pda.Services.PdaApi", true)!;
var data = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bin, "AMES.Data.dll"));
var factory = Activator.CreateInstance(data.GetType("AMES.Data.Connection.AmesConnectionFactory", true)!, "Server=unused;Database=unused;Integrated Security=True")!;
var session = JsonSerializer.Deserialize("""
    {"sessionId":1,"operatorId":"test","employeeNo":"SCTEST1","employeeName":"Test",
     "terminalId":"test","lineId":"test","shiftCode":"DAY","authMethod":0,
     "startedAt":"2026-09-10T00:00:00Z","expiresAt":"2099-01-01T00:00:00Z","isAdmin":true}
    """, authType.GetProperty("Session")!.PropertyType, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
foreach (var (pageName, route, listField) in new[] {
    ("Wh.Wh03InventoryStatus", "/wh/03?tab=adjust", "AdjustReasons"),
    ("Wh.Wh03InventoryStatus", "/fg/adjust", "AdjustReasons"),
    ("Wh.Wh07PdaRelease", "/wh/07", "OutgoingTypes"),
    ("Fg.Fg01Stocking", "/fg/01", "StorageOptions"),
    ("Fg.FgRtnReturn", "/fg/return", "Reasons") })
foreach (var mode in new[] { "ok", "unauthorized", "empty", "forbidden", "server-error", "invalid-json", "offline", "timeout" })
{
    var auth = Activator.CreateInstance(authType)!;
    authType.GetMethod("SignIn")!.Invoke(auth, ["test-token", session]);
    using var client = new HttpClient(new CodeHandler(mode)) { BaseAddress = new Uri("http://test/") };
    var api = Activator.CreateInstance(apiType, client, auth, factory)!;
    var services = new ServiceCollection().AddLogging();
    services.AddSingleton(authType, auth); services.AddSingleton(apiType, api);
    services.AddSingleton<NavigationManager>(new TestNavigation(route));
    services.AddSingleton<IJSRuntime>(new NoJs());
    services.AddSingleton(AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bin, "Radzen.Blazor.dll")).GetType("Radzen.DialogService", true)!);
    await using var provider = services.BuildServiceProvider();
    await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
    Capture.PageType = asm.GetType("AMES.Pda.Components.Pages." + pageName, true)!;
    await renderer.Dispatcher.InvokeAsync(async () => {
        var rendered = await renderer.RenderComponentAsync<Capture>();
        var type = Capture.PageType;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var page = Capture.Page!;
        var modal = (bool)type.GetField("_modalOpen", flags)!.GetValue(page)!;
        var reasons = ((IEnumerable)type.GetField(listField, flags)!.GetValue(page)!).Cast<object>().Count();
        if (mode == "ok" ? modal || reasons != 1 : !modal || reasons != 0)
            throw new Exception($"{route} {mode}: invalid initialization state");
        if (pageName == "Wh.Wh03InventoryStatus")
        {
            if (!(bool)type.GetProperty("DisableInventoryAdjust", flags)!.GetValue(page)!)
                throw new Exception("Save must remain disabled without scanned stock");
            async Task CheckBlocked(string expected)
            {
                await (Task)type.GetMethod("PostInventoryAdjust", flags)!.Invoke(page, null)!;
                if (!(bool)type.GetField("_modalOpen", flags)!.GetValue(page)!
                    || !((string)type.GetField("_modalMessage", flags)!.GetValue(page)!).Contains(expected))
                    throw new Exception("Missing Save validation alert: " + expected);
                typeof(ComponentBase).GetMethod("StateHasChanged", flags)!.Invoke(page, null);
                var html = rendered.ToHtmlString();
                if (!html.Contains("save blocked") || !html.Contains("aria-disabled=\"true\""))
                    throw new Exception("Unavailable Save must render the blocked state");
                type.GetMethod("DismissModal", flags)!.Invoke(page, null);
            }
            if (mode == "ok") await CheckBlocked("Scan a location");
            var stock = JsonSerializer.Deserialize("""{"receiveType":"LOCAL","yn":"N","lotNo":"TEST","barcode":"TEST","qty":10}""",
                apiType.GetNestedType("InboundScanRow")!, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            type.GetField("_invScan", flags)!.SetValue(page, stock);
            type.GetField("_invAdjustDelta", flags)!.SetValue(page, 1m);
            if (mode == "ok") await CheckBlocked("Select a reason");
            type.GetField("_invAdjustReason", flags)!.SetValue(page, "COUNT_DIFF");
            if ((bool)type.GetProperty("DisableInventoryAdjust", flags)!.GetValue(page)! != (mode != "ok"))
                throw new Exception("Save must require a successfully loaded reason, even after a form reset");
            if (mode == "ok")
            {
                typeof(ComponentBase).GetMethod("StateHasChanged", flags)!.Invoke(page, null);
                if (!rendered.ToHtmlString().Contains("save ready") || !rendered.ToHtmlString().Contains("aria-disabled=\"false\""))
                    throw new Exception("Valid Save must render the ready state");
                type.GetField("_invAdjustDelta", flags)!.SetValue(page, 0m);
                await CheckBlocked("Change the quantity");
                type.GetField("_adjustQuantityInvalid", flags)!.SetValue(page, true);
                await CheckBlocked("whole number");
                type.GetMethod("ClearInventoryWork", flags)!.Invoke(page, null);
                if ((string)type.GetField("_invAdjustReason", flags)!.GetValue(page)! != "")
                    throw new Exception("Clear must reset reason selection");
                await CheckBlocked("Scan a location");
            }
        }
        if (mode != "ok" && !rendered.ToHtmlString().Contains("Code List Unavailable"))
            throw new Exception("Expected a recoverable error dialog");
    });
    Console.WriteLine($"PASS {route}: {mode}");
}

sealed class CodeHandler(string mode) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (!request.RequestUri!.AbsolutePath.StartsWith("/api/sys/code-items/")) throw new Exception("Unexpected HTTP request");
        if (mode == "offline") throw new HttpRequestException("Simulated offline connection");
        if (mode == "timeout") throw new TaskCanceledException("Simulated timeout");
        var status = mode switch { "unauthorized" => HttpStatusCode.Unauthorized, "forbidden" => HttpStatusCode.Forbidden,
            "server-error" => HttpStatusCode.ServiceUnavailable, _ => HttpStatusCode.OK };
        var json = mode switch { "empty" => "[]", "invalid-json" => "not-json",
            _ => """[{"codeValue":"COUNT_DIFF","codeName":"Count Diff","codeNameEn":"Count Diff","attribute1":null}]""" };
        return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") });
    }
}
sealed class TestNavigation : NavigationManager
{
    public TestNavigation(string path) => Initialize("http://test/", "http://test" + path);
    protected override void NavigateToCore(string uri, bool forceLoad) => Uri = ToAbsoluteUri(uri).ToString();
}
sealed class NoJs : IJSRuntime
{
    public ValueTask<T> InvokeAsync<T>(string id, object?[]? args) => ValueTask.FromResult(default(T)!);
    public ValueTask<T> InvokeAsync<T>(string id, CancellationToken token, object?[]? args) => InvokeAsync<T>(id, args);
}
sealed class Capture : ComponentBase
{
    public static Type PageType = null!;
    public static object? Page;
    protected override void BuildRenderTree(RenderTreeBuilder b) { b.OpenComponent(0, PageType); b.AddComponentReferenceCapture(1, p => Page = p); b.CloseComponent(); }
}
