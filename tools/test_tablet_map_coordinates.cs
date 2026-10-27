#:property PublishAot=false

using System.Reflection;
using System.Runtime.Loader;

static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
var root = Directory.GetParent(Path.GetDirectoryName(SourcePath())!)!.FullName;
// The API test project links the pure Tablet services, so this check needs neither MAUI nor a DB.
var bin = Path.Combine(root, "src/04_Api/AMES.Api.Tests/bin/Debug/net10.0");
AssemblyLoadContext.Default.Resolving += (_, name) => File.Exists(Path.Combine(bin, name.Name + ".dll"))
    ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bin, name.Name + ".dll")) : null;
var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bin, "AMES.Api.Tests.dll"));
var rowType = assembly.GetType("AMES.Tablet.Services.TabletInventoryApi+InventoryRow", true)!;
var mapType = assembly.GetType("AMES.Tablet.Services.TabletMapCoordinates", true)!;
var columnMethod = mapType.GetMethod("Column")!;
var rowMethod = mapType.GetMethod("Row")!;

object Row(string rackX, string rackY) => Activator.CreateInstance(rowType,
    "B0-08-A1", null, "STORAGE", "B", "B0", "STORAGE", rackX, rackY, "1",
    "5011LL260804000001", "81710-PI000NNB", "TRIM", 120m, "EA", null, null, null, 0m, null)!;

void Check(string rackX, string rackY)
{
    object?[] args = [Row(rackX, rackY)];
    if ((string)columnMethod.Invoke(null, args)! != rackX || (string)rowMethod.Invoke(null, args)! != rackY)
        throw new Exception($"Coordinate mapping failed for {rackX}/{rackY}.");
}

Check("A1", "08");
Check("A2", "100");
Check("08", "A1");
Console.WriteLine("PASS: Tablet map uses the same stored Aisle/Bay axes as the Web map.");
