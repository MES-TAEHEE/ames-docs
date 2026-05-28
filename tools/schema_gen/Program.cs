// ════════════════════════════════════════════════════════════════════════
//  A-MES · SQL DDL Generator from AMES_ERD_data.js
//  Reads the ERD data file, extracts 149 tables + columns,
//  generates AMES_Schema.sql with:
//    - DROP TABLE IF EXISTS (idempotent re-run)
//    - CREATE TABLE statements
//    - PK constraints inline
//    - DEFAULT values for audit / status columns
//    - FK relations as comments (per user choice for dev ease)
//    - Sample seed data for MD_Customer, MD_Vendor, MD_Item, MD_Uom, MD_Line
// ════════════════════════════════════════════════════════════════════════
using System.Text;
using System.Text.RegularExpressions;

string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
string srcPath = Path.Combine(root, "AMES_ERD_data.js");
string outDir = Path.Combine(root, "dist");
string outPath = Path.Combine(outDir, "AMES_Schema.sql");
Directory.CreateDirectory(outDir);

if (!File.Exists(srcPath)) {
    Console.Error.WriteLine($"Source not found: {srcPath}");
    return 1;
}

string text = File.ReadAllText(srcPath);

// ─── Parse modules with regex ─────────────────────────────────────────
// Each module starts with `MODNAME: { cols: N, tables: [`
var moduleRegex = new Regex(@"^([A-Z]+)\s*:\s*\{\s*cols:\s*\d+\s*,\s*tables:\s*\[",
    RegexOptions.Multiline);
var tableRegex = new Regex(@"\{name:'([^']+)',\s*ko:'([^']*)',\s*cols:\[(.*?)\]\}",
    RegexOptions.Singleline);
var colRegex = new Regex(@"\['([^']+)','([^']+)','([^']*)'\]");

var allTables = new List<(string Module, string Name, string Ko, List<(string Name, string Type, string Flag)> Cols)>();

var modMatches = moduleRegex.Matches(text);
for (int mi = 0; mi < modMatches.Count; mi++) {
    var modMatch = modMatches[mi];
    string modName = modMatch.Groups[1].Value;
    int modStart = modMatch.Index + modMatch.Length;
    int modEnd = mi + 1 < modMatches.Count ? modMatches[mi + 1].Index : text.Length;
    string modBody = text.Substring(modStart, modEnd - modStart);

    foreach (Match tm in tableRegex.Matches(modBody)) {
        string tname = tm.Groups[1].Value;
        string tko   = tm.Groups[2].Value;
        string colsBody = tm.Groups[3].Value;
        var cols = new List<(string, string, string)>();
        foreach (Match cm in colRegex.Matches(colsBody)) {
            cols.Add((cm.Groups[1].Value, cm.Groups[2].Value, cm.Groups[3].Value));
        }
        // Auto-inject standard audit columns where missing (skip AspNet* + tables that already have them)
        if (!tname.StartsWith("AspNet")) {
            bool hasCreatedBy = cols.Any(c => c.Item1.Equals("CreatedBy", StringComparison.OrdinalIgnoreCase));
            bool hasCreatedTS = cols.Any(c =>
                c.Item1.Equals("CreatedTS", StringComparison.OrdinalIgnoreCase) ||
                c.Item1.Equals("CreatedAt", StringComparison.OrdinalIgnoreCase));
            bool hasUpdated = cols.Any(c =>
                c.Item1.Equals("ModifiedTS", StringComparison.OrdinalIgnoreCase) ||
                c.Item1.Equals("UpdatedAt", StringComparison.OrdinalIgnoreCase) ||
                c.Item1.Equals("UpdatedTS", StringComparison.OrdinalIgnoreCase));
            if (!hasCreatedBy)  cols.Add(("CreatedBy",  "VARCHAR(50)",  ""));
            if (!hasCreatedTS)  cols.Add(("CreatedTS",  "DATETIME2",    ""));
            if (!hasUpdated)    cols.Add(("ModifiedTS", "DATETIME2",    ""));
        }
        if (cols.Count > 0) allTables.Add((modName, tname, tko, cols));
    }
}

