// 개발 DB 구조로 dist/AMES_Schema.sql 의 DDL 부분을 다시 만든다(10-08). 뒤쪽 저장소 시드는 기존 파일에서 그대로 붙인다.
// 사용: dotnet run -- <연결문자열> <기존 AMES_Schema.sql> <출력 파일>
using System.Collections.Specialized;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;

var (cs, oldPath, outPath) = (args[0], args[1], args[2]);
const string SeedMarker = "-- Repository sample seeds (not exported from the live DB).";

var oldText = File.ReadAllText(oldPath, Encoding.UTF8);
int seedAt = oldText.IndexOf(SeedMarker, StringComparison.Ordinal);
if (seedAt < 0) throw new Exception("seed marker not found");
// 시드 뒤에 이어 붙어 있던 마이그레이션 블록(SCM 박스·케이스, FG 출하·재고 통합 등)은 개발 DB 에 이미 반영된 최종 상태라 버린다
const string AppendedMigrations = "-- Persistent box labels.";
int migAt = oldText.IndexOf(AppendedMigrations, seedAt, StringComparison.Ordinal);
if (migAt < 0) throw new Exception("appended-migration marker not found");
string seeds = oldText[seedAt..migAt].TrimEnd() + "\r\n";

using var conn = new SqlConnection(cs);
var server = new Server(new ServerConnection(conn));
var db = server.Databases[new SqlConnectionStringBuilder(cs).InitialCatalog];

static bool Excluded(string name) =>
    name.StartsWith("TEST_", StringComparison.OrdinalIgnoreCase) || name.Equals("sysdiagrams", StringComparison.OrdinalIgnoreCase);
static bool DiagramObject(string name) =>
    name.Equals("fn_diagramobjects", StringComparison.OrdinalIgnoreCase) || name.Contains("diagram", StringComparison.OrdinalIgnoreCase) && name.StartsWith("sp_", StringComparison.OrdinalIgnoreCase);

var tables = db.Tables.Cast<Table>().Where(t => !t.IsSystemObject && t.Schema == "dbo" && !Excluded(t.Name))
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
var procs = db.StoredProcedures.Cast<StoredProcedure>().Where(p => !p.IsSystemObject && p.Schema == "dbo" && !DiagramObject(p.Name))
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
var views = db.Views.Cast<View>().Where(v => !v.IsSystemObject && v.Schema == "dbo").OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase).ToList();
var funcs = db.UserDefinedFunctions.Cast<UserDefinedFunction>().Where(f => !f.IsSystemObject && f.Schema == "dbo" && !DiagramObject(f.Name))
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();

// 이전 파일에 있었지만 개발 DB 에서 정리된 옛 WH 객체 — 오래된 DB 위에 다시 돌려도 남지 않게 지운다
string[] legacyTables = ["WH_AreaLayout", "WH_AreaMaster", "WH_AreaSection", "WH_InboundPackage", "WH_OLD_Inventory",
                         "WH_Receiving", "WH_ReleasePicking", "WH_TransactionHistory", "WH_WarehouseMaster"];

var sb = new StringBuilder();
void L(string s = "") => sb.Append(s).Append("\r\n");
void Batch(StringCollection sc) { foreach (string? s in sc) { L(s!.TrimEnd()); L("GO"); } }

var today = DateTime.Now.ToString("yyyy-MM-dd");
L($"-- A-MES consolidated schema: AMES_DEV, captured {today} from the development DB (192.168.0.132) with SMO.");
L($"-- {tables.Count} tables / {tables.Sum(t => t.Columns.Count)} columns, {procs.Count} procedures, {views.Count} views, {funcs.Count} functions.");
L("-- Excludes TEST_* tables and SSMS diagram objects (sysdiagrams, sp_*diagram*, fn_diagramobjects).");
L("-- Foreign keys are scripted as they exist in the development DB (policy: new tables do not add FKs — production must never stop on FK errors).");
L("-- Schema only from the live database; sample seeds below are retained from the repository.");
L("-- Recreates the included objects: existing data in these tables will be deleted.");
L("-- Use dist/create_database.sql and the rebuild workflow for a fresh database.");
L("-- Do not run against a database whose data must be preserved.");
L("-- Target database must be selected explicitly by sqlcmd -d or in SSMS.");
L("SET ANSI_NULLS ON;");
L("SET QUOTED_IDENTIFIER ON;");
L("SET ANSI_PADDING ON;");
L("SET NOCOUNT ON;");
L("GO");
L("-- Drop every foreign key on the recreated tables (and legacy WH tables) before dropping tables.");
var allNames = tables.Select(t => t.Name).Concat(legacyTables).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
L("DECLARE @DropFk nvarchar(max) = N'';");
L("SELECT @DropFk += N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(fk.parent_object_id)) + N'.' + QUOTENAME(OBJECT_NAME(fk.parent_object_id)) + N' DROP CONSTRAINT ' + QUOTENAME(fk.name) + N';'");
L("FROM sys.foreign_keys fk");
L("WHERE OBJECT_SCHEMA_NAME(fk.parent_object_id) = N'dbo' AND (OBJECT_NAME(fk.parent_object_id) IN (SELECT value FROM STRING_SPLIT(@Names, N',')) OR OBJECT_NAME(fk.referenced_object_id) IN (SELECT value FROM STRING_SPLIT(@Names, N',')));");
sb.Replace("@Names", "N'" + string.Join(",", allNames) + "'");
L("EXEC sys.sp_executesql @DropFk;");
L("GO");
L("DROP TRIGGER IF EXISTS [dbo].[TR_WH_OLD_Inventory_SyncUnifiedInventory];");
foreach (var p in procs) L($"DROP PROCEDURE IF EXISTS [dbo].[{p.Name}];");
foreach (var v in views) L($"DROP VIEW IF EXISTS [dbo].[{v.Name}];");
foreach (var f in funcs) L($"DROP FUNCTION IF EXISTS [dbo].[{f.Name}];");
foreach (var t in tables) L($"DROP TABLE IF EXISTS [dbo].[{t.Name}];");
foreach (var t in legacyTables) L($"DROP TABLE IF EXISTS [dbo].[{t}];");
L("GO");

