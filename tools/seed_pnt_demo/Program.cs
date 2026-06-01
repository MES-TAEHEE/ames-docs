using System.Data;
using Microsoft.Data.SqlClient;

namespace AMES.Tools.SeedPntDemo;

/// <summary>
/// Demo seed for the PNT (Painting) POP module — enough rows so the
/// 9 PNT screens have something to render on first launch.
///
///   MD_Line          + 1   LINE-PNT-01
///   MD_Equipment     × 1   분체도장 라인 1호
///   MD_Oven          × 1   OVEN-01 (180°C)
///   MD_RfidReader    × 3   R1 (Loading), R2 (Oven entry), R3 (Unloading)
///   MD_Jig           × 6   JIG-007..JIG-012 (hangers + RFID tag)
///   MD_RalColor      × 2   RAL 9005 (black), RAL 3020 (red)
///   MD_DefectCode    × 6   PNT-D01..PNT-D06 (painting defects)
///   PNT_DailyPlan    × 2   Ready rows tied to today
///   PNT_VirtualLot   × 6   4 BOUND + 2 PRE for the picker UI
///   PNT_OvenTempSample × ~120  one sample per 5s for the last 10 min (chart)
/// </summary>
internal static class Program
{
    private const string Cs =
        "Server=localhost;Database=AMES_DEV;User Id=ames_app;Password=!Dev2026;" +
        "TrustServerCertificate=True;Encrypt=True;Connect Timeout=5;";

    private static int Main()
    {
        Console.WriteLine("[seed-pnt] Connecting ...");
        using var conn = new SqlConnection(Cs);
        conn.Open();

        SeedLineAndEquipment(conn);
        SeedOven(conn);
        SeedReaders(conn);
        SeedJigs(conn);
        SeedRalColors(conn);
        SeedDefectCodes(conn);
        SeedDailyPlan(conn);
        SeedVirtualLots(conn);
        SeedOvenTempSamples(conn);

        Console.WriteLine();
        Console.WriteLine("[seed-pnt] Done. Login as P001 / 1234 -> /pnt02.");
        return 0;
    }

    private static void SeedLineAndEquipment(SqlConnection conn)
    {
        Upsert(conn, """
            MERGE dbo.MD_Line AS t
            USING (SELECT @L AS LineID) s ON t.LineID = s.LineID
            WHEN MATCHED THEN UPDATE SET LineName=@N, ModifiedTS=SYSDATETIME()
            WHEN NOT MATCHED THEN INSERT (LineID, LineName, CreatedBy, CreatedTS)
              VALUES (@L, @N, 'seed', SYSDATETIME());
            """, ("@L", "LINE-PNT-01"), ("@N", "Painting Line 1 (Powder)"));

        Upsert(conn, """
            MERGE dbo.MD_Equipment AS t
            USING (SELECT @Id AS EquipID) s ON t.EquipID = s.EquipID
            WHEN MATCHED THEN UPDATE SET EquipName=@N, EquipType='PNT', LineID=@L,
                                          MakerModel='Eisenmann Powder', Status='RUN', ActiveFlag=1,
                                          ModifiedTS=SYSDATETIME()
            WHEN NOT MATCHED THEN INSERT (EquipID, EquipName, EquipType, LineID, MakerModel,
                                          Status, ActiveFlag, CreatedBy, CreatedTS)
              VALUES (@Id, @N, 'PNT', @L, 'Eisenmann Powder', 'RUN', 1, 'seed', SYSDATETIME());
            """, ("@Id", "PNT-EQ-01"), ("@N", "분체도장 라인 1호"), ("@L", "LINE-PNT-01"));
        Console.WriteLine("  line   LINE-PNT-01 + equip PNT-EQ-01");
    }

