using System.Data;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// System / administration (SYS) queries — used by the Office Web.
/// One method per SYS-XX screen, plus a health aggregate for SYS-08.
/// </summary>
public sealed class SysRepository
{
    private readonly AmesConnectionFactory _f;
    public SysRepository(AmesConnectionFactory f) => _f = f;

    // ── DTOs ────────────────────────────────────────────────────────────
    public sealed record UserRow(string UserId, string? UserName, string? Email,
        bool EmailConfirmed, bool LockedOut, int AccessFailedCount,
        string? EmployeeNo, string? EmployeeName, string? Department,
        string? AccountStatus, DateTime? LastLoginTs, string? RolesCsv);

    public sealed record RolePermRow(int RolePermissionId, string? RoleName, string? ModuleCode,
        string? ScreenCode, string? PermissionLevel, bool IsSystemRole);

    public sealed record CalendarRow(int FactoryCalendarId, DateTime? CalendarDate,
        string? DayType, string? HolidayName, int? ShiftCount, string? ShiftCode,
        TimeSpan? StartTime, TimeSpan? EndTime, int? BreakMinutes,
        decimal? NetWorkHours, string? Plant);

    public sealed record InterfaceRow(int InterfaceMonitorId, string? InterfaceCode,
        string? InterfaceName, string? Direction, string? Endpoint, string? Protocol,
        string? ConnStatus, DateTime? LastSyncTs, int? MaxGapMinutes, int? LastRecordCount,
        int? RetryCount, string? LastErrorMsg, bool IsEnabled, int MinutesSinceSync);

    public sealed record AuditRow(long LogId, DateTime? EventTs, string? ActorUserId,
        string? ModuleCode, string? ScreenCode, string? ActionType,
        string? TargetEntity, string? TargetId, string? Result, string? IpAddress, string? Note);

    public sealed record NotifRuleRow(int NotificationRuleId, string? EventTypeCode,
        string? EventName, string? SourceModule, bool IsEnabled,
        string? ChannelsJson, string? RecipientRolesJson);

    public sealed record NotifHistoryRow(long NotificationHistoryId, DateTime? SentAt,
        string? EventTypeCode, string? RecipientUserId, string? Channel,
        string? Subject, string? Status, int? RetryCount, string? ErrorMsg);

    public sealed record ConfigRow(int ConfigId, string? ConfigKey, string? ConfigType,
        string? Category, string? ConfigValue, string? CodeName, string? Unit,
        bool IsActive, int? SortOrder);

    public sealed record HealthKpi(int Users, int Roles, int InterfacesOk, int InterfacesDown,
        int AuditLast24h, int NotifLast24h, int NotifFailedLast24h, int ConfigKeys,
        long DbRowsApprox);

    // ── SYS-01 User Management ──────────────────────────────────────────
    public List<UserRow> ListUsers(int topN = 200)
    {
        const string sql = """
            SELECT TOP (@N)
                   u.Id, u.UserName, u.Email, u.EmailConfirmed, u.AccessFailedCount,
                   CASE WHEN u.LockoutEnd IS NOT NULL AND u.LockoutEnd > SYSDATETIMEOFFSET()
                        THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS LockedOut,
                   p.EmployeeNo, p.EmployeeName, p.Department, p.AccountStatus, p.LastLoginTS,
                   STUFF((SELECT ', ' + r.Name
                          FROM   dbo.AspNetUserRoles ur
                          JOIN   dbo.AspNetRoles r ON r.Id = ur.RoleId
                          WHERE  ur.UserId = u.Id
                          FOR XML PATH('')), 1, 2, '') AS RolesCsv
            FROM   dbo.AspNetUsers       u
            LEFT JOIN dbo.SYS_UserProfile p ON p.UserID = u.Id
            ORDER BY u.UserName;
            """;
        return Query(sql, r => new UserRow(
            (string)r["Id"], r["UserName"] as string, r["Email"] as string,
            (bool)r["EmailConfirmed"], (bool)r["LockedOut"], (int)r["AccessFailedCount"],
            r["EmployeeNo"] as string, r["EmployeeName"] as string, r["Department"] as string,
            r["AccountStatus"] as string, r["LastLoginTS"] as DateTime?,
            r["RolesCsv"] as string),
            ("@N", topN));
    }

    // ── SYS-02 RBAC ─────────────────────────────────────────────────────
    public List<RolePermRow> ListRolePermissions()
    {
        const string sql = """
            SELECT  RolePermissionID, RoleName, ModuleCode, ScreenCode,
                    PermissionLevel, ISNULL(IsSystemRole,0) AS IsSystemRole
            FROM    dbo.SYS_RolePermission
            ORDER   BY RoleName, ModuleCode, ScreenCode;
            """;
        return Query(sql, r => new RolePermRow(
            (int)r["RolePermissionID"], r["RoleName"] as string, r["ModuleCode"] as string,
            r["ScreenCode"] as string, r["PermissionLevel"] as string,
            (bool)r["IsSystemRole"]));
    }

