using System.Data;
using Microsoft.Data.SqlClient;

namespace AMES.Tools.SeedQcDemo;

/// <summary>
/// Demo seed for the QC (Quality) POP module — enough rows so the
/// 9 QC screens have something to render on first launch.
///
///   MD_Line             + 1   LINE-QC-01 (Quality station)
///   QC_InspectionStd    × 3   Incoming / In-Process / Final
///   QC_Inspection       × 4   2 PASS + 1 FAIL + 1 IN_PROGRESS
///   QC_InspectionItem   × ~12 measurement rows for the 4 inspections
///   QC_NCR              × 3   open NCRs (1 minor + 1 major + 1 critical)
///   QC_Hold             × 2   active holds tied to NCRs
///   QC_CAPA             × 2   open + investigating CAPAs
///   QC_CAPA_Action      × 4   in-progress + completed actions
/// </summary>
internal static class Program
{
    private const string Cs =
        "Server=localhost;Database=AMES_DEV;User Id=ames_app;Password=!Dev2026;" +
        "TrustServerCertificate=True;Encrypt=True;Connect Timeout=5;";

    private static int Main()
    {
        Console.WriteLine("[seed-qc] Connecting ...");
        using var conn = new SqlConnection(Cs);
        conn.Open();

        SeedLineAndUsers(conn);
        SeedInspectionStd(conn);
        WipeQcSeed(conn);
        SeedInspections(conn);
        SeedNcrs(conn);
        SeedHolds(conn);
        SeedCapas(conn);

        Console.WriteLine();
        Console.WriteLine("[seed-qc] Done. Login as Q001 / 1234 -> /qc07.");
        return 0;
    }

    private static void SeedLineAndUsers(SqlConnection conn)
    {
        Upsert(conn, """
            MERGE dbo.MD_Line AS t
            USING (SELECT @L AS LineID) s ON t.LineID = s.LineID
            WHEN MATCHED THEN UPDATE SET LineName=@N, ModifiedTS=SYSDATETIME()
            WHEN NOT MATCHED THEN INSERT (LineID, LineName, CreatedBy, CreatedTS)
              VALUES (@L, @N, 'seed', SYSDATETIME());
            """, ("@L", "LINE-QC-01"), ("@N", "Quality Inspection Station"));
        Console.WriteLine("  line   LINE-QC-01");
    }

    private static void SeedInspectionStd(SqlConnection conn)
    {
        var stds = new (string Code, string Name, string Type, string Item, double Aql)[]
        {
            ("STD-INC-DR-TRM-LH", "Door Trim LH · Incoming",   "Incoming",  "DR-TRM-LH-A1", 1.5),
            ("STD-IP-DR-TRM-LH",  "Door Trim LH · In-Process", "InProcess", "DR-TRM-LH-A1", 2.5),
            ("STD-FIN-DR-TRM-LH", "Door Trim LH · Final",      "Final",     "DR-TRM-LH-A1", 0.65),
        };
        foreach (var s in stds)
        {
            Upsert(conn, $$"""
                MERGE dbo.QC_InspectionStd AS t
                USING (SELECT @C AS StdCode) src ON t.StdCode = src.StdCode AND t.VerNo = 'v1.0'
                WHEN MATCHED THEN UPDATE SET StdName=@N, InsType=@T, ItemNo=@I,
                                              AQLLevel={{s.Aql.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}},
                                              SampleInterval=60,
                                              InspItemsJSON=@J,
                                              Status='Active', EffectiveDate=CAST(GETDATE() AS DATE),
                                              ModifiedTS=SYSDATETIME()
                WHEN NOT MATCHED THEN INSERT (StdCode, VerNo, StdName, InsType, ItemNo,
                                              AQLLevel, SampleInterval, InspItemsJSON, Status,
                                              EffectiveDate, CreatedBy, CreatedTS)
                  VALUES (@C, 'v1.0', @N, @T, @I,
                          {{s.Aql.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}}, 60, @J, 'Active',
                          CAST(GETDATE() AS DATE), 'qc-seed', SYSDATETIME());
                """,
                ("@C", s.Code), ("@N", s.Name), ("@T", s.Type), ("@I", s.Item),
                ("@J", "[{\"name\":\"Length\",\"lsl\":210.0,\"usl\":212.0,\"unit\":\"mm\"},"
                     + "{\"name\":\"Width\",\"lsl\":89.5,\"usl\":90.5,\"unit\":\"mm\"},"
                     + "{\"name\":\"Colour\",\"method\":\"Visual\",\"spec\":\"Match master swatch\"},"
                     + "{\"name\":\"Adhesion\",\"lsl\":4.0,\"usl\":99.0,\"unit\":\"N/mm\"}]"));
        }
        Console.WriteLine("  stds   3 inspection standards");
    }

