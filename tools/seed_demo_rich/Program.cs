using Microsoft.Data.SqlClient;

namespace AMES.Tools.SeedDemoRich;

/// <summary>
/// Demo enrichment seed — runs on top of seed_inj_demo / seed_img_demo /
/// seed_pnt_demo / seed_qc_demo and adds the moving parts that make every
/// screen feel alive when the user logs in to inspect:
///
///   INJ
///     ~ 40 PR_ProductionResult spread across this morning (06h–12h) with
///       realistic ramp-up and a defect cluster around 09h
///     6 PR_DefectDetail (3 codes, mixed seq numbers)
///     2 PR_MoldChange in the last 24 h
///     3 PR_AndonCall (RESOLVED 4 h ago, ACKED 90 min ago, OPEN now)
///
///   IMG
///     2 PR_BondSetup audit rows (recipe v2.0 → v2.1)
///
///   PNT
///     Promote the 6 pnt-seed virtual lots through every status:
///       LOT 1 → LABELED   (full cycle done)
///       LOT 2 → CONFIRMED (unloaded waiting label)
///       LOT 3 → OVEN      (curing)
///       LOT 4 → LOADED    (jig bound + R1 pass)
///       LOT 5 → BOUND     (waits in queue)
///       LOT 6 → PRE       (operator hasn't bound yet)
///     PNT_LineEvent rows that show each lot crossing the matching
///     readers (R1 for LOADED+, R2 for OVEN+, R3 for CONFIRMED+)
///
///   QC
///     6 extra QC_Inspection spread across yesterday + today
///     1 Closed Hold (released with USE-AS-IS 2 h ago)
///     2 extra QC_CAPA_Action rows for CAPA-25060001 (D+3 done, D+5 pass)
///
/// Re-run safe: deletes the previous `enrich` rows first.
/// </summary>
internal static class Program
{
    private const string Cs =
        "Server=localhost;Database=AMES_DEV;User Id=ames_app;Password=!Dev2026;" +
        "TrustServerCertificate=True;Encrypt=True;Connect Timeout=5;";

    private static readonly Random Rng = new(2026_06_01);

    private static int Main()
    {
        Console.WriteLine("[enrich] connecting ...");
        using var conn = new SqlConnection(Cs);
        conn.Open();

        Wipe(conn);

        EnrichInj (conn);
        EnrichImg (conn);
        EnrichPnt (conn);
        EnrichQc  (conn);

        Console.WriteLine();
        Console.WriteLine("[enrich] done. Each module now has a living picture.");
        return 0;
    }

    private static void Wipe(SqlConnection conn) => Exec(conn, """
        DELETE FROM dbo.PR_DefectDetail    WHERE CreatedBy='enrich';
        DELETE FROM dbo.PR_ProductionResult WHERE CreatedBy='enrich';
        DELETE FROM dbo.PR_MoldChange      WHERE CreatedBy='enrich';
        DELETE FROM dbo.PR_AndonCall       WHERE CreatedBy='enrich';
        DELETE FROM dbo.PR_BondSetupAudit  WHERE CreatedBy='enrich';
        DELETE FROM dbo.PR_BondSetup       WHERE CreatedBy='enrich';
        DELETE FROM dbo.PNT_LineEvent      WHERE CreatedBy='enrich';
        DELETE FROM dbo.QC_InspectionItem  WHERE CreatedBy='enrich';
        DELETE FROM dbo.QC_Inspection      WHERE CreatedBy='enrich';
        DELETE FROM dbo.QC_HoldRelease     WHERE CreatedBy='enrich';
        DELETE FROM dbo.QC_CAPA_Action     WHERE CreatedBy='enrich';
        """);

