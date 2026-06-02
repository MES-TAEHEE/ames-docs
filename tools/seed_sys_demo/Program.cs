using Microsoft.Data.SqlClient;

namespace AMES.Tools.SeedSysDemo;

/// <summary>
/// Seeds SYS-only rows so the 8 admin screens render with meaningful data.
/// User profiles + role permissions + factory calendar + interfaces +
/// audit log + notification rules/history + config keys.
/// Idempotent — CreatedBy='sys-seed'.
/// </summary>
internal static class Program
{
    private const string Cs =
        "Server=localhost;Database=AMES_DEV;User Id=ames_app;Password=!Dev2026;" +
        "TrustServerCertificate=True;Encrypt=True;Connect Timeout=5;";

    private static int Main()
    {
        Console.WriteLine("[sys-seed] connecting ...");
        using var conn = new SqlConnection(Cs);
        conn.Open();

        Wipe(conn);
        SeedUserProfiles(conn);
        SeedRolePermissions(conn);
        SeedCalendar(conn);
        SeedInterfaces(conn);
        SeedAudit(conn);
        SeedNotifRules(conn);
        SeedNotifHistory(conn);
        SeedConfig(conn);

        Console.WriteLine();
        Console.WriteLine("[sys-seed] done.");
        return 0;
    }

    private static void Wipe(SqlConnection conn) => Exec(conn, """
        DELETE FROM dbo.SYS_Config                WHERE CreatedBy='sys-seed';
        DELETE FROM dbo.SYS_NotificationHistory   WHERE CreatedBy='sys-seed';
        DELETE FROM dbo.SYS_NotificationChannel   WHERE CreatedBy='sys-seed';
        DELETE FROM dbo.SYS_NotificationRule      WHERE CreatedBy='sys-seed';
        DELETE FROM dbo.SYS_AuditLog              WHERE CreatedBy='sys-seed';
        DELETE FROM dbo.SYS_InterfaceMonitor      WHERE CreatedBy='sys-seed';
        DELETE FROM dbo.SYS_FactoryCalendar       WHERE CreatedBy='sys-seed';
        DELETE FROM dbo.SYS_RolePermission        WHERE CreatedBy='sys-seed';
        DELETE FROM dbo.SYS_UserProfile           WHERE CreatedBy='sys-seed';
        """);

    // ── SYS-01 User profiles for the admin user ─────────────────────────
    private static void SeedUserProfiles(SqlConnection conn)
    {
        // Match every AspNetUser that doesn't already have a profile
        Exec(conn, """
            INSERT INTO dbo.SYS_UserProfile
                (UserID, EmployeeNo, EmployeeName, Department, Plant,
                 DefaultShift, AccountStatus, FailedLoginCount, LastLoginTS,
                 CreatedBy, CreatedTS)
            SELECT  u.Id,
                    CONCAT('EMP-', RIGHT(REPLACE(u.Id,'-',''), 4)),
                    COALESCE(u.UserName, u.Email, 'unknown'),
                    'Operations',
                    'SEH-USA',
                    'A',
                    'ACTIVE',
                    0,
                    SYSDATETIME(),
                    'sys-seed', SYSDATETIME()
            FROM    dbo.AspNetUsers u
            WHERE   NOT EXISTS (SELECT 1 FROM dbo.SYS_UserProfile p WHERE p.UserID = u.Id);
            """);
        Console.WriteLine("  up    user profiles seeded for users w/o profile");
    }

