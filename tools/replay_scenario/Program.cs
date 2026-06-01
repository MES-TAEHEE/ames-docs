using Microsoft.Data.SqlClient;

namespace AMES.Tools.ReplayScenario;

/// <summary>
/// End-to-end scenario replays. Each scenario inserts a coherent story
/// into the DB so a demoer can log in immediately and walk through the
/// resulting screens.
///
/// Usage:
///   dotnet run --project tools/replay_scenario              # list
///   dotnet run --project tools/replay_scenario andon
///   dotnet run --project tools/replay_scenario qc-escalation
///   dotnet run --project tools/replay_scenario pnt-full
///   dotnet run --project tools/replay_scenario mold-change
///   dotnet run --project tools/replay_scenario morning-ramp
///   dotnet run --project tools/replay_scenario all           # all five
///   dotnet run --project tools/replay_scenario clean         # wipe scenarios
///
/// Every scenario rows uses CreatedBy = 'scenario:&lt;name&gt;' so a re-run
/// (or `clean`) wipes its own footprint without touching the foundation
/// seeds.
/// </summary>
internal static class Program
{
    private const string Cs =
        "Server=localhost;Database=AMES_DEV;User Id=ames_app;Password=!Dev2026;" +
        "TrustServerCertificate=True;Encrypt=True;Connect Timeout=5;";

