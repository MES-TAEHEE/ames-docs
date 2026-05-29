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
        SeedEquipStatus   (conn);

        Console.WriteLine();
        Console.WriteLine("[seed-img] Done. IMG POP screens now have demo data.");
        Console.WriteLine();
        Console.WriteLine("To test IMG locally, edit appsettings.json:");
        Console.WriteLine("  PopTerminal.LineId    = \"LINE-IMG-01\"");
        Console.WriteLine("  PopTerminal.StationId = \"POP-IMG-01\"");
        Console.WriteLine("then F5 — login routes to IMG-02 automatically.");
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

    // ───────────────────────────────────────────────────────────────────
    private static void Upsert(SqlConnection conn, string sql, params (string Name, string Value)[] pars)
    {
        using var cmd = new SqlCommand(sql, conn);
        foreach (var (n, v) in pars)
            cmd.Parameters.Add(n, SqlDbType.NVarChar).Value = (object?)v ?? DBNull.Value;
        cmd.ExecuteNonQuery();
    }
}