    // ── SYS-02 Role × screen permission grid ────────────────────────────
    private static void SeedRolePermissions(SqlConnection conn)
    {
        var roles = new[] { "Admin", "Planner", "Supervisor", "Operator", "QC", "Maintenance" };
        var grid = new (string M, string S)[]
        {
            ("PP",  "PP-01"), ("PP",  "PP-04"), ("PP",  "PP-OEE"),
            ("POP", "INJ-02"), ("POP", "INJ-04"), ("POP", "INJ-06"), ("POP", "INJ-08"),
            ("WH",  "WH-01"), ("WH",  "WH-04"),
            ("FG",  "FG-01"), ("FG",  "FG-05"),
            ("QC",  "QC-01"), ("QC",  "QC-04"),
            ("MNT", "MNT-01"), ("MNT","MNT-02"), ("MNT","MNT-07"),
            ("RPT", "RPT-09"), ("SYS","SYS-01")
        };
        var perm = new Dictionary<string, string>
        {
            ["Admin"]       = "FULL",
            ["Planner"]     = "EDIT",
            ["Supervisor"]  = "EDIT",
            ["Operator"]    = "VIEW",
            ["QC"]          = "EDIT",
            ["Maintenance"] = "EDIT"
        };

        foreach (var role in roles)
        foreach (var g in grid)
        {
            // narrow: each role only sees its module + universal stuff
            var lvl = role switch
            {
                "Admin"                                   => "FULL",
                "Planner"     when g.M is "PP" or "RPT"   => "EDIT",
                "Supervisor"  when g.M is "POP" or "WH"
                                 or "FG" or "RPT" or "PP" => "EDIT",
                "Operator"    when g.M is "POP" or "WH"
                                 or "FG"                  => "VIEW",
                "QC"          when g.M == "QC" || g.M == "RPT" => "EDIT",
                "Maintenance" when g.M == "MNT" || g.M == "RPT" => "EDIT",
                _ => "NONE"
            };
            if (lvl == "NONE") continue;

            Exec(conn, """
                INSERT INTO dbo.SYS_RolePermission
                    (RoleName, ModuleCode, ScreenCode, PermissionLevel,
                     IsSystemRole, EffectiveTS, CreatedBy, CreatedTS)
                VALUES (@R, @M, @S, @L, @SYS, SYSDATETIME(), 'sys-seed', SYSDATETIME());
                """,
                ("@R", role), ("@M", g.M), ("@S", g.S), ("@L", lvl),
                ("@SYS", role == "Admin"));
        }
        Console.WriteLine($"  rp    role × screen permission grid seeded ({roles.Length} roles)");
    }

    // ── SYS-03 Factory calendar (next 30 + last 7 days) ─────────────────
    private static void SeedCalendar(SqlConnection conn)
    {
        var rng = new Random(3001);
        for (int d = -7; d <= 30; d++)
        {
            var date = DateTime.Today.AddDays(d);
            var dow = date.DayOfWeek;
            var dayType = dow == DayOfWeek.Saturday || dow == DayOfWeek.Sunday ? "WEEKEND" : "WORKDAY";
            // randomly mark one Friday as holiday
            if (d > 0 && d < 15 && dow == DayOfWeek.Friday && rng.Next(3) == 0)
                dayType = "HOLIDAY";

            if (dayType == "WORKDAY")
            {
                foreach (var (sh, s, e, brk, hrs) in new[]
                {
                    ("A", "06:00:00", "14:00:00", 60, 7.0m),
                    ("B", "14:00:00", "22:00:00", 60, 7.0m),
                    ("C", "22:00:00", "06:00:00", 60, 7.0m),
                })
                {
                    Exec(conn, """
                        INSERT INTO dbo.SYS_FactoryCalendar
                            (CalendarDate, DayType, ShiftCount, ShiftCode, StartTime, EndTime,
                             BreakMinutes, NetWorkHours, CalendarYear, Plant, CreatedBy, CreatedTS)
                        VALUES (@D, @DT, 3, @SH, @ST, @ET, @BR, @HR, @Y, 'SEH-USA', 'sys-seed', SYSDATETIME());
                        """,
                        ("@D", date), ("@DT", dayType), ("@SH", sh),
                        ("@ST", TimeSpan.Parse(s)), ("@ET", TimeSpan.Parse(e)),
                        ("@BR", brk), ("@HR", hrs), ("@Y", date.Year));
                }
            }
            else
            {
                Exec(conn, """
                    INSERT INTO dbo.SYS_FactoryCalendar
                        (CalendarDate, DayType, HolidayName, ShiftCount, NetWorkHours,
                         CalendarYear, Plant, CreatedBy, CreatedTS)
                    VALUES (@D, @DT, @H, 0, 0, @Y, 'SEH-USA', 'sys-seed', SYSDATETIME());
                    """,
                    ("@D", date), ("@DT", dayType),
                    ("@H", (object?)(dayType == "HOLIDAY" ? "Plant maint." : null) ?? DBNull.Value),
                    ("@Y", date.Year));
            }
        }
        Console.WriteLine("  cal   factory calendar seeded (37 days × 1-3 shifts)");
    }

