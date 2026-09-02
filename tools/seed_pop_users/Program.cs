using System.Data;
using AMES.Data.Security;
using Microsoft.Data.SqlClient;

namespace AMES.Tools.SeedPopUsers;

/// <summary>
/// Idempotent seed for POP login testing.
/// Inserts 4 users into AspNetUsers + SYS_UserProfile:
///
///   EmployeeNo  Name              Role        PIN     Lines
///   ----------  ----------------  ----------  ------  ---------------
///   E001        Kim Min-jun       Operator    1234    LINE-INJ-01
///   E002        Lee Soo-yeon      Operator    2345    LINE-INJ-01
///   E003        Park Hyun-woo     Operator    3456    LINE-INJ-01,LINE-INJ-02
///   S001        Supervisor Choi   Supervisor  9999    (all lines)
///
/// Run from the repo root:
///   dotnet run --project tools/seed_pop_users
/// </summary>
internal static class Program
{
    private const string ConnectionString =
        "Server=localhost;Database=AMES_DEV;User Id=ames_app;Password=!Dev2026;" +
        "TrustServerCertificate=True;Encrypt=True;Connect Timeout=5;";

    private static readonly (string Id, string EmpNo, string Name, string Dept, string Pin,
        string? Lines, string Shift)[] Users =
    {
        // ── INJ operators (사출) ───────────────────────────────────────
        ("user-e001", "E001", "Kim Min-jun",     "Production", "1234",
            """["LINE-INJ-01"]""",                                            "DAY"),
        ("user-e002", "E002", "Lee Soo-yeon",    "Production", "2345",
            """["LINE-INJ-01"]""",                                            "DAY"),
        ("user-e003", "E003", "Park Hyun-woo",   "Production", "3456",
            """["LINE-INJ-01","LINE-INJ-02"]""",                              "NIGHT"),

        // ── IMG operators (감싸기) ─────────────────────────────────────
        ("user-i001", "I001", "Choi Joo-won",    "Production", "1234",
            """["LINE-IMG-01"]""",                                            "DAY"),
        ("user-i002", "I002", "Han Ji-hye",      "Production", "2345",
            """["LINE-IMG-01"]""",                                            "DAY"),

        // ── PNT operators (분체도장) ───────────────────────────────────
        ("user-p001", "P001", "Jeong Min-su",    "Production", "1234",
            """["LINE-PNT-01"]""",                                            "DAY"),
        ("user-p002", "P002", "Yoon Eun-bi",     "Production", "2345",
            """["LINE-PNT-01"]""",                                            "DAY"),

        // ── QC inspectors (품질) ───────────────────────────────────────
        ("user-q001", "Q001", "Kim Hye-jin",     "Quality",    "1234",
            """["LINE-QC-01"]""",                                             "DAY"),
        ("user-q002", "Q002", "Park Jae-won",    "Quality",    "2345",
            """["LINE-QC-01"]""",                                             "DAY"),

        // ── Supervisor with access to all lines ────────────────────────
        ("user-s001", "S001", "Supervisor Choi", "Production", "9999",
            null /* all lines */,                                             "DAY"),
    };

    private static int Main()
    {
        Console.WriteLine("[seed] Connecting to AMES_DEV ...");
        using var conn = new SqlConnection(ConnectionString);
        conn.Open();

        foreach (var u in Users)
        {
            var pinHash = PinHasher.Hash(u.Pin);
            UpsertAspNetUser  (conn, u.Id, u.EmpNo);
            UpsertUserProfile (conn, u.Id, u.EmpNo, u.Name, u.Dept, u.Shift, u.Lines, pinHash);
            Console.WriteLine($"  ✓ {u.EmpNo,-4}  {u.Name,-18}  PIN={u.Pin}  lines={u.Lines ?? "(all)"}");
        }

        Console.WriteLine();
        Console.WriteLine("[seed] Done. You can now log in from the POP shell.");
        return 0;
    }

