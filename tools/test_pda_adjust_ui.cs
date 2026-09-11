// Run after a Windows PDA build: dotnet run --file tools/test_pda_adjust_ui.cs
// Exercises the compiled component without opening a window or changing stock.
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

Set("_quantityPadOpen", true);
Call("PressQuantityDigit", "1");
Call("PressQuantityDigit", "x");
Check((string)Get("_quantityPadDraft")! == "1", "Reject non-digits.");
Call("PressQuantityDigit", "2");
Call("DeleteQuantityDigit");
Check((string)Get("_quantityPadDraft")! == "1", "Quantity keypad backspace.");
Set("_quantityPadDraft", "123456789");
Call("PressQuantityDigit", "0");
Check((string)Get("_quantityPadDraft")! == "123456789", "Quantity must remain within 9 digits.");
Call("CloseQuantityPad");
Check((string)Get("_quantityPadDraft")! == "", "Cancel must discard unconfirmed quantity.");

Set("_invBarcode", "5011LL260804000001");
Set("_invAdjustReason", "DAMAGE");
Set("_invAdjustNote", "test");
Set("_invAdjustDelta", 3m);
Call("ClearInventoryWork");
foreach (var field in new[] { "_invBarcode", "_invAdjustNote", "_quantityPadDraft" })
    Check((string)Get(field)! == "", "Clear did not reset " + field);
Check((decimal)Get("_invAdjustDelta")! == 0 && !(bool)Get("_quantityPadOpen")!, "Clear quantity and close keypad.");
Check(Get("_invScan") is null, "Clear scanned stock.");

var source = File.ReadAllText(Path.Combine(root, "src/05_Pda/AMES.Pda/Components/Pages/Wh/Wh03InventoryStatus.razor"));
Check(!source.Contains("Supervisor") && source.Contains("aria-disabled=\"@(DisableInventoryAdjust"),
    "Adjust must not require obsolete supervisor approval.");
var request = assembly.GetType("AMES.Pda.Services.PdaApi+AdjustSaveReq", throwOnError: true)!;
Check(!request.GetProperties().Any(p => p.Name.Contains("Supervisor")), "Request must not send supervisor fields.");
var endpoint = File.ReadAllText(Path.Combine(root, "src/04_Api/AMES.Api/Endpoints/FgEndpoints.cs"));
var schema = File.ReadAllText(Path.Combine(root, "dist/pda/PDA_SCHEMA.sql"));
Check(!endpoint.Contains("@SupervisorUserId") && !endpoint.Contains("@SupervisorEmployeeNo")
    && !schema.Contains("@SupervisorUserId") && !schema.Contains("@SupervisorEmployeeNo")
    && schema.Contains("ALTER TABLE dbo.FG_InventoryAdjust DROP COLUMN ApprovedBy"),
    "FG Adjust must not retain supervisor parameters or approval storage.");
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
Console.WriteLine("PASS: Adjust quantity keypad, form reset, no supervisor fields, scan width and native safe-area layout.");
