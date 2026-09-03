using Microsoft.Data.SqlClient;

namespace AMES.Tools.SeedPpDemo;

/// <summary>
/// Seeds enough PP rows for the 12 web screens to render meaningfully:
/// forecast, SAP-imported customer orders, supply plans + lines,
/// MRP runs, purchase requests, line schedule slots, OEE snapshots,
/// downtime events, calendar overrides, line-state samples.
/// Idempotent: every row carries CreatedBy='pp-seed' and is wiped on re-run.
/// </summary>
internal static class Program
{
    private static readonly string Cs =
        Environment.GetEnvironmentVariable("AMES_CONNECTION_STRING") ??
        "Server=localhost;Database=AMES_DEV;User Id=ames_app;Password=!Dev2026;" +
        "TrustServerCertificate=True;Encrypt=True;Connect Timeout=5;";

    private static int Main()
    {
        Console.WriteLine("[pp-seed] connecting ...");
        using var conn = new SqlConnection(Cs);
        conn.Open();

        Wipe(conn);
        SeedForecast(conn);
        SeedCustomerOrders(conn);
        SeedSupplyPlans(conn);
        SeedMrpRuns(conn);
        SeedPurchaseRequests(conn);
        SeedCalendar(conn);
        SeedLineSchedule(conn);
        SeedOee(conn);
        SeedDowntime(conn);
        SeedLineStates(conn);

        Console.WriteLine();
        Console.WriteLine("[pp-seed] done.");
        return 0;
    }

    private static void Wipe(SqlConnection conn) => Exec(conn, """
        DELETE FROM dbo.PP_LineStateLog              WHERE CreatedBy='pp-seed';
        DELETE FROM dbo.PP_LineDowntimeLog           WHERE CreatedBy='pp-seed';
        DELETE FROM dbo.PP_LineOEE                   WHERE CreatedBy='pp-seed';
        DELETE FROM dbo.PP_LineSchedule              WHERE CreatedBy='pp-seed';
        DELETE FROM dbo.PP_ProductionCalendarOverride WHERE CreatedBy='pp-seed';
        DELETE FROM dbo.PP_PurchaseRequest           WHERE CreatedBy='pp-seed';
        DELETE FROM dbo.PP_MRPLog                    WHERE CreatedBy='pp-seed';
        DELETE FROM dbo.PP_SupplyPlanDetail          WHERE CreatedBy='pp-seed';
        DELETE FROM dbo.PP_SupplyPlan                WHERE CreatedBy='pp-seed';
        DELETE FROM dbo.PP_CustomerOrder             WHERE CreatedBy='pp-seed';
        DELETE FROM dbo.PP_Forecast                  WHERE CreatedBy='pp-seed';
        """);

    // ── PP-01 Forecast ─────────────────────────────────────────────────
    private static void SeedForecast(SqlConnection conn)
    {
        var rng = new Random(2026);
        var rows = new (string Cust, string Item)[]
        {
            ("SAV", "DR-TRM-LH-A1"), ("SAV", "DR-TRM-RH-A1"),
            ("GEO", "DR-TRM-LH-A1"), ("GEO", "DR-TRM-RH-A1"),
        };
        for (var m = -3; m <= 6; m++)
        {
            foreach (var r in rows)
            {
                var qty = 400 + rng.Next(0, 200);
                var conf = m <= 0 ? "Actual" : (m <= 2 ? "Firm" : "Tentative");
                Exec(conn, $"""
                    INSERT INTO dbo.PP_Forecast
                        (ForecastBatch, CustomerID, ItemNo, ForecastMonth,
                         ForecastQty, Confidence, Source, ImportedAt, CreatedBy, CreatedTS)
                    VALUES ('FC-2026Q2', @C, @I,
                            DATEADD(month, {m}, CAST(GETDATE() AS DATE)),
                            {qty}, '{conf}', 'SAP',
                            DATEADD(day, -5, SYSDATETIME()), 'pp-seed', SYSDATETIME());
                    """, ("@C", r.Cust), ("@I", r.Item));
            }
        }
        Console.WriteLine("  fc    40 forecast rows (10 months × 4 cust×item)");
    }