    private static void WipeQcSeed(SqlConnection conn)
    {
        ExecRaw(conn, """
            DELETE FROM dbo.QC_InspectionItem WHERE CreatedBy='qc-seed';
            DELETE FROM dbo.QC_CAPA_Action    WHERE CreatedBy='qc-seed';
            DELETE FROM dbo.QC_HoldRelease    WHERE CreatedBy='qc-seed';
            DELETE FROM dbo.QC_Disposition    WHERE CreatedBy='qc-seed';
            DELETE FROM dbo.QC_NCR_Action     WHERE CreatedBy='qc-seed';
            DELETE FROM dbo.QC_Hold           WHERE CreatedBy='qc-seed';
            DELETE FROM dbo.QC_NCR            WHERE CreatedBy='qc-seed';
            DELETE FROM dbo.QC_CAPA           WHERE CreatedBy='qc-seed';
            DELETE FROM dbo.QC_Inspection     WHERE CreatedBy='qc-seed';
            """);
    }

    private static void SeedInspections(SqlConnection conn)
    {
        var rows = new (string No, string Type, string Item, string Verdict, int Sample, int Good, int Defect, int MinAgo)[]
        {
            ("INC-25060101", "Incoming",  "DR-TRM-LH-A1", "PASS",        32, 32,  0, 480),
            ("IP-25060102",  "InProcess", "DR-TRM-LH-A1", "PASS",        20, 19,  1, 240),
            ("IP-25060103",  "InProcess", "DR-TRM-RH-A1", "FAIL",        20, 16,  4,  90),
            ("FIN-25060104", "Final",     "DR-TRM-LH-A1", "IN_PROGRESS", 32, 12,  0,  10),
        };
        foreach (var r in rows)
        {
            ExecRaw(conn, $"""
                INSERT INTO dbo.QC_Inspection
                    (InspectionNo, InspectionType, LotID, ItemNo, Mode, SampleSize, BatchQty,
                     CumulativeGood, DefectQtyTotal, Verdict, CriticalFlag,
                     InspectorID, InsStartTS, InsEndTS, CreatedBy, CreatedTS)
                VALUES ('{r.No}', '{r.Type}', NULL, '{r.Item}', 'Normal', {r.Sample}, {r.Sample * 6},
                        {r.Good}, {r.Defect}, '{r.Verdict}', {(r.Verdict == "FAIL" ? 1 : 0)},
                        'user-q001',
                        DATEADD(minute, -{r.MinAgo},     SYSDATETIME()),
                        DATEADD(minute, -{Math.Max(0, r.MinAgo - 15)}, SYSDATETIME()),
                        'qc-seed', SYSDATETIME());
                """);
        }

        // measurement items for the 3 completed inspections
        ExecRaw(conn, """
            DECLARE @rows TABLE (No VARCHAR(24), Seq INT, Nm NVARCHAR(80), Std NVARCHAR(40),
                                  Meas NVARCHAR(40), Res VARCHAR(10));
            INSERT INTO @rows VALUES
                ('INC-25060101', 1, N'Length',   N'211.0 ±1.0 mm', N'210.8', 'PASS'),
                ('INC-25060101', 2, N'Width',    N'90.0 ±0.5 mm',  N'90.1',  'PASS'),
                ('INC-25060101', 3, N'Colour',   N'Master match',  N'OK',    'PASS'),
                ('IP-25060102',  1, N'Adhesion', N'>= 4 N/mm',     N'4.6',   'PASS'),
                ('IP-25060102',  2, N'Width',    N'90.0 ±0.5 mm',  N'89.9',  'PASS'),
                ('IP-25060103',  1, N'Length',   N'211.0 ±1.0 mm', N'213.2', 'FAIL'),
                ('IP-25060103',  2, N'Width',    N'90.0 ±0.5 mm',  N'90.0',  'PASS'),
                ('IP-25060103',  3, N'Adhesion', N'>= 4 N/mm',     N'2.8',   'FAIL'),
                ('FIN-25060104', 1, N'Length',   N'211.0 ±1.0 mm', N'211.1', 'PASS'),
                ('FIN-25060104', 2, N'Width',    N'90.0 ±0.5 mm',  N'90.2',  'PASS');

            INSERT INTO dbo.QC_InspectionItem
                (InspectionID, ItemSeq, ItemName, Standard, Measured, Result, CreatedBy, CreatedTS)
            SELECT i.InspectionID, r.Seq, r.Nm, r.Std, r.Meas, r.Res, 'qc-seed', SYSDATETIME()
            FROM   @rows r
            JOIN   dbo.QC_Inspection i ON i.InspectionNo = r.No AND i.CreatedBy='qc-seed';
            """);
        Console.WriteLine("  insp   4 inspections (2 PASS / 1 FAIL / 1 IN_PROGRESS) + 10 measurement rows");
    }

