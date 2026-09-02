// Run after API and Windows PDA builds: dotnet run --file tools/test_pda_fg_putaway_ui.cs
#:sdk Microsoft.NET.Sdk.Web
#:property PublishAot=false

using System.Reflection;
using System.Runtime.Loader;
using Microsoft.AspNetCore.Components;

static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
var root = Directory.GetParent(Path.GetDirectoryName(SourcePath())!)!.FullName;
var bins = new[] {
    Path.Combine(root, "src/05_Pda/AMES.Pda/bin/Debug/net10.0-windows10.0.19041.0/win-x64"),
    Path.Combine(root, "src/04_Api/AMES.Api/bin/Debug/net10.0")
};
AssemblyLoadContext.Default.Resolving += (_, name) => {
    var path = bins.Select(bin => Path.Combine(bin, name.Name + ".dll")).FirstOrDefault(File.Exists);
    return path is null ? null : AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
};
void Check(bool valid, string message) { if (!valid) throw new Exception(message); }
var pda = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bins[0], "AMES.Pda.dll"));
var type = pda.GetType("AMES.Pda.Components.Pages.Fg.Fg01Stocking", true)!;
var component = Activator.CreateInstance(type)!;
const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
object? Call(string method, params object?[] args) => type.GetMethod(method, Flags)!.Invoke(component, args);
void Set(string name, object? value) => type.GetField(name, Flags)!.SetValue(component, value);
object? Field(string name) => type.GetField(name, Flags)!.GetValue(component);
bool Is(string name) => (bool)type.GetProperty(name, Flags)!.GetValue(component)!;
var rowType = pda.GetType("AMES.Pda.Services.PdaApi+FgPutAwayScanRow", true)!;
var ctor = rowType.GetConstructors().Single(c => c.GetParameters().Length > 1);
object Row(bool qcPassed = true, bool stocked = false, DateTime? passedAt = null) => ctor.Invoke(ctor.GetParameters().Select(p => p.Name switch {
    "LotNo" => (object)"5011FG260831000101",
    "ItemNo" => "81710-PI000NNB",
    "ItemName" => "TRIM ASSY-TAIL GATE, LWR",
    "Qty" => 32m,
    "Unit" => "EA",
    "QcPassTs" => passedAt,
    "MfgDate" => new DateTime(2026, 8, 20),
    "IsQcPassed" => qcPassed,
    "AlreadyStocked" => stocked,
    _ => p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null
}).ToArray());

Check(!Is("ReadyForStorage") && !Is("ReadyForLocation"), "Scan a QC-passed LOT first.");
Set("_scan", Row());
Check(Is("ReadyForStorage") && !Is("ReadyForLocation"), "Storage selection must precede location scanning.");
foreach (var method in new[] { "BOX", "PALLET", "RACK" }) {
    Call("OnStorageChanged", method);
    Check(Is("RequiresContainer") && !Is("ReadyForLocation"), "Container scan required: " + method);
    Check(((string)Call("ConfirmValidationMessage")!).Contains("Barcode first"), "Confirm must not skip the container.");
    Set("_confirmedContainerBarcode", method + ":50110001");
    Check(Is("ReadyForLocation"), "Validated container opens location scanning.");
    Call("PromptLocationScan");
    Check((bool)Field("_highlightLocationScan")! && (bool)Field("_scrollLocationAfterRender")!, "Scroll and highlight the location step.");
    Call("OnContainerInput", new ChangeEventArgs { Value = method + ":50110002" });
    Check(!Is("ReadyForLocation") && Field("_selectedLocation") is null, "Edited container must be rescanned.");
}
Call("OnStorageChanged", "LOCATION");
Check(!Is("RequiresContainer") && Is("ReadyForLocation"), "Location Only skips containers.");
Check((bool)Field("_highlightLocationScan")!, "Location Only must also highlight the location scanner.");
Call("OnStorageChanged", "BOX");
Check(!Is("ReadyForLocation") && !(bool)Field("_highlightLocationScan")!, "Storage change clears downstream scans.");
Set("_scan", Row(false));
Check(!Is("ReadyForStorage"), "QC failure stays blocked.");
Set("_scan", Row(true, true));
Check(!Is("ReadyForStorage"), "Already-stocked LOT stays blocked.");
Call("ResetForm");
Check(Field("_scan") is null && Field("_storageMethod") is null && (string)Field("_containerBarcode")! == "", "Clear resets the entire flow.");

