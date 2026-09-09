using System.Data;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// Read access to MD_Worker — shop-floor operators who log into POP with a
/// badge number + PIN and have no AspNetUsers row at all.
///
/// Rows are mapped onto <see cref="EmployeeProfileDto"/> rather than a type of
/// their own so the whole POP login path (PopAuthService, PopSessionRepository)
/// stays single-shaped. MD_Worker carries no line assignment and no failure
/// counter, so workers are unrestricted by line and never lock out; that
/// difference is expressed through the mapping below, not through branches
/// in the auth service.
/// </summary>
public sealed class WorkerRepository
{
    private const string SelectList = """
        SELECT WorkerNo, WorkerName, PinHash, ActiveFlag
        FROM   dbo.MD_Worker
        """;

    private readonly AmesConnectionFactory _connFactory;

    public WorkerRepository(AmesConnectionFactory connFactory)
    {
        _connFactory = connFactory ?? throw new ArgumentNullException(nameof(connFactory));
    }

    /// <summary>
    /// Lookup by MD_Worker.WorkerNo (badge number typed / scanned at the terminal).
    /// Returns null when no such worker exists.
    /// </summary>
    public EmployeeProfileDto? FindByWorkerNo(string workerNo)
    {
        const string sql = $"""
            {SelectList}
            WHERE  WorkerNo = @WorkerNo;
            """;

        using var conn = _connFactory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@WorkerNo", SqlDbType.VarChar, 20).Value = workerNo;

        using var rdr = cmd.ExecuteReader();
        return rdr.Read() ? MapToDto(rdr) : null;
    }

    /// <summary>
    /// Active workers ordered by WorkerNo. Feeds the POP login user picker
    /// alongside AuthRepository.ListAllProfiles.
    /// </summary>
    public List<EmployeeProfileDto> ListAllActive()
    {
        const string sql = $"""
            {SelectList}
            WHERE  ActiveFlag = 1
            ORDER  BY WorkerNo;
            """;

        using var conn = _connFactory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        using var rdr  = cmd.ExecuteReader();

        var list = new List<EmployeeProfileDto>();
        while (rdr.Read()) list.Add(MapToDto(rdr));
        return list;
    }