    private static void SeedOven(SqlConnection conn)
    {
        Upsert(conn, """
            MERGE dbo.MD_Oven AS t
            USING (SELECT @Id AS OvenID) s ON t.OvenID = s.OvenID
            WHEN MATCHED THEN UPDATE SET OvenName=@N, LineID=@L, TargetTemp=180,
                                          Tolerance=5, DwellSec=900, ZoneCount=3,
                                          ConveyorSpeed=1.2, MaxLoadKg=200,
                                          Status='RUN', ModifiedTS=SYSDATETIME()
            WHEN NOT MATCHED THEN INSERT (OvenID, OvenName, LineID, TargetTemp,
                                          Tolerance, DwellSec, ZoneCount, ConveyorSpeed,
                                          MaxLoadKg, Status, CreatedBy, CreatedTS)
              VALUES (@Id, @N, @L, 180, 5, 900, 3, 1.2, 200, 'RUN',
                      'seed', SYSDATETIME());
            """, ("@Id", "OVEN-01"), ("@N", "Powder Cure Oven 1"), ("@L", "LINE-PNT-01"));
        Console.WriteLine("  oven   OVEN-01  180C +/-5  15 min");
    }

    private static void SeedReaders(SqlConnection conn)
    {
        var readers = new (string Id, string Gate, string Loc)[]
        {
            ("R1-LOAD",   "R1", "Loading station"),
            ("R2-OVENIN", "R2", "Oven entry"),
            ("R3-UNLOAD", "R3", "Unloading station"),
        };
        foreach (var r in readers)
        {
            Upsert(conn, """
                MERGE dbo.MD_RfidReader AS t
                USING (SELECT @Id AS ReaderID) s ON t.ReaderID = s.ReaderID
                WHEN MATCHED THEN UPDATE SET ReaderName=@L, LineID='LINE-PNT-01',
                                              GateLocation=@G, AntennaCount=4, PowerDbm=24,
                                              Status='ONLINE', ModifiedTS=SYSDATETIME()
                WHEN NOT MATCHED THEN INSERT (ReaderID, ReaderName, LineID, GateLocation,
                                              AntennaCount, PowerDbm, Status,
                                              CreatedBy, CreatedTS)
                  VALUES (@Id, @L, 'LINE-PNT-01', @G, 4, 24, 'ONLINE',
                          'seed', SYSDATETIME());
                """, ("@Id", r.Id), ("@G", r.Gate), ("@L", r.Loc));
        }
        Console.WriteLine("  reader R1-LOAD + R2-OVENIN + R3-UNLOAD");
    }

    private static void SeedJigs(SqlConnection conn)
    {
        for (var i = 7; i <= 12; i++)
        {
            var jigId = $"JIG-{i:000}";
            var tagId = $"TAG-{i:000}-A";
            var cycles = 35 + i * 13;

            Upsert(conn, $"""
                MERGE dbo.MD_Jig AS t
                USING (SELECT @Id AS JigID) s ON t.JigID = s.JigID
                WHEN MATCHED THEN UPDATE SET JigName=@N, HangerCount=32, RatedCycle=200,
                                              CycleCount=@C, HealthStatus='OK',
                                              ReadFailRate=0.5,
                                              CompatItemsJSON='["DR-TRM-LH-A1","DR-TRM-RH-A1"]',
                                              ActiveFlag=1, ModifiedTS=SYSDATETIME()
                WHEN NOT MATCHED THEN INSERT (JigID, JigName, HangerCount, RatedCycle,
                                              CycleCount, HealthStatus, ReadFailRate,
                                              CompatItemsJSON, ActiveFlag, CreatedBy, CreatedTS)
                  VALUES (@Id, @N, 32, 200, @C, 'OK', 0.5,
                          '["DR-TRM-LH-A1","DR-TRM-RH-A1"]', 1, 'seed', SYSDATETIME());
                """,
                ("@Id", jigId), ("@N", $"Hanger Frame {i}"), ("@C", cycles.ToString()));

            Upsert(conn, $"""
                MERGE dbo.MD_RfidTag AS t
                USING (SELECT @T AS TagID) s ON t.TagID = s.TagID
                WHEN MATCHED THEN UPDATE SET EPC=@E, JigID=@J, TagRole='JIG',
                                              HeatRating=230, CycleCount=@C, Status='OK',
                                              ModifiedTS=SYSDATETIME()
                WHEN NOT MATCHED THEN INSERT (TagID, EPC, JigID, TagRole, HeatRating,
                                              CycleCount, Status, CreatedBy, CreatedTS)
                  VALUES (@T, @E, @J, 'JIG', 230, @C, 'OK', 'seed', SYSDATETIME());
                """, ("@T", tagId), ("@E", $"E2802C0001{i:00000}"), ("@J", jigId), ("@C", cycles.ToString()));
        }
        Console.WriteLine("  jigs   JIG-007 .. JIG-012  (+ matching RFID tags)");
    }

