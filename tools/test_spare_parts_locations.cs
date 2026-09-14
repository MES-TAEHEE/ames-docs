#:property PublishAot=false

static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
var root = Directory.GetParent(Path.GetDirectoryName(SourcePath())!)!.FullName;
var schema = File.ReadAllText(Path.Combine(root, "dist", "pda", "PDA_SCHEMA.sql"));
var seed = File.ReadAllText(Path.Combine(root, "dist", "pda", "PDA_SEED.sql"));

if (!schema.Contains("ADD WhCode VARCHAR(20)") || !schema.Contains("ADD AreaCode VARCHAR(20)"))
    throw new Exception("MD_Location warehouse hierarchy migration is missing.");

var rackZones = new[]
{
    "SP_CAB1", "SP_CAB2",
    "SP_A1", "SP_A2", "SP_A3", "SP_B1", "SP_B2", "SP_B3",
    "SP_C1", "SP_C2", "SP_C3", "SP_D1", "SP_D2", "SP_D3",
    "SP_E1", "SP_E2", "SP_E3", "SP_F1", "SP_F2", "SP_F3"
};
var singleLocations = new[]
{
    "SP-RRACKS-01", "SP-RRACKS-02", "SP-CRACK-01",
    "SP-FL1", "SP-FL2", "SP-FL3", "SP-FL4", "SP-EXTRA"
};

if (rackZones.Any(zone => !seed.Contains($"('{zone}'")))
    throw new Exception("A five-level spare-parts rack zone is missing.");
if (singleLocations.Any(location => !seed.Contains($"('{location}'")))
    throw new Exception("A single-level spare-parts location is missing.");
if (rackZones.Length * 5 + singleLocations.Length != 108)
    throw new Exception("Expected 108 spare-parts locations.");

Console.WriteLine("PASS: 20 five-level racks + 8 single-level locations = 108 locations.");
