using Microsoft.Data.SqlClient;

namespace AMES.Tools.SeedFgDemo;

internal static class Program
{
    private const string Cs =
        "Server=localhost;Database=AMES_DEV;User Id=ames_app;Password=!Dev2026;" +
        "TrustServerCertificate=True;Encrypt=True;Connect Timeout=5;";

    private static int Main()
    {
        Console.WriteLine("[fg-seed] connecting ...");
        using var conn = new SqlConnection(Cs);
        conn.Open();

        Wipe(conn);
        SeedStock(conn);
        SeedOrders(conn);
        SeedLoadingHistory(conn);
        SeedReturns(conn);
        SeedDayEnd(conn);

        Console.WriteLine();
        Console.WriteLine("[fg-seed] done.");
        return 0;
    }

    private static void Wipe(SqlConnection conn) => Exec(conn, """
        DELETE FROM dbo.FG_ReturnDisposition WHERE CreatedBy='fg-seed';
        DELETE FROM dbo.FG_CustomerReturn    WHERE CreatedBy='fg-seed';
        DELETE FROM dbo.FG_DayEndClose       WHERE CreatedBy='fg-seed';
        DELETE FROM dbo.FG_DeliveryNote      WHERE CreatedBy='fg-seed';
        DELETE FROM dbo.FG_LoadingConfirm    WHERE CreatedBy='fg-seed';
        DELETE FROM dbo.FG_PickingFifo       WHERE CreatedBy='fg-seed';
        DELETE FROM dbo.FG_ShipmentOrderLine WHERE CreatedBy='fg-seed';
        DELETE FROM dbo.FG_ShipmentOrder     WHERE CreatedBy='fg-seed';
        DELETE FROM dbo.FG_PutAway           WHERE CreatedBy='fg-seed';
        DELETE FROM dbo.FG_Stock             WHERE CreatedBy='fg-seed';
        """);

    private static void SeedStock(SqlConnection conn)
    {
        var rows = new (string Item, string Cust, decimal Qty, string Loc, string Status, int HoursAgo)[]
        {
            ("DR-TRM-LH-A1", "SAV", 32, "FG-A-01", "Available",  48),
            ("DR-TRM-LH-A1", "SAV", 32, "FG-A-01", "Available",  24),
            ("DR-TRM-LH-A1", "SAV", 32, "FG-A-02", "Available",   8),
            ("DR-TRM-RH-A1", "SAV", 32, "FG-A-02", "Available",  16),
            ("DR-TRM-RH-A1", "GEO", 32, "FG-B-01", "Available",   4),
            ("DR-TRM-LH-A1", "GEO", 32, "FG-B-01", "Reserved",   12),
            ("DR-TRM-LH-A1", "SAV", 32, "FG-A-02", "Available",   2),
            ("DR-TRM-RH-A1", "SAV", 24, "FG-A-01", "Available",   1),
        };
        for (var i = 0; i < rows.Length; i++)
        {
            var r = rows[i];
            Exec(conn, $"""
                INSERT INTO dbo.FG_Stock
                    (StockNumber, ItemNo, CustomerCode, Qty, Location, Status, HoldFlag,
                     StockTS, CreatedBy, CreatedTS)
                VALUES (CONCAT('STK-SD-', '{i + 1:000}'),
                        @I, @C, @Q, @L, @S, 0,
                        DATEADD(hour, -{r.HoursAgo}, SYSDATETIME()),
                        'fg-seed', SYSDATETIME());
                """,
                ("@I", r.Item), ("@C", r.Cust), ("@Q", r.Qty), ("@L", r.Loc), ("@S", r.Status));
        }
        Console.WriteLine($"  stock {rows.Length} FG stock rows (mixed customers + ages)");
    }

    private static void SeedOrders(SqlConnection conn)
    {
        // 4 shipment orders + 8 lines
        var orders = new (string Po, string Cust, int DaysOffset, string Carrier, string Dest, string Status)[]
        {
            ("CUST-PO-2026-014", "SAV", -1, "FedEx",      "SAV-PLT-01",  "Shipped"),
            ("CUST-PO-2026-015", "SAV",  0, "FedEx",      "SAV-PLT-01",  "Ready"),
            ("CUST-PO-2026-016", "GEO",  1, "UPS",        "GEO-PLT-02",  "Open"),
            ("CUST-PO-2026-017", "SAV",  3, "UPS",        "SAV-PLT-01",  "Open"),
        };
        for (var i = 0; i < orders.Length; i++)
        {
            var o = orders[i];
            Exec(conn, $"""
                INSERT INTO dbo.FG_ShipmentOrder
                    (ShipOrderNumber, CustomerCode, CustomerPO, Source,
                     ShipDate, CarrierCode, DestPlant, DestDock,
                     ReceiverName, ReceiverPhone, Status, OTDFlag,
                     CreatedBy, CreatedTS)
                VALUES (CONCAT('SO-SEED-', '{i + 1:000}'), @C, @P, 'SAP',
                        DATEADD(day, {o.DaysOffset}, GETDATE()),
                        @Cr, @D, 'DOCK-1',
                        N'Receiving Mgr', '+1-555-0100', @S, 'OnTime',
                        'fg-seed', SYSDATETIME());
                """,
                ("@C", o.Cust), ("@P", o.Po), ("@Cr", o.Carrier), ("@D", o.Dest), ("@S", o.Status));
        }

        // 2 lines per order
        Exec(conn, """
            DECLARE @ids TABLE (OrdId INT, Seq INT);
            INSERT @ids (OrdId, Seq)
            SELECT ShipmentOrderID, n.seq
            FROM   dbo.FG_ShipmentOrder
            CROSS JOIN (VALUES (1),(2)) n(seq)
            WHERE  CreatedBy = 'fg-seed';

            INSERT INTO dbo.FG_ShipmentOrderLine
                (ShipmentOrderID, LineSeq, ItemNo, OrderedQty, AllocatedQty,
                 ReservationStatus, CreatedBy, CreatedTS)
            SELECT OrdId, Seq,
                   CASE WHEN Seq = 1 THEN 'DR-TRM-LH-A1' ELSE 'DR-TRM-RH-A1' END,
                   32, 0, 'Pending', 'fg-seed', SYSDATETIME()
            FROM   @ids;
            """);
        Console.WriteLine("  ord   4 shipment orders + 8 lines (1 Shipped · 1 Ready · 2 Open)");
    }