    // Anchors the AspNetUsers row (FK target / identity handle). PasswordHash is left
    // untouched here — the POP PIN now lives in SYS_UserProfile.PinHash, and this column
    // is reserved for the Web portal password so seeding an operator never clobbers it.
    private static void UpsertAspNetUser(SqlConnection conn, string id, string userName)
    {
        const string sql = """
            MERGE dbo.AspNetUsers AS tgt
            USING (SELECT @Id AS Id) AS src ON tgt.Id = src.Id
            WHEN MATCHED THEN UPDATE SET
                UserName             = @UserName,
                NormalizedUserName   = UPPER(@UserName),
                LockoutEnabled       = 1,
                AccessFailedCount    = 0
            WHEN NOT MATCHED THEN INSERT
                (Id, UserName, NormalizedUserName, SecurityStamp,
                 ConcurrencyStamp, EmailConfirmed, PhoneNumberConfirmed,
                 TwoFactorEnabled, LockoutEnabled, AccessFailedCount)
            VALUES
                (@Id, @UserName, UPPER(@UserName), @SecurityStamp,
                 @ConcurrencyStamp, 0, 0, 0, 1, 0);
            """;

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Id",               SqlDbType.NVarChar, 450).Value = id;
        cmd.Parameters.Add("@UserName",         SqlDbType.NVarChar, 256).Value = userName;
        cmd.Parameters.Add("@SecurityStamp",    SqlDbType.NVarChar    ).Value = Guid.NewGuid().ToString("N");
        cmd.Parameters.Add("@ConcurrencyStamp", SqlDbType.NVarChar    ).Value = Guid.NewGuid().ToString("N");
        cmd.ExecuteNonQuery();
    }

    private static void UpsertUserProfile(
        SqlConnection conn, string userId, string empNo, string empName,
        string dept, string shift, string? assignedLines, string pinHash)
    {
        const string sql = """
            MERGE dbo.SYS_UserProfile AS tgt
            USING (SELECT @UserID AS UserID) AS src ON tgt.UserID = src.UserID
            WHEN MATCHED THEN UPDATE SET
                EmployeeNo       = @EmployeeNo,
                EmployeeName     = @EmployeeName,
                Department       = @Department,
                DefaultShift     = @DefaultShift,
                AssignedLines    = @AssignedLines,
                PinHash          = @PinHash,
                AccountStatus    = 'Active',
                FailedLoginCount = 0,
                ModifiedTS       = SYSDATETIME()
            WHEN NOT MATCHED THEN INSERT
                (UserID, EmployeeNo, EmployeeName, Department, PlantCode, DefaultShift,
                 AssignedLines, PinHash, AccountStatus, FailedLoginCount, CreatedBy, CreatedTS)
            VALUES
                (@UserID, @EmployeeNo, @EmployeeName, @Department, 'SEH-US-01', @DefaultShift,
                 @AssignedLines, @PinHash, 'Active', 0, 'seed', SYSDATETIME());
            """;

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@UserID",        SqlDbType.NVarChar, 450).Value = userId;
        cmd.Parameters.Add("@EmployeeNo",    SqlDbType.VarChar,  20 ).Value = empNo;
        cmd.Parameters.Add("@EmployeeName",  SqlDbType.NVarChar, 50 ).Value = empName;
        cmd.Parameters.Add("@Department",    SqlDbType.VarChar,  30 ).Value = dept;
        cmd.Parameters.Add("@DefaultShift",  SqlDbType.VarChar,  10 ).Value = shift;
        cmd.Parameters.Add("@AssignedLines", SqlDbType.NVarChar     ).Value = (object?)assignedLines ?? DBNull.Value;
        cmd.Parameters.Add("@PinHash",       SqlDbType.NVarChar, 200).Value = pinHash;
        cmd.ExecuteNonQuery();
    }
}
