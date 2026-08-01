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
        string? PlantCode, string? DefaultShift,
        string? AccountStatus, DateTime? LastLoginTs, string? RolesCsv,
        string? AssignedLines, int FailedLoginCount);

    public sealed record LineRow(string LineId, string LineName, string? LineNameEn);

    public sealed record RoleRow(string RoleId, string RoleName, int UserCount);

    public sealed record RolePermRow(int RolePermissionId, string? RoleId, string? RoleName,
        string? ModuleCode, string? ScreenCode, string? PermissionLevel, bool IsSystemRole);

    public sealed record CalendarRow(int FactoryCalendarId, DateTime? CalendarDate,
        string? DayType, string? HolidayName, int? ShiftCount, string? ShiftCode,
        TimeSpan? StartTime, TimeSpan? EndTime, int? BreakMinutes,
        decimal? NetWorkHours, string? PlantCode);

    public sealed record InterfaceRow(int InterfaceMonitorId, string? InterfaceCode,
        string? InterfaceName, string? Direction, string? Endpoint, string? Protocol,
        string? ConnStatus, DateTime? LastSyncTs, int? MaxGapMinutes, int? LastRecordCount,
        int? RetryCount, string? LastErrorMsg, bool IsEnabled, int MinutesSinceSync);

    public sealed record AuditRow(long LogId, DateTime? EventTs, string? ActorUserId,
        string? ModuleCode, string? ScreenCode, string? ProcessCode, string? ActionType,
        string? TargetEntity, string? TargetId, string? Result, string? IpAddress, string? Note);

    public sealed record NotifRuleRow(int NotificationRuleId, string? EventTypeCode,
        string? EventName, string? ModuleCode, string? ProcessCode, string? TriggerCondition, bool IsEnabled,
        string? ChannelsJson, string? RecipientRolesJson);

    public sealed record NotifHistoryRow(long NotificationHistoryId, DateTime? SentAt,
        string? EventTypeCode, string? RecipientUserId, string? Channel,
        string? Subject, string? Status, int? RetryCount, string? ErrorMsg);

    public sealed record NotifChannelRow(int NotificationChannelId, string? UserId,
        string? UserName, string? Channel, string? Address, bool IsEnabled,
        TimeSpan? QuietHoursStart, TimeSpan? QuietHoursEnd, DateTime? VerifiedAt);

    public sealed record ScreenRow(int ScreenId, string ScreenCode, string ModuleCode,
        string? ProcessCode, string? SubProcessCode, string ScreenName, string? ScreenNameEn, string? HRef, string? LidLabel,
        int? SortOrder, bool IsVisible, int RoleCount);

    public sealed record ConfigRow(int ConfigId, string? ConfigKey, string? ConfigType,
        string? Category, string? ConfigValue, string? CodeName, string? Unit,
        bool IsActive, int? SortOrder);

    public sealed record HealthKpi(int Users, int Roles, int InterfacesOk, int InterfacesDown,
        int AuditLast24h, int NotifLast24h, int NotifFailedLast24h, int ConfigKeys,
        int Screens, int Permissions, long DbRowsApprox);

    public sealed record UserSelectRow(string Id, string? UserName, string? Email, string? PhoneNumber);

    // ── SYS-01 User Management ──────────────────────────────────────────
    public List<UserRow> ListUsers(int topN = 200)
    {
        const string sql = """
            SELECT TOP (@N)
                   u.Id, u.UserName, u.Email, u.EmailConfirmed, u.AccessFailedCount,
                   CASE WHEN u.LockoutEnd IS NOT NULL AND u.LockoutEnd > SYSDATETIMEOFFSET()
                        THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS LockedOut,
                   p.EmployeeNo, p.EmployeeName, p.Department, p.PlantCode, p.DefaultShift,
                   p.AccountStatus, p.LastLoginTS,
                   p.AssignedLines,
                   ISNULL(p.FailedLoginCount, 0) AS FailedLoginCount,
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
            r["PlantCode"] as string, r["DefaultShift"] as string,
            r["AccountStatus"] as string, r["LastLoginTS"] as DateTime?,
            r["RolesCsv"] as string, r["AssignedLines"] as string,
            Convert.ToInt32(r["FailedLoginCount"])),
            ("@N", topN));
    }

    public List<UserSelectRow> ListUsersForSelect()
    {
        const string sql = """
            SELECT Id, UserName, Email, PhoneNumber
            FROM   dbo.AspNetUsers
            ORDER  BY UserName;
            """;
        return Query(sql, r => new UserSelectRow(
            (string)r["Id"], r["UserName"] as string,
            r["Email"] as string, r["PhoneNumber"] as string));
    }

    // ── SYS-02 RBAC ─────────────────────────────────────────────────────
    public List<RolePermRow> ListRolePermissions()
    {
        const string sql = """
            SELECT  RolePermissionID, RoleID, RoleName, ModuleCode, ScreenCode,
                    PermissionLevel, ISNULL(IsSystemRole,0) AS IsSystemRole
            FROM    dbo.SYS_RolePermission
            ORDER   BY RoleName, ModuleCode, ScreenCode;
            """;
        return Query(sql, r => new RolePermRow(
            (int)r["RolePermissionID"], r["RoleID"] as string, r["RoleName"] as string,
            r["ModuleCode"] as string, r["ScreenCode"] as string,
            r["PermissionLevel"] as string, (bool)r["IsSystemRole"]));
    }

    public void CreateRolePerm(string? roleId, string roleName, string moduleCode,
        string screenCode, string permissionLevel, bool isSystemRole, string createdBy)
    {
        const string sql = """
            INSERT INTO dbo.SYS_RolePermission
                (RoleID, RoleName, ModuleCode, ScreenCode, PermissionLevel,
                 IsSystemRole, EffectiveTS, CreatedBy, CreatedTS)
            VALUES
                (@RoleID, @RoleName, @Module, @Screen, @Level,
                 @IsSys, SYSDATETIME(), @CreatedBy, SYSDATETIME())
            """;
        Exec(sql,
            ("@RoleID",    (object?)roleId      ?? DBNull.Value),
            ("@RoleName",  roleName),
            ("@Module",    moduleCode),
            ("@Screen",    screenCode),
            ("@Level",     permissionLevel),
            ("@IsSys",     isSystemRole),
            ("@CreatedBy", createdBy));
    }

    public void UpdateRolePerm(int rolePermissionId, string? roleId, string roleName,
        string moduleCode, string screenCode, string permissionLevel,
        bool isSystemRole, string modifiedBy)
    {
        const string sql = """
            UPDATE dbo.SYS_RolePermission
            SET    RoleID          = @RoleID,
                   RoleName        = @RoleName,
                   ModuleCode      = @Module,
                   ScreenCode      = @Screen,
                   PermissionLevel = @Level,
                   IsSystemRole    = @IsSys,
                   ModifiedBy      = @ModifiedBy,
                   ModifiedTS      = SYSDATETIME()
            WHERE  RolePermissionID = @Id
            """;
        Exec(sql,
            ("@Id",         rolePermissionId),
            ("@RoleID",     (object?)roleId ?? DBNull.Value),
            ("@RoleName",   roleName),
            ("@Module",     moduleCode),
            ("@Screen",     screenCode),
            ("@Level",      permissionLevel),
            ("@IsSys",      isSystemRole),
            ("@ModifiedBy", modifiedBy));
    }

    public void DeleteRolePerm(int rolePermissionId)
    {
        Exec("DELETE dbo.SYS_RolePermission WHERE RolePermissionID = @Id",
            ("@Id", rolePermissionId));
    }

    public List<RoleRow> ListRoles()
    {
        const string sql = """
            SELECT r.Id, r.Name,
                   (SELECT COUNT(*) FROM dbo.AspNetUserRoles ur WHERE ur.RoleId = r.Id) AS Cnt
            FROM   dbo.AspNetRoles r
            ORDER  BY r.Name;
            """;
        return Query(sql, r => new RoleRow((string)r["Id"], (string)r["Name"], (int)r["Cnt"]));
    }

    // ── SYS-03 Factory Calendar ─────────────────────────────────────────
    public List<CalendarRow> ListCalendar(int daysAhead = 30, int daysBack = 7)
    {
        const string sql = """
            SELECT  FactoryCalendarID, CalendarDate, DayType, HolidayName,
                    ShiftCount, ShiftCode, StartTime, EndTime, BreakMinutes,
                    NetWorkHours, PlantCode
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
            r["PlantCode"] as string),
            ("@A", daysAhead), ("@B", daysBack));
    }

    public List<CalendarRow> ListCalendarRange(DateTime from, DateTime to)
    {
        const string sql = """
            SELECT  FactoryCalendarID, CalendarDate, DayType, HolidayName,
                    ShiftCount, ShiftCode, StartTime, EndTime, BreakMinutes,
                    NetWorkHours, PlantCode
            FROM    dbo.SYS_FactoryCalendar
            WHERE   CalendarDate BETWEEN CAST(@From AS DATE) AND CAST(@To AS DATE)
            ORDER   BY CalendarDate, ShiftCode;
            """;
        return Query(sql, r => new CalendarRow(
            (int)r["FactoryCalendarID"], r["CalendarDate"] as DateTime?,
            r["DayType"] as string, r["HolidayName"] as string,
            r["ShiftCount"] as int?, r["ShiftCode"] as string,
            r["StartTime"] as TimeSpan?, r["EndTime"] as TimeSpan?,
            r["BreakMinutes"] as int?, r["NetWorkHours"] as decimal?,
            r["PlantCode"] as string),
            ("@From", from.Date), ("@To", to.Date));
    }

    public void InsertCalendarShift(DateTime date, string dayType, string? holidayName,
        int? shiftCount, string? shiftCode, TimeSpan? start, TimeSpan? end,
        int? breakMin, decimal? netHours, int calendarYear, string plantCode, string createdBy)
    {
        const string sql = """
            INSERT INTO dbo.SYS_FactoryCalendar
                   (CalendarDate, DayType, HolidayName, ShiftCount, ShiftCode,
                    StartTime, EndTime, BreakMinutes, NetWorkHours,
                    CalendarYear, PlantCode, CreatedBy, CreatedTS)
            VALUES (CAST(@Date AS DATE), @DayType, @HolidayName, @ShiftCount, @ShiftCode,
                    @Start, @End, @Break, @Net,
                    @Year, @Plant, @CreatedBy, SYSDATETIME())
            """;
        Exec(sql,
            ("@Date",        date.Date),
            ("@DayType",     dayType),
            ("@HolidayName", (object?)holidayName ?? DBNull.Value),
            ("@ShiftCount",  (object?)shiftCount  ?? DBNull.Value),
            ("@ShiftCode",   (object?)shiftCode   ?? DBNull.Value),
            ("@Start",       (object?)start    ?? DBNull.Value),
            ("@End",         (object?)end      ?? DBNull.Value),
            ("@Break",       (object?)breakMin ?? DBNull.Value),
            ("@Net",         (object?)netHours ?? DBNull.Value),
            ("@Year",        calendarYear),
            ("@Plant",       string.IsNullOrWhiteSpace(plantCode) ? (object)DBNull.Value : plantCode),
            ("@CreatedBy",   createdBy));
    }

    public void UpdateCalendarDayMeta(DateTime date, string dayType, string? holidayName, string modifiedBy)
    {
        const string sql = """
            UPDATE dbo.SYS_FactoryCalendar
            SET    DayType      = @DayType,
                   HolidayName  = @HolidayName,
                   ModifiedBy   = @ModifiedBy,
                   ModifiedTS   = SYSDATETIME()
            WHERE  CalendarDate = CAST(@Date AS DATE)
            """;
        Exec(sql,
            ("@Date",        date.Date),
            ("@DayType",     dayType),
            ("@HolidayName", (object?)holidayName ?? DBNull.Value),
            ("@ModifiedBy",  modifiedBy));
    }

    public void UpdateCalendarShift(int id, TimeSpan? start, TimeSpan? end,
        int? breakMin, decimal? netHours, string modifiedBy)
    {
        const string sql = """
            UPDATE dbo.SYS_FactoryCalendar
            SET    StartTime    = @Start,
                   EndTime      = @End,
                   BreakMinutes = @Break,
                   NetWorkHours = @Net,
                   ModifiedBy   = @ModifiedBy,
                   ModifiedTS   = SYSDATETIME()
            WHERE  FactoryCalendarID = @Id
            """;
        Exec(sql,
            ("@Id",         id),
            ("@Start",      (object?)start    ?? DBNull.Value),
            ("@End",        (object?)end      ?? DBNull.Value),
            ("@Break",      (object?)breakMin ?? DBNull.Value),
            ("@Net",        (object?)netHours ?? DBNull.Value),
            ("@ModifiedBy", modifiedBy));
    }

    public void DeleteCalendarDate(DateTime date)
    {
        Exec("DELETE dbo.SYS_FactoryCalendar WHERE CalendarDate = CAST(@Date AS DATE)",
            ("@Date", date.Date));
    }

    public void DeleteCalendarShift(int id)
    {
        Exec("DELETE dbo.SYS_FactoryCalendar WHERE FactoryCalendarID = @Id", ("@Id", id));
    }

    public bool CalendarDateExists(DateTime date)
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(
            "SELECT COUNT(1) FROM dbo.SYS_FactoryCalendar WHERE CalendarDate = CAST(@Date AS DATE)", conn);
        cmd.Parameters.AddWithValue("@Date", date.Date);
        return (int)cmd.ExecuteScalar()! > 0;
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
    public void InsertAuditLog(
        string? moduleCode, string? screenCode,
        string actionType, string? targetEntity, string? targetId,
        string? beforeJson, string? afterJson,
        string actorUserId, string? ipAddress = null,
        string result = "OK", string? note = null)
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand("""
            INSERT INTO dbo.SYS_AuditLog
                   (EventTS, ActorUserID, ModuleCode, ScreenCode, ActionType,
                    TargetEntity, TargetID, BeforeValueJSON, AfterValueJSON,
                    IPAddress, Result, Note, CreatedBy, CreatedTS)
            VALUES (SYSDATETIME(), @Actor, @Mod, @Scr, @Act,
                    @Ent, @TID, @Before, @After,
                    @IP, @Res, @Note, @Actor, SYSDATETIME())
            """, conn);
        cmd.Parameters.AddWithValue("@Actor",  actorUserId);
        cmd.Parameters.AddWithValue("@Mod",    (object?)moduleCode   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Scr",    (object?)screenCode   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Act",    actionType);
        cmd.Parameters.AddWithValue("@Ent",    (object?)targetEntity ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@TID",    (object?)targetId     ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Before", (object?)beforeJson   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@After",  (object?)afterJson    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IP",     (object?)ipAddress    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Res",    result);
        cmd.Parameters.AddWithValue("@Note",   (object?)note         ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public List<AuditRow> ListAudit(int topN = 200)
    {
        const string sql = """
            SELECT TOP (@N)
                   LogID, EventTS, ActorUserID, ModuleCode, ScreenCode, ProcessCode,
                   ActionType, TargetEntity, TargetID, Result, IPAddress, Note
            FROM   dbo.SYS_AuditLog
            ORDER  BY LogID DESC;
            """;
        return Query(sql, r => new AuditRow(
            (long)r["LogID"], r["EventTS"] as DateTime?,
            r["ActorUserID"] as string, r["ModuleCode"] as string,
            r["ScreenCode"] as string, r["ProcessCode"] as string,
            r["ActionType"] as string,
            r["TargetEntity"] as string, r["TargetID"] as string,
            r["Result"] as string, r["IPAddress"] as string,
            r["Note"] as string),
            ("@N", topN));
    }

    // ── SYS-06 Notifications ────────────────────────────────────────────
    public List<NotifRuleRow> ListNotificationRules()
    {
        const string sql = """
            SELECT  NotificationRuleID, EventTypeCode, EventName, ModuleCode, ProcessCode,
                    TriggerCondition, ISNULL(IsEnabled, 1) AS IsEnabled,
                    ChannelsJSON, RecipientRolesJSON
            FROM    dbo.SYS_NotificationRule
            ORDER   BY ModuleCode, EventTypeCode;
            """;
        return Query(sql, r => new NotifRuleRow(
            (int)r["NotificationRuleID"], r["EventTypeCode"] as string,
            r["EventName"] as string, r["ModuleCode"] as string,
            r["ProcessCode"] as string,
            r["TriggerCondition"] as string,
            (bool)r["IsEnabled"],
            r["ChannelsJSON"] as string, r["RecipientRolesJSON"] as string));
    }

    public void InsertNotificationRule(string? eventTypeCode, string? eventName,
        string? moduleCode, string? processCode, string? triggerCondition, bool isEnabled,
        string? channelsJson, string? recipientRolesJson, string createdBy)
    {
        const string sql = """
            INSERT INTO dbo.SYS_NotificationRule
                (EventTypeCode, EventName, ModuleCode, ProcessCode, TriggerCondition,
                 IsEnabled, ChannelsJSON, RecipientRolesJSON, CreatedBy, CreatedTS)
            VALUES
                (@EventTypeCode, @EventName, @ModuleCode, @ProcessCode, @TriggerCondition,
                 @IsEnabled, @ChannelsJSON, @RecipientRolesJSON, @CreatedBy, SYSDATETIME());
            """;
        Exec(sql,
            ("@EventTypeCode",     (object?)eventTypeCode      ?? DBNull.Value),
            ("@EventName",         (object?)eventName          ?? DBNull.Value),
            ("@ModuleCode",        (object?)moduleCode         ?? DBNull.Value),
            ("@ProcessCode",       (object?)processCode        ?? DBNull.Value),
            ("@TriggerCondition",  (object?)triggerCondition   ?? DBNull.Value),
            ("@IsEnabled",         isEnabled),
            ("@ChannelsJSON",      (object?)channelsJson       ?? DBNull.Value),
            ("@RecipientRolesJSON",(object?)recipientRolesJson ?? DBNull.Value),
            ("@CreatedBy",         createdBy));
    }

    public void UpdateNotificationRule(int id, string? eventTypeCode, string? eventName,
        string? moduleCode, string? processCode, string? triggerCondition, bool isEnabled,
        string? channelsJson, string? recipientRolesJson, string modifiedBy)
    {
        const string sql = """
            UPDATE dbo.SYS_NotificationRule SET
                EventTypeCode     = @EventTypeCode,
                EventName         = @EventName,
                ModuleCode        = @ModuleCode,
                ProcessCode       = @ProcessCode,
                TriggerCondition  = @TriggerCondition,
                IsEnabled         = @IsEnabled,
                ChannelsJSON      = @ChannelsJSON,
                RecipientRolesJSON= @RecipientRolesJSON,
                ModifiedBy        = @ModifiedBy,
                ModifiedTS        = SYSDATETIME()
            WHERE NotificationRuleID = @Id;
            """;
        Exec(sql,
            ("@Id",                id),
            ("@EventTypeCode",     (object?)eventTypeCode      ?? DBNull.Value),
            ("@EventName",         (object?)eventName          ?? DBNull.Value),
            ("@ModuleCode",        (object?)moduleCode         ?? DBNull.Value),
            ("@ProcessCode",       (object?)processCode        ?? DBNull.Value),
            ("@TriggerCondition",  (object?)triggerCondition   ?? DBNull.Value),
            ("@IsEnabled",         isEnabled),
            ("@ChannelsJSON",      (object?)channelsJson       ?? DBNull.Value),
            ("@RecipientRolesJSON",(object?)recipientRolesJson ?? DBNull.Value),
            ("@ModifiedBy",        modifiedBy));
    }

    public void DeleteNotificationRule(int id)
    {
        Exec("DELETE dbo.SYS_NotificationRule WHERE NotificationRuleID = @Id", ("@Id", id));
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

    // ── SYS-03 Screen Management ───────────────────────────────────────
    public List<ScreenRow> ListScreens(string? moduleCode = null)
    {
        const string sql = """
            SELECT  m.ScreenID, m.ScreenCode, m.ModuleCode, m.ProcessCode, m.SubProcessCode, m.ScreenName, m.ScreenNameEn,
                    m.HRef, m.LidLabel, m.SortOrder,
                    ISNULL(m.IsVisible, 1) AS IsVisible,
                    (SELECT COUNT(DISTINCT rp.RoleName) FROM dbo.SYS_RolePermission rp WHERE rp.ScreenCode = m.ScreenCode) AS RoleCount
            FROM    dbo.SYS_Screen m
            {WHERE}
            ORDER   BY m.ModuleCode, ISNULL(m.SortOrder, 999), m.ScreenCode;
            """;
        var where = moduleCode is null ? "" : "WHERE m.ModuleCode = @Module";
        var query = sql.Replace("{WHERE}", where);
        return moduleCode is null
            ? Query(query, MapScreen)
            : Query(query, MapScreen, ("@Module", moduleCode));
    }

    public void InsertScreen(string screenCode, string moduleCode, string? processCode,
        string? subProcessCode, string screenName, string? screenNameEn, string? href, string? lidLabel,
        int? sortOrder, bool isVisible, string createdBy)
    {
        const string sql = """
            INSERT INTO dbo.SYS_Screen
                   (ScreenCode, ModuleCode, ProcessCode, SubProcessCode, ScreenName, ScreenNameEn, HRef, LidLabel, SortOrder, IsVisible, CreatedBy)
            VALUES (@Code, @Module, @Process, @SubProc, @Name, @NameEn, @HRef, @Lid, @Sort, @Visible, @CreatedBy)
            """;
        Exec(sql,
            ("@Code",      screenCode),
            ("@Module",    moduleCode),
            ("@Process",   (object?)processCode    ?? DBNull.Value),
            ("@SubProc",   (object?)subProcessCode ?? DBNull.Value),
            ("@Name",      screenName),
            ("@NameEn",    (object?)screenNameEn   ?? DBNull.Value),
            ("@HRef",      (object?)href            ?? DBNull.Value),
            ("@Lid",       (object?)lidLabel        ?? DBNull.Value),
            ("@Sort",      (object?)sortOrder       ?? DBNull.Value),
            ("@Visible",   isVisible),
            ("@CreatedBy", createdBy));
    }

    public void UpdateScreen(int screenId, string screenCode, string moduleCode, string? processCode,
        string? subProcessCode, string screenName, string? screenNameEn, string? href, string? lidLabel,
        int? sortOrder, bool isVisible, string modifiedBy)
    {
        const string sql = """
            UPDATE dbo.SYS_Screen
            SET    ScreenCode      = @Code,
                   ModuleCode      = @Module,
                   ProcessCode     = @Process,
                   SubProcessCode  = @SubProc,
                   ScreenName      = @Name,
                   ScreenNameEn    = @NameEn,
                   HRef            = @HRef,
                   LidLabel        = @Lid,
                   SortOrder       = @Sort,
                   IsVisible       = @Visible,
                   ModifiedBy      = @ModifiedBy,
                   ModifiedTS      = SYSDATETIME()
            WHERE  ScreenID = @Id
            """;
        Exec(sql,
            ("@Id",         screenId),
            ("@Code",       screenCode),
            ("@Module",     moduleCode),
            ("@Process",    (object?)processCode    ?? DBNull.Value),
            ("@SubProc",    (object?)subProcessCode ?? DBNull.Value),
            ("@Name",       screenName),
            ("@NameEn",     (object?)screenNameEn   ?? DBNull.Value),
            ("@HRef",       (object?)href            ?? DBNull.Value),
            ("@Lid",        (object?)lidLabel        ?? DBNull.Value),
            ("@Sort",       (object?)sortOrder       ?? DBNull.Value),
            ("@Visible",    isVisible),
            ("@ModifiedBy", modifiedBy));
    }

    public void DeleteScreen(int screenId, string deletedBy)
    {
        Exec("DELETE FROM dbo.SYS_Screen WHERE ScreenID = @Id", ("@Id", screenId));
    }

    private static ScreenRow MapScreen(IDataReader r) => new(
        (int)r["ScreenID"], (string)r["ScreenCode"], (string)r["ModuleCode"],
        r["ProcessCode"] as string, r["SubProcessCode"] as string,
        (string)r["ScreenName"], r["ScreenNameEn"] as string,
        r["HRef"] as string, r["LidLabel"] as string,
        r["SortOrder"] as int?, (bool)r["IsVisible"], (int)r["RoleCount"]);

    // ── SYS-07 Notification Channels ───────────────────────────────────
    public List<NotifChannelRow> ListNotificationChannels()
    {
        const string sql = """
            SELECT  c.NotificationChannelID, c.UserID,
                    ISNULL(u.UserName, c.UserID) AS UserName,
                    c.Channel, c.Address,
                    ISNULL(c.IsEnabled, 1) AS IsEnabled,
                    c.QuietHoursStart, c.QuietHoursEnd, c.VerifiedAt
            FROM    dbo.SYS_NotificationChannel c
            LEFT JOIN dbo.AspNetUsers u ON u.Id = c.UserID
            ORDER   BY UserName, c.Channel;
            """;
        return Query(sql, r => new NotifChannelRow(
            (int)r["NotificationChannelID"],
            r["UserID"]     as string,
            r["UserName"]   as string,
            r["Channel"]    as string,
            r["Address"]    as string,
            (bool)r["IsEnabled"],
            r["QuietHoursStart"] as TimeSpan?,
            r["QuietHoursEnd"]   as TimeSpan?,
            r["VerifiedAt"]      as DateTime?));
    }

    public void InsertNotificationChannel(string? userId, string channel, string? address,
        bool isEnabled, TimeSpan? quietStart, TimeSpan? quietEnd, string createdBy)
    {
        const string sql = """
            INSERT INTO dbo.SYS_NotificationChannel
                (UserID, Channel, Address, IsEnabled, QuietHoursStart, QuietHoursEnd,
                 CreatedBy, CreatedTS)
            VALUES
                (@UserID, @Channel, @Address, @IsEnabled, @QuietStart, @QuietEnd,
                 @CreatedBy, SYSDATETIME())
            """;
        Exec(sql,
            ("@UserID",     (object?)userId    ?? DBNull.Value),
            ("@Channel",    channel),
            ("@Address",    (object?)address   ?? DBNull.Value),
            ("@IsEnabled",  isEnabled),
            ("@QuietStart", (object?)quietStart ?? DBNull.Value),
            ("@QuietEnd",   (object?)quietEnd   ?? DBNull.Value),
            ("@CreatedBy",  createdBy));
    }

    public void UpdateNotificationChannel(int id, string? userId, string channel,
        string? address, bool isEnabled, TimeSpan? quietStart, TimeSpan? quietEnd,
        string modifiedBy)
    {
        const string sql = """
            UPDATE dbo.SYS_NotificationChannel
            SET    UserID          = @UserID,
                   Channel         = @Channel,
                   Address         = @Address,
                   IsEnabled       = @IsEnabled,
                   QuietHoursStart = @QuietStart,
                   QuietHoursEnd   = @QuietEnd,
                   ModifiedBy      = @ModifiedBy,
                   ModifiedTS      = SYSDATETIME()
            WHERE  NotificationChannelID = @Id
            """;
        Exec(sql,
            ("@Id",         id),
            ("@UserID",     (object?)userId    ?? DBNull.Value),
            ("@Channel",    channel),
            ("@Address",    (object?)address   ?? DBNull.Value),
            ("@IsEnabled",  isEnabled),
            ("@QuietStart", (object?)quietStart ?? DBNull.Value),
            ("@QuietEnd",   (object?)quietEnd   ?? DBNull.Value),
            ("@ModifiedBy", modifiedBy));
    }

    public void DeleteNotificationChannel(int id)
    {
        Exec("DELETE dbo.SYS_NotificationChannel WHERE NotificationChannelID = @Id",
            ("@Id", id));
    }

    // ── SYS-08 System Config ────────────────────────────────────────────
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

    public void SetConfig(int configId, string? value, bool isActive)
    {
        const string sql = """
            UPDATE dbo.SYS_Config
            SET    ConfigValue = @Value,
                   IsActive    = @IsActive,
                   ModifiedTS  = SYSDATETIME()
            WHERE  ConfigID = @Id
            """;
        Exec(sql,
            ("@Id",       configId),
            ("@Value",    (object?)value ?? DBNull.Value),
            ("@IsActive", isActive));
    }

    // 단일 설정키의 값·활성여부 (컬처 결정/언어 스위처 판정용). 없으면 null.
    public (string? Value, bool IsActive)? GetConfigFlag(string key)
    {
        const string sql = "SELECT TOP 1 ConfigValue, ISNULL(IsActive,1) AS IsActive FROM dbo.SYS_Config WHERE ConfigKey = @Key;";
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Key", SqlDbType.VarChar, 60).Value = key;
        using var rdr = cmd.ExecuteReader();
        return rdr.Read() ? (rdr["ConfigValue"] as string, (bool)rdr["IsActive"]) : null;
    }

    // 설정값을 양의 정수로 조회. 없거나 파싱 실패 시 fallback (예: DASH_REFRESH_SEC).
    public int GetConfigInt(string key, int fallback)
    {
        var row = GetConfigFlag(key);
        return row is { } r && int.TryParse(r.Value, out var v) && v > 0 ? v : fallback;
    }

    public void SetRolePermission(string roleId, string roleName,
        string moduleCode, string? processCode, string screenCode,
        string? permLevel, string modifiedBy)
    {
        if (string.IsNullOrEmpty(permLevel))
        {
            Exec("DELETE dbo.SYS_RolePermission WHERE RoleID = @RoleID AND ScreenCode = @Screen",
                ("@RoleID", roleId),
                ("@Screen", screenCode));
            return;
        }
        const string sql = """
            MERGE dbo.SYS_RolePermission AS tgt
            USING (SELECT @RoleID AS RoleID, @Screen AS ScreenCode) AS src
                  ON tgt.RoleID = src.RoleID AND tgt.ScreenCode = src.ScreenCode
            WHEN MATCHED THEN
                UPDATE SET PermissionLevel = @Level,
                           ModuleCode      = @Module,
                           ModifiedBy      = @ModifiedBy,
                           ModifiedTS      = SYSDATETIME()
            WHEN NOT MATCHED THEN
                INSERT (RoleID, RoleName, ModuleCode, ScreenCode, PermissionLevel,
                        IsSystemRole, EffectiveTS, CreatedBy, CreatedTS)
                VALUES (@RoleID, @RoleName, @Module, @Screen, @Level,
                        0, SYSDATETIME(), @ModifiedBy, SYSDATETIME());
            """;
        Exec(sql,
            ("@RoleID",     roleId),
            ("@RoleName",   roleName),
            ("@Module",     moduleCode),
            ("@Screen",     screenCode),
            ("@Level",      permLevel),
            ("@ModifiedBy", modifiedBy));
    }

    public (bool Ok, int Ms, string? Host) PingDatabase()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var conn = _f.OpenConnection();
            using var cmd  = new SqlCommand("SELECT @@SERVERNAME", conn);
            var host = cmd.ExecuteScalar() as string;
            sw.Stop();
            return (true, (int)sw.ElapsedMilliseconds, host);
        }
        catch
        {
            sw.Stop();
            return (false, (int)sw.ElapsedMilliseconds, null);
        }
    }

    // ── SYS-010 System Health ───────────────────────────────────────────
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
              (SELECT COUNT(*) FROM dbo.SYS_Screen WHERE ISNULL(IsVisible,1)=1)                AS Screens,
              (SELECT COUNT(*) FROM dbo.SYS_RolePermission)                                    AS Perms,
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
            return new HealthKpi(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0L);
        return new HealthKpi(
            (int)rdr["Users"], (int)rdr["Roles"],
            (int)rdr["IfOk"], (int)rdr["IfDown"],
            (int)rdr["AuditDay"], (int)rdr["NotifDay"],
            (int)rdr["NotifFail"], (int)rdr["Cfg"],
            (int)rdr["Screens"], (int)rdr["Perms"],
            rdr["RowsApprox"] as long? ?? 0L);
    }

    // ── SYS-01 User Profile Write ───────────────────────────────────────
    public void CreateProfile(string userId, string employeeNo, string employeeName,
        string? department, string? plantCode, string? defaultShift, string createdBy,
        string? assignedLinesJson = null)
    {
        const string sql = """
            INSERT INTO dbo.SYS_UserProfile
                (UserID, EmployeeNo, EmployeeName, Department, PlantCode, DefaultShift,
                 AccountStatus, FailedLoginCount, AssignedLines, CreatedBy, CreatedTS)
            VALUES
                (@UserID, @EmpNo, @EmpName, @Dept, @Plant, @Shift,
                 'ACTIVE', 0, @Lines, @CreatedBy, SYSDATETIME())
            """;
        Exec(sql,
            ("@UserID",    userId),
            ("@EmpNo",     employeeNo),
            ("@EmpName",   employeeName),
            ("@Dept",      (object?)department       ?? DBNull.Value),
            ("@Plant",     (object?)plantCode         ?? DBNull.Value),
            ("@Shift",     (object?)defaultShift      ?? DBNull.Value),
            ("@Lines",     (object?)assignedLinesJson ?? DBNull.Value),
            ("@CreatedBy", createdBy));
    }

    // 자기가입(Register)용 — 관리자 승인 전 승인대기(PENDING) 프로필. 승인 시 관리자가 ACTIVE로 변경.
    public void CreatePendingProfile(string userId, string? employeeName, string createdBy)
    {
        const string sql = """
            INSERT INTO dbo.SYS_UserProfile
                (UserID, EmployeeName, AccountStatus, FailedLoginCount, CreatedBy, CreatedTS)
            VALUES
                (@UserID, @EmpName, 'PENDING', 0, @CreatedBy, SYSDATETIME())
            """;
        Exec(sql,
            ("@UserID",    userId),
            ("@EmpName",   (object?)employeeName ?? DBNull.Value),
            ("@CreatedBy", createdBy));
    }

    public void UpdateProfile(string userId, string employeeNo, string employeeName,
        string? department, string? plantCode, string? defaultShift,
        string accountStatus, string modifiedBy, string? assignedLinesJson = null)
    {
        const string sql = """
            UPDATE dbo.SYS_UserProfile
            SET    EmployeeNo       = @EmpNo,
                   EmployeeName     = @EmpName,
                   Department       = @Dept,
                   PlantCode        = @Plant,
                   DefaultShift     = @Shift,
                   AccountStatus    = @Status,
                   FailedLoginCount = CASE WHEN UPPER(@Status) = 'ACTIVE' THEN 0 ELSE FailedLoginCount END,
                   AssignedLines    = @Lines,
                   ModifiedBy       = @ModifiedBy,
                   ModifiedTS       = SYSDATETIME()
            WHERE  UserID = @UserID
            """;
        Exec(sql,
            ("@UserID",     userId),
            ("@EmpNo",      employeeNo),
            ("@EmpName",    employeeName),
            ("@Dept",       (object?)department       ?? DBNull.Value),
            ("@Plant",      (object?)plantCode         ?? DBNull.Value),
            ("@Shift",      (object?)defaultShift      ?? DBNull.Value),
            ("@Status",     accountStatus),
            ("@Lines",      (object?)assignedLinesJson ?? DBNull.Value),
            ("@ModifiedBy", modifiedBy));
    }

    /// <summary>
    /// Sets (or replaces) the operator's POP login PIN hash. Called from SYS-01 only
    /// when an admin actually types a PIN — leaving the field blank keeps the current
    /// value. Web login (AspNetUsers.PasswordHash) is untouched. Pass a hash produced
    /// by PinHasher.Hash; this method never sees the raw PIN.
    /// </summary>
    public void SetPin(string userId, string pinHash, string modifiedBy)
    {
        Exec("""
            UPDATE dbo.SYS_UserProfile
            SET    PinHash    = @PinHash,
                   ModifiedBy = @ModifiedBy,
                   ModifiedTS = SYSDATETIME()
            WHERE  UserID = @UserID
            """,
            ("@UserID",     userId),
            ("@PinHash",    pinHash),
            ("@ModifiedBy", modifiedBy));
    }

    // ── MD_Line 목록 (ACTIVE만) ─────────────────────────────────────────
    public List<LineRow> ListActiveLines()
    {
        const string sql = """
            SELECT LineID, ISNULL(LineName, LineID) AS LineName, LineNameEn
            FROM   dbo.MD_Line
            WHERE  Status = 'ACTIVE'
            ORDER  BY LineID;
            """;
        return Query(sql, r => new LineRow(
            (string)r["LineID"],
            (string)r["LineName"],
            r["LineNameEn"] as string));
    }

    public void DeleteProfile(string userId)
    {
        Exec("DELETE dbo.SYS_UserProfile WHERE UserID = @UserID", ("@UserID", userId));
    }

    public bool ProfileExists(string userId)
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(
            "SELECT COUNT(1) FROM dbo.SYS_UserProfile WHERE UserID = @UserID", conn);
        cmd.Parameters.AddWithValue("@UserID", userId);
        return (int)cmd.ExecuteScalar()! > 0;
    }

    // ── helpers ──────────────────────────────────────────────────────────
    private void Exec(string sql, params (string Name, object Value)[] pars)
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        foreach (var (n, v) in pars) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

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