    // ── INJ ──────────────────────────────────────────────────────────────
    private static void EnrichInj(SqlConnection conn)
    {
        // Find the active INJ-01 WO (seed_inj_demo creates at least one)
        var woId = ExecScalar<int>(conn, """
            SELECT TOP 1 WoID FROM dbo.PP_WorkOrder
            WHERE LineID = 'LINE-INJ-01'
            ORDER BY CASE WHEN Status='Released' THEN 0 ELSE 1 END, WoID;
            """);
        if (woId == 0) { Console.WriteLine("  inj  skipped — no WO"); return; }

        var lotId = ExecScalar<int>(conn, """
            SELECT TOP 1 LotID FROM dbo.tbl_Lot
            WHERE WoID = @W ORDER BY LotID DESC;
            """, ("@W", woId));

        var moldId = ExecScalarStr(conn, "SELECT TOP 1 MoldID FROM dbo.MD_Mold ORDER BY MoldID;");

        // Today 06:00 → now() PR_ProductionResult, ~18/h ramp from 12 to 22 cycles per hour
        var startToday = DateTime.Today.AddHours(6);
        var rowCount = 0;
        for (var hour = 0; hour < 6; hour++)
        {
            var cyclesThisHour = 12 + hour * 2;  // ramp 12→22
            for (var c = 0; c < cyclesThisHour; c++)
            {
                var ts = startToday.AddHours(hour).AddSeconds(Rng.Next(0, 3600));
                if (ts > DateTime.Now) continue;
                var good = Rng.Next(0, 100) < 96 ? 1 : 0;
                var cycleSec = 40 + Rng.Next(-2, 5);
                Exec(conn, """
                    INSERT INTO dbo.PR_ProductionResult
                        (EntryNo, WoID, LotID, LineID, ProcessCode, GoodQty, CycleSec,
                         MoldID, OperatorID, DefectFlag, EntryAt, CreatedBy, CreatedTS)
                    VALUES (CONCAT('ENR-', FORMAT(@T,'yyMMddHHmmss'),'-',@C),
                            @W, @L, 'LINE-INJ-01', 'INJ', @G, @CSec,
                            @M, 'user-e001', 0, @T, 'enrich', SYSDATETIME());
                    """,
                    ("@T", ts), ("@C", c.ToString()), ("@W", woId), ("@L", lotId),
                    ("@G", good), ("@CSec", cycleSec), ("@M", moldId));
                rowCount++;
            }
        }
        Console.WriteLine($"  inj  {rowCount} cycles · 06h–{DateTime.Now:HH}h");

        // 6 defects: 3 codes, clustered around 09h
        var codes = new[] { "INJ-D02", "INJ-D04", "INJ-D01" };
        for (var i = 0; i < 6; i++)
        {
            var when = startToday.AddHours(3 + Rng.NextDouble() * 3);   // 09–12h
            if (when > DateTime.Now) when = DateTime.Now.AddMinutes(-Rng.Next(5, 60));
            var code = codes[i % 3];
            Exec(conn, """
                INSERT INTO dbo.PR_DefectDetail
                    (WoID, LotID, ProcessCode, DefectCode, Qty, DetectedAt,
                     RegisteredBy, CreatedBy, CreatedTS)
                VALUES (@W, @L, 'INJ', @D, @Q, @T, 'user-e001', 'enrich', SYSDATETIME());
                """,
                ("@W", woId), ("@L", lotId), ("@D", code),
                ("@Q", Rng.Next(1, 3)), ("@T", when));
        }
        Console.WriteLine("  inj  6 defects clustered 09–12h");

        // 2 mold changes in the last 24h
        var equipId = ExecScalarStr(conn,
            "SELECT TOP 1 EquipID FROM dbo.MD_Equipment WHERE LineID='LINE-INJ-01' ORDER BY EquipID;");
        Exec(conn, """
            INSERT INTO dbo.PR_MoldChange
                (EquipID, LineID, OldMoldID, NewMoldID, OldMoldFinalShots, NewMoldStartShots,
                 Reason, DowntimeMin, StartedAt, CompletedAt, ChangedBy, CreatedBy, CreatedTS)
            VALUES
                (@E, 'LINE-INJ-01', @M, @M, 9920, 0, 'Scheduled', 12,
                 DATEADD(hour,-19,SYSDATETIME()), DATEADD(hour,-19,DATEADD(minute,12,SYSDATETIME())),
                 'user-e001', 'enrich', SYSDATETIME()),
                (@E, 'LINE-INJ-01', @M, @M, 8800, 0, 'Worn',      18,
                 DATEADD(hour,-4, SYSDATETIME()), DATEADD(hour,-4, DATEADD(minute,18,SYSDATETIME())),
                 'user-e003', 'enrich', SYSDATETIME());
            """, ("@E", equipId), ("@M", moldId));
        Console.WriteLine("  inj  2 mold-change history rows");

        // 3 andon calls
        Exec(conn, """
            INSERT INTO dbo.PR_AndonCall
                (LineID, EquipID, TriggerSource, Severity, TriggeredAt, AckedBy, AckedAt,
                 ReasonCode, CorrectiveAction, ResumedAt, DowntimeSec, Status, CreatedBy, CreatedTS)
            VALUES
                ('LINE-INJ-01', @E, 'OPERATOR', 'MEDIUM',
                 DATEADD(hour,-4,SYSDATETIME()), 'user-s001', DATEADD(hour,-4,DATEADD(minute,2,SYSDATETIME())),
                 'QUALITY', N'Resolved — colour shift on shot 14',
                 DATEADD(hour,-4,DATEADD(minute,18,SYSDATETIME())), 1080, 'Resolved', 'enrich', SYSDATETIME()),
                ('LINE-INJ-01', @E, 'OPERATOR', 'HIGH',
                 DATEADD(minute,-90,SYSDATETIME()), 'user-s001', DATEADD(minute,-88,SYSDATETIME()),
                 'MACHINE', N'Acked, supervisor inspecting',
                 NULL, NULL, 'Acked', 'enrich', SYSDATETIME()),
                ('LINE-INJ-01', @E, 'OPERATOR', 'HIGH',
                 DATEADD(minute,-3,SYSDATETIME()), NULL, NULL,
                 'MATERIAL', NULL, NULL, NULL, 'Open', 'enrich', SYSDATETIME());
            """, ("@E", equipId));
        Console.WriteLine("  inj  3 andon calls (1 Resolved, 1 Acked, 1 Open)");
    }