    private static void SeedNcrs(SqlConnection conn)
    {
        ExecRaw(conn, """
            INSERT INTO dbo.QC_NCR
                (NcrNumber, SourceType, SourceID, InspectionID, Severity, ItemNo, AffectedQty,
                 Disposition, Status, ReportedBy, ReportedAt, CreatedBy, CreatedTS)
            VALUES
                ('NCR-25060001', 'INSPECTION', 'IP-25060103',
                 (SELECT InspectionID FROM dbo.QC_Inspection WHERE InspectionNo='IP-25060103'),
                 'Major', 'DR-TRM-RH-A1', 96, 'HOLD', 'Open', 'user-q001',
                 DATEADD(minute, -88, SYSDATETIME()), 'qc-seed', SYSDATETIME()),
                ('NCR-25060002', 'POP-DEFECT', 'INJ-08', NULL,
                 'Minor', 'DR-TRM-LH-A1', 6, 'REWORK', 'Open', 'user-q001',
                 DATEADD(hour,  -3,  SYSDATETIME()), 'qc-seed', SYSDATETIME()),
                ('NCR-25060003', 'CUSTOMER', 'CR-2025-014', NULL,
                 'Critical', 'DR-TRM-LH-A1', 32, 'RTV', 'Investigating',
                 'user-q001', DATEADD(hour, -28, SYSDATETIME()),
                 'qc-seed', SYSDATETIME());
            """);
        Console.WriteLine("  ncrs   3 NCRs (Minor + Major + Critical)");
    }

    private static void SeedHolds(SqlConnection conn)
    {
        ExecRaw(conn, """
            INSERT INTO dbo.QC_Hold
                (HoldNumber, SourceNcrID, Severity, AffectedType, ItemNo, HeldQty,
                 PhysicalLocation, Status, HeldBy, HeldAt, CreatedBy, CreatedTS)
            VALUES
                ('HLD-25060001',
                 (SELECT NcrID FROM dbo.QC_NCR WHERE NcrNumber='NCR-25060001'),
                 'Major', 'LOT', 'DR-TRM-RH-A1', 96, 'BAY-Q-01', 'Held',
                 'user-q001', DATEADD(minute, -85, SYSDATETIME()),
                 'qc-seed', SYSDATETIME()),
                ('HLD-25060002',
                 (SELECT NcrID FROM dbo.QC_NCR WHERE NcrNumber='NCR-25060003'),
                 'Critical', 'FG', 'DR-TRM-LH-A1', 32, 'BAY-Q-02', 'Held',
                 'user-q001', DATEADD(hour, -27, SYSDATETIME()),
                 'qc-seed', SYSDATETIME());
            """);
        Console.WriteLine("  holds  2 active holds");
    }

