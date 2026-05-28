// ════════════════════════════════════════════════════════════════════════
//  A-MES · SQL Server Connection Smoke Test
//  Validates that the dev environment can:
//   1. Connect to AMES_DEV via ames_app (SQL Authentication)
//   2. Run plain queries (SELECT @@VERSION etc.)
//   3. Create a stored procedure (mimics planned tech-stack pattern)
//   4. Call the SP with parameters + read results
//   5. INSERT/SELECT/DELETE roundtrip (CRUD)
//   6. Drop test objects (cleanup)
//  Tech stack matched: .NET 9 + Microsoft.Data.SqlClient + ADO.NET + SP
// ════════════════════════════════════════════════════════════════════════
using Microsoft.Data.SqlClient;
using System.Diagnostics;

const string CONN_STR =
    "Server=localhost;Database=AMES_DEV;User Id=ames_app;Password=!Dev2026;" +
    "TrustServerCertificate=True;Encrypt=True;Connect Timeout=5;";

var results = new List<(string Step, bool Pass, string Detail, long Ms)>();
var totalSw = Stopwatch.StartNew();

void Record(string step, bool pass, string detail, long ms) {
    results.Add((step, pass, detail, ms));
    var status = pass ? "✓ PASS" : "✗ FAIL";
    var color = pass ? ConsoleColor.Green : ConsoleColor.Red;
    var prev = Console.ForegroundColor;
    Console.ForegroundColor = color;
    Console.Write($"  {status}  ");
    Console.ForegroundColor = prev;
    Console.WriteLine($"[{ms,4}ms]  {step}  →  {detail}");
}

Console.WriteLine("╔════════════════════════════════════════════════════════════════╗");
Console.WriteLine("║  A-MES SQL Server Connection Smoke Test                        ║");
Console.WriteLine("║  Target: AMES_DEV @ localhost · ames_app                       ║");
Console.WriteLine("╚════════════════════════════════════════════════════════════════╝\n");

SqlConnection? conn = null;

// ─── Step 1 · Open connection ──────────────────────────────────────────
try {
    var sw = Stopwatch.StartNew();
    conn = new SqlConnection(CONN_STR);
    conn.Open();
    sw.Stop();
    Record("Connection.Open",
        conn.State == System.Data.ConnectionState.Open,
        $"State={conn.State}, ServerVersion={conn.ServerVersion}",
        sw.ElapsedMilliseconds);
} catch (Exception ex) {
    Record("Connection.Open", false, $"EXCEPTION: {ex.GetType().Name} - {ex.Message}", 0);
    Console.WriteLine("\n  ⚠ Connection failed - aborting remaining tests.");
    PrintSummary();
    return 1;
}

// ─── Step 2 · SELECT @@VERSION, current login, current DB ──────────────
try {
    var sw = Stopwatch.StartNew();
    using var cmd = new SqlCommand(
        @"SELECT
            CAST(SERVERPROPERTY('ProductVersion') AS NVARCHAR(20))  AS Version,
            CAST(SERVERPROPERTY('Edition')         AS NVARCHAR(100)) AS Edition,
            SUSER_NAME()                            AS LoginAs,
            USER_NAME()                             AS DbUserAs,
            DB_NAME()                               AS CurrentDb,
            (SELECT COUNT(*) FROM sys.databases)    AS DbCount;",
        conn);
    using var rdr = cmd.ExecuteReader();
    if (rdr.Read()) {
        var detail = $"v{rdr["Version"]}, login={rdr["LoginAs"]}, db_user={rdr["DbUserAs"]}, " +
                     $"current_db={rdr["CurrentDb"]}, total_dbs={rdr["DbCount"]}";
        sw.Stop();
        Record("Plain SELECT (server info)", true, detail, sw.ElapsedMilliseconds);
    }
} catch (Exception ex) {
    Record("Plain SELECT (server info)", false, $"{ex.GetType().Name}: {ex.Message}", 0);
}