    /// <summary>
    /// Creates a badge-only worker row when WorkerNo is not registered yet, and
    /// returns true when this call is the one that created it.
    ///
    /// Feeds the POP login screen's badge scan: a scanned EOS badge whose number
    /// nobody knows becomes a worker on the spot so the operator is not stuck at
    /// the terminal. Only ever inserts — an ActiveFlag=0 row stays disabled, so a
    /// worker an administrator switched off cannot come back by rescanning the
    /// badge, and a name already corrected in the admin screen is not overwritten.
    /// PinHash is left null, which means the account can only sign in by badge.
    /// </summary>
    /// <param name="workerName">Name read off the badge; falls back to the number.</param>
    public bool EnsureRegistered(string workerNo, string? workerName = null)
    {
        const string sql = """
            INSERT INTO dbo.MD_Worker (WorkerNo, WorkerName, ActiveFlag, CreatedBy)
            SELECT @WorkerNo, @WorkerName, 1, 'POP-SCAN'
            WHERE  NOT EXISTS (SELECT 1 FROM dbo.MD_Worker WHERE WorkerNo = @WorkerNo);
            """;

        var name = string.IsNullOrWhiteSpace(workerName) ? workerNo : workerName.Trim();
        if (name.Length > 50) name = name[..50];   // MD_Worker.WorkerName NVARCHAR(50)

        using var conn = _connFactory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@WorkerNo",   SqlDbType.VarChar,  20).Value = workerNo;
        cmd.Parameters.Add("@WorkerName", SqlDbType.NVarChar, 50).Value = name;

        try { return cmd.ExecuteNonQuery() > 0; }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            // Two terminals scanned the same new badge at once — the unique index
            // did its job and the row exists either way.
            return false;
        }
    }

    /// <summary>
    /// Stores the operator's POP PIN. Called when a badge-only worker picks one on
    /// the login screen, which is the only backup path they have if the scanner dies.
    /// </summary>
    public void SetPin(string workerNo, string pinHash)
    {
        const string sql = """
            UPDATE dbo.MD_Worker
            SET    PinHash    = @PinHash,
                   ModifiedBy = 'POP-PIN',
                   ModifiedTS = SYSDATETIME()
            WHERE  WorkerNo   = @WorkerNo;
            """;

        using var conn = _connFactory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@WorkerNo", SqlDbType.VarChar,   20).Value = workerNo;
        cmd.Parameters.Add("@PinHash",  SqlDbType.NVarChar, 200).Value = pinHash;
        cmd.ExecuteNonQuery();
    }

    // ── MD-032 현장 작업자 관리 (Web) ─────────────────────────────────────
    // POP 로그인 경로는 위의 DTO 매핑을 쓰고, 관리 화면은 행 그대로를 본다.
    // PinHash 는 화면에 내려보내지 않고 설정 여부(HasPin)만 노출한다.

    public sealed record WorkerRow(int WorkerId, string WorkerNo, string WorkerName, bool HasPin, bool ActiveFlag,
        string CreatedBy, DateTime? CreatedTs, string? ModifiedBy, DateTime? ModifiedTs);

    private const string AdminSelect = """
        SELECT WorkerID, WorkerNo, WorkerName,
               CASE WHEN PinHash IS NULL OR PinHash = '' THEN 0 ELSE 1 END AS HasPin,
               ActiveFlag, CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
        FROM   dbo.MD_Worker
        """;

    public List<WorkerRow> ListAll()
    {
        const string sql = $"""
            {AdminSelect}
            ORDER  BY WorkerNo;
            """;
        using var conn = _connFactory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        using var rdr  = cmd.ExecuteReader();
        var list = new List<WorkerRow>();
        while (rdr.Read()) list.Add(MapRow(rdr));
        return list;
    }

    public bool Exists(string workerNo)
    {
        using var conn = _connFactory.OpenConnection();
        using var cmd  = new SqlCommand("SELECT 1 FROM dbo.MD_Worker WHERE WorkerNo = @WorkerNo", conn);
        cmd.Parameters.Add("@WorkerNo", SqlDbType.VarChar, 20).Value = workerNo;
        return cmd.ExecuteScalar() is not null;
    }

    public void Insert(string workerNo, string workerName, string? pinHash, bool activeFlag, string actor)
    {
        const string sql = """
            INSERT INTO dbo.MD_Worker (WorkerNo, WorkerName, PinHash, ActiveFlag, CreatedBy, CreatedTS)
            VALUES (@WorkerNo, @WorkerName, @PinHash, @ActiveFlag, @Actor, SYSDATETIME());
            """;
        using var conn = _connFactory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@WorkerNo",   SqlDbType.VarChar,   20).Value = workerNo;
        cmd.Parameters.Add("@WorkerName", SqlDbType.NVarChar,  50).Value = workerName;
        cmd.Parameters.Add("@PinHash",    SqlDbType.NVarChar, 200).Value = (object?)pinHash ?? DBNull.Value;
        cmd.Parameters.Add("@ActiveFlag", SqlDbType.Bit).Value            = activeFlag;
        cmd.Parameters.Add("@Actor",      SqlDbType.VarChar,   50).Value = actor;
        cmd.ExecuteNonQuery();
    }

    /// <summary>이름·사용여부 수정. WorkerNo 는 PR_PopSession.OperatorID 로 실적에 남아 있어 바꾸지 않는다.</summary>
    public void Update(int workerId, string workerName, bool activeFlag, string actor)
    {
        const string sql = """
            UPDATE dbo.MD_Worker
            SET    WorkerName = @WorkerName,
                   ActiveFlag = @ActiveFlag,
                   ModifiedBy = @Actor,
                   ModifiedTS = SYSDATETIME()
            WHERE  WorkerID   = @WorkerID;
            """;
        using var conn = _connFactory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@WorkerID",   SqlDbType.Int).Value            = workerId;
        cmd.Parameters.Add("@WorkerName", SqlDbType.NVarChar,  50).Value = workerName;
        cmd.Parameters.Add("@ActiveFlag", SqlDbType.Bit).Value            = activeFlag;
        cmd.Parameters.Add("@Actor",      SqlDbType.NVarChar, 450).Value = actor;
        cmd.ExecuteNonQuery();
    }

    /// <summary>관리자 PIN 설정. null 이면 PIN 을 지워 배지 전용 계정으로 되돌린다(다음 배지 로그인 때 PIN 재설정을 강제받는다).</summary>
    public void SetPinByAdmin(int workerId, string? pinHash, string actor)
    {
        const string sql = """
            UPDATE dbo.MD_Worker
            SET    PinHash    = @PinHash,
                   ModifiedBy = @Actor,
                   ModifiedTS = SYSDATETIME()
            WHERE  WorkerID   = @WorkerID;
            """;
        using var conn = _connFactory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@WorkerID", SqlDbType.Int).Value            = workerId;
        cmd.Parameters.Add("@PinHash",  SqlDbType.NVarChar, 200).Value = (object?)pinHash ?? DBNull.Value;
        cmd.Parameters.Add("@Actor",    SqlDbType.NVarChar, 450).Value = actor;
        cmd.ExecuteNonQuery();
    }

    public void Delete(int workerId)
    {
        using var conn = _connFactory.OpenConnection();
        using var cmd  = new SqlCommand("DELETE FROM dbo.MD_Worker WHERE WorkerID = @WorkerID", conn);
        cmd.Parameters.Add("@WorkerID", SqlDbType.Int).Value = workerId;
        cmd.ExecuteNonQuery();
    }

    private static WorkerRow MapRow(SqlDataReader r) => new(
        (int)r["WorkerID"], (string)r["WorkerNo"], (string)r["WorkerName"],
        Convert.ToInt32(r["HasPin"]) == 1, (bool)r["ActiveFlag"],
        (string)r["CreatedBy"], r["CreatedTS"] as DateTime?, r["ModifiedBy"] as string, r["ModifiedTS"] as DateTime?);

    private static EmployeeProfileDto MapToDto(SqlDataReader rdr)
    {
        var workerNo = (string)rdr["WorkerNo"];
        return new EmployeeProfileDto
        {
            // OperatorID / CreatedBy downstream. Workers have no GUID, so the
            // badge number itself identifies them in PR_PopSession and results.
            UserId            = workerNo,
            UserName          = workerNo,
            EmployeeNo        = workerNo,
            EmployeeName      = (string)rdr["WorkerName"],
            PinHash           = rdr["PinHash"] as string,
            PasswordHash      = string.Empty,          // workers never sign into the web portal
            AccountStatus     = (bool)rdr["ActiveFlag"] ? "Active" : "Disabled",
            AssignedLinesJson = null,                  // null => every line is allowed
            Department        = null,
            DefaultShift      = null,
            FailedLoginCount  = 0,
            IsWorker          = true,
        };
    }
}
