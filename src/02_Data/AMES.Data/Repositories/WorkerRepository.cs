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
