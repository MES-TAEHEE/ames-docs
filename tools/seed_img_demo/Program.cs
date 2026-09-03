using System.Data;
using Microsoft.Data.SqlClient;

namespace AMES.Tools.SeedImgDemo;

/// <summary>
/// Demo seed for the IMG (Wrapping) POP module.
///
///   MD_Item        + 1   FABRIC-GY-C03 (fabric raw item)
///   MD_DefectCode  × 6   IMG-D01..IMG-D06
///   MD_Equipment   × 1   감싸기 1호기 on LINE-IMG-01
///   tbl_Lot        × 3   3 fabric rolls (Grey / Black / Red)
///   PR_EquipStatusLog: initial RUN snapshot for the line
///
/// Run:
///   dotnet run --project tools/seed_img_demo
/// </summary>
internal static class Program
{
    private const string Cs =
        "Server=localhost;Database=AMES_DEV;User Id=ames_app;Password=!Dev2026;" +
        "TrustServerCertificate=True;Encrypt=True;Connect Timeout=5;";

    private static int Main()
    {
        Console.WriteLine("[seed-img] Connecting to AMES_DEV ...");
        using var conn = new SqlConnection(Cs);
        conn.Open();

        SeedFabricItem    (conn);
        SeedEquipment     (conn);
        SeedDefectCodes   (conn);
        SeedFabricLots    (conn);
        SeedRecipes       (conn);
        SeedWorkOrders    (conn);
        SeedEquipStatus   (conn);
        SeedInProgress    (conn);   // active WO with bond + roll mounted + cycles + defect

        Console.WriteLine();
        Console.WriteLine("[seed-img] Done. IMG POP screens now have demo data.");
        Console.WriteLine();
        Console.WriteLine("To test IMG locally — no appsettings.json edit needed:");
        Console.WriteLine("  F5 → login as I001 (PIN 1234)  or  I002 (PIN 2345)");
        Console.WriteLine("  PopAuthService reroutes the session to LINE-IMG-01 →");
        Console.WriteLine("  Login dispatches to IMG-03 Production Entry.");
        return 0;
    }

    private static void SeedFabricItem(SqlConnection conn)
    {
        var items = new (string No, string Name, string NameEn)[]
        {
            ("FAB-GY-C03", "원단 GY-C03 (Grey)",   "Fabric Roll · Grey"),
            ("FAB-BK-C01", "원단 BK-C01 (Black)",  "Fabric Roll · Black"),
            ("FAB-RD-C02", "원단 RD-C02 (Red)",    "Fabric Roll · Red"),
        };
        foreach (var i in items)
        {
            Upsert(conn, """
                MERGE dbo.MD_Item AS t
                USING (SELECT @No AS ItemNo) s ON t.ItemNo = s.ItemNo
                WHEN MATCHED THEN UPDATE SET ItemName=@N, ItemNameEN=@NE,
                                              ItemType='FABRIC', ItemCategory='WRAP',
                                              DefaultUOM='M', ActiveFlag=1, ModifiedTS=SYSDATETIME()
                WHEN NOT MATCHED THEN INSERT (ItemNo, ItemName, ItemNameEN, ItemType, ItemCategory,
                                              DefaultUOM, ActiveFlag, CreatedBy, CreatedTS)
                  VALUES (@No, @N, @NE, 'FABRIC', 'WRAP', 'M', 1, 'seed', SYSDATETIME());
                """,
                ("@No", i.No), ("@N", i.Name), ("@NE", i.NameEn));
            Console.WriteLine($"  item   {i.No,-12} {i.NameEn}");
        }
    }