    private static void SeedRalColors(SqlConnection conn)
    {
        var ral = new (string Code, string Name, string Hex)[]
        {
            ("RAL 9005", "Jet Black",     "#0a0a0a"),
            ("RAL 3020", "Traffic Red",   "#cc0605"),
        };
        foreach (var c in ral)
        {
            Upsert(conn, """
                MERGE dbo.MD_RalColor AS t
                USING (SELECT @C AS RALCode) s ON t.RALCode = s.RALCode
                WHEN MATCHED THEN UPDATE SET ColorName=@N, HexValue=@H, CureTemp=180,
                                              CureDuration=900, ActiveFlag=1,
                                              ModifiedTS=SYSDATETIME()
                WHEN NOT MATCHED THEN INSERT (RALCode, ColorName, HexValue, CureTemp,
                                              CureDuration, ActiveFlag, CreatedBy, CreatedTS)
                  VALUES (@C, @N, @H, 180, 900, 1, 'seed', SYSDATETIME());
                """, ("@C", c.Code), ("@N", c.Name), ("@H", c.Hex));
        }
    }

    private static void SeedDefectCodes(SqlConnection conn)
    {
        var defs = new (string Code, string Ko, string En)[]
        {
            ("PNT-D01", "오렌지 필",         "Orange Peel"),
            ("PNT-D02", "러닝/새깅",         "Running / Sagging"),
            ("PNT-D03", "크레이터",           "Crater (Fisheye)"),
            ("PNT-D04", "도장 두께 부족",     "Low Film Build"),
            ("PNT-D05", "색차",               "Colour Mismatch"),
            ("PNT-D06", "이물 / 더스트",      "Contamination"),
        };
        foreach (var d in defs)
        {
            Upsert(conn, """
                MERGE dbo.MD_DefectCode AS t
                USING (SELECT @C AS DefectCode) s ON t.DefectCode = s.DefectCode
                WHEN MATCHED THEN UPDATE SET DefectName=@N, DefectNameEn=@E,
                                              ProcessCode='PNT', DefectCategory='COATING',
                                              SeverityLevel='MEDIUM', DispositionDefault='REWORK',
                                              Status='Active', ModifiedTS=SYSDATETIME()
                WHEN NOT MATCHED THEN INSERT (DefectCode, DefectName, DefectNameEn, ProcessCode,
                                              DefectCategory, SeverityLevel, DispositionDefault,
                                              Status, CreatedBy, CreatedTS)
                  VALUES (@C, @N, @E, 'PNT', 'COATING', 'MEDIUM', 'REWORK', 'Active',
                          'seed', SYSDATETIME());
                """, ("@C", d.Code), ("@N", d.Ko), ("@E", d.En));
        }
    }

    private static void SeedDailyPlan(SqlConnection conn)
    {
        // Wipe and reissue today's PNT seed rows so re-runs are idempotent.
        ExecRaw(conn, """
            DELETE FROM dbo.PNT_VirtualLot WHERE CreatedBy='pnt-seed';
            DELETE FROM dbo.PNT_DailyPlan  WHERE CreatedBy='pnt-seed';
            DELETE FROM dbo.PNT_OvenTempSample WHERE CreatedBy='pnt-seed';
            """);

        var plans = new (string Item, string Ral, int Qty, int Jigs, int Lots)[]
        {
            ("DR-TRM-LH-A1", "RAL 9005", 192, 6, 6),
            ("DR-TRM-RH-A1", "RAL 9005",  96, 3, 3),
        };
        foreach (var p in plans)
        {
            ExecRaw(conn, $"""
                INSERT INTO dbo.PNT_DailyPlan
                    (WoID, PlanDate, ItemNo, RalColor, TargetQty, LineID, OvenID,
                     StartTime, JigsRequired, LotsRequired, ReadyFlag, CreatedBy, CreatedTS)
                VALUES (NULL, CAST(GETDATE() AS DATE), '{p.Item}', '{p.Ral}', {p.Qty},
                        'LINE-PNT-01', 'OVEN-01', '06:00', {p.Jigs}, {p.Lots}, 1,
                        'pnt-seed', SYSDATETIME());
                """);
        }
        Console.WriteLine("  plan   2 Ready rows for today");
    }