    // ── IMG ──────────────────────────────────────────────────────────────
    private static void EnrichImg(SqlConnection conn)
    {
        var equipId = ExecScalarStr(conn,
            "SELECT TOP 1 EquipID FROM dbo.MD_Equipment WHERE LineID='LINE-IMG-01' ORDER BY EquipID;");
        if (string.IsNullOrEmpty(equipId)) { Console.WriteLine("  img  skipped — no equip"); return; }

        // Active bond setup
        Exec(conn, """
            INSERT INTO dbo.PR_BondSetup
                (LineID, RecipeID, PressureSp, TempSp, HoldSecSp, TensionSp,
                 LoadedAt, LoadedBy, Status, CreatedBy, CreatedTS)
            VALUES ('LINE-IMG-01', 'REC-01', 2.80, 82.0, 12, 3.20,
                    DATEADD(hour,-2,SYSDATETIME()), 'user-i001', 'Active',
                    'enrich', SYSDATETIME());
            """);
        var bondId = ExecScalar<int>(conn,
            "SELECT TOP 1 BondSetupID FROM dbo.PR_BondSetup WHERE CreatedBy='enrich' ORDER BY BondSetupID DESC;");

        // 3 audit rows showing the recipe was tuned over the day
        Exec(conn, """
            INSERT INTO dbo.PR_BondSetupAudit
                (BondSetupID, FieldName, OldValue, NewValue, ReasonCode,
                 ChangedBy, ChangedAt, CreatedBy, CreatedTS)
            VALUES
                (@B, 'PressureSp', '2.60', '2.80', 'TUNE-UP', 'user-s001',
                 DATEADD(hour,-9,SYSDATETIME()), 'enrich', SYSDATETIME()),
                (@B, 'TempSp',     '80.0', '82.0', 'TUNE-UP', 'user-s001',
                 DATEADD(hour,-9,SYSDATETIME()), 'enrich', SYSDATETIME()),
                (@B, 'HoldSecSp',  '11',   '12',   'FINE',    'user-i001',
                 DATEADD(hour,-2,SYSDATETIME()), 'enrich', SYSDATETIME());
            """, ("@B", bondId));
        Console.WriteLine("  img  active bond setup + 3 audit rows");
    }

