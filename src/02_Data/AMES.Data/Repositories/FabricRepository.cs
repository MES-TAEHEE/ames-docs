using System.Data;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// Fabric-roll lifecycle for the IMG (Wrapping) module:
///   IMG-04 mount (PR_FabricIssue + PR_FabricIssueAttempt)
///   IMG-03 cycle deduction (PR_FabricDeductionLog + tbl_Lot.RemainingQty)
/// </summary>
public sealed class FabricRepository
{
    private readonly AmesConnectionFactory _factory;
    public FabricRepository(AmesConnectionFactory f) => _factory = f;

    /// <summary>Rolls available to mount (Status OPEN + RemainingQty &gt; 0), newest first.</summary>
    public List<FabricRollDto> ListAvailableRolls()
    {
        const string sql = """
            SELECT TOP 30
                   l.LotID, l.LotCode, l.ItemNo, i.ItemName,
                   l.ExpiryDate, l.Status,
                   ISNULL(l.RemainingQty,0) AS RemainingM,
                   ISNULL(l.QualityFlag,'') AS ColorCode
            FROM   dbo.tbl_Lot l
            LEFT  JOIN dbo.MD_Item i ON i.ItemNo = l.ItemNo
            WHERE  l.ProcessCode = 'WH'
              AND  ISNULL(l.Status,'OPEN') = 'OPEN'
              AND  ISNULL(l.RemainingQty,0) > 0
            ORDER  BY l.ProducedAt DESC;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        using var rdr  = cmd.ExecuteReader();
        var list = new List<FabricRollDto>();
        while (rdr.Read())
            list.Add(MapRoll(rdr));
        return list;
    }

    /// <summary>Roll currently mounted on this line (most recent PR_FabricIssue without DismountedAt).</summary>
    public FabricRollDto? GetMountedRoll(string lineId)
    {
        const string sql = """
            SELECT TOP 1 l.LotID, l.LotCode, l.ItemNo, i.ItemName,
                   l.ExpiryDate, l.Status,
                   ISNULL(l.RemainingQty,0) AS RemainingM,
                   ISNULL(fi.ColorCode,'') AS ColorCode
            FROM   dbo.PR_FabricIssue fi
            JOIN   dbo.tbl_Lot        l ON l.LotID = fi.FabricRollLotID
            LEFT  JOIN dbo.MD_Item    i ON i.ItemNo = l.ItemNo
            WHERE  fi.LineID = @L AND fi.DismountedAt IS NULL
            ORDER  BY fi.MountedAt DESC;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@L", SqlDbType.VarChar, 20).Value = lineId;
        using var rdr = cmd.ExecuteReader();
        return rdr.Read() ? MapRoll(rdr) : null;
    }

