using System.Data;
using Microsoft.Data.SqlClient;

namespace AMES.Tools.SeedInjDemo;

/// <summary>
/// Idempotent demo seed for the INJ (Injection) module — enough rows so all
/// INJ POP screens have something to show on a freshly-applied schema.
///
///   MD_Item     × 3   Door Trim LH / RH, Console Upper
///   MD_Mold     × 4   DT-A1 / DT-A1-R (replacement) / DT-B1 / CSL-B2
///   MD_Recipe   × 3   one per Item
///   MD_Equipment× 1   사출기 #1 on LINE-INJ-01
///   MD_DefectCode × 6 INJ-D01 .. INJ-D06
///   PP_WorkOrder × 3  Door Trim LH (D-1 high), Door Trim RH, Console Upper
///   PR_EquipStatusLog: one initial RUN row for 사출기 #1
///
/// Run:
///   dotnet run --project tools/seed_inj_demo
/// </summary>
internal static class Program
{
    private const string Cs =
        "Server=localhost;Database=AMES_DEV;User Id=ames_app;Password=!Dev2026;" +
        "TrustServerCertificate=True;Encrypt=True;Connect Timeout=5;";

    private static int Main()
    {
        Console.WriteLine("[seed-inj] Connecting to AMES_DEV ...");
        using var conn = new SqlConnection(Cs);
        conn.Open();

        SeedItems       (conn);
        SeedMolds       (conn);
        SeedRecipes     (conn);
        SeedEquipment   (conn);
        SeedDefectCodes (conn);
        SeedWorkOrders  (conn);
        SeedEquipStatus (conn);

        Console.WriteLine();
        Console.WriteLine("[seed-inj] Done. INJ POP screens now have demo data.");
        return 0;
    }

    // ───────────────────────────────────────────────────────────────────────
    private static void SeedItems(SqlConnection conn)
    {
        var items = new (string No, string Name, string NameEn, string Cat, string Uom)[]
        {
            ("DR-TRM-LH-A1", "도어트림 LH",     "Door Trim LH",   "INJ-DOOR",    "EA"),
            ("DR-TRM-RH-A1", "도어트림 RH",     "Door Trim RH",   "INJ-DOOR",    "EA"),
            ("CSL-UP-B2",    "콘솔 어퍼",       "Console Upper",  "INJ-CONSOLE", "EA"),
        };
        foreach (var i in items)
        {
            Upsert(conn, """
                MERGE dbo.MD_Item AS t
                USING (SELECT @No AS ItemNo) s ON t.ItemNo = s.ItemNo
                WHEN MATCHED THEN UPDATE SET ItemName=@N, ItemNameEN=@NE, ItemType='FINISHED',
                                              ItemCategory=@C, DefaultUOM=@U, ActiveFlag=1, ModifiedTS=SYSDATETIME()
                WHEN NOT MATCHED THEN INSERT (ItemNo, ItemName, ItemNameEN, ItemType, ItemCategory,
                                              DefaultUOM, ActiveFlag, CreatedBy, CreatedTS)
                  VALUES (@No, @N, @NE, 'FINISHED', @C, @U, 1, 'seed', SYSDATETIME());
                """,
                ("@No", i.No), ("@N", i.Name), ("@NE", i.NameEn), ("@C", i.Cat), ("@U", i.Uom));
            Console.WriteLine($"  item  {i.No,-16} {i.NameEn}");
        }
    }

    private static void SeedMolds(SqlConnection conn)
    {
        var molds = new (string Id, string Name, int Rated, int Current, string Status)[]
        {
            ("MOLD-DT-A1",   "Door Trim LH Mold (2-cav)",  10000, 8800, "Mounted"),    // 88%
            ("MOLD-DT-A1-R", "Door Trim LH Mold (spare)",  10000,    0, "Available"),
            ("MOLD-DT-B1",   "Door Trim RH Mold (2-cav)",  10000, 4500, "Available"),  // 45%
            ("MOLD-CSL-B2",  "Console Upper Mold",          8000, 1200, "Available"),  // 15%
        };
        foreach (var m in molds)
        {
            Upsert(conn, """
                MERGE dbo.MD_Mold AS t
                USING (SELECT @Id AS MoldID) s ON t.MoldID = s.MoldID
                WHEN MATCHED THEN UPDATE SET MoldName=@N, RatedShots=@R, CurrentShots=@C,
                                              CavityCount=2, Status=@S, ModifiedTS=SYSDATETIME()
                WHEN NOT MATCHED THEN INSERT (MoldID, MoldName, RatedShots, CurrentShots, CavityCount,
                                              Status, CreatedBy, CreatedTS)
                  VALUES (@Id, @N, @R, @C, 2, @S, 'seed', SYSDATETIME());
                """,
                ("@Id", m.Id), ("@N", m.Name),
                ("@R", m.Rated.ToString()), ("@C", m.Current.ToString()), ("@S", m.Status));
            Console.WriteLine($"  mold  {m.Id,-14} {m.Current}/{m.Rated} ({m.Status})");
        }
    }

