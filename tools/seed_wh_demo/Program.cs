namespace AMES.Tools.SeedWhDemo;

// Retired: this tool seeded pre-consolidation tables and non-master demo parts.
// Keep the entry point to make old scripts fail explicitly, before any DB access.
internal static class Program
{
    private static int Main()
    {
        Console.Error.WriteLine("Warehouse demo seeding is retired. Use explicit test-DB fixtures in AMES.Data.Tests; no database was accessed.");
        return 1;
    }
}