    // ── PNT ──────────────────────────────────────────────────────────────
    private static void EnrichPnt(SqlConnection conn)
    {
        // Pull the 6 pnt-seed virtual lots in IssuedAt order.
        var ids = new List<int>();
        using (var cmd = new SqlCommand("""
            SELECT VirtualLotID FROM dbo.PNT_VirtualLot
            WHERE CreatedBy='pnt-seed' ORDER BY IssuedAt;
            """, conn))
        using (var rdr = cmd.ExecuteReader())
            while (rdr.Read()) ids.Add((int)rdr["VirtualLotID"]);

        if (ids.Count < 6) { Console.WriteLine("  pnt  skipped — need 6 seeded lots"); return; }

        var statuses = new[] { "LABELED", "CONFIRMED", "OVEN", "LOADED", "BOUND", "PRE" };
        var loaded   = new[] { 32,        32,           32,     32,       0,       0 };
        var good     = new[] { 30,        31,           0,      0,        0,       0 };
        var defect   = new[] { 2,         1,            0,      0,        0,       0 };

        for (var i = 0; i < 6; i++)
        {
            Exec(conn, """
                UPDATE dbo.PNT_VirtualLot
                SET    Status = @S, LoadedQty = @LQ, ConfirmedQty = @G, DefectQty = @D,
                       ModifiedTS = SYSDATETIME()
                WHERE  VirtualLotID = @V;
                """,
                ("@V", ids[i]), ("@S", statuses[i]),
                ("@LQ", loaded[i]), ("@G", good[i]), ("@D", defect[i]));
        }

        // R1 / R2 / R3 events that show lots crossing readers
        // LOT 1 (LABELED)   passed R1, R2, R3
        // LOT 2 (CONFIRMED) passed R1, R2, R3
        // LOT 3 (OVEN)      passed R1, R2
        // LOT 4 (LOADED)    passed R1
        // LOT 5 (BOUND)     no reader pass yet
        // LOT 6 (PRE)       no reader pass yet
        var passes = new[]
        {
            (LotIdx: 0, R1: -180, R2: -160, R3: -40),
            (LotIdx: 1, R1: -120, R2: -100, R3: -10),
            (LotIdx: 2, R1: -65,  R2: -45,  R3: 0),
            (LotIdx: 3, R1: -25,  R2: 0,    R3: 0),
        };
        foreach (var p in passes)
        {
            var jigId = ExecScalarStr(conn,
                "SELECT JigID FROM dbo.PNT_VirtualLot WHERE VirtualLotID=@V;",
                ("@V", ids[p.LotIdx]));
            void Pass(string reader, int minutesAgo)
            {
                if (minutesAgo == 0) return;
                Exec(conn, $"""
                    INSERT INTO dbo.PNT_LineEvent
                        (TagID, JigID, LotID, ReaderID, AntennaPort, TagRole,
                         EventTS, Rssi, ReadCount, TriggerType, CreatedBy, CreatedTS)
                    VALUES (NULL, @J, NULL, @R, 1, 'JIG',
                            DATEADD(minute, {minutesAgo}, SYSDATETIME()),
                            -42, 1, 'PE', 'enrich', SYSDATETIME());
                    """, ("@J", jigId), ("@R", reader));
            }
            Pass("R1-LOAD",   p.R1);
            Pass("R2-OVENIN", p.R2);
            Pass("R3-UNLOAD", p.R3);
        }
        Console.WriteLine("  pnt  6 lots in distinct statuses (PRE→LABELED) + 10 RFID events");
    }

