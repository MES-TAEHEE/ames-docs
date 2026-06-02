using AMES.Data.Connection;
using AMES.Data.Repositories;

namespace AMES.Tools.RptSmoke;

internal static class Program
{
    private const string Cs =
        "Server=localhost;Database=AMES_DEV;User Id=ames_app;Password=!Dev2026;" +
        "TrustServerCertificate=True;Encrypt=True;Connect Timeout=5;";

    private static int Main()
    {
        var f = new AmesConnectionFactory(Cs);
        var r = new RptRepository(f);
        int fails = 0;
        void Try(string id, Action a)
        {
            try { a(); Console.WriteLine($"  {id}  ok"); }
            catch (Exception ex) { Console.WriteLine($"  {id}  FAIL  {ex.Message}"); fails++; }
        }
        Console.WriteLine("[rpt-smoke] running 8 aggregate queries ...");
        Try("RPT-01 daily-prod      ", () => Console.Write($"({r.ListDailyProduction().Count} rows) "));
        Try("RPT-02 defect-pareto   ", () => Console.Write($"({r.ListDefectPareto().Count} rows) "));
        Try("RPT-03 daily-shipment  ", () => Console.Write($"({r.ListDailyShipment().Count} rows) "));
        Try("RPT-04 otd             ", () => Console.Write($"({r.ListOtd().Count} rows) "));
        Try("RPT-05 inventory       ", () => Console.Write($"({r.ListInventory().Count} rows) "));
        Try("RPT-06 equip-oee       ", () => Console.Write($"({r.ListEquipmentOee().Count} rows) "));
        Try("RPT-07 monthly-kpi     ", () => Console.Write($"({r.ListMonthlyKpi().Count} rows) "));
        Try("RPT-08 schedule-adh    ", () => Console.Write($"({r.ListScheduleAdherence().Count} rows) "));
        Console.WriteLine();
        Console.WriteLine(fails == 0 ? "[rpt-smoke] OK" : $"[rpt-smoke] FAIL {fails}/8");
        return fails == 0 ? 0 : 1;
    }
}
