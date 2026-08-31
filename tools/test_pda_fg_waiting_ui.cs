// Run after a Windows PDA build: dotnet run --file tools/test_pda_fg_waiting_ui.cs
#:sdk Microsoft.NET.Sdk.Web
#:property PublishAot=false

using System.Reflection;
using System.Runtime.Loader;

static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
var root = Directory.GetParent(Path.GetDirectoryName(SourcePath())!)!.FullName;
var bin = Path.Combine(root, "src/05_Pda/AMES.Pda/bin/Debug/net10.0-windows10.0.19041.0/win-x64");
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
    && source.IndexOf("@row.ItemNo") < source.IndexOf("@row.ItemName"), "Show part number and name below the LOT title.");
var activity = File.ReadAllText(Path.Combine(root, "src/05_Pda/AMES.Pda/Platforms/Android/MainActivity.cs"));
Check(activity.Contains("ScreenOrientation = ScreenOrientation.Portrait"), "Lock every Android PDA route to portrait.");
Check(source.Contains(".OrderBy(x => x.QcPassTs ?? DateTime.MaxValue)"), "Waiting order must be oldest QC pass first.");
var api = File.ReadAllText(Path.Combine(root, "src/04_Api/AMES.Api/Endpoints/FgEndpoints.cs"));
Check(api.Contains("ORDER BY CASE WHEN Q.InsEndTS IS NULL THEN 1 ELSE 0 END,")
    && api.Contains("Q.InsEndTS, L.ProducedAt, L.LotID;"), "Order oldest first before applying the API row limit.");
Console.WriteLine("PASS: FG Waiting Schedule layout, four counters, 1/5/10-day boundaries and oldest-first query.");
