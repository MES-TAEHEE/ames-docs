// Run after a Windows PDA build: dotnet run --file tools/test_pda_transactions_ui.cs
#:sdk Microsoft.NET.Sdk.Web
#:property PublishAot=false

using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
var root = Directory.GetParent(Path.GetDirectoryName(SourcePath())!)!.FullName;
var bin = Path.Combine(root, "src/05_Pda/AMES.Pda/bin/Debug/net10.0-windows10.0.19041.0/win-x64");
AssemblyLoadContext.Default.Resolving += (_, name) =>
    File.Exists(Path.Combine(bin, name.Name + ".dll"))
        ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bin, name.Name + ".dll")) : null;
var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bin, "AMES.Pda.dll"));
var type = assembly.GetType("AMES.Pda.Components.Pages.Wh.Wh08TransactionHistory", true)!;
var rowType = assembly.GetType("AMES.Pda.Services.PdaApi+WarehouseTransactionRow", true)!;
var component = Activator.CreateInstance(type)!;
const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
void Set(string name, object value) => type.GetField(name, Flags)!.SetValue(component, value);
object? Get(string name) => type.GetField(name, Flags)!.GetValue(component);
object? Call(string name, params object[] args) => type.GetMethod(name, Flags)!.Invoke(component, args);
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
object Row(string direction, decimal delta, string unit, string worker = "admin") =>
    JsonSerializer.Deserialize(JsonSerializer.Serialize(new {
        RowNo = 1, LotNo = "LOT-A", PartNo = "PART-A", LocationId = "B0-09-D2",
        Direction = direction, Qty = 999, DeltaQty = delta, Unit = unit,
        WorkerId = worker, ReasonCode = "COUNT_DIFF"
    }), rowType)!;
var rows = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(rowType))!;
rows.Add(Row("IN", 2.5m, "EA"));
rows.Add(Row("IN", 1m, "EA"));
rows.Add(Row("IN", 3m, "KG"));
rows.Add(Row("OUT", -4m, "EA"));
var adjustment = Row("ADJ", -0.5m, "EA");
rows.Add(adjustment);
Set("_rows", rows);
Set("_search", "CASE-BARCODE");
var filtered = (IEnumerable)type.GetProperty("FilteredRows", Flags)!.GetValue(component)!;
Check(filtered.Cast<object>().Count() == 5, "Do not re-filter server-resolved case/box/location results as LOT/part text.");
var movementQty = type.GetMethod("MovementQty", BindingFlags.Static | BindingFlags.NonPublic)!;
Check((decimal)movementQty.Invoke(null, new[] { rows[0] })! == 2.5m, "Keep fractional movement quantities.");
Check((decimal)movementQty.Invoke(null, new[] { rows[3] })! == 4m, "Use released quantity, not remaining stock.");
Set("_worker", "missing-worker");
filtered = (IEnumerable)type.GetProperty("FilteredRows", Flags)!.GetValue(component)!;
Check(!filtered.Cast<object>().Any(), "Apply worker filter to records.");
Set("_worker", "admin");
Call("SetType", "OUT");
filtered = (IEnumerable)type.GetProperty("FilteredRows", Flags)!.GetValue(component)!;
Check(filtered.Cast<object>().Count() == 1, "Type filter must select only OUT records.");
Call("OpenDetail", adjustment);
Check(ReferenceEquals(Get("_detailRow"), adjustment), "Open the selected adjustment in a dialog.");
Call("CloseDetail");
Check(Get("_detailRow") is null && (string)Get("_search")! == "CASE-BARCODE", "Closing detail must preserve the search.");
Set("_dateFrom", new DateTime(2026, 8, 31));
Set("_dateTo", new DateTime(2026, 8, 1));
await (Task)Call("Load")!;
Check((string)Get("_msg")! == "From date cannot be after To date.", "Block inverted date ranges before API calls.");
Call("BeginFilterEdit");
Check((bool)Get("_editingFilters")!, "Do not steal keyboard focus while editing dates or worker filters.");
var js = new FocusSpy();
type.GetProperty("JS", Flags)!.SetValue(component, js);
Set("_editingBarcode", true);
Call("OnSearchInput", new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "81710-PI000NNB" });
await (Task)Call("FocusScannerAsync")!;
Check(js.Calls == 0 && (string)Get("_search")! == "81710-PI000NNB", "Typing must preserve the value and focus.");
Set("_editingBarcode", false);
await (Task)Call("FocusScannerAsync")!;
Check(js.Calls == 1 && js.Selector == ".wh08-barcode-input", "Resume scanner focus without selecting Worker ID.");
Console.WriteLine("PASS: barcode results, movement quantities, filters, adjustment dialog, date validation, manual entry and scanner focus.");

sealed class FocusSpy : Microsoft.JSInterop.IJSRuntime
{
    public int Calls { get; private set; }
    public string? Selector { get; private set; }
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
    {
        Calls++;
        Selector = args?.FirstOrDefault() as string;
        return ValueTask.FromResult(default(TValue)!);
    }
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        => InvokeAsync<TValue>(identifier, args);
}
