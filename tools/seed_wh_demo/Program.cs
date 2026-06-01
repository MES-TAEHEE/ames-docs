using Microsoft.Data.SqlClient;

namespace AMES.Tools.SeedWhDemo;

/// <summary>
/// Fills the warehouse tables that the PDA WH module reads from.
/// Idempotent: every row carries CreatedBy='wh-seed' and is wiped on re-run.
/// </summary>
internal static class Program
{
    private const string Cs =
        "Server=localhost;Database=AMES_DEV;User Id=ames_app;Password=!Dev2026;" +
        "TrustServerCertificate=True;Encrypt=True;Connect Timeout=5;";

    private static int Main()
    {
        Console.WriteLine("[wh-seed] connecting ...");
        using var conn = new SqlConnection(Cs);
        conn.Open();

        Wipe(conn);
        SeedLocations(conn);
        SeedPurchaseOrders(conn);
        SeedInventory(conn);
        SeedReleaseSchedule(conn);
        SeedTransactions(conn);

        Console.WriteLine();
        Console.WriteLine("[wh-seed] done. WH PDA screens now have data.");
        return 0;
    }

    private static void Wipe(SqlConnection conn) => Exec(conn, """
        DELETE FROM dbo.WH_TransactionHistory WHERE OperatorID = 'wh-seed' OR Note = 'wh-seed';
        DELETE FROM dbo.WH_ReleaseSchedule    WHERE CreatedBy='wh-seed';
        DELETE FROM dbo.WH_Inventory          WHERE CreatedBy='wh-seed';
        DELETE FROM dbo.WH_PurchaseOrder      WHERE CreatedBy='wh-seed';
        DELETE FROM dbo.MD_Location           WHERE CreatedBy='wh-seed';
        """);

    // ── MD_Location (6 rows: A/B/C zones × 2 bays) ───────────────────────
    private static void SeedLocations(SqlConnection conn)
    {
        var rows = new (string Id, string Name, string Zone, string Aisle, string Bay)[]
        {
            ("WH-A-01", "Raw Steel A-01",   "A", "01", "01"),
            ("WH-A-02", "Raw Steel A-02",   "A", "01", "02"),
            ("WH-B-01", "Fabric B-01",      "B", "02", "01"),
            ("WH-B-02", "Fabric B-02",      "B", "02", "02"),
            ("WH-C-01", "Paint Powder C-01","C", "03", "01"),
            ("WH-C-02", "Paint Powder C-02","C", "03", "02"),
        };
        foreach (var r in rows)
            Exec(conn, """
                INSERT INTO dbo.MD_Location
                    (LocationID, LocationName, ZoneCode, Aisle, Bay, Slot,
                     Capacity, LocationType, PlantID, ActiveFlag, CreatedBy, CreatedTS)
                VALUES (@Id, @N, @Z, @A, @B, '01', 500, 'RACK', 'SEH-US-01', 1,
                        'wh-seed', SYSDATETIME());
                """, ("@Id", r.Id), ("@N", r.Name), ("@Z", r.Zone), ("@A", r.Aisle), ("@B", r.Bay));
        Console.WriteLine("  loc   6 locations (A/B/C × 2 bays)");
    }

    // ── WH_PurchaseOrder (8 lines: 3 received / 3 open / 2 late) ─────────
    private static void SeedPurchaseOrders(SqlConnection conn)
    {
        var rows = new (string Po, int Ln, string Vendor, string Item, decimal Qty, decimal Rec, int DueOffsetDays, string Status)[]
        {
            ("PO-2026-001", 1, "V-STEEL-01",  "STL-A36-04",    1200, 1200, -5, "Received"),
            ("PO-2026-001", 2, "V-STEEL-01",  "STL-A36-06",     800,  800, -5, "Received"),
            ("PO-2026-002", 1, "V-FAB-22",    "FAB-BK-C01",     300,  120, -2, "Partial"),
            ("PO-2026-003", 1, "V-FAB-22",    "FAB-RD-C02",     200,    0,  0, "Open"),
            ("PO-2026-003", 2, "V-FAB-22",    "FAB-GY-C03",     150,    0,  0, "Open"),
            ("PO-2026-004", 1, "V-PAINT-08",  "PNT-RAL-9005",   400,    0,  2, "Open"),
            ("PO-2026-005", 1, "V-FAB-22",    "FAB-BK-C01",     250,    0, -3, "Open"),  // late
            ("PO-2026-005", 2, "V-FAB-22",    "FAB-GY-C03",     180,    0, -3, "Open"),  // late
        };
        foreach (var r in rows)
        {
            Exec(conn, $"""
                INSERT INTO dbo.WH_PurchaseOrder
                    (PoNumber, PoLineNo, VendorID, ItemNo, OrderQty, ReceivedQty,
                     UnitCode, OrderDate, DueDate, Status, CreatedBy, CreatedAt)
                VALUES (@Po, @Ln, @V, @I, @Q, @R,
                        'EA',
                        DATEADD(day, -10, GETDATE()),
                        DATEADD(day, {r.DueOffsetDays}, GETDATE()),
                        @St, 'wh-seed', SYSDATETIME());
                """,
                ("@Po", r.Po), ("@Ln", r.Ln), ("@V", r.Vendor), ("@I", r.Item),
                ("@Q", r.Qty), ("@R", r.Rec), ("@St", r.Status));
        }
        Console.WriteLine("  po    8 PO lines (3 received · 3 open · 2 late)");
    }