    private static void SeedEquipment(SqlConnection conn)
    {
        // make sure the line itself exists
        Upsert(conn, """
            MERGE dbo.MD_Line AS t
            USING (SELECT @L AS LineID) s ON t.LineID = s.LineID
            WHEN MATCHED THEN UPDATE SET LineName=@N, ModifiedTS=SYSDATETIME()
            WHEN NOT MATCHED THEN INSERT (LineID, LineName, CreatedBy, CreatedTS)
              VALUES (@L, @N, 'seed', SYSDATETIME());
            """, ("@L", "LINE-IMG-01"), ("@N", "Wrapping Line 1"));

        Upsert(conn, """
            MERGE dbo.MD_Equipment AS t
            USING (SELECT @Id AS EquipID) s ON t.EquipID = s.EquipID
            WHEN MATCHED THEN UPDATE SET EquipName=@N, EquipType='IMG', LineID=@L,
                                          MakerModel='Husky-WrapPro', Status='RUN', ActiveFlag=1,
                                          ModifiedTS=SYSDATETIME()
            WHEN NOT MATCHED THEN INSERT (EquipID, EquipName, EquipType, LineID, MakerModel,
                                          Status, ActiveFlag, CreatedBy, CreatedTS)
              VALUES (@Id, @N, 'IMG', @L, 'Husky-WrapPro', 'RUN', 1, 'seed', SYSDATETIME());
            """,
            ("@Id", "IMG-EQ-01"), ("@N", "감싸기 1호기"), ("@L", "LINE-IMG-01"));
        Console.WriteLine("  equip  IMG-EQ-01 감싸기 1호기 → LINE-IMG-01");
    }

    private static void SeedDefectCodes(SqlConnection conn)
    {
        var defs = new (string Code, string Ko, string En)[]
        {
            ("IMG-D01", "리프팅 (들뜸)",      "Lifting"),
            ("IMG-D02", "주름",               "Wrinkle"),
            ("IMG-D03", "본드 번짐",          "Bond Bleed"),
            ("IMG-D04", "원단 찢어짐",        "Fabric Tear"),
            ("IMG-D05", "위치 어긋남",        "Misalignment"),
            ("IMG-D06", "색차 / 오색",        "Color Difference"),
        };
        foreach (var d in defs)
        {
            Upsert(conn, """
                MERGE dbo.MD_DefectCode AS t
                USING (SELECT @C AS DefectCode) s ON t.DefectCode = s.DefectCode
                WHEN MATCHED THEN UPDATE SET DefectName=@N, DefectNameEn=@E,
                                              ProcessCode='IMG', SeverityLevel='MEDIUM',
                                              DispositionDefault='REWORK', Status='Active',
                                              ModifiedTS=SYSDATETIME()
                WHEN NOT MATCHED THEN INSERT (DefectCode, DefectName, DefectNameEn, ProcessCode,
                                              DefectCategory, SeverityLevel, DispositionDefault,
                                              Status, CreatedBy, CreatedTS)
                  VALUES (@C, @N, @E, 'IMG', 'WRAPPING', 'MEDIUM', 'REWORK', 'Active',
                          'seed', SYSDATETIME());
                """,
                ("@C", d.Code), ("@N", d.Ko), ("@E", d.En));
        }
    }

    private static void SeedFabricLots(SqlConnection conn)
    {
        // Fabric rolls live as tbl_Lot rows with ProcessCode='WH' and QualityFlag=ColorCode.
        var rolls = new (string LotCode, string ItemNo, string Color, decimal Metres)[]
        {
            ("FAB-GY-2507-020", "FAB-GY-C03", "GY-C03",  50.0m),
            ("FAB-BK-2507-011", "FAB-BK-C01", "BK-C01", 120.0m),
            ("FAB-RD-2507-005", "FAB-RD-C02", "RD-C02",  85.0m),
        };
        foreach (var r in rolls)
        {
            Upsert(conn, $"""
                MERGE dbo.tbl_Lot AS t
                USING (SELECT @C AS LotCode) s ON t.LotCode = s.LotCode
                WHEN MATCHED THEN UPDATE SET ItemNo=@I, BatchSize=@M, RemainingQty=@M,
                                              ProcessCode='WH', QualityFlag=@Q,
                                              ProducedAt=SYSDATETIME(), Status='OPEN',
                                              ExpiryDate=DATEADD(month,12,GETDATE()),
                                              ModifiedTS=SYSDATETIME()
                WHEN NOT MATCHED THEN INSERT
                    (LotCode, ItemNo, ProcessCode, BatchSize, RemainingQty, ProducedAt,
                     Status, QualityFlag, ExpiryDate, CreatedBy, CreatedTS)
                  VALUES (@C, @I, 'WH', @M, @M, SYSDATETIME(), 'OPEN', @Q,
                          DATEADD(month,12,GETDATE()), 'seed', SYSDATETIME());
                """,
                ("@C", r.LotCode), ("@I", r.ItemNo), ("@Q", r.Color), ("@M", r.Metres.ToString("0.000")));
            Console.WriteLine($"  roll   {r.LotCode,-22} {r.Color}  {r.Metres,6:0.0} m");
        }
    }