    // ── PP-02 SAP Import / PP-OTD ──────────────────────────────────────
    private static void SeedCustomerOrders(SqlConnection conn)
    {
        var rows = new (string No, int Ln, string Cust, string Item, int OQ, int SQ, int Req, int Prom, string Status)[]
        {
            ("SO-2026-021", 1, "SAV", "DR-TRM-LH-A1", 320, 320,  -7,  -7, "Shipped"),
            ("SO-2026-021", 2, "SAV", "DR-TRM-RH-A1", 320, 320,  -7,  -7, "Shipped"),
            ("SO-2026-022", 1, "GEO", "DR-TRM-LH-A1", 256,   0,   3,   3, "Open"),
            ("SO-2026-023", 1, "SAV", "DR-TRM-LH-A1", 480, 240,   1,   2, "Partial"),
            ("SO-2026-024", 1, "GEO", "DR-TRM-RH-A1", 192,   0,   7,   7, "Open"),
            ("SO-2026-019", 1, "SAV", "DR-TRM-LH-A1", 160, 160, -14, -14, "Shipped"),
        };
        foreach (var r in rows)
        {
            Exec(conn, $"""
                INSERT INTO dbo.PP_CustomerOrder
                    (SoNumber, SoLineNo, CustomerID, ItemNo,
                     OrderQty, ShippedQty, OrderDate, RequestedDeliveryDate, PromisedDate,
                     Status, SapSyncedAt, CreatedBy, CreatedTS)
                VALUES (@N, @L, @C, @I,
                        @O, @S,
                        DATEADD(day, -10, GETDATE()),
                        DATEADD(day, {r.Req}, GETDATE()),
                        DATEADD(day, {r.Prom}, GETDATE()),
                        @St, DATEADD(day, -1, SYSDATETIME()), 'pp-seed', SYSDATETIME());
                """,
                ("@N", r.No), ("@L", r.Ln), ("@C", r.Cust), ("@I", r.Item),
                ("@O", r.OQ), ("@S", r.SQ), ("@St", r.Status));
        }
        Console.WriteLine("  so    6 customer orders (mixed shipped/open/partial)");
    }

    // ── PP-03 Supply Plans ─────────────────────────────────────────────
    private static void SeedSupplyPlans(SqlConnection conn)
    {
        Exec(conn, """
            INSERT INTO dbo.PP_SupplyPlan
                (PlanCode, PlanPeriod, Status, ConfirmedAt, SapImportBatch, CreatedBy, CreatedTS)
            VALUES
                ('PLAN-2026-04', DATEADD(month, -1, CAST(GETDATE() AS DATE)), 'Confirmed',
                 DATEADD(day, -28, SYSDATETIME()), 'SAP-FC-2026-04', 'pp-seed', SYSDATETIME()),
                ('PLAN-2026-05', CAST(GETDATE() AS DATE),                     'Draft',
                 NULL, 'SAP-FC-2026-05', 'pp-seed', SYSDATETIME()),
                ('PLAN-2026-06', DATEADD(month,  1, CAST(GETDATE() AS DATE)), 'Draft',
                 NULL, 'SAP-FC-2026-06', 'pp-seed', SYSDATETIME());

            DECLARE @ids TABLE (PlanID INT);
            INSERT @ids SELECT PlanID FROM dbo.PP_SupplyPlan WHERE CreatedBy='pp-seed';

            INSERT INTO dbo.PP_SupplyPlanDetail
                (PlanID, ItemNo, PlannedQty, FgOnHand, NetRequirement, DueDate, CreatedBy, CreatedTS)
            SELECT p.PlanID, x.Item, x.Qty, x.OnHand,
                   CASE WHEN x.Qty - x.OnHand > 0 THEN x.Qty - x.OnHand ELSE 0 END,
                   DATEADD(month, 1, GETDATE()),
                   'pp-seed', SYSDATETIME()
            FROM   @ids p
            CROSS JOIN (VALUES
                ('DR-TRM-LH-A1', 800, 200),
                ('DR-TRM-RH-A1', 600, 150),
                ('FAB-BK-C01',   400,  80)
            ) AS x(Item, Qty, OnHand);
            """);
        Console.WriteLine("  plan  3 supply plans + 9 detail lines");
    }

    // ── PP-05 MRP Runs ─────────────────────────────────────────────────
    private static void SeedMrpRuns(SqlConnection conn)
    {
        var rng = new Random(2027);
        for (var i = 0; i < 5; i++)
        {
            var hAgo = i * 6 + 2;
            Exec(conn, $"""
                INSERT INTO dbo.PP_MRPLog
                    (RunAt, RunBy, HorizonStart, HorizonEnd,
                     WosConsidered, PrsCreated, ShortageCount, DurationMs, Status,
                     CreatedBy, CreatedTS)
                VALUES (DATEADD(hour, -{hAgo}, SYSDATETIME()), 'admin@ames.local',
                        DATEADD(day, -1, CAST(GETDATE() AS DATE)),
                        DATEADD(day, 30, CAST(GETDATE() AS DATE)),
                        {18 + rng.Next(0,5)}, {3 + rng.Next(0,5)}, {rng.Next(0,3)},
                        {800 + rng.Next(0, 1200)}, 'Success', 'pp-seed', SYSDATETIME());
                """);
        }
        Console.WriteLine("  mrp   5 MRP runs (last 30h)");
    }

