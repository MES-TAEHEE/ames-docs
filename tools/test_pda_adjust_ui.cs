// Run after a Windows PDA build: dotnet run --file tools/test_pda_adjust_ui.cs
// Exercises the compiled component without opening a window or changing stock.
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
var type = assembly.GetType("AMES.Pda.Components.Pages.Wh.Wh03InventoryStatus", throwOnError: true)!;
var component = Activator.CreateInstance(type)!;
const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
void Set(string name, object value) => type.GetField(name, Flags)!.SetValue(component, value);
object? Get(string name) => type.GetField(name, Flags)!.GetValue(component);
void Call(string name, params object[] values) => type.GetMethod(name, Flags)!.Invoke(component, values);
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

Set("_pinPadOpen", true);
Call("PressSupervisorPin", "1");
Call("PressSupervisorPin", "x");
Call("ConfirmSupervisorPin");
Check((string)Get("_pinPadDraft")! == "1" && (bool)Get("_pinPadOpen")!, "Reject non-digits and incomplete PIN.");
Call("PressSupervisorPin", "2");
Call("PressSupervisorPin", "3");
Call("PressSupervisorPin", "5");
Call("DeleteSupervisorPin");
Call("PressSupervisorPin", "4");
Call("ConfirmSupervisorPin");
Check((string)Get("_invAdjustSupervisorPin")! == "1234" && !(bool)Get("_pinPadOpen")!, "Confirm PIN without saving inventory.");
Check((string)Get("_pinPadDraft")! == "", "Clear popup draft after confirmation.");

Set("_pinPadOpen", true);
Set("_pinPadDraft", "9999");
Call("CloseSupervisorPinPad");
Check((string)Get("_invAdjustSupervisorPin")! == "1234", "Cancel must preserve the confirmed PIN.");
Set("_pinPadOpen", true);
Set("_pinPadDraft", "123456789012");
Call("PressSupervisorPin", "3");
Check((string)Get("_pinPadDraft")! == "123456789012", "PIN must remain within 12 digits.");

Set("_invBarcode", "5011LL260804000001");
Set("_invAdjustSupervisorEmployeeNo", "test-supervisor");
Set("_invAdjustReason", "DAMAGE");
Set("_invAdjustNote", "test");
Set("_invAdjustDelta", 3m);
Call("ClearInventoryWork");
foreach (var field in new[] { "_invBarcode", "_invAdjustSupervisorPin", "_invAdjustSupervisorEmployeeNo", "_invAdjustNote", "_pinPadDraft" })
    Check((string)Get(field)! == "", "Clear did not reset " + field);
Check((decimal)Get("_invAdjustDelta")! == 0 && !(bool)Get("_pinPadOpen")!, "Clear quantity and close keypad.");
Check(Get("_invScan") is null, "Clear scanned stock.");

var source = File.ReadAllText(Path.Combine(root, "src/05_Pda/AMES.Pda/Components/Pages/Wh/Wh03InventoryStatus.razor"));
Check(source.Contains("ClearInventoryWork();\n            ShowAlert(\"Saved\"", StringComparison.Ordinal)
    || source.Contains("ClearInventoryWork();\r\n            ShowAlert(\"Saved\"", StringComparison.Ordinal),
    "Successful save must reset the form before showing confirmation.");
var css = File.ReadAllText(Path.Combine(root, "src/05_Pda/AMES.Pda/wwwroot/css/pda.css"));
Check(css.Contains(".wh03-work-scan > .pda-fld:only-child") && css.Contains("grid-column: 1 / -1;"),
    "The scan field must span the row when the developer Scan button is absent.");
Check(css.Contains("--pda-safe-top: 0px;") && !css.Contains("max(env(safe-area-inset-top), 52px)"),
    "The shared shell must not add a fixed top spacer.");
var mainPage = File.ReadAllText(Path.Combine(root, "src/05_Pda/AMES.Pda/MainPage.xaml"));
Check(mainPage.Contains("SafeAreaEdges=\"Container\""), "Native system bars must remain outside the content.");
Console.WriteLine("PASS: Adjust PIN keypad, form reset, scan width and native safe-area layout.");
