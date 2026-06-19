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
        string? AccountStatus, DateTime? LastLoginTs, string? RolesCsv,
        string? AssignedLines);

    public sealed record LineRow(string LineId, string LineName);

    public sealed record RoleRow(string RoleId, string RoleName, int UserCount);

    public sealed record RolePermRow(int RolePermissionId, string? RoleId, string? RoleName,
        string? ModuleCode, string? ScreenCode, string? PermissionLevel, bool IsSystemRole);

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
        string? EventName, string? SourceModule, string? TriggerCondition, bool IsEnabled,
        string? ChannelsJson, string? RecipientRolesJson);

    public sealed record NotifHistoryRow(long NotificationHistoryId, DateTime? SentAt,
        string? EventTypeCode, string? RecipientUserId, string? Channel,
        string? Subject, string? Status, int? RetryCount, string? ErrorMsg);

    public sealed record NotifChannelRow(int NotificationChannelId, string? UserId,
        string? UserName, string? Channel, string? Address, bool IsEnabled,
        TimeSpan? QuietHoursStart, TimeSpan? QuietHoursEnd, DateTime? VerifiedAt);

    public sealed record MenuRow(int MenuId, string MenuCode, string SectionCode,
        string MenuName, string? MenuNameEn, string? HRef, string? LidLabel,
        int? SortOrder, bool IsVisible, int RoleCount);

    public sealed record MenuRoleRow(int MenuRoleId, int MenuId,
        string RoleName, string PermType);

    public sealed record ConfigRow(int ConfigId, string? ConfigKey, string? ConfigType,
        string? Category, string? ConfigValue, string? CodeName, string? Unit,
        bool IsActive, int? SortOrder);

    public sealed record HealthKpi(int Users, int Roles, int InterfacesOk, int InterfacesDown,
        int AuditLast24h, int NotifLast24h, int NotifFailedLast24h, int ConfigKeys,
        long DbRowsApprox);

    public sealed record UserSelectRow(string Id, string? UserName, string? Email, string? PhoneNumber);

    // ── SYS-01 User Management ──────────────────────────────────────────
    public List<UserRow> ListUsers(int topN = 200)
    {
        const string sql = """
            SELECT TOP (@N)
                   u.Id, u.UserName, u.Email, u.EmailConfirmed, u.AccessFailedCount,
                   CASE WHEN u.LockoutEnd IS NOT NULL AND u.LockoutEnd > SYSDATETIMEOFFSET()
                        THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS LockedOut,
                   p.EmployeeNo, p.EmployeeName, p.Department, p.AccountStatus, p.LastLoginTS,
                   p.AssignedLines,
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
            r["RolesCsv"] as string, r["AssignedLines"] as string),
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

    public List<CalendarRow> ListCalendarRange(DateTime from, DateTime to)
    {
        const string sql = """
            SELECT  FactoryCalendarID, CalendarDate, DayType, HolidayName,
                    ShiftCount, ShiftCode, StartTime, EndTime, BreakMinutes,
                    NetWorkHours, Plant
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
            r["Plant"] as string),
            ("@From", from.Date), ("@To", to.Date));
    }

    public void InsertCalendarShift(DateTime date, string dayType, string? holidayName,
        int? shiftCount, string? shiftCode, TimeSpan? start, TimeSpan? end,
        int? breakMin, decimal? netHours, int calendarYear, string plant, string createdBy)
    {
        const string sql = """
            INSERT INTO dbo.SYS_FactoryCalendar
                   (CalendarDate, DayType, HolidayName, ShiftCount, ShiftCode,
                    StartTime, EndTime, BreakMinutes, NetWorkHours,
                    CalendarYear, Plant, CreatedBy, CreatedTS)
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
            ("@Plant",       string.IsNullOrWhiteSpace(plant) ? (object)DBNull.Value : plant),
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
                    TriggerCondition, ISNULL(IsEnabled, 1) AS IsEnabled,
                    ChannelsJSON, RecipientRolesJSON
            FROM    dbo.SYS_NotificationRule
            ORDER   BY SourceModule, EventTypeCode;
            """;
        return Query(sql, r => new NotifRuleRow(
            (int)r["NotificationRuleID"], r["EventTypeCode"] as string,
            r["EventName"] as string, r["SourceModule"] as string,
            r["TriggerCondition"] as string,
            (bool)r["IsEnabled"],
            r["ChannelsJSON"] as string, r["RecipientRolesJSON"] as string));
    }

    public void InsertNotificationRule(string? eventTypeCode, string? eventName,
        string? sourceModule, string? triggerCondition, bool isEnabled,
        string? channelsJson, string? recipientRolesJson, string createdBy)
    {
        const string sql = """
            INSERT INTO dbo.SYS_NotificationRule
                (EventTypeCode, EventName, SourceModule, TriggerCondition,
                 IsEnabled, ChannelsJSON, RecipientRolesJSON, CreatedBy, CreatedTS)
            VALUES
                (@EventTypeCode, @EventName, @SourceModule, @TriggerCondition,
                 @IsEnabled, @ChannelsJSON, @RecipientRolesJSON, @CreatedBy, SYSDATETIME());
            """;
        Exec(sql,
            ("@EventTypeCode",     (object?)eventTypeCode      ?? DBNull.Value),
            ("@EventName",         (object?)eventName          ?? DBNull.Value),
            ("@SourceModule",      (object?)sourceModule       ?? DBNull.Value),
            ("@TriggerCondition",  (object?)triggerCondition   ?? DBNull.Value),
            ("@IsEnabled",         isEnabled),
            ("@ChannelsJSON",      (object?)channelsJson       ?? DBNull.Value),
            ("@RecipientRolesJSON",(object?)recipientRolesJson ?? DBNull.Value),
            ("@CreatedBy",         createdBy));
    }

    public void UpdateNotificationRule(int id, string? eventTypeCode, string? eventName,
        string? sourceModule, string? triggerCondition, bool isEnabled,
        string? channelsJson, string? recipientRolesJson, string modifiedBy)
    {
        const string sql = """
            UPDATE dbo.SYS_NotificationRule SET
                EventTypeCode     = @EventTypeCode,
                EventName         = @EventName,
                SourceModule      = @SourceModule,
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
            ("@SourceModule",      (object?)sourceModule       ?? DBNull.Value),
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

    // ── SYS-04 Menu Management ─────────────────────────────────────────
    public List<MenuRow> ListMenus(string? sectionCode = null)
    {
        const string sql = """
            SELECT  m.MenuID, m.MenuCode, m.SectionCode, m.MenuName, m.MenuNameEn,
                    m.HRef, m.LidLabel, m.SortOrder,
                    ISNULL(m.IsVisible, 1) AS IsVisible,
                    (SELECT COUNT(*) FROM dbo.MD_MenuRole mr WHERE mr.MenuID = m.MenuID) AS RoleCount
            FROM    dbo.MD_Menu m
            {WHERE}
            ORDER   BY m.SectionCode, ISNULL(m.SortOrder, 999), m.MenuCode;
            """;
        var where = sectionCode is null ? "" : "WHERE m.SectionCode = @Section";
        var query = sql.Replace("{WHERE}", where);
        return sectionCode is null
            ? Query(query, MapMenu)
            : Query(query, MapMenu, ("@Section", sectionCode));
    }

    public List<MenuRoleRow> ListMenuRoles(int menuId)
    {
        const string sql = """
            SELECT  MenuRoleID, MenuID, RoleName, PermType
            FROM    dbo.MD_MenuRole
            WHERE   MenuID = @MenuID
            ORDER   BY RoleName, PermType;
            """;
        return Query(sql, r => new MenuRoleRow(
            (int)r["MenuRoleID"], (int)r["MenuID"],
            (string)r["RoleName"], (string)r["PermType"]),
            ("@MenuID", menuId));
    }

    public void UpdateMenuBasic(int menuId, string? menuNameEn, bool isVisible, string modifiedBy)
    {
        const string sql = """
            UPDATE dbo.MD_Menu
            SET    MenuNameEn  = @NameEn,
                   IsVisible   = @Visible,
                   ModifiedBy  = @ModifiedBy,
                   ModifiedTS  = SYSDATETIME()
            WHERE  MenuID = @Id
            """;
        Exec(sql,
            ("@Id",         menuId),
            ("@NameEn",     (object?)menuNameEn ?? DBNull.Value),
            ("@Visible",    isVisible),
            ("@ModifiedBy", modifiedBy));
    }

    public void InsertMenuRole(int menuId, string roleName, string permType, string createdBy)
    {
        const string sql = """
            INSERT INTO dbo.MD_MenuRole (MenuID, RoleName, PermType, CreatedBy)
            VALUES (@MenuID, @Role, @Perm, @CreatedBy)
            """;
        Exec(sql,
            ("@MenuID",    menuId),
            ("@Role",      roleName),
            ("@Perm",      permType),
            ("@CreatedBy", createdBy));
    }

    public void DeleteMenuRole(int menuRoleId)
    {
        Exec("DELETE dbo.MD_MenuRole WHERE MenuRoleID = @Id", ("@Id", menuRoleId));
    }

    public bool MenuRoleExists(int menuId, string roleName, string permType)
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(
            "SELECT COUNT(1) FROM dbo.MD_MenuRole WHERE MenuID=@M AND RoleName=@R AND PermType=@P", conn);
        cmd.Parameters.AddWithValue("@M", menuId);
        cmd.Parameters.AddWithValue("@R", roleName);
        cmd.Parameters.AddWithValue("@P", permType);
        return (int)cmd.ExecuteScalar()! > 0;
    }

    private static MenuRow MapMenu(IDataReader r) => new(
        (int)r["MenuID"], (string)r["MenuCode"], (string)r["SectionCode"],
        (string)r["MenuName"], r["MenuNameEn"] as string,
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

    // ── SYS-01 User Profile Write ───────────────────────────────────────
    public void CreateProfile(string userId, string employeeNo, string employeeName,
        string? department, string? plant, string? defaultShift, string createdBy,
        string? assignedLinesJson = null)
    {
        const string sql = """
            INSERT INTO dbo.SYS_UserProfile
                (UserID, EmployeeNo, EmployeeName, Department, Plant, DefaultShift,
                 AccountStatus, FailedLoginCount, AssignedLines, CreatedBy, CreatedTS)
            VALUES
                (@UserID, @EmpNo, @EmpName, @Dept, @Plant, @Shift,
                 'Active', 0, @Lines, @CreatedBy, SYSDATETIME())
            """;
        Exec(sql,
            ("@UserID",    userId),
            ("@EmpNo",     employeeNo),
            ("@EmpName",   employeeName),
            ("@Dept",      (object?)department       ?? DBNull.Value),
            ("@Plant",     (object?)plant             ?? DBNull.Value),
            ("@Shift",     (object?)defaultShift      ?? DBNull.Value),
            ("@Lines",     (object?)assignedLinesJson ?? DBNull.Value),
            ("@CreatedBy", createdBy));
    }

    public void UpdateProfile(string userId, string employeeNo, string employeeName,
        string? department, string? plant, string? defaultShift,
        string accountStatus, string modifiedBy, string? assignedLinesJson = null)
    {
        const string sql = """
            UPDATE dbo.SYS_UserProfile
            SET    EmployeeNo    = @EmpNo,
                   EmployeeName  = @EmpName,
                   Department    = @Dept,
                   Plant         = @Plant,
                   DefaultShift  = @Shift,
                   AccountStatus = @Status,
                   AssignedLines = @Lines,
                   ModifiedBy    = @ModifiedBy,
                   ModifiedTS    = SYSDATETIME()
            WHERE  UserID = @UserID
            """;
        Exec(sql,
            ("@UserID",     userId),
            ("@EmpNo",      employeeNo),
            ("@EmpName",    employeeName),
            ("@Dept",       (object?)department       ?? DBNull.Value),
            ("@Plant",      (object?)plant             ?? DBNull.Value),
            ("@Shift",      (object?)defaultShift      ?? DBNull.Value),
            ("@Status",     accountStatus),
            ("@Lines",      (object?)assignedLinesJson ?? DBNull.Value),
            ("@ModifiedBy", modifiedBy));
    }

    // ── MD_Line 목록 (ACTIVE만) ─────────────────────────────────────────
    public List<LineRow> ListActiveLines()
    {
        const string sql = """
            SELECT LineID, ISNULL(LineName, LineID) AS LineName
            FROM   dbo.MD_Line
            WHERE  Status = 'ACTIVE'
            ORDER  BY LineID;
            """;
        return Query(sql, r => new LineRow((string)r["LineID"], (string)r["LineName"]));
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