    public FabricRollDto? GetRollByCode(string lotCode)
    {
        const string sql = """
            SELECT TOP 1 l.LotID, l.LotCode, l.ItemNo, i.ItemName,
                   l.ExpiryDate, l.Status,
                   ISNULL(l.RemainingQty,0) AS RemainingM,
                   ISNULL(l.QualityFlag,'') AS ColorCode
            FROM   dbo.tbl_Lot l
            LEFT  JOIN dbo.MD_Item i ON i.ItemNo = l.ItemNo
            WHERE  l.LotCode = @C;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@C", SqlDbType.VarChar, 40).Value = lotCode;
        using var rdr = cmd.ExecuteReader();
        return rdr.Read() ? MapRoll(rdr) : null;
    }

    /// <summary>
    /// Records a scan attempt regardless of result. Always written for audit
    /// (BR-IMG-02 — every mismatch must be discoverable).
    /// </summary>
    public void LogScanAttempt(int? woId, int? scannedLotId, string scannedColor,
                               string? expectedColor, string result,
                               string operatorId, string employeeNo)
    {
        const string sql = """
            INSERT INTO dbo.PR_FabricIssueAttempt
                (WoID, ScannedRollLotID, ScannedColor, ExpectedColor, Result,
                 AttemptedBy, AttemptedAt, CreatedBy, CreatedTS)
            VALUES (@W, @L, @S, @E, @R, @Op, SYSDATETIME(), @By, SYSDATETIME());
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@W",  SqlDbType.Int).Value = (object?)woId ?? DBNull.Value;
        cmd.Parameters.Add("@L",  SqlDbType.Int).Value = (object?)scannedLotId ?? DBNull.Value;
        cmd.Parameters.Add("@S",  SqlDbType.VarChar, 10).Value = scannedColor ?? string.Empty;
        cmd.Parameters.Add("@E",  SqlDbType.VarChar, 10).Value = (object?)expectedColor ?? DBNull.Value;
        cmd.Parameters.Add("@R",  SqlDbType.VarChar, 10).Value = result;
        cmd.Parameters.Add("@Op", SqlDbType.NVarChar, 450).Value = operatorId;
        cmd.Parameters.Add("@By", SqlDbType.VarChar, 50).Value   = employeeNo;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Mounts a verified roll on the line. Dismounts any previously open roll
    /// on the same line so there's never more than one.
    /// </summary>
    public int MountRoll(int woId, int rollLotId, string colorCode, decimal initialM,
                         string lineId, string operatorId, int? sessionId, string employeeNo)
    {
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            // dismount previous open issue on this line
            using (var d = new SqlCommand("""
                UPDATE dbo.PR_FabricIssue
                SET    DismountedAt = SYSDATETIME(),
                       FinalRemainingM = (SELECT RemainingQty FROM dbo.tbl_Lot WHERE LotID = FabricRollLotID),
                       ModifiedTS = SYSDATETIME()
                WHERE  LineID = @L AND DismountedAt IS NULL;
                """, conn, tx))
            {
                d.Parameters.Add("@L", SqlDbType.VarChar, 20).Value = lineId;
                d.ExecuteNonQuery();
            }

            using var cmd = new SqlCommand("""
                INSERT INTO dbo.PR_FabricIssue
                    (WoID, FabricRollLotID, ColorCode, MountedAt, InitialRemainingM,
                     OperatorID, SessionID, LineID, CreatedBy, CreatedTS)
                OUTPUT INSERTED.FabricIssueID
                VALUES (@W, @R, @C, SYSDATETIME(), @M, @Op, @Sess, @L, @By, SYSDATETIME());
                """, conn, tx);
            cmd.Parameters.Add("@W",   SqlDbType.Int).Value = woId;
            cmd.Parameters.Add("@R",   SqlDbType.Int).Value = rollLotId;
            cmd.Parameters.Add("@C",   SqlDbType.VarChar, 10).Value = colorCode;
            cmd.Parameters.Add("@M",   SqlDbType.Decimal).Value = initialM;
            cmd.Parameters.Add("@Op",  SqlDbType.NVarChar, 450).Value = operatorId;
            cmd.Parameters.Add("@Sess",SqlDbType.Int).Value = (object?)sessionId ?? DBNull.Value;
            cmd.Parameters.Add("@L",   SqlDbType.VarChar, 20).Value = lineId;
            cmd.Parameters.Add("@By",  SqlDbType.VarChar, 50).Value = employeeNo;
            var id = (int)cmd.ExecuteScalar()!;
            tx.Commit();
            return id;
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>
    /// Deduct fabric meters from a roll's RemainingQty + write the audit log.
    /// Returns the new RemainingQty.
    /// </summary>
    public decimal DeductFromRoll(int rollLotId, decimal consumedM, int resultId, string employeeNo)
    {
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            decimal before, after;
            using (var read = new SqlCommand(
                "SELECT ISNULL(RemainingQty,0) FROM dbo.tbl_Lot WHERE LotID=@L;", conn, tx))
            {
                read.Parameters.Add("@L", SqlDbType.Int).Value = rollLotId;
                before = (decimal)(read.ExecuteScalar() ?? 0m);
            }
            after = Math.Max(0m, before - consumedM);

            using (var upd = new SqlCommand("""
                UPDATE dbo.tbl_Lot
                SET    RemainingQty = @After,
                       Status       = CASE WHEN @After <= 0 THEN 'EXHAUSTED' ELSE Status END,
                       ModifiedTS   = SYSDATETIME()
                WHERE  LotID = @L;
                """, conn, tx))
            {
                upd.Parameters.Add("@After", SqlDbType.Decimal).Value = after;
                upd.Parameters.Add("@L",     SqlDbType.Int).Value     = rollLotId;
                upd.ExecuteNonQuery();
            }

            using (var log = new SqlCommand("""
                INSERT INTO dbo.PR_FabricDeductionLog
                    (FabricRollLotID, ResultID, ConsumedM, BeforeM, AfterM,
                     DeductedAt, CreatedBy, CreatedTS)
                VALUES (@L, @R, @C, @Before, @After, SYSDATETIME(), @By, SYSDATETIME());
                """, conn, tx))
            {
                log.Parameters.Add("@L",      SqlDbType.Int).Value = rollLotId;
                log.Parameters.Add("@R",      SqlDbType.Int).Value = resultId;
                log.Parameters.Add("@C",      SqlDbType.Decimal).Value = consumedM;
                log.Parameters.Add("@Before", SqlDbType.Decimal).Value = before;
                log.Parameters.Add("@After",  SqlDbType.Decimal).Value = after;
                log.Parameters.Add("@By",     SqlDbType.VarChar, 50).Value = employeeNo;
                log.ExecuteNonQuery();
            }

            tx.Commit();
            return after;
        }
        catch { tx.Rollback(); throw; }
    }

    private static FabricRollDto MapRoll(IDataReader r) => new()
    {
        LotId       = (int)r["LotID"],
        LotCode     = r["LotCode"] as string ?? string.Empty,
        ItemNo      = r["ItemNo"]  as string ?? string.Empty,
        ItemName    = r["ItemName"] as string,
        ColorCode   = r["ColorCode"] as string,
        RemainingM  = r["RemainingM"] as decimal? ?? 0,
        ExpiryDate  = r["ExpiryDate"] as DateTime?,
        Status      = r["Status"]    as string,
    };
}