    private static void SeedRecipes(SqlConnection conn)
    {
        // IMG also needs an MD_Item for the *finished* wrapped part — fabric is
        // only the raw input. We reuse Door Trim LH from the INJ seed; if it
        // exists fine, if not insert a minimal version.
        Upsert(conn, """
            MERGE dbo.MD_Item AS t
            USING (SELECT @No AS ItemNo) s ON t.ItemNo = s.ItemNo
            WHEN MATCHED THEN UPDATE SET ItemName=@N, ModifiedTS=SYSDATETIME()
            WHEN NOT MATCHED THEN INSERT (ItemNo, ItemName, ItemNameEN, ItemType, ItemCategory,
                                          DefaultUOM, ActiveFlag, CreatedBy, CreatedTS)
              VALUES (@No, @N, 'Door Trim LH Wrapped', 'FINISHED', 'IMG-DOOR',
                      'EA', 1, 'seed', SYSDATETIME());
            """, ("@No", "DR-TRM-LH-W"), ("@N", "도어트림 LH (감싸기 완료)"));

        Upsert(conn, """
            MERGE dbo.MD_Recipe AS t
            USING (SELECT @R AS RecipeID) s ON t.RecipeID = s.RecipeID
            WHEN MATCHED THEN UPDATE SET RecipeName=@N, ItemNo=@I, CycleTime=38,
                                          Status='Active', Version='1.0', ModifiedTS=SYSDATETIME()
            WHEN NOT MATCHED THEN INSERT (RecipeID, RecipeName, RecipeType, ItemNo, CycleTime,
                                          Version, Status, EffectiveDate, CreatedBy, CreatedTS)
              VALUES (@R, @N, 'WRAPPING', @I, 38, '1.0', 'Active', GETDATE(),
                      'seed', SYSDATETIME());
            """, ("@R", "RCP-IMG-A1"), ("@N", "IMG Wrap Recipe"), ("@I", "DR-TRM-LH-W"));
        Console.WriteLine("  recipe RCP-IMG-A1   wrap 38s / DR-TRM-LH-W");
    }

    private static void SeedWorkOrders(SqlConnection conn)
    {
        var wos = new (string No, string Item, int Qty, int Done, int Pri, int DueOffset)[]
        {
            ("WO-IMG-2026-0529-301", "DR-TRM-LH-W", 192, 0, 1, 2),
            ("WO-IMG-2026-0529-302", "DR-TRM-LH-W", 100, 0, 3, 4),
        };
        foreach (var w in wos)
        {
            var sql = $"""
                MERGE dbo.PP_WorkOrder AS t
                USING (SELECT @No AS WoNumber) s ON t.WoNumber = s.WoNumber
                WHEN MATCHED THEN UPDATE SET
                    ItemNo=@I, OrderQty=@Q, OpenQty=@Q, CompletedQty=@D, LineID='LINE-IMG-01',
                    RecipeID='RCP-IMG-A1', Routing='A',
                    PlannedStart=DATEADD(day,-1,SYSDATETIME()),
                    PlannedEnd=DATEADD(day,{w.DueOffset},SYSDATETIME()),
                    DueDate=DATEADD(day,{w.DueOffset},GETDATE()), Status='Released',
                    Priority=@P, ReleasedAt=SYSDATETIME(),
                    TerminalLock=NULL, ActualStart=NULL, ActualEnd=NULL,
                    ModifiedTS=SYSDATETIME()
                WHEN NOT MATCHED THEN INSERT
                    (WoNumber, ItemNo, OrderQty, OpenQty, CompletedQty, ScrapQty, LineID,
                     RecipeID, Routing, PlannedStart, PlannedEnd, DueDate, Status,
                     Priority, ReleasedAt, CreatedBy, CreatedTS)
                  VALUES
                    (@No, @I, @Q, @Q, @D, 0, 'LINE-IMG-01', 'RCP-IMG-A1', 'A',
                     DATEADD(day,-1,SYSDATETIME()), DATEADD(day,{w.DueOffset},SYSDATETIME()),
                     DATEADD(day,{w.DueOffset},GETDATE()), 'Released', @P, SYSDATETIME(),
                     'seed', SYSDATETIME());
                """;
            Upsert(conn, sql,
                ("@No", w.No), ("@I", w.Item),
                ("@Q", w.Qty.ToString()), ("@D", w.Done.ToString()), ("@P", w.Pri.ToString()));
            Console.WriteLine($"  wo     {w.No,-24} {w.Item,-14} {w.Done}/{w.Qty}  D-{w.DueOffset}");
        }
    }