var api = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bins[1], "AMES.Api.dll"));
var endpoints = api.GetType("AMES.Api.Endpoints.FgEndpoints", true)!;
var validate = endpoints.GetMethod("ValidatePutAwayContainer", BindingFlags.NonPublic | BindingFlags.Static)!;
string? Error(string method, string? barcode) => (string?)validate.Invoke(null, [method, barcode]);
foreach (var method in new[] { "BOX", "PALLET", "RACK" }) {
    Check(Error(method, method + ":50112608310001") is null, "Accept matching container: " + method);
    Check(Error(method, "FGLOC:FG-A-01-01") is not null, "Location barcode cannot substitute for a container.");
    Check(Error(method, "LOT:5011FG260831000101") is not null, "LOT cannot substitute for a container.");
    Check(Error(method, null) is not null && Error(method, method + ":") is not null, "Missing container is rejected.");
    Check(Error(method, method + ":" + new string('A', 81)) is not null, "Reject oversized barcode before DB truncation.");
}
Check(Error("BOX", "PALLET:50110001") is not null, "Wrong container type is blocked.");
Check(Error("LOCATION", null) is null && Error("LOCATION", "BOX:50110001") is not null, "Location Only cannot retain a stale container.");
Check(Error("UNKNOWN", "BOX:50110001") is not null, "Unknown storage cannot default to Location.");
var source = File.ReadAllText(Path.Combine(root, "src/05_Pda/AMES.Pda/Components/Pages/Fg/Fg01Stocking.razor"));
Set("_scan", Row(passedAt: new DateTime(2026, 8, 31)));
var fields = ((IEnumerable<(string Label, string Value)>)Call("LotFields")!).ToArray();
Check(fields.Select(f => f.Label).SequenceEqual(new[] { "LOT NO", "PART NO", "PART NM", "QTY", "QC Passed Date" }), "Show exactly five LOT fields in order.");
Check(fields.Select(f => f.Value).SequenceEqual(new[] { "5011FG260831000101", "81710-PI000NNB", "TRIM ASSY-TAIL GATE, LWR", "32 EA", "08/31/2026" }), "Show QC completion date, not production date.");
Set("_scan", Row(stocked: true));
fields = ((IEnumerable<(string Label, string Value)>)Call("LotFields")!).ToArray();
Check(fields.Length == 5 && fields[^1].Value == "-", "Missing QC date stays unknown and stocked LOTs do not add extra fields.");
Check(!source.Contains("<RadzenBadge") && !source.Contains("LotStatusText"), "Remove the QC status badge without changing QC validation.");
var locationType = pda.GetType("AMES.Pda.Services.PdaApi+FgPutAwayLocationRow", true)!;
var location = Activator.CreateInstance(locationType, new object?[] {
    "FG-A-01-01", "FG Warehouse A-01-01", "A", "01", "02", "03", 5000m, 32m, 4968m,
    "CUSTOMER", true, "Ready", "LOCATION", "FGLOC:FG-A-01-01"
})!;
var locationFields = ((IEnumerable<(string Label, string Value)>)Call("LocationFields", location)!).ToArray();
Check(locationFields.Select(f => f.Label).SequenceEqual(new[] { "Zone", "Bay", "Slot", "Current", "Free" }), "Show exactly five location rows.");
Check(locationFields.Select(f => f.Value).SequenceEqual(new[] { "A", "02", "03", "32", "4968" }), "Location fields must use the scanned location data.");
Check(source.Contains("@Text(_selectedLocation.ScannedBarcode)") && !source.Contains("LocationSummary") && !source.Contains("SCANNED LOCATION"), "Location header contains only the scanned barcode.");
Check(!source.Contains("class=\"wh02-location-list fg01-location-list\""), "Use the compact single-column information rows, not a two-column location grid.");
Check(source.Contains("pdaScan.scrollTo") && source.Contains("location-attention"), "Reuse inbound scroll and highlight.");
Console.WriteLine("PASS: FG storage selection, container/location gating, reset, QC guards, barcode type validation and labels.");
