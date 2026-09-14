#:property PublishAot=false

using System.Reflection;
using System.Runtime.Loader;

static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
var root = Directory.GetParent(Path.GetDirectoryName(SourcePath())!)!.FullName;
var bin = Path.Combine(root, "src/08_Tablet/AMES.Tablet/bin/Debug/net10.0-windows10.0.19041.0/win-x64");
AssemblyLoadContext.Default.Resolving += (_, name) => File.Exists(Path.Combine(bin, name.Name + ".dll"))
    ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bin, name.Name + ".dll")) : null;
var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bin, "AMES.Tablet.dll"));
var rowType = assembly.GetType("AMES.Tablet.Services.TabletInventoryService+InventoryRow", true)!;
var pageType = assembly.GetType("AMES.Tablet.Components.Pages.Home", true)!;
var map = pageType.GetMethod("TryMapPosition", BindingFlags.Static | BindingFlags.NonPublic)!;

object Row(string rackX, string rackY) => Activator.CreateInstance(rowType,
    "B0-08-A1", null, "STORAGE", "B", "B0", "STORAGE", rackX, rackY, "1",
    "5011LL260804000001", "81710-PI000NNB", "TRIM", 120m, "EA")!;

void Check(string rackX, string rackY, string expectedRow, int expectedColumn)
{
    object?[] args = [Row(rackX, rackY), null, 0];
    if (!(bool)map.Invoke(null, args)! || (string)args[1]! != expectedRow || (int)args[2]! != expectedColumn)
        throw new Exception($"Coordinate mapping failed for {rackX}/{rackY}.");
}

Check("A", "08", "A", 8);
Check("08", "A", "A", 8);
Console.WriteLine("PASS: Tablet map accepts both row/column coordinate orders.");