    private static void SeedRecipes(SqlConnection conn)
    {
        var recipes = new (string Id, string Name, string ItemNo, int CT)[]
        {
            ("RCP-DT-A1",  "Door Trim LH Recipe", "DR-TRM-LH-A1", 42),
            ("RCP-DT-B1",  "Door Trim RH Recipe", "DR-TRM-RH-A1", 40),
            ("RCP-CSL-B2", "Console Recipe",      "CSL-UP-B2",    55),
        };
        foreach (var r in recipes)
        {
            Upsert(conn, """
                MERGE dbo.MD_Recipe AS t
                USING (SELECT @Id AS RecipeID) s ON t.RecipeID = s.RecipeID
                WHEN MATCHED THEN UPDATE SET RecipeName=@N, ItemNo=@I, CycleTime=@C,
                                              Status='Active', Version='1.0', ModifiedTS=SYSDATETIME()
                WHEN NOT MATCHED THEN INSERT (RecipeID, RecipeName, RecipeType, ItemNo, CycleTime,
                                              Version, Status, EffectiveDate, CreatedBy, CreatedTS)
                  VALUES (@Id, @N, 'INJECTION', @I, @C, '1.0', 'Active', GETDATE(), 'seed', SYSDATETIME());
                """,
                ("@Id", r.Id), ("@N", r.Name), ("@I", r.ItemNo), ("@C", r.CT.ToString()));
        }
    }

    private static void SeedEquipment(SqlConnection conn)
    {
        Upsert(conn, """
            MERGE dbo.MD_Equipment AS t
            USING (SELECT @Id AS EquipID) s ON t.EquipID = s.EquipID
            WHEN MATCHED THEN UPDATE SET EquipName=@N, EquipType='INJ', LineID=@L,
                                          MakerModel='Toshiba 650T', Status='RUN', ActiveFlag=1,
                                          ModifiedTS=SYSDATETIME()
            WHEN NOT MATCHED THEN INSERT (EquipID, EquipName, EquipType, LineID, MakerModel,
                                          Status, ActiveFlag, CreatedBy, CreatedTS)
              VALUES (@Id, @N, 'INJ', @L, 'Toshiba 650T', 'RUN', 1, 'seed', SYSDATETIME());
            """,
            ("@Id", "INJ-EQ-01"), ("@N", "사출기 #1 (650T)"), ("@L", "LINE-INJ-01"));
        Console.WriteLine("  equip INJ-EQ-01 사출기 #1 → LINE-INJ-01");
    }

    private static void SeedDefectCodes(SqlConnection conn)
    {
        var defs = new (string Code, string Ko, string En)[]
        {
            ("INJ-D01", "싱크마크",        "Sink Mark"),
            ("INJ-D02", "미성형",          "Short Shot"),
            ("INJ-D03", "플래시 / 버",     "Flash (Burr)"),
            ("INJ-D04", "웰드라인",        "Weld Line"),
            ("INJ-D05", "변형",            "Deformation"),
            ("INJ-D06", "색차",            "Color Difference"),
        };
        foreach (var d in defs)
        {
            Upsert(conn, """
                MERGE dbo.MD_DefectCode AS t
                USING (SELECT @C AS DefectCode) s ON t.DefectCode = s.DefectCode
                WHEN MATCHED THEN UPDATE SET DefectName=@N, DefectNameEn=@E,
                                              ProcessCode='INJ', SeverityLevel='MEDIUM',
                                              DispositionDefault='REWORK', Status='Active',
                                              ModifiedTS=SYSDATETIME()
                WHEN NOT MATCHED THEN INSERT (DefectCode, DefectName, DefectNameEn, ProcessCode,
                                              DefectCategory, SeverityLevel, DispositionDefault,
                                              Status, CreatedBy, CreatedTS)
                  VALUES (@C, @N, @E, 'INJ', 'MOLDING', 'MEDIUM', 'REWORK', 'Active',
                          'seed', SYSDATETIME());
                """,
                ("@C", d.Code), ("@N", d.Ko), ("@E", d.En));
        }
    }

