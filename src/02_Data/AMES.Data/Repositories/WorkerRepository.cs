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