    private static void SeedEquipStatus(SqlConnection conn)
    {
        using var cmd = new SqlCommand("""
            IF NOT EXISTS (SELECT 1 FROM dbo.PR_EquipStatusLog WHERE EquipID='IMG-EQ-01')
              INSERT INTO dbo.PR_EquipStatusLog
                (EquipID, LineID, Status, ReasonCode, StartedAt, CreatedBy, CreatedTS)
              VALUES ('IMG-EQ-01','LINE-IMG-01','RUN','NORMAL', SYSDATETIME(), 'seed', SYSDATETIME());
            """, conn);
        cmd.ExecuteNonQuery();
    }

    // ── In-progress demo state ─────────────────────────────────────────────
    // Brings the IMG dashboard to life: 1 active bond setup + 1 mounted
    // fabric roll + 6 produced cycles (30 EA, 7.5 m consumed) + 1 wrinkle
    // defect. Idempotent: deletes anything tagged 'img-seed' before re-inserting.
    private static void SeedInProgress(SqlConnection conn)
    {
        Console.WriteLine();
        Console.WriteLine("[seed-img] Rebuilding in-progress state ...");

        const string LineId       = "LINE-IMG-01";
        const string OperatorUser = "user-i001";
        const string WoNumber     = "WO-IMG-2026-0529-301";
        const string ItemNo       = "DR-TRM-LH-W";
        const string FabricLotCode= "FAB-GY-2507-020";

        var (woId, fabricRollLotId) = ResolveIds(conn, WoNumber, FabricLotCode);
        if (woId == 0 || fabricRollLotId == 0)
        {
            Console.WriteLine("  ! could not resolve WoID / FabricLotID — skipping.");
            return;
        }

        // 1. wipe previous in-progress seed rows so the script is idempotent.
        ExecRaw(conn, """
            DELETE FROM dbo.PR_BondCycleLog       WHERE CreatedBy = 'img-seed';
            DELETE FROM dbo.PR_DefectDetail       WHERE CreatedBy = 'img-seed';
            DELETE FROM dbo.PR_FabricDeductionLog WHERE CreatedBy = 'img-seed';
            DELETE FROM dbo.PR_ProductionResult   WHERE CreatedBy = 'img-seed';
            DELETE FROM dbo.tbl_Lot               WHERE CreatedBy = 'img-seed';
            DELETE FROM dbo.PR_FabricIssue        WHERE CreatedBy = 'img-seed';
            DELETE FROM dbo.PR_BondSetup          WHERE CreatedBy = 'img-seed';
            """);

        // 2. reset roll back to its initial 50 m + WO progress to 0 so the
        //    counts below land at predictable numbers.
        ExecRaw(conn, $"""
            UPDATE dbo.tbl_Lot
            SET    RemainingQty = BatchSize, Status='OPEN', ModifiedTS=SYSDATETIME()
            WHERE  LotCode = '{FabricLotCode}';

            UPDATE dbo.PP_WorkOrder
            SET    CompletedQty=0, Status='In Progress', ActualStart=SYSDATETIME(),
                   TerminalLock='POP-DEV-01', ModifiedTS=SYSDATETIME()
            WHERE  WoID = {woId};
            """);

        // 3. Bond setup APPLIED
        var bondSetupId = ExecScalar<int>(conn, $"""
            INSERT INTO dbo.PR_BondSetup
                (WoID, LineID, RecipeID, PressureSp, TempSp, HoldSecSp, TensionSp,
                 LoadedAt, LoadedBy, Status, CreatedBy, CreatedTS)
            OUTPUT INSERTED.BondSetupID
            VALUES ({woId}, '{LineId}', 'RCP-IMG-A1', 4.2, 90, 120, 20,
                    SYSDATETIME(), '{OperatorUser}', 'APPLIED',
                    'img-seed', SYSDATETIME());
            """);
        Console.WriteLine($"  bond   id={bondSetupId}  90°C / 4.2 bar / 120 s · APPLIED");

        // 4. Mount the grey fabric roll
        var fabricIssueId = ExecScalar<int>(conn, $"""
            INSERT INTO dbo.PR_FabricIssue
                (WoID, FabricRollLotID, ColorCode, MountedAt, InitialRemainingM,
                 OperatorID, LineID, CreatedBy, CreatedTS)
            OUTPUT INSERTED.FabricIssueID
            VALUES ({woId}, {fabricRollLotId}, 'GY-C03',
                    DATEADD(minute, -180, SYSDATETIME()), 50.0,
                    '{OperatorUser}', '{LineId}', 'img-seed', SYSDATETIME());
            """);
        Console.WriteLine($"  mount  id={fabricIssueId}  {FabricLotCode} (GY-C03) on {LineId}");

        // 5. Six production cycles spread over the last 3 hours, 5 EA each.
        //    Each cycle deducts 1.25 m. Total: 30 EA produced, 7.5 m consumed.
        const decimal metresPerUnit = 0.25m;
        const int     qtyPerCycle   = 5;
        int producedTotal = 0;
        decimal consumedTotal = 0m;
        int? firstResultIdForDefect = null;
        for (var i = 0; i < 6; i++)
        {
            var minutesAgo = (6 - i) * 25;   // 150, 125, 100, 75, 50, 25 minutes ago
            var consumed   = qtyPerCycle * metresPerUnit;

            // tbl_Lot for the produced batch
            var lotId = ExecScalar<int>(conn, $"""
                INSERT INTO dbo.tbl_Lot
                    (LotCode, ItemNo, WoID, LineID, ProcessCode, BatchSize, RemainingQty,
                     ProducedAt, Status, QualityFlag, CreatedBy, CreatedTS)
                OUTPUT INSERTED.LotID
                VALUES (CONCAT('L', FORMAT(DATEADD(minute, -{minutesAgo}, SYSDATETIME()), 'yyMMddHHmmss'), '-IMG'),
                        '{ItemNo}', {woId}, '{LineId}', 'IMG',
                        {qtyPerCycle}, {qtyPerCycle},
                        DATEADD(minute, -{minutesAgo}, SYSDATETIME()),
                        'OPEN', 'PENDING', 'img-seed', SYSDATETIME());
                """);

            // EntryNo column is VARCHAR(28); keep it short:
            //   IMG-YYMMDD-HHmmss-{i}   ≈ 21 chars
            var resultId = ExecScalar<int>(conn, $"""
                INSERT INTO dbo.PR_ProductionResult
                    (EntryNo, WoID, LotID, LineID, ProcessCode, GoodQty, CycleSec,
                     FabricRollID, FabricConsumedM, BondTempAvg,
                     OperatorID, DefectFlag, EntryAt, CreatedBy, CreatedTS)
                OUTPUT INSERTED.ResultID
                VALUES (CONCAT('IMG-', FORMAT(GETDATE(),'yyMMdd-HHmmss'), '-{i}'),
                        {woId}, {lotId}, '{LineId}', 'IMG', {qtyPerCycle}, 38,
                        {fabricRollLotId}, {consumed.ToString(System.Globalization.CultureInfo.InvariantCulture)},
                        {(90.0m + (i % 3) * 0.4m).ToString(System.Globalization.CultureInfo.InvariantCulture)},
                        '{OperatorUser}', 0,
                        DATEADD(minute, -{minutesAgo}, SYSDATETIME()),
                        'img-seed', SYSDATETIME());
                """);

            // fabric deduction log
            ExecRaw(conn, $"""
                INSERT INTO dbo.PR_FabricDeductionLog
                    (FabricRollLotID, ResultID, ConsumedM, BeforeM, AfterM,
                     DeductedAt, CreatedBy, CreatedTS)
                VALUES ({fabricRollLotId}, {resultId},
                        {consumed.ToString(System.Globalization.CultureInfo.InvariantCulture)},
                        {(50m - consumedTotal).ToString(System.Globalization.CultureInfo.InvariantCulture)},
                        {(50m - consumedTotal - consumed).ToString(System.Globalization.CultureInfo.InvariantCulture)},
                        DATEADD(minute, -{minutesAgo}, SYSDATETIME()),
                        'img-seed', SYSDATETIME());
                """);

            // bond cycle PLC sample
            ExecRaw(conn, $"""
                INSERT INTO dbo.PR_BondCycleLog
                    (ResultID, BondSetupID, PressureAvg, TempAvg, HoldActualSec,
                     TensionAvg, WithinSpec, SampledAt, CreatedBy, CreatedTS)
                VALUES ({resultId}, {bondSetupId}, 4.2,
                        {(90.0m + (i % 3) * 0.4m).ToString(System.Globalization.CultureInfo.InvariantCulture)},
                        120, 20, 1,
                        DATEADD(minute, -{minutesAgo}, SYSDATETIME()),
                        'img-seed', SYSDATETIME());
                """);

            producedTotal += qtyPerCycle;
            consumedTotal += consumed;
            firstResultIdForDefect ??= resultId;
        }
        Console.WriteLine($"  prod   {producedTotal} EA across 6 cycles · {consumedTotal:0.0} m consumed");

        // 6. Deduct the consumed fabric from the roll + bump WO completed qty.
        ExecRaw(conn, $"""
            UPDATE dbo.tbl_Lot
            SET    RemainingQty = BatchSize - {consumedTotal.ToString(System.Globalization.CultureInfo.InvariantCulture)},
                   ModifiedTS   = SYSDATETIME()
            WHERE  LotID = {fabricRollLotId};

            UPDATE dbo.PP_WorkOrder
            SET    CompletedQty = {producedTotal}, ModifiedTS = SYSDATETIME()
            WHERE  WoID = {woId};
            """);

        // 7. One wrinkle defect attached to the first cycle so defect rate > 0.
        ExecRaw(conn, $"""
            INSERT INTO dbo.PR_DefectDetail
                (ResultID, WoID, ProcessCode, DefectCode, Qty,
                 ReasonNote, DetectedAt, RegisteredBy, CreatedBy, CreatedTS)
            VALUES ({firstResultIdForDefect}, {woId}, 'IMG', 'IMG-D02', 1,
                    'Tension drift mid-cycle.',
                    DATEADD(minute, -120, SYSDATETIME()),
                    '{OperatorUser}', 'img-seed', SYSDATETIME());
            """);
        Console.WriteLine($"  defect IMG-D02 Wrinkle  qty=1  → defect rate ≈ {1.0 * 100 / (producedTotal + 1):0.0}%");
    }

    private static (int WoId, int FabricLotId) ResolveIds(SqlConnection conn, string woNumber, string fabricLotCode)
    {
        int woId = ExecScalar<int>(conn,
            $"SELECT ISNULL((SELECT TOP 1 WoID FROM dbo.PP_WorkOrder WHERE WoNumber='{woNumber}'),0);");
        int lotId = ExecScalar<int>(conn,
            $"SELECT ISNULL((SELECT TOP 1 LotID FROM dbo.tbl_Lot WHERE LotCode='{fabricLotCode}'),0);");
        return (woId, lotId);
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

    // ───────────────────────────────────────────────────────────────────
    private static void Upsert(SqlConnection conn, string sql, params (string Name, string Value)[] pars)
    {
        using var cmd = new SqlCommand(sql, conn);
        foreach (var (n, v) in pars)
            cmd.Parameters.Add(n, SqlDbType.NVarChar).Value = (object?)v ?? DBNull.Value;
        cmd.ExecuteNonQuery();
    }
}