    private static void SeedWorkOrders(SqlConnection conn)
    {
        // Idempotent via WoNumber check (WoID is identity, so MERGE is on natural key WoNumber)
        var wos = new (string No, string Item, int Qty, int Done, int Pri, int DueOffset,
                       string Mold, string Recipe)[]
        {
            ("WO-2026-0529-007", "DR-TRM-LH-A1", 200, 128, 1,  1, "MOLD-DT-A1",  "RCP-DT-A1"),
            ("WO-2026-0529-012", "DR-TRM-RH-A1", 100,   0, 3,  3, "MOLD-DT-B1",  "RCP-DT-B1"),
            ("WO-2026-0529-019", "CSL-UP-B2",     64,   0, 3,  5, "MOLD-CSL-B2", "RCP-CSL-B2"),
        };
        foreach (var w in wos)
        {
            // Inline DueOffset to avoid sqlserver's nvarchar→int coercion mismatch in DATEADD.
            var sql = $"""
                MERGE dbo.PP_WorkOrder AS t
                USING (SELECT @No AS WoNumber) s ON t.WoNumber = s.WoNumber
                WHEN MATCHED THEN UPDATE SET
                    ItemNo=@I, OrderQty=@Q, OpenQty=@Q, CompletedQty=@D, LineID='LINE-INJ-01',
                    MoldID=@M, RecipeID=@R, Routing='A',
                    PlannedStart=DATEADD(day,-1,SYSDATETIME()),
                    PlannedEnd=DATEADD(day,{w.DueOffset},SYSDATETIME()),
                    DueDate=DATEADD(day,{w.DueOffset},GETDATE()), Status='Released',
                    Priority=@P, ReleasedAt=SYSDATETIME(),
                    TerminalLock=NULL, ActualStart=NULL, ActualEnd=NULL,
                    ModifiedTS=SYSDATETIME()
                WHEN NOT MATCHED THEN INSERT
                    (WoNumber, ItemNo, OrderQty, OpenQty, CompletedQty, ScrapQty, LineID,
                     MoldID, RecipeID, Routing, PlannedStart, PlannedEnd, DueDate, Status,
                     Priority, ReleasedAt, CreatedBy, CreatedTS)
                  VALUES
                    (@No, @I, @Q, @Q, @D, 0, 'LINE-INJ-01', @M, @R, 'A',
                     DATEADD(day,-1,SYSDATETIME()), DATEADD(day,{w.DueOffset},SYSDATETIME()),
                     DATEADD(day,{w.DueOffset},GETDATE()), 'Released', @P, SYSDATETIME(),
                     'seed', SYSDATETIME());
                """;
            Upsert(conn, sql,
                ("@No",  w.No), ("@I", w.Item),
                ("@Q",   w.Qty.ToString()),
                ("@D",   w.Done.ToString()),
                ("@P",   w.Pri.ToString()),
                ("@M",   w.Mold), ("@R", w.Recipe));
            Console.WriteLine($"  wo    {w.No,-22} {w.Item,-14} {w.Done}/{w.Qty}  D-{w.DueOffset}  P{w.Pri}");
        }
    }

    private static void SeedEquipStatus(SqlConnection conn)
    {
        // One snapshot row so the dashboard has something to read.
        using var cmd = new SqlCommand("""
            IF NOT EXISTS (SELECT 1 FROM dbo.PR_EquipStatusLog WHERE EquipID='INJ-EQ-01')
              INSERT INTO dbo.PR_EquipStatusLog
                (EquipID, LineID, Status, ReasonCode, StartedAt, CreatedBy, CreatedTS)
              VALUES ('INJ-EQ-01','LINE-INJ-01','RUN','NORMAL', SYSDATETIME(), 'seed', SYSDATETIME());
            """, conn);
        cmd.ExecuteNonQuery();
    }

    // ───────────────────────────────────────────────────────────────────────
    private static void Upsert(SqlConnection conn, string sql, params (string Name, string Value)[] pars)
    {
        using var cmd = new SqlCommand(sql, conn);
        foreach (var (n, v) in pars)
            cmd.Parameters.Add(n, SqlDbType.NVarChar).Value = (object?)v ?? DBNull.Value;
        cmd.ExecuteNonQuery();
    }
}
