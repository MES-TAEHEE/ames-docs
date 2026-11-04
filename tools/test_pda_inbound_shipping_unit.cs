// Run after a Windows PDA build: dotnet run --file tools/test_pda_inbound_shipping_unit.cs
// Exercises the compiled Inbound component without API calls or database writes.
#:sdk Microsoft.NET.Sdk.Web
#:property PublishAot=false

using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
var root = Directory.GetParent(Path.GetDirectoryName(SourcePath())!)!.FullName;
var bin = Environment.GetEnvironmentVariable("AMES_PDA_TEST_BIN")
    ?? Path.Combine(root, "src/05_Pda/AMES.Pda/bin/Debug/net10.0-windows10.0.19041.0/win-x64");
AssemblyLoadContext.Default.Resolving += (_, name) => File.Exists(Path.Combine(bin, name.Name + ".dll"))
    ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bin, name.Name + ".dll")) : null;
var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bin, "AMES.Pda.dll"));
var type = assembly.GetType("AMES.Pda.Components.Pages.Wh.Wh02PdaInbound", true)!;
var resultType = assembly.GetType("AMES.Pda.Services.PdaApi+InboundDocumentResult", true)!;
var page = Activator.CreateInstance(type)!;
const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
void Check(bool value, string message) { if (!value) throw new Exception(message); }
object? Property(string name) => type.GetProperty(name, Flags)!.GetValue(page);
string? Validate() => (string?)type.GetMethod("ReceiveValidationMessage", Flags)!.Invoke(page, null);
void Load(bool required = false, bool firstReceived = false, bool deliveryNote = false, bool allReceived = false)
{
    type.GetMethod("ResetCurrentFields", Flags)!.Invoke(page, null);
    var result = JsonSerializer.Deserialize(JsonSerializer.Serialize(new
    {
        Document = new
        {
            ReceiveType = "LOCAL", InboundDocumentId = 0,
            DocumentBarcode = "SHU-20261104-000001", DocumentNo = "SHU-20261104-000001",
            IsPortalCase = !deliveryNote, CaseNo = deliveryNote ? null : "SHU-20261104-000001",
            TotalBoxes = 2, ScannedBoxes = allReceived ? 2 : firstReceived ? 1 : 0,
            Yn = allReceived ? "Y" : "N"
        },
        Lines = Array.Empty<object>(),
        Boxes = new[]
        {
            new { PartNo = "PART-1", BoxBarcode = "UIT-1", LotNo = "UIT-1", Qty = 10m, Unit = "EA",
                Yn = firstReceived || allReceived ? "Y" : "N", ScanRequired = required },
            new { PartNo = "PART-2", BoxBarcode = "UIT-2", LotNo = "UIT-2", Qty = 20m, Unit = "G",
                Yn = allReceived ? "Y" : "N", ScanRequired = false }
        }
    }), resultType)!;
    type.GetMethod("ApplyDocumentResult", Flags)!.Invoke(page, [result]);
}

Load();
Check((bool)Property("DocumentReadyToReceive")! && Validate() is null,
    "SHU with only bulk items must be receivable without child scans.");
Check((string)Property("DocumentTitle")! == "SHIPPING UNIT", "SHU title must identify the shipping unit.");
Load(required: true);
Check(!(bool)Property("DocumentReadyToReceive")! && Validate()!.Contains("UIT-1"),
    "A scan-required item must prevent bulk receipt until it is scanned.");
var pending = (HashSet<string>)type.GetField("_pendingDocumentBoxes", Flags)!.GetValue(page)!;
pending.Add("UIT-1");
Check((bool)Property("DocumentReadyToReceive")! && Validate() is null,
    "Scanning only the required item must unlock the mixed SHU.");
Load(required: true, firstReceived: true);
Check((bool)Property("DocumentReadyToReceive")! && Validate() is null,
    "An already received scan-required item must not block the remaining bulk item.");
Load(allReceived: true);
Check(!(bool)Property("DocumentReadyToReceive")! && Validate()!.Contains("fully received"),
    "Fully received units must not be received again.");
Load(deliveryNote: true);
Check(!(bool)Property("DocumentReadyToReceive")! && Validate()!.Contains("status only"),
    "A Delivery Note must remain a viewer, not a bulk receipt action.");
Console.WriteLine("PASS: 7 compiled Inbound checks (bulk, mixed, partial, duplicate and DN viewer); no API/DB writes.");