    // ── QC ───────────────────────────────────────────────────────────────
    private static void EnrichQc(SqlConnection conn)
    {
        // 6 more inspections at varied verdicts + times
        var rows = new (string Type, string Item, string Verdict, int Sample, int Good, int Def, int MinAgo)[]
        {
            ("Incoming",  "DR-TRM-LH-A1", "PASS",     32, 32,  0, 1320),  // 22 h ago
            ("Incoming",  "DR-TRM-RH-A1", "PASS",     32, 32,  0, 1200),
            ("InProcess", "DR-TRM-LH-A1", "PASS",     20, 20,  0, 720),
            ("InProcess", "DR-TRM-LH-A1", "PASS",     20, 19,  1, 360),
            ("Final",     "DR-TRM-LH-A1", "PASS",     32, 32,  0, 240),
            ("Final",     "DR-TRM-LH-A1", "IN_PROGRESS", 32, 8, 0, 6),
        };
        foreach (var r in rows)
        {
            Exec(conn, $"""
                INSERT INTO dbo.QC_Inspection
                    (InspectionNo, InspectionType, ItemNo, Mode, SampleSize, BatchQty,
                     CumulativeGood, DefectQtyTotal, Verdict, CriticalFlag,
                     InspectorID, InsStartTS, InsEndTS, CreatedBy, CreatedTS)
                VALUES (CONCAT(LEFT('{r.Type}',3),'-ENR-',FORMAT(SYSDATETIME(),'yyMMddHHmm'),'-{Rng.Next(100,999)}'),
                        '{r.Type}', '{r.Item}', 'Normal', {r.Sample}, {r.Sample * 6},
                        {r.Good}, {r.Def}, '{r.Verdict}', 0, 'user-q001',
                        DATEADD(minute, -{r.MinAgo}, SYSDATETIME()),
                        DATEADD(minute, -{Math.Max(0, r.MinAgo - 12)}, SYSDATETIME()),
                        'enrich', SYSDATETIME());
                """);
        }
        Console.WriteLine($"  qc   {rows.Length} extra inspections (yesterday + today)");

        // 1 closed Hold (released as USE-AS-IS)
        var holdId = ExecScalar<int>(conn, """
            SELECT TOP 1 HoldID FROM dbo.QC_Hold WHERE Status='Held' ORDER BY HoldID;
            """);
        if (holdId > 0)
        {
            Exec(conn, """
                INSERT INTO dbo.QC_HoldRelease
                    (HoldID, EventType, ReleaseAction, ReleaseReason,
                     ReleasedBy, ReleasedAt, Note, CreatedBy, CreatedTS)
                VALUES (@H, 'Release', 'USE-AS-IS',
                        N'Customer concession #CCN-2026-014 — minor cosmetic',
                        'user-q001', DATEADD(hour, -2, SYSDATETIME()),
                        N'Released with rework note',
                        'enrich', SYSDATETIME());
                UPDATE dbo.QC_Hold SET Status='Released', ModifiedTS=SYSDATETIME() WHERE HoldID=@H;
                """, ("@H", holdId));
            Console.WriteLine($"  qc   1 hold released 2 h ago (USE-AS-IS concession)");
        }

        // 2 extra CAPA actions for CAPA-25060001
        var capaId = ExecScalar<int>(conn,
            "SELECT TOP 1 CapaID FROM dbo.QC_CAPA WHERE CapaNumber='CAPA-25060001';");
        if (capaId > 0)
        {
            Exec(conn, """
                INSERT INTO dbo.QC_CAPA_Action
                    (CapaID, ActionType, CheckDay, Description, Metric, TargetValue,
                     ActualValue, Verdict, OwnerID, DueDate, CompletedAt,
                     CreatedBy, CreatedTS)
                VALUES
                    (@C, 'Verification', 3, N'D+3 stability check',
                     N'Defect rate', N'<= 2 %', N'0.8 %', 'PASS',
                     'user-q001', DATEADD(day, -2, GETDATE()), DATEADD(day, -2, SYSDATETIME()),
                     'enrich', SYSDATETIME()),
                    (@C, 'Verification', 5, N'D+5 stability check',
                     N'Defect rate', N'<= 2 %', N'1.1 %', 'PASS',
                     'user-q001', DATEADD(day, 0, GETDATE()), DATEADD(hour, -6, SYSDATETIME()),
                     'enrich', SYSDATETIME());
                """, ("@C", capaId));
            Console.WriteLine($"  qc   2 extra CAPA actions on CAPA-25060001 (D+3 + D+5)");
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────
    private static void Exec(SqlConnection conn, string sql, params (string Name, object Value)[] pars)
    {
        using var cmd = new SqlCommand(sql, conn);
        foreach (var (n, v) in pars) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
    private static T ExecScalar<T>(SqlConnection conn, string sql, params (string Name, object Value)[] pars) where T : struct
    {
        using var cmd = new SqlCommand(sql, conn);
        foreach (var (n, v) in pars) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        var v2 = cmd.ExecuteScalar();
        return v2 is null || v2 is DBNull ? default : (T)Convert.ChangeType(v2, typeof(T));
    }
    private static string ExecScalarStr(SqlConnection conn, string sql, params (string Name, object Value)[] pars)
    {
        using var cmd = new SqlCommand(sql, conn);
        foreach (var (n, v) in pars) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        var v2 = cmd.ExecuteScalar();
        return v2 is null || v2 is DBNull ? string.Empty : v2.ToString() ?? string.Empty;
    }
}