Console.WriteLine($"Parsed {allTables.Count} tables from {Path.GetFileName(srcPath)}");
var byMod = allTables.GroupBy(t => t.Module).Select(g => $"{g.Key}={g.Count()}");
Console.WriteLine("  " + string.Join("  ", byMod));

if (allTables.Count == 0) {
    Console.Error.WriteLine("No tables parsed - regex may need fix");
    return 2;
}

// ─── Generate SQL ─────────────────────────────────────────────────────
var sb = new StringBuilder();
string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

sb.AppendLine("-- ════════════════════════════════════════════════════════════════════════");
sb.AppendLine("-- A-MES Database Schema (Auto-generated)");
sb.AppendLine($"-- Generated: {ts}");
sb.AppendLine($"-- Source: AMES_ERD_data.js");
sb.AppendLine($"-- Total tables: {allTables.Count}");
sb.AppendLine("-- Engine: SQL Server 2022/2025");
sb.AppendLine("-- Pattern: Stored Procedure + ADO.NET (per VOL01 Tech Stack)");
sb.AppendLine("-- FK constraints: not applied (commented as -- FK -> Target.Col)");
sb.AppendLine("-- ════════════════════════════════════════════════════════════════════════");
sb.AppendLine();
sb.AppendLine("USE [AMES_DEV];");
sb.AppendLine("GO");
sb.AppendLine();
sb.AppendLine("SET ANSI_NULLS ON;");
sb.AppendLine("SET QUOTED_IDENTIFIER ON;");
sb.AppendLine("SET NOCOUNT ON;");
sb.AppendLine("GO");
sb.AppendLine();

// ─── DROP section (reverse order so child tables drop first; FK-free so order is loose) ─
sb.AppendLine("-- ────────────────────────────────────────────────────────────────────────");
sb.AppendLine("-- DROP existing tables (idempotent re-run)");
sb.AppendLine("-- ────────────────────────────────────────────────────────────────────────");
foreach (var t in allTables.AsEnumerable().Reverse()) {
    sb.AppendLine($"IF OBJECT_ID(N'dbo.{t.Name}', N'U') IS NOT NULL DROP TABLE dbo.{t.Name};");
}
sb.AppendLine("GO");
sb.AppendLine();

// ─── CREATE TABLE section grouped by module ───────────────────────────
var modules = allTables.Select(t => t.Module).Distinct().ToList();
foreach (var mod in modules) {
    sb.AppendLine($"-- ╔══════════════════════════════════════════════════════════════════════╗");
    sb.AppendLine($"-- ║  Module: {mod,-60} ║");
    sb.AppendLine($"-- ╚══════════════════════════════════════════════════════════════════════╝");
    sb.AppendLine();

    foreach (var t in allTables.Where(x => x.Module == mod)) {
        EmitCreateTable(sb, t.Name, t.Ko, t.Cols);
    }
}

// ─── Seed data ────────────────────────────────────────────────────────
EmitSeedData(sb);

// ─── Final verification SELECT ───────────────────────────────────────
sb.AppendLine();
sb.AppendLine("-- ════════════════════════════════════════════════════════════════════════");
sb.AppendLine("-- Verification");
sb.AppendLine("-- ════════════════════════════════════════════════════════════════════════");
sb.AppendLine("SELECT");
sb.AppendLine("  COUNT(*) AS TotalTables");
sb.AppendLine("FROM sys.tables;");
sb.AppendLine("GO");
sb.AppendLine();
sb.AppendLine("SELECT");
sb.AppendLine("  LEFT(name, 4) AS Module,");
sb.AppendLine("  COUNT(*)      AS Tables");
sb.AppendLine("FROM sys.tables");
sb.AppendLine("WHERE name LIKE 'MD[_]%' OR name LIKE 'WH[_]%' OR name LIKE 'PP[_]%'");
sb.AppendLine("   OR name LIKE 'PR[_]%' OR name LIKE 'PNT[_]%' OR name LIKE 'QC[_]%'");
sb.AppendLine("   OR name LIKE 'FG[_]%' OR name LIKE 'MNT[_]%' OR name LIKE 'SYS[_]%'");
sb.AppendLine("   OR name LIKE 'AspNet%' OR name = 'tbl_Lot'");
sb.AppendLine("GROUP BY LEFT(name, 4)");
sb.AppendLine("ORDER BY 1;");
sb.AppendLine("GO");

