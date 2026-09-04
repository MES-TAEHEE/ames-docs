// Run after a Windows PDA build: dotnet run --file tools/test_pda_warehouse_scenarios.cs
// Verifies the test-scenario guards without opening the PDA or mutating stock.
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

void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bin, "AMES.Pda.dll"));
var inboundType = assembly.GetType("AMES.Pda.Components.Pages.Wh.Wh02PdaInbound", throwOnError: true)!;
var inbound = Activator.CreateInstance(inboundType)!;
const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
void Set(string name, object value) => inboundType.GetField(name, Flags)!.SetValue(inbound, value);
object? Get(string name) => inboundType.GetField(name, Flags)!.GetValue(inbound);
void Call(string name, params object[] values) => inboundType.GetMethod(name, Flags)!.Invoke(inbound, values);
var initialMode = (string?)inboundType.GetField("_mode", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(inbound);
Check(string.IsNullOrEmpty(initialMode), "Inbound must start without a selected receive type.");

var inboundSource = File.ReadAllText(Path.Combine(root, "src/05_Pda/AMES.Pda/Components/Pages/Wh/Wh02PdaInbound.razor"));
Check(inboundSource.Contains("Select LOCAL or CKD before scanning."), "Missing receive-type validation.");
Check(inboundSource.Contains("is blocked for inbound."), "Missing blocked-location validation.");
Check(inboundSource.Contains("capacity would be exceeded"), "Missing location-capacity validation.");
Check(inboundSource.Contains("INBOUND TEST SCENARIOS") && inboundSource.Contains("WH002-TC-018"),
    "TEST login must expose all executable WH002 scenarios.");
Check(inboundSource.Contains("Auth.Session?.EmployeeNo, \"TEST\""),
    "Inbound scenario helper must be restricted to TEST login.");
Check(inboundSource.Contains("wh02-test-nav") && inboundSource.Contains("_testPanelOpen"),
    "Scenario content must open from the navigation button instead of occupying the work screen.");

var loadTestValueStart = inboundSource.IndexOf("private async Task LoadTestValue", StringComparison.Ordinal);
var loadTestValueEnd = inboundSource.IndexOf("private string? _changeFromLocation", loadTestValueStart, StringComparison.Ordinal);
var loadTestValueSource = inboundSource[loadTestValueStart..loadTestValueEnd];
Check(loadTestValueSource.Contains("await Scan();") && loadTestValueSource.Contains("await ScanLocation();"),
    "Test-data buttons must execute barcode and location scans instead of only loading input values.");
Check(inboundSource.Contains("OPEN INVENTORY") && inboundSource.Contains("OPEN TRANSACTIONS"),
    "Scenario 16 must provide direct navigation to both verification screens.");
Check(inboundSource.Contains("API ERROR:") && inboundSource.Contains("_simulateReceiveApiFailure"),
    "Scenario 18 must provide a TEST-only receive API failure switch.");

const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.NonPublic;
var scenarios = (Array)inboundType.GetField("InboundTestScenarios", StaticFlags)!.GetValue(null)!;
Check(scenarios.Length == 18, "WH002 must expose exactly 18 executable scenarios.");
var expectedModes = new[]
{
    "LOCAL", "CKD", "LOCAL", "LOCAL", "LOCAL", "CKD", "LOCAL", "LOCAL", "LOCAL",
    "LOCAL", "LOCAL", "LOCAL", "LOCAL", "LOCAL", "LOCAL", "LOCAL", "LOCAL", "LOCAL"
};
var expectedFirstValues = new string?[]
{
    null, null, "5011LL260903900001", "5011LL260828000001", "5011202608280001",
    "CKD202608280001CASE00001", "WH-UNKNOWN-999999", "5011202608280001",
    "5011202608280001", "5011202608280001", "5011202608280001", "5011LL260903900001",
    "5011LL260903900001", "5011LL260903900001", "5011LL260903900001",
    "5011LL260903900001", "260827014", "5011LL260903900002"
};
for (var index = 0; index < scenarios.Length; index++)
{
    var scenario = scenarios.GetValue(index)!;
    var scenarioType = scenario.GetType();
    var no = (int)scenarioType.GetProperty("No")!.GetValue(scenario)!;
    var testCaseId = (string)scenarioType.GetProperty("TestCaseId")!.GetValue(scenario)!;
    var mode = (string)scenarioType.GetProperty("Mode")!.GetValue(scenario)!;
    var autoScan = (bool)scenarioType.GetProperty("AutoScan")!.GetValue(scenario)!;
    var values = (Array)scenarioType.GetProperty("Values")!.GetValue(scenario)!;
    var firstValue = values.Length == 0
        ? null
        : (string)values.GetValue(0)!.GetType().GetProperty("Value")!.GetValue(values.GetValue(0)!)!;
    Check(no == index + 1, $"Scenario number is not continuous at index {index}.");
    Check(testCaseId == $"WH002-TC-{index + 1:000}", $"Scenario ID does not match scenario {index + 1}.");
    Check(mode == expectedModes[index], $"Scenario {index + 1} has the wrong receive mode.");
    Check(firstValue == expectedFirstValues[index], $"Scenario {index + 1} has the wrong primary test barcode.");
    Check(autoScan == (index is not 0 and not 1 and not 15), $"Scenario {index + 1} has the wrong auto-scan setting.");
}

foreach (var scenarioNo in new[] { 5, 6 })
{
    var scenario = scenarios.GetValue(scenarioNo - 1)!;
    var values = (Array)scenario.GetType().GetProperty("Values")!.GetValue(scenario)!;
    Check(values.Length == 4, $"Scenario {scenarioNo} must contain one document/case and three box barcodes.");
}

Check(!inboundSource.Contains("작업 유형 미선택 상태 스캔"),
    "The non-executable receive-type scenario must remain excluded.");
Set("_mode", "CKD");
Call("SelectTestScenario", 2);
Check((string)Get("_mode")! == "LOCAL", "Renumbered TC03 must prepare the LOCAL LOT scenario.");

var releaseSource = File.ReadAllText(Path.Combine(root, "src/05_Pda/AMES.Pda/Components/Pages/Wh/Wh07PdaRelease.razor"));
Check(releaseSource.Contains("DisplayFifoLotsFor(line.ItemNo)"), "Release must display its FIFO LOT list.");
Check(releaseSource.Contains("lot.LocationNo") && releaseSource.Contains("lot.ProductionDate"),
    "Release FIFO rows must show location and ProducedAt.");
Check(releaseSource.Contains("RELEASE TEST SCENARIOS") && releaseSource.Contains("WH003-TC-018"),
    "TEST login must expose all executable WH003 scenarios.");
Check(releaseSource.Contains("Auth.Session?.EmployeeNo, \"TEST\"")
      && releaseSource.Contains("wh02-test-nav") && releaseSource.Contains("_testPanelOpen"),
    "Release scenarios must be restricted to TEST login and open from the navigation button.");
Check(releaseSource.Contains("OPEN INVENTORY") && releaseSource.Contains("OPEN TRANSACTIONS"),
    "Release scenario 17 must link to both verification screens.");
Check(releaseSource.Contains("API ERROR:") && releaseSource.Contains("_simulateReleaseApiFailure"),
    "Release scenario 18 must provide a TEST-only API failure switch.");

var releaseType = assembly.GetType("AMES.Pda.Components.Pages.Wh.Wh07PdaRelease", throwOnError: true)!;
var releaseScenarios = (Array)releaseType.GetField("ReleaseTestScenarios", StaticFlags)!.GetValue(null)!;
Check(releaseScenarios.Length == 18, "WH003 must expose exactly 18 executable scenarios.");
var expectedReleaseFirstValues = new[]
{
    "2026082801", "5011LL260820000010", "WH-RELEASE-UNKNOWN-999999", "2026082801", "2026082801",
    "2026082801", "2026082801", "2026082801", "2026082801", "2026082801", "2026082801",
    "2026082801", "2026082801", "2026082801", "2026082801", "2026082801",
    "5011LL260701000001", "PDA-REL-TEST-02"
};
for (var index = 0; index < releaseScenarios.Length; index++)
{
    var scenario = releaseScenarios.GetValue(index)!;
    var scenarioType = scenario.GetType();
    var no = (int)scenarioType.GetProperty("No")!.GetValue(scenario)!;
    var testCaseId = (string)scenarioType.GetProperty("TestCaseId")!.GetValue(scenario)!;
    var autoScan = (bool)scenarioType.GetProperty("AutoScan")!.GetValue(scenario)!;
    var values = (Array)scenarioType.GetProperty("Values")!.GetValue(scenario)!;
    var firstValue = (string)values.GetValue(0)!.GetType().GetProperty("Value")!.GetValue(values.GetValue(0)!)!;
    Check(no == index + 1, $"Release scenario number is not continuous at index {index}.");
    Check(testCaseId == $"WH003-TC-{index + 1:000}", $"Release scenario ID does not match scenario {index + 1}.");
    Check(firstValue == expectedReleaseFirstValues[index], $"Release scenario {index + 1} has the wrong primary test value.");
    Check(autoScan == (index != 16), $"Release scenario {index + 1} has the wrong auto-scan setting.");
}

var releaseLoadStart = releaseSource.IndexOf("private async Task LoadTestValue", StringComparison.Ordinal);
var releaseLoadEnd = releaseSource.IndexOf("private static bool ShowDeveloperScanButtons", releaseLoadStart, StringComparison.Ordinal);
var releaseLoadSource = releaseSource[releaseLoadStart..releaseLoadEnd];
Check(releaseLoadSource.Contains("await ProcessBarcodeAsync(value.Value)")
      && releaseLoadSource.Contains("OnOutgoingTypeChanged(value.Value)"),
    "Release test-data buttons must execute both barcode and outgoing-type actions.");

var schema = File.ReadAllText(Path.Combine(root, "dist/pda/PDA_SCHEMA.sql"));
Check(schema.Contains("COALESCE(L.ProducedAt") && schema.Contains("W.LastReceivedAt") && schema.Contains("L.LotID"),
    "FIFO ordering must use ProducedAt, LastReceivedAt and LotID.");
Check(schema.Contains("capacity would be exceeded", StringComparison.OrdinalIgnoreCase),
    "Database receive procedure must enforce location capacity.");
Check(schema.Contains("@SimulateFailure bit = 0")
    && schema.Contains("Database transaction was rolled back."),
    "Scenario 18 must fail inside the receive transaction and roll it back.");

var seed = File.ReadAllText(Path.Combine(root, "dist/pda/PDA_SEED.sql"));
foreach (var value in new[]
{
    "5011202608280001", "5011LL260828000001", "5011LL260828000002", "5011LL260828000003",
    "CKD202608280001CASE00001", "CKD260828000000001", "CKD260828000000002", "CKD260828000000003",
    "5011LL260903900001", "5011LL260903900002", "260827014", "WH010201", "WH019901", "WH019902",
    "pda-scenario-seed", "pda-test-user", "@TestPinHash"
})
    Check(seed.Contains(value), "Missing scenario seed: " + value);

foreach (var value in new[]
{
    "2026082801", "PDA-REL-TEST-02", "5011LL260701000001", "5011LL260715000002",
    "5011LL260801000003", "5011LL260601000004", "5011LL260820000010", "5011LL260101000018"
})
    Check(seed.Contains(value), "Missing release scenario seed: " + value);

var apiSource = File.ReadAllText(Path.Combine(root, "src/04_Api/AMES.Api/Endpoints/WhEndpoints.cs"));
Check(apiSource.Contains("body.SimulateFailure")
      && apiSource.Contains("Simulated Release API failure. Database transaction was rolled back."),
    "Release scenario 18 must fail inside the API transaction and roll back.");

Console.WriteLine("PASS: Warehouse Inbound and Release scenario guards, FIFO, rollback, Adjust and transaction checks.");