    private static void SeedLoadingHistory(SqlConnection conn)
    {
        // Loading + DN for the Shipped order
        Exec(conn, """
            DECLARE @So INT = (SELECT TOP 1 ShipmentOrderID FROM dbo.FG_ShipmentOrder
                               WHERE CreatedBy='fg-seed' AND Status='Shipped');
            IF @So IS NULL RETURN;

            INSERT INTO dbo.FG_LoadingConfirm
                (LoadingNumber, ShipmentOrderID, LicensePlate, CarrierCode,
                 DriverName, DriverPhone, DockNo, ArrivalTS, DepartureTS,
                 SealNo, OTDStatus, OperatorID, ConfirmedAt, CreatedBy, CreatedTS)
            VALUES ('LDG-SEED-001', @So, 'AL-2026-X1', 'FedEx',
                    N'Mike Johnson', '+1-555-0150', 'DOCK-1',
                    DATEADD(hour, -25, SYSDATETIME()), DATEADD(hour, -22, SYSDATETIME()),
                    'SEAL-9912', 'OnTime', 'user-e001',
                    DATEADD(hour, -22, SYSDATETIME()), 'fg-seed', SYSDATETIME());

            DECLARE @Ld INT = SCOPE_IDENTITY();

            INSERT INTO dbo.FG_DeliveryNote
                (DnNumber, ShipmentOrderID, LoadingID, CustomerCode,
                 FormatTemplate, Revision, IssuedAt, IssuedBy,
                 EdiStatus, CreatedBy, CreatedTS)
            VALUES ('DN-SEED-001', @So, @Ld, 'SAV', 'STANDARD', 1,
                    DATEADD(hour, -22, SYSDATETIME()), 'user-e001',
                    'Sent', 'fg-seed', SYSDATETIME());
            """);
        Console.WriteLine("  ldg   1 loading + 1 delivery note for the shipped order");
    }

    private static void SeedReturns(SqlConnection conn)
    {
        Exec(conn, """
            INSERT INTO dbo.FG_CustomerReturn
                (ReturnNumber, RMANo, CustomerCode, ReturnReason,
                 ItemsJSON, Status, ReceivedAt, ReceivedBy,
                 CapaTriggered, CreatedBy, CreatedTS)
            VALUES
                ('RMA-SEED-001', 'SAV-RMA-2026-007', 'SAV',
                 N'Bond adhesion below spec',
                 N'[{"itemNo":"DR-TRM-LH-A1","qty":6}]',
                 'Open', DATEADD(hour, -36, SYSDATETIME()), 'user-q001',
                 1, 'fg-seed', SYSDATETIME()),
                ('RMA-SEED-002', 'GEO-RMA-2026-002', 'GEO',
                 N'Cosmetic — paint orange peel',
                 N'[{"itemNo":"DR-TRM-RH-A1","qty":3}]',
                 'Inspecting', DATEADD(hour, -8, SYSDATETIME()), 'user-q001',
                 0, 'fg-seed', SYSDATETIME());
            """);
        Console.WriteLine("  rtn   2 customer returns (1 Open · 1 Inspecting)");
    }

    private static void SeedDayEnd(SqlConnection conn)
    {
        Exec(conn, """
            INSERT INTO dbo.FG_DayEndClose
                (CloseNumber, CloseDate, ClosedBy, ClosedAt, CloseMode,
                 ChecklistJSON, KpiJSON, ErpFeedStatus, CreatedBy, CreatedTS)
            VALUES ('DEC-SEED-001', DATEADD(day,-1,CAST(GETDATE() AS DATE)),
                    'user-s001', DATEADD(hour, -14, SYSDATETIME()),
                    'Auto',
                    N'{"itemsCount":48,"shipped":48,"reconciled":true}',
                    N'{"otd":98.0,"defectRate":1.8}',
                    'Sent', 'fg-seed', SYSDATETIME());
            """);
        Console.WriteLine("  dec   1 prior-day close snapshot");
    }

    private static void Exec(SqlConnection conn, string sql, params (string Name, object Value)[] pars)
    {
        using var cmd = new SqlCommand(sql, conn);
        foreach (var (n, v) in pars) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
}