    public List<(string RoleId, string RoleName, int UserCount)> ListRoles()
    {
        const string sql = """
            SELECT r.Id, r.Name,
                   (SELECT COUNT(*) FROM dbo.AspNetUserRoles ur WHERE ur.RoleId = r.Id) AS Cnt
            FROM   dbo.AspNetRoles r
            ORDER  BY r.Name;
            """;
        return Query(sql, r => ((string)r["Id"], (string)r["Name"], (int)r["Cnt"]));
    }

    // ── SYS-03 Factory Calendar ─────────────────────────────────────────
    public List<CalendarRow> ListCalendar(int daysAhead = 30, int daysBack = 7)
    {
        const string sql = """
            SELECT  FactoryCalendarID, CalendarDate, DayType, HolidayName,
                    ShiftCount, ShiftCode, StartTime, EndTime, BreakMinutes,
                    NetWorkHours, Plant
            FROM    dbo.SYS_FactoryCalendar
            WHERE   CalendarDate BETWEEN DATEADD(DAY, -@B, CAST(SYSDATETIME() AS DATE))
                                     AND DATEADD(DAY,  @A, CAST(SYSDATETIME() AS DATE))
            ORDER   BY CalendarDate, ShiftCode;
            """;
        return Query(sql, r => new CalendarRow(
            (int)r["FactoryCalendarID"], r["CalendarDate"] as DateTime?,
            r["DayType"] as string, r["HolidayName"] as string,
            r["ShiftCount"] as int?, r["ShiftCode"] as string,
            r["StartTime"] as TimeSpan?, r["EndTime"] as TimeSpan?,
            r["BreakMinutes"] as int?, r["NetWorkHours"] as decimal?,
            r["Plant"] as string),
            ("@A", daysAhead), ("@B", daysBack));
    }

    // ── SYS-04 Interface Monitor ────────────────────────────────────────
    public List<InterfaceRow> ListInterfaces()
    {
        const string sql = """
            SELECT  InterfaceMonitorID, InterfaceCode, InterfaceName, Direction, Endpoint,
                    Protocol, ConnStatus, LastSyncTS, MaxGapMinutes, LastRecordCount,
                    RetryCount, LastErrorMsg, ISNULL(IsEnabled,1) AS IsEnabled,
                    DATEDIFF(MINUTE, LastSyncTS, SYSDATETIME()) AS MinutesSince
            FROM    dbo.SYS_InterfaceMonitor
            ORDER   BY InterfaceCode;
            """;
        return Query(sql, r => new InterfaceRow(
            (int)r["InterfaceMonitorID"], r["InterfaceCode"] as string,
            r["InterfaceName"] as string, r["Direction"] as string,
            r["Endpoint"] as string, r["Protocol"] as string,
            r["ConnStatus"] as string, r["LastSyncTS"] as DateTime?,
            r["MaxGapMinutes"] as int?, r["LastRecordCount"] as int?,
            r["RetryCount"] as int?, r["LastErrorMsg"] as string,
            (bool)r["IsEnabled"], r["MinutesSince"] as int? ?? 0));
    }

    // ── SYS-05 Audit Log ────────────────────────────────────────────────
    public List<AuditRow> ListAudit(int topN = 200)
    {
        const string sql = """
            SELECT TOP (@N)
                   LogID, EventTS, ActorUserID, ModuleCode, ScreenCode,
                   ActionType, TargetEntity, TargetID, Result, IPAddress, Note
            FROM   dbo.SYS_AuditLog
            ORDER  BY LogID DESC;
            """;
        return Query(sql, r => new AuditRow(
            (long)r["LogID"], r["EventTS"] as DateTime?,
            r["ActorUserID"] as string, r["ModuleCode"] as string,
            r["ScreenCode"] as string, r["ActionType"] as string,
            r["TargetEntity"] as string, r["TargetID"] as string,
            r["Result"] as string, r["IPAddress"] as string,
            r["Note"] as string),
            ("@N", topN));
    }

    // ── SYS-06 Notifications ────────────────────────────────────────────
    public List<NotifRuleRow> ListNotificationRules()
    {
        const string sql = """
            SELECT  NotificationRuleID, EventTypeCode, EventName, SourceModule,
                    ISNULL(IsEnabled, 1) AS IsEnabled,
                    ChannelsJSON, RecipientRolesJSON
            FROM    dbo.SYS_NotificationRule
            ORDER   BY SourceModule, EventTypeCode;
            """;
        return Query(sql, r => new NotifRuleRow(
            (int)r["NotificationRuleID"], r["EventTypeCode"] as string,
            r["EventName"] as string, r["SourceModule"] as string,
            (bool)r["IsEnabled"],
            r["ChannelsJSON"] as string, r["RecipientRolesJSON"] as string));
    }