// ─── Step 3 · CREATE test table ────────────────────────────────────────
try {
    var sw = Stopwatch.StartNew();
    using var cmd = new SqlCommand(
        @"IF OBJECT_ID('dbo.tbl_SmokeTest', 'U') IS NOT NULL DROP TABLE dbo.tbl_SmokeTest;
          CREATE TABLE dbo.tbl_SmokeTest (
            ID         INT IDENTITY(1,1) PRIMARY KEY,
            ItemNo     VARCHAR(20) NOT NULL,
            ItemName   NVARCHAR(80) NOT NULL,
            Qty        DECIMAL(12,3) NOT NULL DEFAULT 0,
            CreatedAt  DATETIME2 NOT NULL DEFAULT SYSDATETIME()
          );", conn);
    cmd.ExecuteNonQuery();
    sw.Stop();
    Record("CREATE TABLE dbo.tbl_SmokeTest", true, "5 columns + IDENTITY + DEFAULT", sw.ElapsedMilliseconds);
} catch (Exception ex) {
    Record("CREATE TABLE", false, $"{ex.GetType().Name}: {ex.Message}", 0);
}

// ─── Step 4 · CREATE Stored Procedure (planned tech stack pattern) ─────
try {
    var sw = Stopwatch.StartNew();
    using var cmd1 = new SqlCommand(
        @"IF OBJECT_ID('dbo.SP_SmokeTest_Insert', 'P') IS NOT NULL DROP PROCEDURE dbo.SP_SmokeTest_Insert;",
        conn);
    cmd1.ExecuteNonQuery();

    using var cmd2 = new SqlCommand(
        @"CREATE PROCEDURE dbo.SP_SmokeTest_Insert
            @ItemNo   VARCHAR(20),
            @ItemName NVARCHAR(80),
            @Qty      DECIMAL(12,3),
            @NewID    INT OUTPUT
          AS
          BEGIN
            SET NOCOUNT ON;
            INSERT INTO dbo.tbl_SmokeTest (ItemNo, ItemName, Qty) VALUES (@ItemNo, @ItemName, @Qty);
            SET @NewID = SCOPE_IDENTITY();
          END;", conn);
    cmd2.ExecuteNonQuery();
    sw.Stop();
    Record("CREATE PROCEDURE SP_SmokeTest_Insert", true, "3 input + 1 output param", sw.ElapsedMilliseconds);
} catch (Exception ex) {
    Record("CREATE PROCEDURE", false, $"{ex.GetType().Name}: {ex.Message}", 0);
}

// ─── Step 5 · Call SP with parameters (the planned production pattern) ─
var insertedIds = new List<int>();
try {
    var sw = Stopwatch.StartNew();
    var samples = new (string code, string name, decimal qty)[] {
        ("GRILLE-02",   "Radiator Grille - Black",   100m),
        ("CONS-ASM-01", "Console Assembly Upper",     50m),
        ("DOOR-TRIM-LH","Door Trim LH (Gray)",       200.500m),
    };
    foreach (var s in samples) {
        using var cmd = new SqlCommand("dbo.SP_SmokeTest_Insert", conn);
        cmd.CommandType = System.Data.CommandType.StoredProcedure;
        cmd.Parameters.Add("@ItemNo",   System.Data.SqlDbType.VarChar, 20).Value  = s.code;
        cmd.Parameters.Add("@ItemName", System.Data.SqlDbType.NVarChar, 80).Value = s.name;
        cmd.Parameters.Add("@Qty",      System.Data.SqlDbType.Decimal).Value      = s.qty;
        var outId = cmd.Parameters.Add("@NewID", System.Data.SqlDbType.Int);
        outId.Direction = System.Data.ParameterDirection.Output;
        cmd.ExecuteNonQuery();
        insertedIds.Add((int)outId.Value);
    }
    sw.Stop();
    Record("EXEC SP_SmokeTest_Insert × 3",
        insertedIds.Count == 3,
        $"Inserted IDs: [{string.Join(", ", insertedIds)}]",
        sw.ElapsedMilliseconds);
} catch (Exception ex) {
    Record("EXEC Stored Procedure", false, $"{ex.GetType().Name}: {ex.Message}", 0);
}

// ─── Step 6 · SELECT back the inserted rows ────────────────────────────
try {
    var sw = Stopwatch.StartNew();
    using var cmd = new SqlCommand(
        "SELECT ID, ItemNo, ItemName, Qty, CreatedAt FROM dbo.tbl_SmokeTest ORDER BY ID;",
        conn);
    using var rdr = cmd.ExecuteReader();
    var rows = new List<string>();
    while (rdr.Read()) {
        rows.Add($"#{rdr["ID"]} {rdr["ItemNo"],-15} {rdr["ItemName"],-28} qty={rdr["Qty"],8}");
    }
    rdr.Close();
    sw.Stop();
    Record("SELECT all rows", rows.Count == 3, $"{rows.Count} rows returned", sw.ElapsedMilliseconds);
    Console.WriteLine();
    foreach (var r in rows) Console.WriteLine("           " + r);
    Console.WriteLine();
} catch (Exception ex) {
    Record("SELECT rows", false, $"{ex.GetType().Name}: {ex.Message}", 0);
}

// ─── Step 7 · Transaction (BEGIN / UPDATE / COMMIT) ────────────────────
try {
    var sw = Stopwatch.StartNew();
    using var txn = conn.BeginTransaction();
    using (var cmd = new SqlCommand(
        "UPDATE dbo.tbl_SmokeTest SET Qty = Qty * 2 WHERE ItemNo = @c;", conn, txn))
    {
        cmd.Parameters.AddWithValue("@c", "GRILLE-02");
        var affected = cmd.ExecuteNonQuery();
        sw.Stop();
        if (affected == 1) {
            txn.Commit();
            Record("Transaction (UPDATE + COMMIT)", true, $"{affected} row updated, committed", sw.ElapsedMilliseconds);
        } else {
            txn.Rollback();
            Record("Transaction", false, $"Expected 1 row affected, got {affected}", sw.ElapsedMilliseconds);
        }
    }
} catch (Exception ex) {
    Record("Transaction", false, $"{ex.GetType().Name}: {ex.Message}", 0);
}

// ─── Step 8 · DELETE + cleanup ─────────────────────────────────────────
try {
    var sw = Stopwatch.StartNew();
    using var cmd = new SqlCommand(
        @"DELETE FROM dbo.tbl_SmokeTest;
          DROP PROCEDURE dbo.SP_SmokeTest_Insert;
          DROP TABLE dbo.tbl_SmokeTest;", conn);
    cmd.ExecuteNonQuery();
    sw.Stop();
    Record("Cleanup (DELETE + DROP PROC + DROP TABLE)", true, "test artifacts removed", sw.ElapsedMilliseconds);
} catch (Exception ex) {
    Record("Cleanup", false, $"{ex.GetType().Name}: {ex.Message}", 0);
}

// ─── Step 9 · Connection pool / re-open ────────────────────────────────
try {
    var sw = Stopwatch.StartNew();
    conn.Close();
    conn.Open();   // Should hit the pool (very fast)
    sw.Stop();
    Record("Connection pool reuse (Close+Open)",
        conn.State == System.Data.ConnectionState.Open,
        $"State={conn.State}, took {sw.ElapsedMilliseconds}ms (pool hit if <5ms)",
        sw.ElapsedMilliseconds);
} catch (Exception ex) {
    Record("Connection pool", false, $"{ex.GetType().Name}: {ex.Message}", 0);
}

conn?.Close();
conn?.Dispose();
totalSw.Stop();
PrintSummary();
return results.All(r => r.Pass) ? 0 : 2;

void PrintSummary() {
    Console.WriteLine();
    Console.WriteLine("──────────────────────────────────────────────────────────────────");
    var passed = results.Count(r => r.Pass);
    var failed = results.Count - passed;
    var allOk = failed == 0;
    var prev = Console.ForegroundColor;
    Console.ForegroundColor = allOk ? ConsoleColor.Green : ConsoleColor.Red;
    Console.WriteLine($"  {(allOk ? "✓ ALL TESTS PASSED" : $"✗ {failed} TEST(S) FAILED")}  ({passed}/{results.Count} steps, {totalSw.ElapsedMilliseconds}ms total)");
    Console.ForegroundColor = prev;
    Console.WriteLine("──────────────────────────────────────────────────────────────────");
}
