using Microsoft.Data.SqlClient;

namespace AMES.Tools.SeedMntDemo;

/// <summary>
/// Seeds enough MNT rows for the 9 web screens to render meaningfully:
/// equipment status (per MD_Equipment), failure register events,
/// OEE log per equipment, mold shot counters, PM schedule + executions,
/// MWO (work orders) + tasks, spare parts master + transactions.
/// Idempotent: every row carries CreatedBy='mnt-seed' and is wiped on re-run.
/// </summary>
internal static class Program
{
    private const string Cs =
        "Server=localhost;Database=AMES_DEV;User Id=ames_app;Password=!Dev2026;" +
        "TrustServerCertificate=True;Encrypt=True;Connect Timeout=5;";

    private static int Main()
    {
        Console.WriteLine("[mnt-seed] connecting ...");
        using var conn = new SqlConnection(Cs);
        conn.Open();

        Wipe(conn);
        SeedEquipmentStatus(conn);
        SeedFailures(conn);
        SeedOeeLog(conn);
        SeedMoldShots(conn);
        SeedPmSchedules(conn);
        SeedMwo(conn);
        SeedSpareParts(conn);

        Console.WriteLine();
        Console.WriteLine("[mnt-seed] done.");
        return 0;
    }

    private static void Wipe(SqlConnection conn) => Exec(conn, """
        DELETE FROM dbo.MNT_SparePartsTxn   WHERE CreatedBy='mnt-seed';
        DELETE FROM dbo.MD_SparePart        WHERE CreatedBy='mnt-seed';
        DELETE FROM dbo.MNT_WorkOrderTask   WHERE CreatedBy='mnt-seed';
        DELETE FROM dbo.MNT_WorkOrder       WHERE CreatedBy='mnt-seed';
        DELETE FROM dbo.MNT_PMExecution     WHERE CreatedBy='mnt-seed';
        DELETE FROM dbo.MNT_PMSchedule      WHERE CreatedBy='mnt-seed';
        DELETE FROM dbo.MNT_MoldShotCount   WHERE CreatedBy='mnt-seed';
        DELETE FROM dbo.MNT_OEELog          WHERE CreatedBy='mnt-seed';
        DELETE FROM dbo.MNT_FailureAction   WHERE CreatedBy='mnt-seed';
        DELETE FROM dbo.MNT_FailureRegister WHERE CreatedBy='mnt-seed';
        DELETE FROM dbo.MNT_EquipmentStatus WHERE CreatedBy='mnt-seed';
        """);

    // ── MNT-01 Equipment status (one row per active equipment) ──────────
    private static void SeedEquipmentStatus(SqlConnection conn)
    {
        var rng = new Random(2026);
        var equips = ReadAllStrings(conn,
            "SELECT TOP 40 EquipID FROM dbo.MD_Equipment WHERE ISNULL(ActiveFlag,1)=1 ORDER BY EquipID");
        if (equips.Count == 0)
        {
            Console.WriteLine("  es    [skip] no equipment in MD_Equipment");
            return;
        }
        string[] states = { "RUN", "RUN", "RUN", "RUN", "IDLE", "DOWN", "SETUP" };
        var n = 0;
        foreach (var eq in equips)
        {
            var st = states[rng.Next(states.Length)];
            var oee = st == "RUN" ? 70 + rng.Next(20) : (st == "DOWN" ? 0 : 30 + rng.Next(30));
            var runHrs = 1200 + rng.Next(0, 4800);
            var cyc = 80_000 + rng.Next(0, 600_000);
            var nextPm = rng.Next(60) - 10;        // some overdue
            Exec(conn, """
                INSERT INTO dbo.MNT_EquipmentStatus
                    (EquipID, LineID, Status, TodayOEE, RuntimeHours, CycleCount,
                     NextPMDate, MountedMoldID, PLCConnTS, CreatedBy, CreatedTS)
                SELECT @E, e.LineID, @S, @O, @R, @C,
                       DATEADD(day, @P, CAST(SYSDATETIME() AS DATE)),
                       NULL, DATEADD(minute, -@M, SYSDATETIME()), 'mnt-seed', SYSDATETIME()
                FROM   dbo.MD_Equipment e
                WHERE  e.EquipID = @E;
                """,
                ("@E", eq), ("@S", st), ("@O", oee),
                ("@R", (decimal)runHrs), ("@C", (long)cyc),
                ("@P", nextPm), ("@M", rng.Next(2, 30)));
            n++;
        }
        Console.WriteLine($"  es    {n} equipment status rows");
    }