    // ── PP-06 Purchase Requests ────────────────────────────────────────
    private static void SeedPurchaseRequests(SqlConnection conn)
    {
        var rows = new (string PR, string Item, string Vendor, int Qty, int DaysAhead, string Status, string? Po)[]
        {
            ("PR-2026-031", "FAB-BK-C01",   "V-FAB-22",   200,  5, "Approved",  "PO-SAP-554"),
            ("PR-2026-032", "STL-A36-04",   "V-STEEL-01", 600,  3, "Approved",  "PO-SAP-555"),
            ("PR-2026-033", "PNT-RAL-9005", "V-PAINT-08", 150,  7, "Pending",   null),
            ("PR-2026-034", "FAB-RD-C02",   "V-FAB-22",   180, 10, "Pending",   null),
            ("PR-2026-035", "POW-RAL3020",  "V-PAINT-08",  80, 14, "Draft",     null),
        };
        foreach (var r in rows)
        {
            Exec(conn, $"""
                INSERT INTO dbo.PP_PurchaseRequest
                    (PrNumber, ItemNo, VendorID, RequiredQty, RequiredDate,
                     Status, ApprovedAt, SapPoNumber, CreatedBy, CreatedTS)
                VALUES (@PR, @I, @V, {r.Qty},
                        DATEADD(day, {r.DaysAhead}, GETDATE()),
                        @S, {(r.Status == "Approved" ? "DATEADD(day, -1, SYSDATETIME())" : "NULL")},
                        @Po, 'pp-seed', SYSDATETIME());
                """,
                ("@PR", r.PR), ("@I", r.Item), ("@V", r.Vendor),
                ("@S", r.Status), ("@Po", (object?)r.Po ?? DBNull.Value));
        }
        Console.WriteLine("  pr    5 purchase requests (2 approved · 2 pending · 1 draft)");
    }

    // ── PP-CAL Calendar overrides ──────────────────────────────────────
    private static void SeedCalendar(SqlConnection conn)
    {
        Exec(conn, """
            INSERT INTO dbo.PP_ProductionCalendarOverride
                (OverrideDate, LineID, DayType, PatternID, CapacityFactor, Reason,
                 ApprovedBy, ApprovedAt, CreatedBy, CreatedTS)
            VALUES
                (DATEADD(day, 3, CAST(GETDATE() AS DATE)),  NULL,          'Holiday',  NULL,        0.0,
                 N'Memorial Day — plant closed',   NULL, NULL, 'pp-seed', SYSDATETIME()),
                (DATEADD(day, 5, CAST(GETDATE() AS DATE)),  'LINE-INJ-01', 'Overtime', 'PAT-OT12', 1.5,
                 N'Catch-up shift for PO-2026-005', NULL, NULL, 'pp-seed', SYSDATETIME()),
                (DATEADD(day, 10, CAST(GETDATE() AS DATE)), 'LINE-PNT-01', 'Reduced',  'PAT-HALF',  0.5,
                 N'Booth maintenance',              NULL, NULL, 'pp-seed', SYSDATETIME());
            """);
        Console.WriteLine("  cal   3 calendar overrides");
    }

    // ── PP-LSB Line Schedule ───────────────────────────────────────────
    private static void SeedLineSchedule(SqlConnection conn)
    {
        // 3 days × 2 lines × 2 slots each = 12 rows
        for (var d = 0; d < 3; d++)
        {
            foreach (var line in new[] { "LINE-INJ-01", "LINE-IMG-01" })
            {
                Exec(conn, $"""
                    INSERT INTO dbo.PP_LineSchedule
                        (LineID, ScheduleDate, StartMin, EndMin, PlannedQty, Status, PublishedAt,
                         CreatedBy, CreatedTS)
                    VALUES
                        (@L, DATEADD(day, {d}, CAST(GETDATE() AS DATE)),  360,  720, 200, 'Published',
                         DATEADD(day, -1, SYSDATETIME()), 'pp-seed', SYSDATETIME()),
                        (@L, DATEADD(day, {d}, CAST(GETDATE() AS DATE)),  720, 1080, 220, 'Published',
                         DATEADD(day, -1, SYSDATETIME()), 'pp-seed', SYSDATETIME());
                    """, ("@L", line));
            }
        }
        Console.WriteLine("  lsb   12 schedule slots (3 days × 2 lines × 2 shifts)");
    }