File.WriteAllText(outPath, sb.ToString());
Console.WriteLine($"\n✓ Generated: {outPath}");
Console.WriteLine($"  Size: {new FileInfo(outPath).Length / 1024.0:F1} KB");

return 0;

// ────────────────────────────────────────────────────────────────────────
//  Emit one CREATE TABLE statement
// ────────────────────────────────────────────────────────────────────────
static void EmitCreateTable(StringBuilder sb, string tname, string tko, List<(string Name, string Type, string Flag)> cols) {
    sb.AppendLine($"-- ── {tname}  ({tko})");
    sb.AppendLine($"CREATE TABLE dbo.{tname} (");

    var pkCols = cols.Where(c => c.Flag == "PK").Select(c => c.Name).ToList();

    for (int i = 0; i < cols.Count; i++) {
        var (name, type, flag) = cols[i];
        string sqlType = TranslateType(type);
        string nullable = (flag == "PK") ? "NOT NULL" : (IsNotNullField(name) ? "NOT NULL" : "    NULL");
        string defaultClause = GetDefault(name, type);
        string fkComment = flag.StartsWith("FK:") ? $"  -- FK -> {flag.Substring(3)}" : "";
        string comma = (i == cols.Count - 1 && pkCols.Count == 0) ? "" : ",";
        sb.AppendLine($"  [{name,-25}] {sqlType,-20} {nullable}{defaultClause}{comma}{fkComment}");
    }

    if (pkCols.Count > 0) {
        var pkList = string.Join(", ", pkCols.Select(p => $"[{p}]"));
        sb.AppendLine($"  CONSTRAINT PK_{tname} PRIMARY KEY CLUSTERED ({pkList})");
    }

    sb.AppendLine(");");
    sb.AppendLine("GO");
    sb.AppendLine();
}

// ────────────────────────────────────────────────────────────────────────
//  Translate ERD types to SQL Server compatible
// ────────────────────────────────────────────────────────────────────────
static string TranslateType(string t) {
    t = t.Trim();
    // Already SQL Server types — pass through
    return t;
}

static bool IsNotNullField(string name) {
    // Conservative: only the main "Name" + CreatedBy are NOT NULL.
    // (PK is already NOT NULL via PRIMARY KEY clause; everything else nullable for dev flexibility.)
    var hardRequired = new[] {
        "ItemName", "CreatedBy"
    };
    return hardRequired.Any(p => name.Equals(p, StringComparison.OrdinalIgnoreCase));
}