    // ── MNT-02 Failure register ─────────────────────────────────────────
    private static void SeedFailures(SqlConnection conn)
    {
        var rng = new Random(2027);
        var equips = ReadAllStrings(conn,
            "SELECT TOP 12 EquipID FROM dbo.MD_Equipment WHERE ISNULL(ActiveFlag,1)=1 ORDER BY EquipID");
        if (equips.Count == 0) return;

        var samples = new (string Type, string Sym, string Urg, string Src, string St, int HoursAgo)[]
        {
            ("MECHANICAL", "Hydraulic pressure fluctuation",     "HIGH",   "OPERATOR", "OPEN",        2),
            ("ELECTRICAL", "Servo amp E-stop tripped",            "URGENT", "ANDON",    "IN_PROGRESS", 6),
            ("TOOLING",    "Mold cooling water low flow",         "MED",    "PLC",      "RESOLVED",   28),
            ("MECHANICAL", "Clamp cylinder seal leak",            "MED",    "OPERATOR", "OPEN",       10),
            ("ELECTRICAL", "PLC IO module timeout",               "HIGH",   "PLC",      "IN_PROGRESS",15),
            ("QUALITY",    "Burn marks on flow front",            "LOW",    "QC",       "OPEN",        3),
            ("PROCESS",    "Cycle time drift +12 %",              "LOW",    "OPERATOR", "OPEN",       18),
            ("TOOLING",    "Mold venting blocked",                "MED",    "OPERATOR", "RESOLVED",   45),
            ("MECHANICAL", "Robot arm calibration drift",         "MED",    "ANDON",    "OPEN",       20),
            ("ELECTRICAL", "Heater band failure zone 4",          "URGENT", "PLC",      "IN_PROGRESS", 1),
            ("PROCESS",    "Short shot on cavity 3",              "MED",    "QC",       "OPEN",        8),
            ("MECHANICAL", "Ejector pin sticking",                "LOW",    "OPERATOR", "RESOLVED",   60),
        };
        int n = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            var s = samples[i];
            var eq = equips[i % equips.Count];
            Exec(conn, $"""
                INSERT INTO dbo.MNT_FailureRegister
                    (FailureNumber, EquipID, FailureType, Symptom, Urgency, Source, Status,
                     ReportedAt, ResolvedAt, CreatedBy, CreatedTS)
                VALUES (@N, @E, @T, @Sym, @U, @Sr, @St,
                        DATEADD(HOUR, -{s.HoursAgo}, SYSDATETIME()),
                        @R, 'mnt-seed', SYSDATETIME());
                """,
                ("@N", $"FAIL-{DateTime.Now:yyMM}-{i + 1:D3}"),
                ("@E", eq), ("@T", s.Type), ("@Sym", s.Sym),
                ("@U", s.Urg), ("@Sr", s.Src), ("@St", s.St),
                ("@R", s.St == "RESOLVED"
                    ? (object)DateTime.Now.AddHours(-s.HoursAgo + 4)
                    : DBNull.Value));
            n++;
        }
        Console.WriteLine($"  fail  {n} failure records (open/in-progress/resolved mix)");
    }

    // ── MNT-03 OEE Log per equipment (14 days × shift A/B) ─────────────
    private static void SeedOeeLog(SqlConnection conn)
    {
        var rng = new Random(2028);
        var equips = ReadAllStrings(conn,
            "SELECT TOP 8 EquipID FROM dbo.MD_Equipment WHERE ISNULL(ActiveFlag,1)=1 ORDER BY EquipID");
        if (equips.Count == 0) return;

        int n = 0;
        for (int d = 13; d >= 0; d--)
        {
            foreach (var eq in equips)
            {
                foreach (var shift in new[] { "A", "B" })
                {
                    var avail = 75 + rng.Next(20);
                    var perf  = 80 + rng.Next(15);
                    var qual  = 95 + rng.Next(5);
                    var oee   = avail * perf * qual / 10000.0;
                    var planned = 480;
                    var down = (int)(planned * (1.0 - avail / 100.0));
                    var total = 600 + rng.Next(80);
                    var good = (int)(total * qual / 100.0);

                    Exec(conn, $"""
                        INSERT INTO dbo.MNT_OEELog
                            (OEERecordNumber, EquipID, LineID, AggLevel, AggDate, ShiftCode,
                             PlannedTimeMin, DowntimeMin, Availability, Performance, Quality, OEE,
                             GoodQty, TotalQty, CreatedBy, CreatedTS)
                        SELECT @REC, @E, e.LineID, 'SHIFT',
                               DATEADD(DAY, -{d}, CAST(SYSDATETIME() AS DATE)),
                               @SH, {planned}, {down}, {avail}, {perf}, {qual}, {oee:0.00},
                               {good}, {total}, 'mnt-seed', SYSDATETIME()
                        FROM   dbo.MD_Equipment e WHERE e.EquipID = @E;
                        """,
                        ("@REC", $"OEE-{DateTime.Today.AddDays(-d):yyMMdd}-{eq}-{shift}"),
                        ("@E", eq), ("@SH", shift));
                    n++;
                }
            }
        }
        Console.WriteLine($"  oee   {n} OEE log rows (14d × {equips.Count} equip × 2 shifts)");
    }

    // ── MNT-04 Mold shot counters ───────────────────────────────────────
    private static void SeedMoldShots(SqlConnection conn)
    {
        var molds = ReadAllStrings(conn,
            "SELECT TOP 10 MoldID FROM dbo.MD_Mold ORDER BY MoldID");
        if (molds.Count == 0)
        {
            Console.WriteLine("  mold  [skip] no molds in MD_Mold");
            return;
        }
        var rng = new Random(2029);
        int n = 0;
        foreach (var m in molds)
        {
            var life = 800_000 + rng.Next(0, 400_000);
            var cur  = rng.Next(0, life + 50_000);
            var thr  = cur > life * 0.9 ? "CRITICAL"
                      : cur > life * 0.7 ? "WARNING" : "OK";
            var st   = thr == "CRITICAL" ? "DUE_REFURBISH" : "ACTIVE";
            Exec(conn, """
                INSERT INTO dbo.MNT_MoldShotCount
                    (MoldID, CurrentShots, LifetimeShots, Status, ThresholdLevel,
                     RefurbishCount, CreatedBy, CreatedTS)
                VALUES (@M, @C, @L, @S, @T, @R, 'mnt-seed', SYSDATETIME());
                """,
                ("@M", m), ("@C", cur), ("@L", life),
                ("@S", st), ("@T", thr), ("@R", rng.Next(0, 4)));
            n++;
        }
        Console.WriteLine($"  mold  {n} mold shot counters");
    }

    // ── MNT-05 PM Schedules ─────────────────────────────────────────────
    private static void SeedPmSchedules(SqlConnection conn)
    {
        var rng = new Random(2030);
        var equips = ReadAllStrings(conn,
            "SELECT TOP 15 EquipID FROM dbo.MD_Equipment WHERE ISNULL(ActiveFlag,1)=1 ORDER BY EquipID");
        if (equips.Count == 0) return;

        var types = new[] { "DAILY", "WEEKLY", "MONTHLY", "QUARTERLY", "ANNUAL" };
        var bases = new[] { "TIME",  "TIME",   "CYCLE",   "TIME",      "TIME"   };
        var cycles = new[] { 1,        7,       50_000,    90,           365     };

        int n = 0;
        for (int i = 0; i < equips.Count; i++)
        {
            var eq = equips[i];
            var ti = i % types.Length;
            var due = rng.Next(-3, 25);   // some overdue
            var st  = due < 0 ? "OVERDUE" : (due <= 3 ? "DUE" : "OK");
            Exec(conn, $"""
                INSERT INTO dbo.MNT_PMSchedule
                    (PMPlanNumber, EquipID, PMType, CycleBasis, CycleValue,
                     LastPMDate, NextDueDate, Status, CreatedBy, CreatedTS)
                VALUES (@P, @E, @T, @B, {cycles[ti]},
                        DATEADD(DAY, -{rng.Next(2, 60)}, CAST(SYSDATETIME() AS DATE)),
                        DATEADD(DAY,  {due},             CAST(SYSDATETIME() AS DATE)),
                        @S, 'mnt-seed', SYSDATETIME());
                """,
                ("@P", $"PM-{DateTime.Today:yyMM}-{i + 1:D3}"),
                ("@E", eq), ("@T", types[ti]), ("@B", bases[ti]), ("@S", st));
            n++;
        }
        Console.WriteLine($"  pm    {n} PM schedules");
    }

    // ── MNT-07 MWO (Maintenance Work Order) + tasks ─────────────────────
    private static void SeedMwo(SqlConnection conn)
    {
        var rng = new Random(2031);
        var equips = ReadAllStrings(conn,
            "SELECT TOP 10 EquipID FROM dbo.MD_Equipment WHERE ISNULL(ActiveFlag,1)=1 ORDER BY EquipID");
        if (equips.Count == 0) return;

        var samples = new (string Typ, string Pr, string Src, string St, int HoursAgo, int Lab)[]
        {
            ("CM",  "HIGH",   "FAILURE", "IN_PROGRESS", 3,  90),
            ("PM",  "MED",    "PM",      "ISSUED",      6,   0),
            ("CM",  "URGENT", "ANDON",   "IN_PROGRESS", 2, 120),
            ("PM",  "LOW",    "PM",      "COMPLETED",  30, 180),
            ("CM",  "MED",    "FAILURE", "COMPLETED",  48, 240),
            ("PdM", "LOW",    "MANUAL",  "ISSUED",     12,   0),
            ("CM",  "HIGH",   "ANDON",   "OPEN",        1,   0),
            ("PM",  "MED",    "PM",      "COMPLETED",  72, 150),
        };
        int n = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            var s  = samples[i];
            var eq = equips[i % equips.Count];

            Exec(conn, $"""
                INSERT INTO dbo.MNT_WorkOrder
                    (WoNumber, WoType, EquipID, Priority, SourceType, Status,
                     IssuedAt, StartedAt, CompletedAt, LaborMinutes, ActionDesc,
                     CreatedBy, CreatedTS)
                VALUES (@N, @T, @E, @P, @Sr, @St,
                        DATEADD(HOUR, -{s.HoursAgo},     SYSDATETIME()),
                        @SA,
                        @CA,
                        {s.Lab},
                        @AD, 'mnt-seed', SYSDATETIME());
                """,
                ("@N",  $"MWO-{DateTime.Today:yyMM}-{i + 1:D3}"),
                ("@T",  s.Typ), ("@E", eq), ("@P", s.Pr), ("@Sr", s.Src), ("@St", s.St),
                ("@SA", s.St == "ISSUED" || s.St == "OPEN"
                    ? DBNull.Value : (object)DateTime.Now.AddHours(-s.HoursAgo + 1)),
                ("@CA", s.St == "COMPLETED"
                    ? (object)DateTime.Now.AddHours(-s.HoursAgo + 4) : DBNull.Value),
                ("@AD", $"{s.Typ} on {eq} — {s.Src.ToLower()}-triggered"));

            // Pull back the inserted WoID by WoNumber
            int woId = (int)ExecScalar(conn,
                "SELECT WorkOrderID FROM dbo.MNT_WorkOrder WHERE WoNumber=@N",
                ("@N", $"MWO-{DateTime.Today:yyMM}-{i + 1:D3}"));

            // 3 task rows per WO
            var done = s.St == "COMPLETED";
            for (int t = 1; t <= 3; t++)
            {
                var result = done ? "PASS" : (s.St == "IN_PROGRESS" && t == 1 ? "PASS" : "PENDING");
                Exec(conn, """
                    INSERT INTO dbo.MNT_WorkOrderTask
                        (WorkOrderID, TaskSeq, TaskName, TaskType, Result, Note,
                         CompletedAt, CreatedBy, CreatedTS)
                    VALUES (@W, @SQ, @TN, 'CHECK', @R, NULL,
                            @CA, 'mnt-seed', SYSDATETIME());
                    """,
                    ("@W", woId), ("@SQ", t),
                    ("@TN", t == 1 ? "Visual inspection"
                          : t == 2 ? "Parameter check"
                          :          "Functional test"),
                    ("@R", result),
                    ("@CA", result == "PASS"
                        ? (object)DateTime.Now.AddHours(-s.HoursAgo + t)
                        : DBNull.Value));
            }
            n++;
        }
        Console.WriteLine($"  mwo   {n} maintenance work orders + {n * 3} tasks");
    }

    // ── MNT-08 Spare parts master + transactions ────────────────────────
    private static void SeedSpareParts(SqlConnection conn)
    {
        var parts = new (string No, string Name, string Cat, int Safety, int Reorder, int Lead, decimal Cost, string Loc)[]
        {
            ("SP-BRG-6204", "Ball bearing 6204",     "BRG",  20,  30, 14,    8.50m, "WH-MNT-A1"),
            ("SP-BRG-6206", "Ball bearing 6206",     "BRG",  15,  25, 14,   11.20m, "WH-MNT-A1"),
            ("SP-SEAL-32",  "Cylinder seal Ø32",     "SEAL", 30,  50,  7,    2.40m, "WH-MNT-A2"),
            ("SP-SEAL-50",  "Cylinder seal Ø50",     "SEAL", 25,  40,  7,    3.60m, "WH-MNT-A2"),
            ("SP-FLT-HYD",  "Hydraulic filter",      "FLT",  10,  20, 21,   38.00m, "WH-MNT-B1"),
            ("SP-FLT-AIR",  "Air filter cart",       "FLT",  12,  18, 14,   18.00m, "WH-MNT-B1"),
            ("SP-HTR-2KW",  "Heater band 2kW",       "ELE",   8,  12, 28,   54.00m, "WH-MNT-C1"),
            ("SP-SENS-PT100","Temp sensor PT100",    "ELE",   6,  10, 21,   32.00m, "WH-MNT-C1"),
            ("SP-MOT-1HP",  "Servo motor 1HP",       "ELE",   2,   3, 45,  680.00m, "WH-MNT-C2"),
            ("SP-OIL-46",   "Hydraulic oil ISO 46",  "LUB",  50, 100,  3,    4.20m, "WH-MNT-D1"),
            ("SP-GREASE-EP","Grease EP-2 cart",      "LUB",  20,  35,  3,    6.80m, "WH-MNT-D1"),
            ("SP-FUSE-25A", "Fuse 25A NH00",         "ELE",  40,  60,  7,    1.20m, "WH-MNT-C3"),
        };
        foreach (var p in parts)
        {
            Exec(conn, """
                INSERT INTO dbo.MD_SparePart
                    (PartNo, PartName, Category, UnitCost, UOM,
                     SafetyStock, ReorderPoint, ReorderQty, LeadTimeDays,
                     StorageLoc, ActiveFlag, CreatedBy, CreatedTS)
                VALUES (@N, @Nm, @C, @UC, 'EA',
                        @SS, @RP, @RQ, @LT, @SL, 1, 'mnt-seed', SYSDATETIME());
                """,
                ("@N", p.No), ("@Nm", p.Name), ("@C", p.Cat), ("@UC", p.Cost),
                ("@SS", p.Safety), ("@RP", p.Reorder), ("@RQ", p.Reorder * 2),
                ("@LT", p.Lead), ("@SL", p.Loc));
        }

        // initial stock + a couple of issue transactions per part
        var rng = new Random(2032);
        int tx = 0;
        foreach (var p in parts)
        {
            var open = p.Reorder + rng.Next(-5, 30);   // some below RP
            // initial receipt
            Exec(conn, """
                INSERT INTO dbo.MNT_SparePartsTxn
                    (PartNo, PartName, Category, MoveType, Qty, BalanceAfter, UnitPrice,
                     StorageLoc, RefType, RefID, TxnAt, Note, CreatedBy, CreatedTS)
                VALUES (@N, @Nm, @C, 'IN', @Q, @Q, @UC, @SL, 'PO', @REF,
                        DATEADD(DAY, -30, SYSDATETIME()), 'opening receipt',
                        'mnt-seed', SYSDATETIME());
                """,
                ("@N", p.No), ("@Nm", p.Name), ("@C", p.Cat),
                ("@Q", open + 30), ("@UC", p.Cost), ("@SL", p.Loc),
                ("@REF", $"PO-2026-{rng.Next(100, 999)}"));
            tx++;

            // 1–2 issue transactions
            int bal = open + 30;
            for (int i = 0; i < 2; i++)
            {
                var q = rng.Next(2, 15);
                bal -= q;
                Exec(conn, """
                    INSERT INTO dbo.MNT_SparePartsTxn
                        (PartNo, PartName, Category, MoveType, Qty, BalanceAfter, UnitPrice,
                         StorageLoc, RefType, RefID, TxnAt, Note, CreatedBy, CreatedTS)
                    VALUES (@N, @Nm, @C, 'OUT', @Q, @B, @UC, @SL, 'MWO', @REF,
                            DATEADD(HOUR, -@H, SYSDATETIME()), 'issued to MWO',
                            'mnt-seed', SYSDATETIME());
                    """,
                    ("@N", p.No), ("@Nm", p.Name), ("@C", p.Cat),
                    ("@Q", q), ("@B", bal), ("@UC", p.Cost),
                    ("@SL", p.Loc),
                    ("@REF", $"MWO-{DateTime.Today:yyMM}-{rng.Next(1, 8):D3}"),
                    ("@H", rng.Next(1, 120)));
                tx++;
            }
        }
        Console.WriteLine($"  sp    {parts.Length} spare parts + {tx} stock txns");
    }

    // ── helpers ─────────────────────────────────────────────────────────
    private static void Exec(SqlConnection conn, string sql, params (string Name, object Value)[] pars)
    {
        using var cmd = new SqlCommand(sql, conn);
        foreach (var (n, v) in pars) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
    private static object ExecScalar(SqlConnection conn, string sql, params (string Name, object Value)[] pars)
    {
        using var cmd = new SqlCommand(sql, conn);
        foreach (var (n, v) in pars) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        return cmd.ExecuteScalar() ?? DBNull.Value;
    }
    private static List<string> ReadAllStrings(SqlConnection conn, string sql)
    {
        var list = new List<string>();
        using var cmd = new SqlCommand(sql, conn);
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read()) list.Add(rdr.GetString(0));
        return list;
    }
}
