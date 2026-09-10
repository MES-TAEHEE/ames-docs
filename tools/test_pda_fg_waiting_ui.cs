// Run after a Windows PDA build: dotnet run --file tools/test_pda_fg_waiting_ui.cs
#:sdk Microsoft.NET.Sdk.Web
#:property PublishAot=false

using System.Reflection;
using System.Runtime.Loader;

static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
var root = Directory.GetParent(Path.GetDirectoryName(SourcePath())!)!.FullName;
var bin = Path.GetFullPath(args.FirstOrDefault() ?? Path.Combine(root, "src/05_Pda/AMES.Pda/bin/Debug/net10.0-windows10.0.19041.0/win-x64"));
AssemblyLoadContext.Default.Resolving += (_, name) =>
    File.Exists(Path.Combine(bin, name.Name + ".dll"))
        ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bin, name.Name + ".dll"))
        : null;
var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bin, "AMES.Pda.dll"));
var type = assembly.GetType("AMES.Pda.Components.Pages.Fg.Fg01QcComplete", true)!;
var rowType = assembly.GetType("AMES.Pda.Services.PdaApi+FgQcCompletedRow", true)!;
var component = Activator.CreateInstance(type)!;
const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
var now = new DateTime(2026, 8, 31, 12, 0, 0);
type.GetField("_asOf", Flags)!.SetValue(component, now);
object Row(DateTime? passedAt) => Activator.CreateInstance(rowType,
    new object?[] { 1, "260828001", null, "81710-PI000NNB", "TRIM ASSY-TAIL GATE, LWR", null, 540m, "EA", null, passedAt })!;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
string Age(DateTime? passedAt) => (string)type.GetMethod("AgeText", Flags)!.Invoke(component, [Row(passedAt)])!;
Check(Age(null) == "-", "Unknown QC time must not become a fabricated age.");
Check(Age(now.AddHours(-23)) == "Under 1 day", "Age uses elapsed 24-hour days, not calendar boundaries.");
Check(Age(now.AddHours(-25)) == "Over 1 day", "One-day label.");
Check(Age(now.AddDays(-3)) == "Over 3 days", "Multiple-day label.");
Check(Age(now.AddHours(1)) == "Under 1 day", "Age cannot be negative.");
string Color(DateTime? passedAt) => (string)type.GetMethod("AgeClass", Flags)!.Invoke(component, [Row(passedAt)])!;
Check(Color(null) == "" && Color(now.AddDays(-1)) == "", "Unknown time or exactly one day is neutral.");
Check(Color(now.AddDays(-1).AddSeconds(-1)) == "waiting-age-1" && Color(now.AddDays(-5)) == "waiting-age-1", "One-day band is yellow.");
Check(Color(now.AddDays(-5).AddSeconds(-1)) == "waiting-age-5" && Color(now.AddDays(-10)) == "waiting-age-5", "Five-day band is orange.");
Check(Color(now.AddDays(-10).AddSeconds(-1)) == "waiting-age-10", "Ten-day band is red.");
var rows = (System.Collections.IList)type.GetField("_rows", Flags)!.GetValue(component)!;
foreach (var passedAt in new DateTime?[] { null, now.AddDays(-1), now.AddDays(-1).AddSeconds(-1),
    now.AddDays(-5), now.AddDays(-5).AddSeconds(-1), now.AddDays(-10), now.AddDays(-10).AddSeconds(-1), now.AddHours(1) })
    rows.Add(Row(passedAt));
int Count(int days) => (int)type.GetMethod("OverDaysCount", Flags)!.Invoke(component, [days])!;
Check(Count(1) == 5 && Count(5) == 3 && Count(10) == 1,
    "Counts must be cumulative and strictly exceed 1, 5 and 10 days, excluding unknown or future times.");

var source = File.ReadAllText(Path.Combine(root, "src/05_Pda/AMES.Pda/Components/Pages/Fg/Fg01QcComplete.razor"));
Check(!source.Contains("Last Updated") && !source.Contains("FG-001") && !source.Contains("OpenPutAway")
    && !source.Contains("@onclick") && !source.Contains("wh01-qty-grid"), "Remove the old header, selected-card action and grid.");
foreach (var label in new[] { "Total", "Over 1 Day", "Over 5 Days", "Over 10 Days" })
    Check(source.Contains($"<span>{label}</span>"), "Missing summary: " + label);
foreach (var field in new[] { "@row.ItemNo", "@row.ItemName", "@row.LotNo", "@row.Qty", "Since QC Pass" })
    Check(source.Contains(field.TrimStart('@')), "Missing field: " + field);