    // ── SYS-04 Interface monitor entries ────────────────────────────────
    private static void SeedInterfaces(SqlConnection conn)
    {
        var rng = new Random(3002);
        var ifs = new (string Code, string Name, string Dir, string Endpoint, string Proto, string Status, int Gap, int Rec, int Retry, string? Err)[]
        {
            ("SAP-FC", "SAP forecast pull",     "INBOUND",  "https://sap-host/api/forecast", "HTTPS",  "OK",  3, 240, 0, null),
            ("SAP-SO", "SAP customer orders",   "INBOUND",  "https://sap-host/api/so",       "HTTPS",  "OK",  4,  18, 0, null),
            ("SAP-PR", "SAP purchase requests", "OUTBOUND", "https://sap-host/api/pr",       "HTTPS",  "OK",  6,   5, 0, null),
            ("PLC-INJ","PLC injection line",    "INBOUND",  "opc.tcp://10.50.1.20:4840",     "OPC-UA", "OK",  1, 720, 0, null),
            ("PLC-IMG","PLC image-wrap line",   "INBOUND",  "opc.tcp://10.50.1.21:4840",     "OPC-UA", "WARN",18,  60, 2, "occasional timeout"),
            ("PLC-PNT","PLC paint line",        "INBOUND",  "opc.tcp://10.50.1.22:4840",     "OPC-UA", "DOWN",95,   0, 8, "connection refused"),
            ("WMS",    "WMS sync",              "INBOUND",  "https://wms/api/movements",     "HTTPS",  "OK",  9,  42, 0, null),
            ("EDI",    "EDI customer feed",     "OUTBOUND", "as2://customer.edi/inbox",      "AS2",    "OK", 12,  12, 1, null),
        };
        foreach (var i in ifs)
        {
            Exec(conn, """
                INSERT INTO dbo.SYS_InterfaceMonitor
                    (InterfaceCode, InterfaceName, Direction, Endpoint, Protocol,
                     ConnStatus, LastSyncTS, MaxGapMinutes, LastRecordCount,
                     RetryCount, LastErrorMsg, IsEnabled, CreatedBy, CreatedTS)
                VALUES (@C, @N, @D, @E, @P,
                        @S, DATEADD(MINUTE, -@G, SYSDATETIME()), @MG, @R,
                        @RT, @ER, 1, 'sys-seed', SYSDATETIME());
                """,
                ("@C", i.Code), ("@N", i.Name), ("@D", i.Dir), ("@E", i.Endpoint),
                ("@P", i.Proto), ("@S", i.Status), ("@G", i.Gap),
                ("@MG", 15), ("@R", i.Rec), ("@RT", i.Retry),
                ("@ER", (object?)i.Err ?? DBNull.Value));
        }
        Console.WriteLine($"  if    {ifs.Length} interface monitor rows");
    }

    // ── SYS-05 Audit log entries ────────────────────────────────────────
    private static void SeedAudit(SqlConnection conn)
    {
        var rng = new Random(3003);
        string[] modules = { "POP", "WH", "FG", "PP", "MNT", "QC", "SYS" };
        string[] actions = { "CREATE", "UPDATE", "DELETE", "LOGIN", "LOGOUT", "APPROVE", "REJECT" };
        string[] results = { "OK", "OK", "OK", "OK", "FAIL" };
        for (int i = 1; i <= 60; i++)
        {
            var m = modules[rng.Next(modules.Length)];
            var a = actions[rng.Next(actions.Length)];
            var r = results[rng.Next(results.Length)];
            var minutesAgo = rng.Next(1, 60 * 24 * 7);   // last 7d

            Exec(conn, """
                INSERT INTO dbo.SYS_AuditLog
                    (EventTS, ActorUserID, ModuleCode, ScreenCode, ActionType,
                     TargetEntity, TargetID, Result, IPAddress, Note,
                     CreatedBy, CreatedTS)
                VALUES (DATEADD(MINUTE, -@M, SYSDATETIME()),
                        (SELECT TOP 1 Id FROM dbo.AspNetUsers ORDER BY Id),
                        @MOD, @SC, @AC, @ENT, @TID, @R, @IP, @NT,
                        'sys-seed', SYSDATETIME());
                """,
                ("@M", minutesAgo),
                ("@MOD", m),
                ("@SC", $"{m}-{rng.Next(1, 10):D2}"),
                ("@AC", a),
                ("@ENT", a == "LOGIN" || a == "LOGOUT" ? "User"
                       : new[] { "WorkOrder", "Lot", "Stock", "MWO", "Failure" }[rng.Next(5)]),
                ("@TID", $"{rng.Next(1000, 9999)}"),
                ("@R", r),
                ("@IP", $"10.0.{rng.Next(1, 5)}.{rng.Next(10, 200)}"),
                ("@NT", a switch
                {
                    "LOGIN"  => "successful authentication",
                    "LOGOUT" => "session ended",
                    "DELETE" => "row removed by user",
                    "FAIL"   => "validation failed",
                    _        => ""
                }));
        }
        Console.WriteLine("  aud   60 audit log entries (last 7 days)");
    }