    // ── WH_Inventory (12 stock lines spread across 6 locations) ──────────
    private static void SeedInventory(SqlConnection conn)
    {
        var rows = new (string Item, string Loc, decimal OnHand, decimal Res, int ExpiryOffsetDays)[]
        {
            ("STL-A36-04",   "WH-A-01",  680, 200,  365),
            ("STL-A36-06",   "WH-A-02",  420, 100,  365),
            ("STL-A36-04",   "WH-A-02",  120,   0,  365),
            ("FAB-BK-C01",   "WH-B-01",  240,  80,  180),
            ("FAB-BK-C01",   "WH-B-02",  180,   0,  180),
            ("FAB-RD-C02",   "WH-B-01",  150,  30,  180),
            ("FAB-GY-C03",   "WH-B-02",  210,  50,  180),
            ("PNT-RAL-9005", "WH-C-01",  300,  40,   90),
            ("PNT-RAL-3020", "WH-C-01",  120,   0,   90),
            ("PNT-RAL-9005", "WH-C-02",   80,   0,   30),  // soon-expire
            ("DR-TRM-LH-A1", "WH-A-01",   40,  20,  730),
            ("DR-TRM-RH-A1", "WH-A-01",   32,   8,  730),
        };
        foreach (var r in rows)
        {
            Exec(conn, $"""
                INSERT INTO dbo.WH_Inventory
                    (ItemNo, LocationID, OnHandQty, ReservedQty,
                     LastReceivedAt, ExpiryDate, Status, CreatedBy, CreatedTS)
                VALUES (@I, @L, @O, @R,
                        DATEADD(day, -3, SYSDATETIME()),
                        DATEADD(day, {r.ExpiryOffsetDays}, GETDATE()),
                        'OK', 'wh-seed', SYSDATETIME());
                """,
                ("@I", r.Item), ("@L", r.Loc), ("@O", r.OnHand), ("@R", r.Res));
        }
        Console.WriteLine("  inv   12 inventory lines (raw steel + fabric + paint + FG)");
    }

    // ── WH_ReleaseSchedule (5 demands for production) ────────────────────
    private static void SeedReleaseSchedule(SqlConnection conn)
    {
        var rows = new (string Item, decimal Demand, decimal Picked, int HoursOffset, string Status, byte Pri)[]
        {
            ("FAB-BK-C01", 60, 60,  -2, "Picked",  1),
            ("STL-A36-04", 80, 40,   2, "Partial", 2),
            ("FAB-GY-C03", 40,  0,   4, "Open",    2),
            ("PNT-RAL-9005",30, 0,   6, "Open",    3),
            ("FAB-RD-C02", 25,  0,  -1, "Open",    1),  // late
        };
        foreach (var r in rows)
        {
            Exec(conn, $"""
                INSERT INTO dbo.WH_ReleaseSchedule
                    (ItemNo, DemandQty, PickedQty,
                     RequiredAt, Priority, Status, CreatedBy, CreatedTS)
                VALUES (@I, @D, @P,
                        DATEADD(hour, {r.HoursOffset}, SYSDATETIME()),
                        @Pri, @St, 'wh-seed', SYSDATETIME());
                """,
                ("@I", r.Item), ("@D", r.Demand), ("@P", r.Picked),
                ("@Pri", (int)r.Pri), ("@St", r.Status));
        }
        Console.WriteLine("  rel   5 release rows (1 done · 1 partial · 3 open · 1 late)");
    }

    // ── WH_TransactionHistory (20 mixed txns spread across last 7 days) ─
    private static void SeedTransactions(SqlConnection conn)
    {
        var rng = new Random(2026);
        var types = new[] { "RECEIVE", "ISSUE", "ADJUST", "MOVE" };
        var items = new[] { "STL-A36-04", "FAB-BK-C01", "PNT-RAL-9005", "FAB-GY-C03" };
        var locs  = new[] { "WH-A-01", "WH-B-01", "WH-C-01", "WH-A-02" };
        var reasons = new[] { "PO-RCV", "WO-ISSUE", "COUNT", "TRANSFER" };

        for (var i = 0; i < 20; i++)
        {
            var hAgo  = rng.Next(0, 7 * 24);
            var type  = types[i % 4];
            var delta = type == "RECEIVE" ? rng.Next(20, 100)
                      : type == "ISSUE"   ? -rng.Next(5, 40)
                      : type == "ADJUST"  ? rng.Next(-10, 11)
                      : 0;
            var before = rng.Next(50, 500);
            Exec(conn, $"""
                INSERT INTO dbo.WH_TransactionHistory
                    (TxnTime, TxnType, ItemNo, LocationID,
                     QtyBefore, Delta, QtyAfter, ReasonCode,
                     OperatorID, Note, CreatedBy, CreatedTS)
                VALUES (DATEADD(hour, -{hAgo}, SYSDATETIME()), @T, @I, @L,
                        @B, {delta}, {before + delta}, @R,
                        'user-e001', 'wh-seed', 'wh-seed', SYSDATETIME());
                """,
                ("@T", type), ("@I", items[i % 4]), ("@L", locs[i % 4]),
                ("@B", before), ("@R", reasons[i % 4]));
        }
        Console.WriteLine("  txn   20 transactions across last 7 days");
    }

    // ── helper ──────────────────────────────────────────────────────────
    private static void Exec(SqlConnection conn, string sql, params (string Name, object Value)[] pars)
    {
        using var cmd = new SqlCommand(sql, conn);
        foreach (var (n, v) in pars) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
}