foreach (var cssClass in new[] { "wh01-release-card", "wh01-card-top wh01-card-top-compact", "wh01-release-facts" })
    Check(source.Contains(cssClass), "Reuse Schedule layout: " + cssClass);
Check(!source.Contains("<dl"), "Do not render a tall label/value form.");
Check(source.Contains(">QC WAITING</span>"), "Use the QC Waiting screen title.");
Check(source.Contains("<div class=\"wh01-po-no\">@row.LotNo</div>"), "LOT number is the card title.");
Check(source.IndexOf("@row.LotNo") < source.IndexOf("@row.ItemNo")
    && source.IndexOf("@row.ItemNo") < source.IndexOf("row.ItemName"), "Show part number and name below the LOT title.");
var activity = File.ReadAllText(Path.Combine(root, "src/05_Pda/AMES.Pda/Platforms/Android/MainActivity.cs"));
Check(activity.Contains("ScreenOrientation = ScreenOrientation.Portrait"), "Lock every Android PDA route to portrait.");
Check(source.Contains(".OrderBy(x => x.QcPassTs ?? DateTime.MaxValue)"), "Waiting order must be oldest QC pass first.");
var api = File.ReadAllText(Path.Combine(root, "src/04_Api/AMES.Api/Endpoints/FgEndpoints.cs"));
Check(api.Contains("ORDER BY CASE WHEN Q.InsEndTS IS NULL THEN 1 ELSE 0 END,")
    && api.Contains("Q.InsEndTS, L.ProducedAt, L.LotID;"), "Order oldest QC pass first.");
Check(source.Contains("role=\"alert\"") && source.Contains("@if (!_loadError && !_isLoading)"), "Errors must be visible without misleading zero counters.");

var apiType = assembly.GetType("AMES.Pda.Services.PdaApi", true)!;
var auth = Activator.CreateInstance(assembly.GetType("AMES.Pda.Services.AuthState", true)!)!;
var data = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bin, "AMES.Data.dll"));
var factory = Activator.CreateInstance(data.GetType("AMES.Data.Connection.AmesConnectionFactory", true)!, "Server=unused;Database=unused;Integrated Security=True")!;
var handler = new QcHandler();
using var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
type.GetProperty("Api", Flags)!.SetValue(component, Activator.CreateInstance(apiType, client, auth, factory));
async Task Load(string mode)
{
    handler.Mode = mode;
    await (Task)type.GetMethod("Load", Flags)!.Invoke(component, null)!;
    Check(!(bool)type.GetField("_isLoading", Flags)!.GetValue(component)!, "REFRESH must recover after every result.");
}
foreach (var (mode, message) in new[] { ("401", "session has expired"), ("403", "permission"), ("500", "API/DB connection"),
    ("offline", "API/DB connection"), ("timeout", "timed out"), ("json", "invalid response"), ("null", "invalid response") })
{
    await Load("ok");
    Check(((System.Collections.IList)type.GetField("_rows", Flags)!.GetValue(component)!).Count == 1, "Load waiting stock before testing refresh failure.");
    await Load(mode);
    Check((bool)type.GetField("_loadError", Flags)!.GetValue(component)! && ((System.Collections.IList)type.GetField("_rows", Flags)!.GetValue(component)!).Count == 0,
        "Refresh error must clear stale stock: " + mode);
    Check(((string)type.GetField("_message", Flags)!.GetValue(component)!).Contains(message), "Expected error: " + mode);
    await Load("empty");
    Check(!(bool)type.GetField("_loadError", Flags)!.GetValue(component)! && ((string)type.GetField("_message", Flags)!.GetValue(component)!).Contains("No finished goods"),
        "Successful empty response must remain distinct from failure.");
}
Console.WriteLine("PASS: FG Waiting Schedule layout, four counters, 1/5/10-day boundaries and oldest-first query.");
Console.WriteLine("PASS: QC Waiting 401/403/500/offline/timeout/invalid/null errors, stale data clearing and REFRESH recovery.");

sealed class QcHandler : HttpMessageHandler
{
    public string Mode = "ok";
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (request.RequestUri!.AbsolutePath != "/api/fg/qc-completed") throw new Exception("Unexpected request");
        if (Mode == "offline") throw new HttpRequestException();
        if (Mode == "timeout") throw new TaskCanceledException();
        var body = Mode switch { "empty" => "[]", "json" => "invalid", "null" => "null",
            _ => """[{"lotId":1,"lotNo":"TEST","itemNo":"PART","qty":10,"qcPassTs":"2026-09-01T00:00:00"}]""" };
        return Task.FromResult(new HttpResponseMessage(int.TryParse(Mode, out var code) ? (System.Net.HttpStatusCode)code : System.Net.HttpStatusCode.OK)
            { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
    }
}