    // ── SYS-06 Notification rules & history ─────────────────────────────
    private static void SeedNotifRules(SqlConnection conn)
    {
        var rules = new (string Code, string Name, string Mod, bool On, string Ch, string Roles)[]
        {
            ("FAILURE_OPEN",  "Equipment failure opened", "MNT",  true,  "EMAIL,SMS",  "Maintenance,Supervisor"),
            ("PM_OVERDUE",    "PM schedule overdue",      "MNT",  true,  "EMAIL",      "Maintenance"),
            ("QC_HOLD",       "QC hold created",          "QC",   true,  "EMAIL,SMS",  "QC,Supervisor"),
            ("OTD_AT_RISK",   "Shipment at risk",         "FG",   true,  "EMAIL",      "Supervisor,Planner"),
            ("ANDON_RAISED",  "Andon call raised",        "POP",  true,  "PUSH,SMS",   "Supervisor,Maintenance"),
            ("LOW_STOCK",     "Spare part low stock",     "MNT",  true,  "EMAIL",      "Maintenance"),
            ("SAP_SYNC_FAIL", "SAP sync failed",          "SYS",  true,  "EMAIL,SMS",  "Admin"),
            ("PLC_DOWN",      "PLC interface down",       "SYS",  true,  "EMAIL,SMS",  "Admin,Maintenance"),
            ("DAILY_DIGEST",  "Daily KPI digest",         "RPT",  false, "EMAIL",      "Planner,Admin"),
        };
        foreach (var r in rules)
        {
            Exec(conn, """
                INSERT INTO dbo.SYS_NotificationRule
                    (EventTypeCode, EventName, SourceModule, IsEnabled,
                     ChannelsJSON, RecipientRolesJSON, CreatedBy, CreatedTS)
                VALUES (@C, @N, @M, @On, @Ch, @Ro, 'sys-seed', SYSDATETIME());
                """,
                ("@C", r.Code), ("@N", r.Name), ("@M", r.Mod), ("@On", r.On),
                ("@Ch", $"[\"{string.Join("\",\"", r.Ch.Split(','))}\"]"),
                ("@Ro", $"[\"{string.Join("\",\"", r.Roles.Split(','))}\"]"));
        }
        Console.WriteLine($"  rule  {rules.Length} notification rules");
    }

    private static void SeedNotifHistory(SqlConnection conn)
    {
        var rng = new Random(3004);
        string[] events = { "FAILURE_OPEN", "QC_HOLD", "ANDON_RAISED", "PLC_DOWN", "LOW_STOCK" };
        string[] channels = { "EMAIL", "SMS", "PUSH" };
        string[] statuses = { "SENT", "SENT", "SENT", "SENT", "FAILED" };
        int sent = 0;
        for (int i = 1; i <= 40; i++)
        {
            var ev = events[rng.Next(events.Length)];
            var ch = channels[rng.Next(channels.Length)];
            var st = statuses[rng.Next(statuses.Length)];
            var minutesAgo = rng.Next(1, 60 * 24);
            Exec(conn, """
                INSERT INTO dbo.SYS_NotificationHistory
                    (EventTypeCode, RecipientUserID, Channel, Address, Subject, Body,
                     Status, RetryCount, SentAt, ErrorMsg, CreatedBy, CreatedTS)
                VALUES (@E,
                        (SELECT TOP 1 Id FROM dbo.AspNetUsers ORDER BY Id),
                        @C, @A, @S, @B, @St, @R,
                        DATEADD(MINUTE, -@M, SYSDATETIME()),
                        @ER, 'sys-seed', SYSDATETIME());
                """,
                ("@E", ev), ("@C", ch),
                ("@A", ch == "EMAIL" ? "alerts@seh-usa.com"
                      : ch == "SMS"   ? "+1-555-0100" : "device-push-1"),
                ("@S", $"[A-MES] {ev}"),
                ("@B", "Auto-generated alert from rule engine"),
                ("@St", st),
                ("@R", st == "FAILED" ? rng.Next(1, 4) : 0),
                ("@M", minutesAgo),
                ("@ER", st == "FAILED" ? (object)"SMS gateway 503" : DBNull.Value));
            sent++;
        }
        Console.WriteLine($"  hist  {sent} notification history rows");
    }

    // ── SYS-07 Configuration ────────────────────────────────────────────
    private static void SeedConfig(SqlConnection conn)
    {
        var cfg = new (string Key, string Type, string Cat, string Val, string Code, string? Unit, int Sort)[]
        {
            ("DEFAULT_SHIFT_HOURS",  "DECIMAL", "Operations", "7.0",   "Net working hours per shift", "h", 10),
            ("PM_LOOKAHEAD_DAYS",    "INT",     "Maintenance", "30",   "PM lookahead window",         "d", 20),
            ("OTD_TARGET_PCT",       "DECIMAL", "KPI",        "95.0",  "On-time delivery target",     "%", 30),
            ("OEE_TARGET_PCT",       "DECIMAL", "KPI",        "85.0",  "OEE target",                  "%", 40),
            ("YIELD_TARGET_PCT",     "DECIMAL", "KPI",        "99.0",  "Yield target",                "%", 50),
            ("ALERT_REPEAT_MINUTES", "INT",     "Notifications","15",  "Repeat-alert interval",       "min",60),
            ("DASH_REFRESH_SEC",     "INT",     "UI",         "30",    "Dashboard refresh interval",  "s", 70),
            ("SAP_SYNC_INTERVAL_MIN","INT",     "Interfaces", "5",     "SAP sync interval",           "min",80),
            ("PLC_HEARTBEAT_SEC",    "INT",     "Interfaces", "10",    "PLC heartbeat",               "s", 90),
            ("SESSION_TIMEOUT_MIN",  "INT",     "Security",   "60",    "Session timeout",             "min",100),
            ("PASSWORD_MIN_LEN",     "INT",     "Security",   "8",     "Minimum password length",     null,110),
            ("PLANT_TIMEZONE",       "STRING",  "Operations", "America/Chicago", "Plant timezone",   null,120),
            ("LANGUAGE_DEFAULT",     "STRING",  "UI",         "en-US", "Default UI language",         null,130),
            ("MAX_RETRY_COUNT",      "INT",     "Interfaces", "3",     "Max retry per interface call",null,140),
            ("LOG_RETENTION_DAYS",   "INT",     "System",     "365",   "Audit log retention",         "d", 150),
        };
        foreach (var c in cfg)
        {
            Exec(conn, """
                INSERT INTO dbo.SYS_Config
                    (ConfigKey, ConfigType, Category, ConfigValue, CodeName, Unit,
                     SortOrder, IsActive, CreatedBy, CreatedTS)
                VALUES (@K, @T, @Ca, @V, @Cn, @U, @So, 1, 'sys-seed', SYSDATETIME());
                """,
                ("@K", c.Key), ("@T", c.Type), ("@Ca", c.Cat),
                ("@V", c.Val), ("@Cn", c.Code),
                ("@U", (object?)c.Unit ?? DBNull.Value), ("@So", c.Sort));
        }
        Console.WriteLine($"  cfg   {cfg.Length} config keys");
    }

    // ── helpers ─────────────────────────────────────────────────────────
    private static void Exec(SqlConnection conn, string sql, params (string Name, object Value)[] pars)
    {
        using var cmd = new SqlCommand(sql, conn);
        foreach (var (n, v) in pars) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
}