    // ── PP-OEE Snapshots ───────────────────────────────────────────────
    private static void SeedOee(SqlConnection conn)
    {
        var rng = new Random(2028);
        var lines = new[] { "LINE-INJ-01", "LINE-INJ-02", "LINE-IMG-01" };
        var shifts = new[] { "A", "B" };   // 공통코드 WORK_SHIFT 기준 (DAY/NIGHT → A/B)
        for (var d = -7; d < 0; d++)
        {
            foreach (var line in lines)
            foreach (var shift in shifts)
            {
                var load = 480; // 8 hours = 480 min
                var pdown = 30;
                var udown = rng.Next(5, 40);
                var op = load - pdown - udown;
                var produced = 200 + rng.Next(0, 80);
                var good = produced - rng.Next(0, 8);
                var a = (decimal)op / load;
                var p = 0.85m + (decimal)(rng.NextDouble() * 0.12);
                var q = (decimal)good / produced;
                var oee = a * p * q;
                Exec(conn, $"""
                    INSERT INTO dbo.PP_LineOEE
                        (LineID, PeriodDate, ShiftCode,
                         LoadingMin, PlannedDownMin, UnplannedDownMin, OperatingMin,
                         TotalProducedQty, GoodQty,
                         Availability, Performance, Quality, OEE,
                         CreatedBy, CreatedTS)
                    VALUES (@L, DATEADD(day, {d}, CAST(GETDATE() AS DATE)), @S,
                            {load}, {pdown}, {udown}, {op},
                            {produced}, {good},
                            {a.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)},
                            {p.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)},
                            {q.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)},
                            {oee.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)},
                            'pp-seed', SYSDATETIME());
                    """, ("@L", line), ("@S", shift));
            }
        }
        Console.WriteLine($"  oee   {lines.Length * shifts.Length * 7} OEE snapshots (7 days × 3 lines × 2 shifts)");
    }

    // ── PP-DTL Downtime Log ────────────────────────────────────────────
    private static void SeedDowntime(SqlConnection conn)
    {
        var rng = new Random(2029);
        var lines = new[] { "LINE-INJ-01", "LINE-INJ-02", "LINE-IMG-01" };
        var reasons = new[] { "MACHINE", "QUALITY", "MATERIAL", "CHANGEOVER" };
        for (var i = 0; i < 12; i++)
        {
            var hAgo = rng.Next(0, 7 * 24);
            var dur = rng.Next(5, 60);
            var line = lines[i % lines.Length];
            var reason = reasons[i % reasons.Length];
            Exec(conn, $"""
                INSERT INTO dbo.PP_LineDowntimeLog
                    (LineID, StartTS, EndTS, DurationMin, ReasonCode, CauseCode, Comment,
                     CreatedBy, CreatedTS)
                VALUES (@L,
                        DATEADD(hour, -{hAgo}, SYSDATETIME()),
                        DATEADD(minute, {dur}, DATEADD(hour, -{hAgo}, SYSDATETIME())),
                        {dur}, @R, 'CAUSE-{i:00}',
                        N'Auto-seeded {reason.ToLower()} event',
                        'pp-seed', SYSDATETIME());
                """, ("@L", line), ("@R", reason));
        }
        Console.WriteLine("  dtl   12 downtime events (last 7 days)");
    }

    // ── PP-ODM Line state samples ──────────────────────────────────────
    private static void SeedLineStates(SqlConnection conn)
    {
        var rng = new Random(2030);
        var lines = new[] { "LINE-INJ-01", "LINE-IMG-01", "LINE-PNT-01" };
        foreach (var line in lines)
        {
            // last 4 hours, 1 sample per minute = 240 rows
            for (var m = 0; m < 240; m++)
            {
                var run = rng.NextDouble() > 0.12;  // ~88% running
                var state = run ? "RUN" : (rng.NextDouble() > 0.5 ? "IDLE" : "DOWN");
                Exec(conn, $"""
                    INSERT INTO dbo.PP_LineStateLog
                        (LineID, MinuteTS, State, PlanState, RunFlag,
                         ClassifiedAt, CreatedBy, CreatedTS)
                    VALUES (@L, DATEADD(minute, -{m}, SYSDATETIME()),
                            @S, 'PLAN-RUN', {(run ? 1 : 0)},
                            SYSDATETIME(), 'pp-seed', SYSDATETIME());
                    """, ("@L", line), ("@S", state));
            }
        }
        Console.WriteLine($"  odm   {lines.Length * 240} state samples (4h × 1/min × 3 lines)");
    }

    // ── helper ─────────────────────────────────────────────────────────
    private static void Exec(SqlConnection conn, string sql, params (string Name, object Value)[] pars)
    {
        using var cmd = new SqlCommand(sql, conn);
        foreach (var (n, v) in pars) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
}
