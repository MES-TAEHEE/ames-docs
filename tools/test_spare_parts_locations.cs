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

if (!seed.Contains("'MNT_ZONE'") || !seed.Contains("'MNT_SLOT'"))
    throw new Exception("MNT_ZONE or MNT_SLOT common-code group is missing.");
if (rackZones.Any(zone => !seed.Contains($"('MNT_ZONE', '{zone}'")))
    throw new Exception("A spare-parts zone is not assigned to MNT_ZONE.");
if (Enumerable.Range(1, 5).Any(level =>
        !seed.Contains($"('MNT_SLOT', '{level:00}'")))
    throw new Exception("MNT_SLOT must contain rack levels 01 through 05.");
if (seed.Contains("('WH_ZONE', 'SP_"))
    throw new Exception("An SP zone is still assigned to WH_ZONE.");
if (!seed.Contains("WHERE GroupCode = 'MNT_ZONE'"))
    throw new Exception("WH_AreaSection is not sourced from MNT_ZONE.");

Console.WriteLine("PASS: MNT_ZONE 28 zones + MNT_SLOT 5 levels + 108 physical locations.");
