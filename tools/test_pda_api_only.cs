#:property PublishAot=false

static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
var root = Directory.GetParent(Path.GetDirectoryName(SourcePath())!)!.FullName;
string Read(string path) => File.ReadAllText(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine("PASS: " + message);
}

var pdaRoot = Path.Combine(root, "src", "05_Pda", "AMES.Pda");
var pdaSources = Directory.EnumerateFiles(pdaRoot, "*", SearchOption.AllDirectories)
    .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
    .Select(File.ReadAllText)
    .ToList();
var forbidden = new[] { "AmesConnectionFactory", "SqlConnection", "SqlCommand", "SqlDataReader", "Microsoft.Data.SqlClient", "AMES.Data" };
Check(forbidden.All(token => pdaSources.All(source => !source.Contains(token, StringComparison.Ordinal))),
    "PDA has no direct database dependency");

var client = Read("src/05_Pda/AMES.Pda/Services/PdaApi.cs");
var endpoints = Read("src/04_Api/AMES.Api/Endpoints/WhEndpoints.cs");
var fgEndpoints = Read("src/04_Api/AMES.Api/Endpoints/FgEndpoints.cs");
var apiProgram = Read("src/04_Api/AMES.Api/Program.cs");
foreach (var route in new[] { "/api/wh/inventory", "/api/wh/inventory/scan", "/api/wh/inventory/locations", "/api/wh/locations", "/api/wh/inventory/lots" })
    Check(client.Contains(route, StringComparison.Ordinal), $"PDA uses API route {route}");
foreach (var route in new[] { "MapGet(\"/inventory\"", "MapGet(\"/inventory/scan\"", "MapGet(\"/inventory/locations\"" })
    Check(endpoints.Contains(route, StringComparison.Ordinal), $"API exposes {route[8..^1]}");
Check(apiProgram.Contains("CustomSchemaIds", StringComparison.Ordinal)
      && apiProgram.Contains("UseSwaggerUI", StringComparison.Ordinal),
    "Swagger supports duplicate PDA DTO names and exposes its UI");
Check(endpoints.Split("WithTags(\"PDA Test Scenarios\")").Length - 1 == 6
      && fgEndpoints.Split("WithTags(\"PDA Test Scenarios\")").Length - 1 == 1,
    "Swagger separates all seven PDA test endpoints from operation groups");