    private static void SeedCapas(SqlConnection conn)
    {
        ExecRaw(conn, """
            INSERT INTO dbo.QC_CAPA
                (CapaNumber, Type, TriggerType, Phase, Status, RootCause, Cause4M,
                 OwnerID, OpenedAt, DueDate, CreatedBy, CreatedTS)
            VALUES
                ('CAPA-25060001', 'Corrective', 'NCR', 'Action', 'In Progress',
                 N'Bond pressure drift on IMG-02', 'Machine',
                 'user-q001', DATEADD(day, -3, SYSDATETIME()), DATEADD(day, 4, GETDATE()),
                 'qc-seed', SYSDATETIME()),
                ('CAPA-25060002', 'Preventive', 'Audit', 'Plan', 'Open',
                 N'Calibration logbook for callipers missing', 'Method',
                 'user-q001', DATEADD(day, -1, SYSDATETIME()), DATEADD(day, 12, GETDATE()),
                 'qc-seed', SYSDATETIME());

            INSERT INTO dbo.QC_CAPA_Action
                (CapaID, ActionType, CheckDay, Description, Metric, TargetValue, ActualValue,
                 Verdict, OwnerID, DueDate, CompletedAt, CreatedBy, CreatedTS)
            SELECT c.CapaID, 'Corrective', 1,
                   N'Replace pneumatic regulator on IMG-02',
                   N'Pressure stability ±0.1 bar',
                   N'<=0.10', N'0.08', 'PASS',
                   'user-q001', DATEADD(day, -1, GETDATE()), DATEADD(day, -1, SYSDATETIME()),
                   'qc-seed', SYSDATETIME()
            FROM   dbo.QC_CAPA c WHERE c.CapaNumber='CAPA-25060001';

            INSERT INTO dbo.QC_CAPA_Action
                (CapaID, ActionType, CheckDay, Description, Metric, TargetValue,
                 OwnerID, DueDate, CreatedBy, CreatedTS)
            SELECT c.CapaID, 'Verification', 7,
                   N'7-day stability check after regulator swap',
                   N'Defect rate',
                   N'<= 2 %',
                   'user-q001', DATEADD(day, 4, GETDATE()),
                   'qc-seed', SYSDATETIME()
            FROM   dbo.QC_CAPA c WHERE c.CapaNumber='CAPA-25060001';

            INSERT INTO dbo.QC_CAPA_Action
                (CapaID, ActionType, CheckDay, Description, Metric, TargetValue,
                 OwnerID, DueDate, CreatedBy, CreatedTS)
            SELECT c.CapaID, 'Preventive', 3,
                   N'Re-issue calliper calibration logbooks',
                   N'Logbook coverage',
                   N'100 %',
                   'user-q001', DATEADD(day, 2, GETDATE()),
                   'qc-seed', SYSDATETIME()
            FROM   dbo.QC_CAPA c WHERE c.CapaNumber='CAPA-25060002';
            """);
        Console.WriteLine("  capas  2 CAPAs + 3 actions");
    }

    // ───────────────────────────────────────────────────────────────────────
    private static void Upsert(SqlConnection conn, string sql, params (string Name, string Value)[] pars)
    {
        using var cmd = new SqlCommand(sql, conn);
        foreach (var (n, v) in pars)
            cmd.Parameters.Add(n, SqlDbType.NVarChar).Value = (object?)v ?? DBNull.Value;
        cmd.ExecuteNonQuery();
    }
    private static void ExecRaw(SqlConnection conn, string sql)
    {
        using var cmd = new SqlCommand(sql, conn);
        cmd.ExecuteNonQuery();
    }
}