    private static void SeedVirtualLots(SqlConnection conn)
    {
        // Pull the first plan ID we just inserted
        var planId = ExecScalar<int>(conn, """
            SELECT TOP 1 PlanID FROM dbo.PNT_DailyPlan
            WHERE CreatedBy='pnt-seed' ORDER BY PlanID;
            """);
        if (planId == 0) { Console.WriteLine("  lots   skipped — no plan"); return; }

        // 4 BOUND lots + 2 PRE for the picker preview
        var jigs = new[] { "JIG-007", "JIG-009", "JIG-012", "JIG-008" };
        for (var i = 0; i < 4; i++)
        {
            ExecRaw(conn, $"""
                INSERT INTO dbo.PNT_VirtualLot
                    (LotID, PlanID, JigID, ItemNo, RalColor, TargetQty, LoadedQty,
                     ConfirmedQty, DefectQty, Status, EnhancedInspection, IssuedAt,
                     BindAt, BindReason, CreatedBy, CreatedTS)
                VALUES (NULL, {planId}, '{jigs[i]}', 'DR-TRM-LH-A1', 'RAL 9005',
                        32, 0, 0, 0, 'BOUND', 0,
                        DATEADD(minute, -{60 - i * 10}, SYSDATETIME()),
                        DATEADD(minute, -{59 - i * 10}, SYSDATETIME()),
                        'AUTO', 'pnt-seed', SYSDATETIME());
                """);
        }
        for (var i = 0; i < 2; i++)
        {
            ExecRaw(conn, $"""
                INSERT INTO dbo.PNT_VirtualLot
                    (LotID, PlanID, JigID, ItemNo, RalColor, TargetQty, LoadedQty,
                     ConfirmedQty, DefectQty, Status, EnhancedInspection, IssuedAt,
                     BindAt, BindReason, CreatedBy, CreatedTS)
                VALUES (NULL, {planId}, NULL, 'DR-TRM-LH-A1', 'RAL 9005',
                        32, 0, 0, 0, 'PRE', 0,
                        DATEADD(minute, -{20 - i * 5}, SYSDATETIME()),
                        NULL, NULL, 'pnt-seed', SYSDATETIME());
                """);
        }
        Console.WriteLine("  lots   4 BOUND + 2 PRE");
    }

    private static void SeedOvenTempSamples(SqlConnection conn)
    {
        // Last 10 minutes of samples, one every 5 seconds, oscillating around 180°C.
        var rng = new Random(2026);
        for (var i = 0; i < 120; i++)
        {
            var temp = 180.0 + (rng.NextDouble() - 0.5) * 4.0;  // 178-182
            ExecRaw(conn, $"""
                INSERT INTO dbo.PNT_OvenTempSample
                    (OvenID, ZoneID, TempC, SampledAt, CreatedBy, CreatedTS)
                VALUES ('OVEN-01', 1, {temp.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)},
                        DATEADD(second, -{(120 - i) * 5}, SYSDATETIME()),
                        'pnt-seed', SYSDATETIME());
                """);
        }
        Console.WriteLine("  oven samples x 120 (last 10 min, zone 1)");
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
    private static T ExecScalar<T>(SqlConnection conn, string sql) where T : struct
    {
        using var cmd = new SqlCommand(sql, conn);
        var v = cmd.ExecuteScalar();
        return v is null || v is DBNull ? default : (T)Convert.ChangeType(v, typeof(T));
    }
}
