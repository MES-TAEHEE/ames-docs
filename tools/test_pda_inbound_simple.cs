#:sdk Microsoft.NET.Sdk.Web
#:property PublishAot=false

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
var root = Directory.GetParent(Path.GetDirectoryName(SourcePath())!)!.FullName;
var bin = Path.Combine(root, "src/05_Pda/AMES.Pda/bin/Debug/net10.0-windows10.0.19041.0/win-x64");
AssemblyLoadContext.Default.Resolving += (_, name) => File.Exists(Path.Combine(bin, name.Name + ".dll"))
    ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bin, name.Name + ".dll")) : null;
var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bin, "AMES.Pda.dll"));
var componentType = assembly.GetType("AMES.Pda.Components.Pages.Wh.Wh02PdaInbound", true)!;
var component = Activator.CreateInstance(componentType)!;
var authType = assembly.GetType("AMES.Pda.Services.AuthState", true)!;
var auth = Activator.CreateInstance(authType)!;
const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
void Check(bool value, string message) { if (!value) throw new Exception(message); }
object? Field(string name) => componentType.GetField(name, Flags)!.GetValue(component);
object? Property(string name) => componentType.GetProperty(name, Flags)!.GetValue(component);
void Set(string name, object value) => componentType.GetField(name, Flags)!.SetValue(component, value);
object? Call(string name, params object[] values) => componentType.GetMethod(name, Flags)!.Invoke(component, values);
async Task Act(string name, params object[] values) => await (Task)Call(name, values)!;
using var client = new HttpClient { BaseAddress = new Uri("http://localhost:5210") };
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

using (var anonymous = await client.PostAsync("/api/wh/inbound/test/simple-reset", null))
    Check(anonymous.StatusCode == HttpStatusCode.Unauthorized, "Anonymous reset must be rejected.");
var originalToken = await Login("TEST");
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", originalToken);
using (var forbidden = await client.PostAsync("/api/wh/inbound/test/simple-reset", null))
    Check(forbidden.StatusCode == HttpStatusCode.Forbidden, "Full TEST account must not reset TEST1 data.");
var token = await Login("TEST1");
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
var sessionType = authType.GetProperty("Session")!.PropertyType;
var session = JsonSerializer.Deserialize(await client.GetStringAsync("/api/auth/me"), sessionType,
    new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
authType.GetMethod("SignIn")!.Invoke(auth, [token, session]);
componentType.GetProperty("Auth", Flags)!.SetValue(component, auth);
var apiType = assembly.GetType("AMES.Pda.Services.PdaApi", true)!;
var api = Activator.CreateInstance(apiType, [client, auth, null])!;
componentType.GetProperty("Api", Flags)!.SetValue(component, api);
Check(((Array)Property("ActiveTestScenarios")!).Length == 5, "TEST1 requires five PPT steps.");

async Task<int> ReceivedBoxes(string mode, string document)
{
    using var json = JsonDocument.Parse(await client.GetStringAsync($"/api/wh/inbound/document?mode={mode}&barcode={document}"));
    return json.RootElement.GetProperty("boxes").EnumerateArray()
        .Count(box => box.GetProperty("yn").GetString() == "Y");
}

try
{
    await Act("ResetSimpleTestData");
    Check((string)Field("_modalTitle")! == "Test Data Ready", "Dedicated data reset failed: " + Field("_modalMessage"));
    Call("DismissModal");
    foreach (var mode in new[] { "LOCAL", "CKD" })
    {
        Call("SelectSimpleMode", mode);
        Call("SelectTestScenario", 0);
        await Act("StartTestScenario");
        Check((string)Field("_mode")! == "", "PPT 1 must show the initial unselected mode.");
        Call("SetMode", mode);
        Check((string)Field("_simpleMode")! == mode, "Main screen mode must carry into the PPT helper.");
        Call("SelectTestScenario", 1);
        await Act("StartTestScenario");
        var values = (Array)Property("CurrentTestValues")!;
        await Act("LoadTestValue", values.GetValue(0)!);
        Check(Field("_scan") is not null && Field("_document") is null, "Single box scan must show only the item.");
        await Act("LoadTestValue", values.GetValue(1)!);
        Check(Field("_document") is not null, "Document scan must display the list.");
        Call("SelectTestScenario", 2);
        await Act("StartTestScenario");
        Check(!(bool)Property("DocumentReadyForLocation")!, "Location must be locked before box scans.");
        values = (Array)Property("CurrentTestValues")!;
        await Act("LoadTestValue", values.GetValue(0)!);
        Check((int)Property("DisplayScannedBoxes")! == 1, "First scanned box must count once.");
        await Act("LoadTestValue", values.GetValue(0)!);
        Check((int)Property("DisplayScannedBoxes")! == 1 && (bool)Field("_modalOpen")!, "Duplicate scan must not increment.");
        Call("DismissModal");
        await Act("LoadTestValue", values.GetValue(1)!);
        await Act("LoadTestValue", values.GetValue(2)!);
        Check((int)Property("DisplayScannedBoxes")! == 3 && (bool)Property("DocumentReadyForLocation")!, "All boxes must unlock Location.");
        Call("SelectTestScenario", 3);
        Check((int)Property("DisplayScannedBoxes")! == 3, "Changing PPT step must preserve scans.");
        values = (Array)Property("CurrentTestValues")!;
        await Act("LoadTestValue", values.GetValue(0)!);
        Check(Field("_selectedLocation") is not null && Call("ReceiveValidationMessage") is null, "Location must enable receive.");
        Call("SelectTestScenario", 4);
        Set("_simulateReceiveApiFailure", true);
        await Act("Receive");
        var document = (string)Property("SimpleDocument")!;
        Check((string)Field("_modalTitle")! == "Receive Failed", "Simulated failure must show an alert.");
        Check(await ReceivedBoxes(mode, document) == 0, "Failed receive must leave every box unreceived.");
        Check((int)Property("DisplayScannedBoxes")! == 3 && Field("_selectedLocation") is not null, "Failure must retain input for retry.");
        Call("DismissModal");
        Set("_simulateReceiveApiFailure", false);
        await Act("Receive");
        Check((string)Field("_modalTitle")! == "Receive Complete", "Normal receive must complete: " + Field("_modalMessage"));
        Check(await ReceivedBoxes(mode, document) == 3, "All three boxes must be received.");
        await Act("ModalPrimary");
        Check(Field("_scan") is null && Field("_document") is null, "Completion must clear the form.");
    }
    Console.WriteLine("PASS: TEST1 authentication and reset authorization; LOCAL/CKD PPT flows, duplicate scans, Location gating, receive rollback and retry.");
}
finally
{
    using var reset = await client.PostAsync("/api/wh/inbound/test/simple-reset", null);
    reset.EnsureSuccessStatusCode();
}