    private static readonly Dictionary<string, Action<SqlConnection>> Scenarios = new()
    {
        ["andon"]          = AndonFlow,
        ["qc-escalation"]  = QcEscalation,
        ["pnt-full"]       = PntFullCycle,
        ["mold-change"]    = MoldChangeInProgress,
        ["morning-ramp"]   = MorningRamp,
    };

    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Available scenarios:");
            foreach (var k in Scenarios.Keys) Console.WriteLine($"  {k}");
            Console.WriteLine("  all       (run all five)");
            Console.WriteLine("  clean     (wipe every scenario row)");
            return 0;
        }

        using var conn = new SqlConnection(Cs);
        conn.Open();

        var name = args[0].ToLowerInvariant();
        if (name == "clean") { CleanAll(conn); return 0; }
        if (name == "all")
        {
            foreach (var (k, fn) in Scenarios)
            {
                Console.WriteLine($"▶ {k}");
                CleanOne(conn, k);
                fn(conn);
                Console.WriteLine();
            }
            return 0;
        }

        if (!Scenarios.TryGetValue(name, out var run))
        {
            Console.WriteLine($"Unknown scenario '{name}'. Run without args to list.");
            return 1;
        }

        Console.WriteLine($"▶ {name}");
        CleanOne(conn, name);
        run(conn);
        Console.WriteLine();
        return 0;
    }

    // ── Scenario 1: ANDON flow ───────────────────────────────────────────
    private static void AndonFlow(SqlConnection conn)
    {
        var equipId = ScalarStr(conn,
            "SELECT TOP 1 EquipID FROM dbo.MD_Equipment WHERE LineID='LINE-INJ-01' ORDER BY EquipID;");

        // 3-stage story: raised 90 s ago → acked 60 s ago → still running (not yet resolved)
        Exec(conn, """
            INSERT INTO dbo.PR_AndonCall
                (LineID, EquipID, TriggerSource, Severity, TriggeredAt, AckedBy, AckedAt,
                 ReasonCode, CorrectiveAction, ResumedAt, DowntimeSec, Status, CreatedBy, CreatedTS)
            VALUES ('LINE-INJ-01', @E, 'OPERATOR', 'HIGH',
                    DATEADD(second, -90, SYSDATETIME()),
                    'user-s001',
                    DATEADD(second, -60, SYSDATETIME()),
                    'MACHINE', N'Supervisor on site, inspecting',
                    NULL, NULL, 'Acked', 'scenario:andon', SYSDATETIME());
            """, ("@E", equipId));

        Console.WriteLine("  → ANDON raised 90 s ago, supervisor acked 60 s ago");
        Console.WriteLine("    Visit INJ-08 to see the ACKED state, then press Resume to end the flow.");
    }

    // ── Scenario 2: QC escalation chain ──────────────────────────────────
    private static void QcEscalation(SqlConnection conn)
    {
        var woId = Scalar<int>(conn, """
            SELECT TOP 1 WoID FROM dbo.PP_WorkOrder
            WHERE LineID='LINE-IMG-01' ORDER BY WoID;
            """);
        var lotId = Scalar<int>(conn, """
            SELECT TOP 1 LotID FROM dbo.tbl_Lot WHERE WoID = @W;
            """, ("@W", woId));

        // 1. Defect noticed on IMG-05 12 min ago
        Exec(conn, """
            INSERT INTO dbo.PR_DefectDetail
                (WoID, LotID, ProcessCode, DefectCode, Qty, ReasonNote,
                 DetectedAt, RegisteredBy, CreatedBy, CreatedTS)
            VALUES (@W, @L, 'IMG', 'IMG-D04', 6, N'Adhesion below 4 N/mm',
                    DATEADD(minute,-12,SYSDATETIME()), 'user-i001',
                    'scenario:qc-escalation', SYSDATETIME());
            """, ("@W", woId), ("@L", lotId));

        // 2. QC In-Process inspection 10 min ago → FAIL
        Exec(conn, """
            INSERT INTO dbo.QC_Inspection
                (InspectionNo, InspectionType, LotID, ItemNo, Mode,
                 SampleSize, BatchQty, CumulativeGood, DefectQtyTotal,
                 Verdict, CriticalFlag, InspectorID,
                 InsStartTS, InsEndTS, CreatedBy, CreatedTS)
            VALUES (CONCAT('IP-SCN-', FORMAT(SYSDATETIME(),'yyMMddHHmm')),
                    'InProcess', @L, 'DR-TRM-LH-A1', 'Enhanced',
                    20, 96, 14, 6, 'FAIL', 1, 'user-q001',
                    DATEADD(minute,-10,SYSDATETIME()),
                    DATEADD(minute, -8,SYSDATETIME()),
                    'scenario:qc-escalation', SYSDATETIME());
            """, ("@L", lotId));

        // 3. NCR raised 7 min ago
        Exec(conn, """
            INSERT INTO dbo.QC_NCR
                (NcrNumber, SourceType, SourceID, Severity, ItemNo, AffectedQty,
                 Disposition, Status, ReportedBy, ReportedAt, CreatedBy, CreatedTS)
            VALUES (CONCAT('NCR-SCN-', FORMAT(SYSDATETIME(),'yyMMddHHmm')),
                    'INSPECTION', 'IP-SCN', 'Major', 'DR-TRM-LH-A1', 96, 'HOLD',
                    'Open', 'user-q001', DATEADD(minute,-7,SYSDATETIME()),
                    'scenario:qc-escalation', SYSDATETIME());
            """);
        var ncrId = Scalar<int>(conn, """
            SELECT TOP 1 NcrID FROM dbo.QC_NCR WHERE CreatedBy='scenario:qc-escalation' ORDER BY NcrID DESC;
            """);

        // 4. Hold created 5 min ago
        Exec(conn, """
            INSERT INTO dbo.QC_Hold
                (HoldNumber, SourceNcrID, Severity, AffectedType, LotID,
                 ItemNo, HeldQty, PhysicalLocation, Status, HeldBy, HeldAt,
                 CreatedBy, CreatedTS)
            VALUES (CONCAT('HLD-SCN-', FORMAT(SYSDATETIME(),'yyMMddHHmm')),
                    @N, 'Major', 'LOT', @L, 'DR-TRM-LH-A1', 96, 'BAY-Q-03',
                    'Held', 'user-q001', DATEADD(minute,-5,SYSDATETIME()),
                    'scenario:qc-escalation', SYSDATETIME());
            """, ("@N", ncrId), ("@L", lotId));

        Console.WriteLine("  → IMG defect IMG-D04 logged 12 min ago");
        Console.WriteLine("  → QC IP inspection FAILED 10 min ago");
        Console.WriteLine("  → NCR raised 7 min ago, Hold opened 5 min ago — awaiting disposition");
        Console.WriteLine("    Visit QC-07 (dashboard), QC-04 (NCR), QC-05 (Hold) to walk the chain.");
    }

    // ── Scenario 3: PNT full cycle (one lot through every stage) ─────────
    private static void PntFullCycle(SqlConnection conn)
    {
        var planId = Scalar<int>(conn,
            "SELECT TOP 1 PlanID FROM dbo.PNT_DailyPlan WHERE PlanDate=CAST(GETDATE() AS DATE);");
        if (planId == 0) { Console.WriteLine("  skipped — no PNT plan today (run seed_pnt_demo first)"); return; }

        const string jigId = "JIG-007";

        // PRE → BOUND → LOADED → OVEN → CONFIRMED → LABELED over the last 30 min
        Exec(conn, """
            INSERT INTO dbo.PNT_VirtualLot
                (PlanID, JigID, ItemNo, RalColor, TargetQty, LoadedQty,
                 ConfirmedQty, DefectQty, Status, EnhancedInspection,
                 IssuedAt, IssuedBy, BindAt, BindReason,
                 CreatedBy, CreatedTS)
            VALUES (@P, @J, 'DR-TRM-LH-A1', 'RAL 9005', 32, 32,
                    30, 2, 'LABELED', 0,
                    DATEADD(minute,-30,SYSDATETIME()), 'user-p001',
                    DATEADD(minute,-29,SYSDATETIME()), 'PDA',
                    'scenario:pnt-full', SYSDATETIME());
            """, ("@P", planId), ("@J", jigId));
        var virtualLotId = Scalar<int>(conn, """
            SELECT TOP 1 VirtualLotID FROM dbo.PNT_VirtualLot
            WHERE CreatedBy='scenario:pnt-full' ORDER BY VirtualLotID DESC;
            """);

        // R1/R2/R3 events
        var passes = new[] { ("R1-LOAD", -28), ("R2-OVENIN", -24), ("R3-UNLOAD", -6) };
        foreach (var (reader, minAgo) in passes)
        {
            Exec(conn, $"""
                INSERT INTO dbo.PNT_LineEvent
                    (TagID, JigID, LotID, ReaderID, AntennaPort, TagRole,
                     EventTS, Rssi, ReadCount, TriggerType, CreatedBy, CreatedTS)
                VALUES (NULL, @J, NULL, @R, 1, 'JIG',
                        DATEADD(minute, {minAgo}, SYSDATETIME()), -42, 1, 'PE',
                        'scenario:pnt-full', SYSDATETIME());
                """, ("@J", jigId), ("@R", reader));
        }

        // PNT_JigLoad
        Exec(conn, """
            INSERT INTO dbo.PNT_JigLoad
                (JigID, LotID, LoadedQty, OperatorID, PdaScanAt, R1ReadAt,
                 MatchStatus, LineID, CreatedBy, CreatedTS)
            VALUES (@J, NULL, 32, 'user-p001',
                    DATEADD(minute,-28,SYSDATETIME()),
                    DATEADD(minute,-28,SYSDATETIME()),
                    'OK', 'LINE-PNT-01', 'scenario:pnt-full', SYSDATETIME());
            """, ("@J", jigId));

        // PNT_OvenLog (entry 24 min ago, exit 9 min ago, 15-min dwell)
        Exec(conn, """
            INSERT INTO dbo.PNT_OvenLog
                (OvenID, JigID, LotID, EntryTS, ExitTS, DwellSec,
                 MinTemp, MaxTemp, AvgTemp, WithinSpec,
                 CreatedBy, CreatedTS)
            VALUES ('OVEN-01', @J, NULL,
                    DATEADD(minute,-24,SYSDATETIME()),
                    DATEADD(minute, -9,SYSDATETIME()),
                    900, 178.5, 181.7, 180.1, 1,
                    'scenario:pnt-full', SYSDATETIME());
            """, ("@J", jigId));

        Console.WriteLine("  → Issued + bound JIG-007 30 min ago");
        Console.WriteLine("  → R1 28 min · R2 24 min · oven entry 24 min, exit 9 min · R3 6 min");
        Console.WriteLine("  → Lot status LABELED, 30 OK / 2 NG");
        Console.WriteLine("    Walk PNT-02 → PNT-03 → PNT-04 (events) → PNT-05 → PNT-06 → PNT-07.");
    }

    // ── Scenario 4: Mold change in progress (right now) ──────────────────
    private static void MoldChangeInProgress(SqlConnection conn)
    {
        var equipId = ScalarStr(conn,
            "SELECT TOP 1 EquipID FROM dbo.MD_Equipment WHERE LineID='LINE-INJ-01' ORDER BY EquipID;");
        var moldA   = ScalarStr(conn, "SELECT TOP 1 MoldID FROM dbo.MD_Mold ORDER BY MoldID;");
        var moldB   = ScalarStr(conn, "SELECT TOP 1 MoldID FROM dbo.MD_Mold ORDER BY MoldID DESC;");

        // Started 4 min ago, not yet completed (CompletedAt = null) → INJ-06 timer ticking
        Exec(conn, """
            INSERT INTO dbo.PR_MoldChange
                (EquipID, LineID, OldMoldID, NewMoldID, OldMoldFinalShots,
                 NewMoldStartShots, Reason, DowntimeMin, StartedAt, CompletedAt,
                 ChangedBy, CreatedBy, CreatedTS)
            VALUES (@E, 'LINE-INJ-01', @A, @B, 9876, 0, 'Scheduled', NULL,
                    DATEADD(minute, -4, SYSDATETIME()), NULL,
                    'user-e001', 'scenario:mold-change', SYSDATETIME());
            """, ("@E", equipId), ("@A", moldA), ("@B", moldB));

        Console.WriteLine($"  → Mold change {moldA} → {moldB} started 4 min ago, still running");
        Console.WriteLine("    Visit INJ-06 to see the SMED timer mid-flow.");
    }

    // ── Scenario 5: Morning ramp-up (fresh hour of cycles) ───────────────
    private static void MorningRamp(SqlConnection conn)
    {
        var woId = Scalar<int>(conn, """
            SELECT TOP 1 WoID FROM dbo.PP_WorkOrder
            WHERE LineID='LINE-INJ-01' ORDER BY WoID;
            """);
        var lotId = Scalar<int>(conn, "SELECT TOP 1 LotID FROM dbo.tbl_Lot WHERE WoID=@W;", ("@W", woId));
        var moldId = ScalarStr(conn, "SELECT TOP 1 MoldID FROM dbo.MD_Mold ORDER BY MoldID;");

        // 20 cycles distributed across the last 60 minutes
        var rng = new Random(2026);
        for (var i = 0; i < 20; i++)
        {
            var minAgo = 60 - i * 3 + rng.Next(-1, 2);
            var sec = 40 + rng.Next(-2, 5);
            Exec(conn, """
                INSERT INTO dbo.PR_ProductionResult
                    (EntryNo, WoID, LotID, LineID, ProcessCode, GoodQty,
                     CycleSec, MoldID, OperatorID, DefectFlag, EntryAt,
                     CreatedBy, CreatedTS)
                VALUES (CONCAT('SCN-', FORMAT(SYSDATETIME(),'yyMMddHHmmss'),'-',@C),
                        @W, @L, 'LINE-INJ-01', 'INJ', 1, @S, @M,
                        'user-e001', 0,
                        DATEADD(minute, -@A, SYSDATETIME()),
                        'scenario:morning-ramp', SYSDATETIME());
                """,
                ("@C", i.ToString()), ("@W", woId), ("@L", lotId),
                ("@S", sec), ("@M", moldId), ("@A", minAgo));
        }
        Console.WriteLine("  → 20 fresh INJ cycles spread across the last 60 minutes");
        Console.WriteLine("    Visit INJ-02 to watch the hourly chart fill in.");
    }

    // ── Cleanup ──────────────────────────────────────────────────────────
    private static void CleanOne(SqlConnection conn, string name)
    {
        var tag = $"scenario:{name}";
        var tables = new[]
        {
            "PR_AndonCall","PR_DefectDetail","PR_MoldChange","PR_ProductionResult",
            "QC_InspectionItem","QC_Inspection","QC_NCR","QC_Hold","QC_HoldRelease",
            "PNT_VirtualLot","PNT_LineEvent","PNT_JigLoad","PNT_OvenLog"
        };
        foreach (var t in tables)
            Exec(conn, $"DELETE FROM dbo.{t} WHERE CreatedBy = @T;", ("@T", tag));
    }

    private static void CleanAll(SqlConnection conn)
    {
        Console.WriteLine("Wiping all scenario rows ...");
        foreach (var k in Scenarios.Keys) CleanOne(conn, k);
        Console.WriteLine("Done.");
    }

    // ── helpers ──────────────────────────────────────────────────────────
    private static void Exec(SqlConnection conn, string sql, params (string Name, object Value)[] pars)
    {
        using var cmd = new SqlCommand(sql, conn);
        foreach (var (n, v) in pars) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
    private static T Scalar<T>(SqlConnection conn, string sql, params (string Name, object Value)[] pars) where T : struct
    {
        using var cmd = new SqlCommand(sql, conn);
        foreach (var (n, v) in pars) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        var v2 = cmd.ExecuteScalar();
        return v2 is null || v2 is DBNull ? default : (T)Convert.ChangeType(v2, typeof(T));
    }
    private static string ScalarStr(SqlConnection conn, string sql, params (string Name, object Value)[] pars)
    {
        using var cmd = new SqlCommand(sql, conn);
        foreach (var (n, v) in pars) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        var v2 = cmd.ExecuteScalar();
        return v2 is null || v2 is DBNull ? string.Empty : v2.ToString() ?? string.Empty;
    }
}
