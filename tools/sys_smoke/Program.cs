using AMES.Data.Connection;
using AMES.Data.Repositories;

namespace AMES.Tools.SysSmoke;

internal static class Program
{
    private const string Cs =
        "Server=localhost;Database=AMES_DEV;User Id=ames_app;Password=!Dev2026;" +
        "TrustServerCertificate=True;Encrypt=True;Connect Timeout=5;";

    private static int Main()
    {
        var f = new AmesConnectionFactory(Cs);
        var s = new SysRepository(f);
        int fails = 0;
        void Try(string id, Action a)
        {
            try { a(); Console.WriteLine($"  {id}  ok"); }
            catch (Exception ex) { Console.WriteLine($"  {id}  FAIL  {ex.Message}"); fails++; }
        }
        Console.WriteLine("[sys-smoke] running 9 queries ...");
        Try("SYS-01 users          ", () => Console.Write($"({s.ListUsers().Count} rows) "));
        Try("SYS-02 role-perms     ", () => Console.Write($"({s.ListRolePermissions().Count} rows) "));
        Try("SYS-02 roles          ", () => Console.Write($"({s.ListRoles().Count} rows) "));
        Try("SYS-03 calendar       ", () => Console.Write($"({s.ListCalendar().Count} rows) "));
        Try("SYS-04 interfaces     ", () => Console.Write($"({s.ListInterfaces().Count} rows) "));
        Try("SYS-05 audit          ", () => Console.Write($"({s.ListAudit().Count} rows) "));
        Try("SYS-06 notif-rules    ", () => Console.Write($"({s.ListNotificationRules().Count} rows) "));
        Try("SYS-06 notif-history  ", () => Console.Write($"({s.ListNotificationHistory().Count} rows) "));
        Try("SYS-07 config         ", () => Console.Write($"({s.ListConfig().Count} rows) "));
        Try("SYS-08 health         ", () => { var k = s.GetHealth(); Console.Write($"(users={k.Users} roles={k.Roles} ifOk={k.InterfacesOk} ifDown={k.InterfacesDown}) "); });
        Console.WriteLine();
        Console.WriteLine(fails == 0 ? "[sys-smoke] OK" : $"[sys-smoke] FAIL {fails}/10");
        return fails == 0 ? 0 : 1;
    }
}