static string GetDefault(string name, string type) {
    // Audit timestamps default to SYSDATETIME()
    if (name.Equals("CreatedAt", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("CreatedTS", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("UpdatedAt", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("EntryAt", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("EventTS", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("TxnTime", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("InsStartTS", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("LoggedAt", StringComparison.OrdinalIgnoreCase)) {
        return " DEFAULT SYSDATETIME()";
    }
    // ActiveFlag/IsEnabled etc default 1
    if (name.Equals("ActiveFlag", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("IsActive", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("IsEnabled", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("UseFlag", StringComparison.OrdinalIgnoreCase)) {
        return " DEFAULT 1";
    }
    return "";
}

// ────────────────────────────────────────────────────────────────────────
//  Sample seed data — minimum to get development going
// ────────────────────────────────────────────────────────────────────────
static void EmitSeedData(StringBuilder sb) {
    sb.AppendLine();
    sb.AppendLine("-- ════════════════════════════════════════════════════════════════════════");
    sb.AppendLine("-- Sample seed data (minimal — covers SAV/GEO plants, key items, vendors)");
    sb.AppendLine("-- ════════════════════════════════════════════════════════════════════════");
    sb.AppendLine();

    // UOMs
    sb.AppendLine("-- UOMs");
    sb.AppendLine("INSERT INTO dbo.MD_Uom (UOMCode, UOMName, UOMCategory, BaseFlag, ConvFactor, DecimalPrec, Symbol, ActiveFlag, CreatedBy, CreatedTS) VALUES");
    sb.AppendLine("  ('EA',  'Each',       'QTY',    1, 1,      0, 'ea',  1, 'admin', SYSDATETIME()),");
    sb.AppendLine("  ('BOX', 'Box',        'QTY',    0, 1,      0, 'box', 1, 'admin', SYSDATETIME()),");
    sb.AppendLine("  ('PLT', 'Pallet',     'QTY',    0, 1,      0, 'plt', 1, 'admin', SYSDATETIME()),");
    sb.AppendLine("  ('LB',  'Pound',      'WEIGHT', 1, 1,      2, 'lb',  1, 'admin', SYSDATETIME()),");
    sb.AppendLine("  ('KG',  'Kilogram',   'WEIGHT', 0, 2.205,  3, 'kg',  1, 'admin', SYSDATETIME()),");
    sb.AppendLine("  ('M',   'Meter',      'LENGTH', 1, 1,      3, 'm',   1, 'admin', SYSDATETIME()),");
    sb.AppendLine("  ('FT',  'Foot',       'LENGTH', 0, 0.3048, 2, 'ft',  1, 'admin', SYSDATETIME()),");
    sb.AppendLine("  ('GAL', 'Gallon',     'VOLUME', 1, 1,      2, 'gal', 1, 'admin', SYSDATETIME()),");
    sb.AppendLine("  ('HR',  'Hour',       'TIME',   1, 1,      2, 'hr',  1, 'admin', SYSDATETIME());");
    sb.AppendLine("GO");
    sb.AppendLine();

    // Customers
    sb.AppendLine("-- Customers");
    sb.AppendLine("INSERT INTO dbo.MD_Customer (CustomerID, CustomerCode, CustomerName, CustomerNameEn, CustomerType, Country, EDIFlag, CurrencyCode, Status, CreatedBy, CreatedTS) VALUES");
    sb.AppendLine("  ('CUS-SAV',    'SAV',  N'SEYON E-HWA Detroit',   'SAV (Detroit Plant)',     'PLANT', 'USA', 0, 'USD', 'ACTIVE', 'admin', SYSDATETIME()),");
    sb.AppendLine("  ('CUS-GEO',    'GEO',  N'SEYON E-HWA Birmingham','GEO (Birmingham Plant)',  'PLANT', 'USA', 0, 'USD', 'ACTIVE', 'admin', SYSDATETIME()),");
    sb.AppendLine("  ('CUS-FORD',   'FORD', N'Ford Motor Company',    'Ford Motor Company',      'OEM',   'USA', 1, 'USD', 'ACTIVE', 'admin', SYSDATETIME()),");
    sb.AppendLine("  ('CUS-GM',     'GM',   N'General Motors',        'General Motors',          'OEM',   'USA', 1, 'USD', 'ACTIVE', 'admin', SYSDATETIME()),");
    sb.AppendLine("  ('CUS-STEL',   'STEL', N'Stellantis NA',         'Stellantis North America','OEM',   'USA', 1, 'USD', 'ACTIVE', 'admin', SYSDATETIME()),");
    sb.AppendLine("  ('CUS-HMMA',   'HMMA', N'Hyundai Motor Mfg AL',  'HMMA',                    'OEM',   'USA', 1, 'USD', 'ACTIVE', 'admin', SYSDATETIME()),");
    sb.AppendLine("  ('CUS-BYD',    'BYD',  N'BYD Motors',            'BYD Motors',              'OEM',   'USA', 0, 'USD', 'ACTIVE', 'admin', SYSDATETIME());");
    sb.AppendLine("GO");
    sb.AppendLine();

    // Vendors
    sb.AppendLine("-- Vendors");
    sb.AppendLine("INSERT INTO dbo.MD_Vendor (VendorID, VendorName, VendorType, VendorCategory, Phone, Email, EdiFlag, OtdTargetRate, PaymentTerms, ActiveFlag, CreatedBy, CreatedTS) VALUES");
    sb.AppendLine("  ('SUP-CHEM',  N'ChemTech Industries',   'SUPPLIER', N'Polymer/Resin',  '(313) 555-0142', 'sales@chemtech.us',    1, 98.50, 'Net 30', 1, 'admin', SYSDATETIME()),");
    sb.AppendLine("  ('SUP-EAST',  N'Eastern Coatings Co.',  'SUPPLIER', N'Paint/Powder',   '(419) 555-0188', 'orders@eastcoat.us',   1, 97.00, 'Net 45', 1, 'admin', SYSDATETIME()),");
    sb.AppendLine("  ('SUP-PREC',  N'Precision Mold Inc.',   'SUPPLIER', N'Mold/Tooling',   '(216) 555-0211', 'support@precmold.us',  0, 95.00, 'Net 60', 1, 'admin', SYSDATETIME()),");
    sb.AppendLine("  ('SUP-ABS',   N'ABS Resin Inc.',        'SUPPLIER', N'Resin',          '(513) 555-0367', 'sales@absresin.us',    1, 98.00, 'Net 30', 1, 'admin', SYSDATETIME()),");
    sb.AppendLine("  ('SUP-HAARTZ',N'Haartz Corporation',    'SUPPLIER', N'Fabric',         '(978) 555-0421', 'fabric@haartz.us',     1, 99.00, 'Net 30', 1, 'admin', SYSDATETIME());");
    sb.AppendLine("GO");
    sb.AppendLine();

    // Lines (parent for WorkCenter, Equipment, Oven, RfidReader)
    sb.AppendLine("-- Production Lines");
    sb.AppendLine("INSERT INTO dbo.MD_Line (LineID, LineName, LineType, PlantID, DailyCap, ShiftPattern, RfidEnabledFlag, Status, CreatedBy, CreatedTS) VALUES");
    sb.AppendLine("  ('LINE-INJ-01', N'Injection Line 1 (650T)',  'INJECTION', 'SAV', 4800, '2-SHIFT', 0, 'ACTIVE', 'admin', SYSDATETIME()),");
    sb.AppendLine("  ('LINE-INJ-02', N'Injection Line 2 (850T)',  'INJECTION', 'SAV', 3600, '2-SHIFT', 0, 'ACTIVE', 'admin', SYSDATETIME()),");
    sb.AppendLine("  ('LINE-IMG-01', N'Wrapping Line 1',           'WRAPPING',  'SAV', 1200, '2-SHIFT', 0, 'ACTIVE', 'admin', SYSDATETIME()),");
    sb.AppendLine("  ('LINE-PNT-01', N'Paint Line 1 (Powder)',     'PAINTING',  'GEO',  800, '3-SHIFT', 1, 'ACTIVE', 'admin', SYSDATETIME()),");
    sb.AppendLine("  ('LINE-PNT-02', N'Paint Line 2 (Liquid)',     'PAINTING',  'GEO',  600, '3-SHIFT', 1, 'ACTIVE', 'admin', SYSDATETIME());");
    sb.AppendLine("GO");
    sb.AppendLine();

    // Items
    sb.AppendLine("-- Items");
    sb.AppendLine("INSERT INTO dbo.MD_Item (ItemNo, ItemName, ItemNameEN, ItemType, ItemCategory, DefaultUOM, RoutingType, MinStock, SafetyStock, UnitCost, CustItemNoSAV, CustItemNoGEO, ActiveFlag, CreatedBy, CreatedTS) VALUES");
    sb.AppendLine("  ('FIN-CONS-01', N'Console Upper (Black)',  'Console Upper - Black',  'FINISHED', N'Console',     'EA',  'A', 50,  100, 24.50,  'SAV-CONS-01', NULL,         1, 'admin', SYSDATETIME()),");
    sb.AppendLine("  ('FIN-GRIL-02', N'Radiator Grille',        'Radiator Grille',        'FINISHED', N'Grille',      'EA',  'B', 30,   80, 18.20,  'SAV-GRIL-02', 'GEO-GRIL-02',1, 'admin', SYSDATETIME()),");
    sb.AppendLine("  ('FIN-DOOR-LH',N'Door Trim LH (Gray)',     'Door Trim LH - Gray',    'FINISHED', N'Door Trim',   'EA',  'A', 40,  120, 31.00,  'SAV-DOOR-LH', NULL,         1, 'admin', SYSDATETIME()),");
    sb.AppendLine("  ('FIN-DOOR-RH',N'Door Trim RH (Gray)',     'Door Trim RH - Gray',    'FINISHED', N'Door Trim',   'EA',  'A', 40,  120, 31.00,  'SAV-DOOR-RH', NULL,         1, 'admin', SYSDATETIME()),");
    sb.AppendLine("  ('FIN-BUMP-FR',N'Front Bumper',            'Front Bumper',           'FINISHED', N'Bumper',      'EA',  'B', 20,   60, 78.50,  NULL,          'GEO-BUMP-FR',1, 'admin', SYSDATETIME()),");
    sb.AppendLine("  ('RAW-PP-NAT', N'PP Pellet Natural',       'PP Pellet Natural',      'RAW',      N'Resin',       'LB', NULL, 2000, 5000, 1.20,  NULL,          NULL,         1, 'admin', SYSDATETIME()),");
    sb.AppendLine("  ('RAW-ABS-BLK',N'ABS Resin Black',         'ABS Resin Black',        'RAW',      N'Resin',       'LB', NULL, 1500, 3000, 1.85,  NULL,          NULL,         1, 'admin', SYSDATETIME()),");
    sb.AppendLine("  ('FAB-GRY-01', N'Vinyl Gray',              'Vinyl Gray',             'FABRIC',   N'Vinyl',       'M',  NULL,  500, 1500, 4.20,  NULL,          NULL,         1, 'admin', SYSDATETIME()),");
    sb.AppendLine("  ('POW-RAL9005',N'Powder Black RAL9005',    'Powder Coat Black 9005', 'POWDER',   N'Paint',       'LB', NULL,  300, 1000, 12.50, NULL,          NULL,         1, 'admin', SYSDATETIME()),");
    sb.AppendLine("  ('POW-RAL7042',N'Powder Gray RAL7042',     'Powder Coat Gray 7042',  'POWDER',   N'Paint',       'LB', NULL,  200,  800, 12.50, NULL,          NULL,         1, 'admin', SYSDATETIME());");
    sb.AppendLine("GO");
    sb.AppendLine();

    // Equipment
    sb.AppendLine("-- Equipment");
    sb.AppendLine("INSERT INTO dbo.MD_Equipment (EquipID, EquipName, EquipType, LineID, MakerModel, InstallDate, TheoreticalCycle, TargetOEE, PlcAddress, Status, ActiveFlag, CreatedBy, CreatedTS) VALUES");
    sb.AppendLine("  ('INJ-650-01',  N'Husky 650T Injection',     'INJ_MACHINE',  'LINE-INJ-01', N'Husky H650 RS135/132', '2023-06-15', 45.0, 85.00, '192.168.10.21', 'IDLE', 1, 'admin', SYSDATETIME()),");
    sb.AppendLine("  ('INJ-850-02',  N'Husky 850T Injection',     'INJ_MACHINE',  'LINE-INJ-02', N'Husky H850 RS180/180', '2023-08-22', 52.0, 85.00, '192.168.10.22', 'IDLE', 1, 'admin', SYSDATETIME()),");
    sb.AppendLine("  ('IMG-PRESS-01',N'Vinyl Wrapping Press',     'WRAP_PRESS',   'LINE-IMG-01', N'Dieffenbacher VP-400', '2024-02-10', 60.0, 82.00, '192.168.10.31', 'IDLE', 1, 'admin', SYSDATETIME()),");
    sb.AppendLine("  ('PNT-ROBOT-01',N'Paint Robot ABB IRB-6700', 'PNT_ROBOT',    'LINE-PNT-01', N'ABB IRB-6700-235',     '2024-04-05', 30.0, 80.00, '192.168.10.41', 'IDLE', 1, 'admin', SYSDATETIME()),");
    sb.AppendLine("  ('OVEN-A1',     N'Cure Oven Zone A1',        'OVEN_UNIT',    'LINE-PNT-01', N'Eisenmann CT-180',     '2024-04-05', 0.0,  90.00, '192.168.10.42', 'IDLE', 1, 'admin', SYSDATETIME());");
    sb.AppendLine("GO");
    sb.AppendLine();

    sb.AppendLine("PRINT '✓ Seed data inserted: 9 UOMs, 7 Customers, 5 Vendors, 5 Lines, 10 Items, 5 Equipment';");
    sb.AppendLine("GO");
}