    public List<NotifHistoryRow> ListNotificationHistory(int topN = 100)
    {
        const string sql = """
            SELECT TOP (@N)
                   NotificationHistoryID, SentAt, EventTypeCode, RecipientUserID,
                   Channel, Subject, Status, RetryCount, ErrorMsg
            FROM   dbo.SYS_NotificationHistory
            ORDER  BY NotificationHistoryID DESC;
            """;
        return Query(sql, r => new NotifHistoryRow(
            (long)r["NotificationHistoryID"], r["SentAt"] as DateTime?,
            r["EventTypeCode"] as string, r["RecipientUserID"] as string,
            r["Channel"] as string, r["Subject"] as string,
            r["Status"] as string, r["RetryCount"] as int?,
            r["ErrorMsg"] as string),
            ("@N", topN));
    }

    // ── SYS-07 System Config ────────────────────────────────────────────
    public List<ConfigRow> ListConfig()
    {
        const string sql = """
            SELECT  ConfigID, ConfigKey, ConfigType, Category, ConfigValue,
                    CodeName, Unit, ISNULL(IsActive,1) AS IsActive, SortOrder
            FROM    dbo.SYS_Config
            ORDER   BY Category, ISNULL(SortOrder, 999), ConfigKey;
            """;
        return Query(sql, r => new ConfigRow(
            (int)r["ConfigID"], r["ConfigKey"] as string,
            r["ConfigType"] as string, r["Category"] as string,
            r["ConfigValue"] as string, r["CodeName"] as string,
            r["Unit"] as string, (bool)r["IsActive"], r["SortOrder"] as int?));
    }

    // ── SYS-08 System Health ────────────────────────────────────────────
    public HealthKpi GetHealth()
    {
        const string sql = """
            SELECT
              (SELECT COUNT(*) FROM dbo.AspNetUsers)                                          AS Users,
              (SELECT COUNT(*) FROM dbo.AspNetRoles)                                          AS Roles,
              (SELECT COUNT(*) FROM dbo.SYS_InterfaceMonitor WHERE ConnStatus IN ('OK','UP'))  AS IfOk,
              (SELECT COUNT(*) FROM dbo.SYS_InterfaceMonitor WHERE ConnStatus NOT IN ('OK','UP') OR ConnStatus IS NULL) AS IfDown,
              (SELECT COUNT(*) FROM dbo.SYS_AuditLog          WHERE EventTS >= DATEADD(HOUR,-24,SYSDATETIME())) AS AuditDay,
              (SELECT COUNT(*) FROM dbo.SYS_NotificationHistory WHERE SentAt >= DATEADD(HOUR,-24,SYSDATETIME())) AS NotifDay,
              (SELECT COUNT(*) FROM dbo.SYS_NotificationHistory WHERE SentAt >= DATEADD(HOUR,-24,SYSDATETIME())
                                                                  AND Status IN ('FAILED','ERROR')) AS NotifFail,
              (SELECT COUNT(*) FROM dbo.SYS_Config WHERE ISNULL(IsActive,1)=1)                 AS Cfg,
              ISNULL((SELECT SUM(p.rows)
                      FROM   sys.partitions p
                      JOIN   sys.tables     t ON t.object_id = p.object_id
                      WHERE  p.index_id IN (0,1)
                        AND  t.is_ms_shipped = 0), 0)                                          AS RowsApprox;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        using var rdr  = cmd.ExecuteReader();
        if (!rdr.Read())
            return new HealthKpi(0, 0, 0, 0, 0, 0, 0, 0, 0L);
        return new HealthKpi(
            (int)rdr["Users"], (int)rdr["Roles"],
            (int)rdr["IfOk"], (int)rdr["IfDown"],
            (int)rdr["AuditDay"], (int)rdr["NotifDay"],
            (int)rdr["NotifFail"], (int)rdr["Cfg"],
            rdr["RowsApprox"] as long? ?? 0L);
    }

    // ── helpers ──────────────────────────────────────────────────────────
    private List<T> Query<T>(string sql, Func<IDataReader, T> map, params (string Name, object Value)[] pars)
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        foreach (var (n, v) in pars) cmd.Parameters.AddWithValue(n, v);
        using var rdr  = cmd.ExecuteReader();
        var list = new List<T>();
        while (rdr.Read()) list.Add(map(rdr));
        return list;
    }
}