var tableOpt = new ScriptingOptions
{
    ScriptSchema = true, ScriptData = false, IncludeHeaders = false, SchemaQualify = true, AnsiPadding = false,
    DriPrimaryKey = true, DriUniqueKeys = true, DriChecks = true, DriDefaults = true, DriIndexes = true, DriForeignKeys = false,
    DriClustered = true, DriNonClustered = true, Indexes = true, NonClusteredIndexes = true, ClusteredIndexes = true,
    FullTextIndexes = true, Triggers = true, ExtendedProperties = true, NoCollation = false, Statistics = false,
    TargetServerVersion = SqlServerVersion.Version160, NoFileGroup = false,
};
foreach (var t in tables)
{
    L($"-- Table: dbo.{t.Name}");
    Batch(t.Script(tableOpt));
}

L("-- ════ Foreign keys (as in the development DB) ════");
var fkOpt = new ScriptingOptions { DriForeignKeys = true, SchemaQualify = true, IncludeHeaders = false, TargetServerVersion = SqlServerVersion.Version160 };
foreach (var t in tables)
    foreach (ForeignKey fk in t.ForeignKeys)
        Batch(fk.Script(fkOpt));

var modOpt = new ScriptingOptions { SchemaQualify = true, IncludeHeaders = false, ExtendedProperties = true, TargetServerVersion = SqlServerVersion.Version160 };
L("-- ════ Functions ════");
foreach (var f in funcs) Batch(f.Script(modOpt));
L("-- ════ Views ════");
foreach (var v in views) Batch(v.Script(modOpt));
L("-- ════ Stored procedures ════");
foreach (var p in procs) Batch(p.Script(modOpt));

// SYS_Screen 시드는 개발 DB 의 현재 화면 목록으로 다시 뜬다(옛 07-24 판 + 꼬리의 포탈 화면 갱신을 대신한다)
const string ScreenSeedStart = "-- Seed: SYS_Screen";
int ss = seeds.IndexOf(ScreenSeedStart, StringComparison.Ordinal);
int se = seeds.IndexOf("PRINT 'SYS_Screen:", ss, StringComparison.Ordinal);
se = seeds.IndexOf("GO", se, StringComparison.Ordinal) + 2;
var scr = new StringBuilder();
static string N(object v) => v is DBNull ? "NULL" : "N'" + ((string)v).Replace("'", "''") + "'";
static string A(object v) => v is DBNull ? "NULL" : "'" + ((string)v).Replace("'", "''") + "'";
int screenRows = 0;
using (var c2 = new SqlConnection(cs))
{
    c2.Open();
    using var cmd = new SqlCommand("""
        SELECT ScreenCode, ModuleCode, ProcessCode, SubProcessCode, ScreenName, ScreenNameEn, HRef, LidLabel, SortOrder, IsVisible
        FROM dbo.SYS_Screen ORDER BY ModuleCode, ProcessCode, ISNULL(SortOrder, 9999), ScreenCode;
        """, c2);
    using var r = cmd.ExecuteReader();
    scr.Append($"-- Seed: SYS_Screen — regenerated {today} from the development DB (all modules)\r\n");
    while (r.Read())
    {
        screenRows++;
        scr.Append("INSERT INTO dbo.SYS_Screen (ScreenCode, ModuleCode, ProcessCode, SubProcessCode, ScreenName, ScreenNameEn, HRef, LidLabel, SortOrder, IsVisible, CreatedBy) VALUES (")
           .Append($"{A(r[0])}, {A(r[1])}, {A(r[2])}, {A(r[3])}, {N(r[4])}, {N(r[5])}, {A(r[6])}, {A(r[7])}, ")
           .Append(r[8] is DBNull ? "NULL" : r[8].ToString()).Append(", ")
           .Append(r[9] is DBNull ? "NULL" : ((bool)r[9] ? "1" : "0")).Append(", 'seed');\r\n");
    }
    scr.Append($"GO\r\nPRINT 'SYS_Screen: {screenRows} rows';\r\nGO");
}
seeds = seeds[..ss] + scr + seeds[se..];

// 일부 프로시저가 QUOTED_IDENTIFIER OFF 로 뽑혀 그 설정이 남는다 — 필터 인덱스가 있는 테이블에 넣는 시드 전에 되돌린다
L("SET ANSI_NULLS ON;");
L("SET QUOTED_IDENTIFIER ON;");
L("GO");
sb.Append(seeds);
File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(true));
Console.WriteLine($"tables={tables.Count} procs={procs.Count} views={views.Count} funcs={funcs.Count} lines={sb.ToString().Split('\n').Length}");
