using System.Data;
using AMES.Data.Connection;
using AMES.Data.Services;
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
        int FailedLoginCount, bool HasProfile = true, bool IsAdmin = false)
    {
        // 등록된 계정 — 프로필이 있어야 로그인할 수 있다. 담당자·수신자 콤보는 이 계정만 고르게 한다(10-07)
        public bool IsApproved => HasProfile;
    }

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

    public sealed record UserSelectRow(string Id, string? UserName, string? Email, string? PhoneNumber, bool IsApproved = true);

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
                   ISNULL(p.FailedLoginCount, 0) AS FailedLoginCount,
                   CAST(CASE WHEN p.UserID IS NULL THEN 0 ELSE 1 END AS BIT) AS HasProfile,
                   STUFF((SELECT ', ' + r.Name
                          FROM   dbo.AspNetUserRoles ur
                          JOIN   dbo.AspNetRoles r ON r.Id = ur.RoleId
                          WHERE  ur.UserId = u.Id
                          FOR XML PATH('')), 1, 2, '') AS RolesCsv,
                   CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.AspNetUserRoles ar WHERE ar.UserId = u.Id AND ar.RoleId = @AdminRole)
                        THEN 1 ELSE 0 END AS BIT) AS IsAdmin
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
            r["RolesCsv"] as string,
            Convert.ToInt32(r["FailedLoginCount"]),
            (bool)r["HasProfile"], (bool)r["IsAdmin"]),
            ("@N", topN), ("@AdminRole", SystemRoles.AdminId));
    }

    /// <summary>사용자가 가진 역할(ID·현재 이름) — 화면 권한·Admin 판정은 이 ID 로 한다(쿠키의 역할 이름은 쓰지 않는다).</summary>
    public List<(string Id, string Name)> ListUserRoles(string userId)
    {
        const string sql = """
            SELECT ur.RoleId, ISNULL(r.Name, N'') AS Name
            FROM   dbo.AspNetUserRoles ur
            LEFT JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
            WHERE  ur.UserId = @UserId;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@UserId", SqlDbType.NVarChar, 450).Value = userId;
        using var r = cmd.ExecuteReader();
        var list = new List<(string, string)>();
        while (r.Read()) list.Add(((string)r["RoleId"], (string)r["Name"]));
        return list;
    }

    public bool UserHasRole(string userId, string roleId)
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand("SELECT 1 FROM dbo.AspNetUserRoles WHERE UserId = @UserId AND RoleId = @RoleId", conn);
        cmd.Parameters.Add("@UserId", SqlDbType.NVarChar, 450).Value = userId;
        cmd.Parameters.Add("@RoleId", SqlDbType.NVarChar, 450).Value = roleId;
        return cmd.ExecuteScalar() is not null;
    }

    public List<UserSelectRow> ListUsersForSelect()
    {
        const string sql = """
            SELECT u.Id, u.UserName, u.Email, u.PhoneNumber,
                   CAST(CASE WHEN p.UserID IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS IsApproved
            FROM   dbo.AspNetUsers u
            LEFT JOIN dbo.SYS_UserProfile p ON p.UserID = u.Id
            ORDER  BY UserName;
            """;
        return Query(sql, r => new UserSelectRow(
            (string)r["Id"], r["UserName"] as string,
            r["Email"] as string, r["PhoneNumber"] as string, (bool)r["IsApproved"]));
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

    public enum RoleChangeResult { Ok, NotFound, Protected, Duplicate, InUse }

    // 화면 권한·알림 수신·Admin/Supervisor 판정은 모두 역할 ID 로 하므로 이름 변경은 표시만 바뀐다 — 다시 로그인시키지 않는다.
    // 시스템 역할(SystemRoles)도 이름은 바꿀 수 있다. 권한 행의 RoleName 은 표시·옛 시드 호환용 사본이라 같이 바꾼다
    public RoleChangeResult RenameRole(string roleId, string newName)
    {
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        var oldName = RoleNameLocked(conn, tx, roleId);
        if (oldName is null) return RoleChangeResult.NotFound;

        const string sql = """
            IF EXISTS (SELECT 1 FROM dbo.AspNetRoles WHERE NormalizedName = @Norm AND Id <> @Id)
            BEGIN SELECT 0; RETURN; END
            UPDATE dbo.AspNetRoles SET Name = @Name, NormalizedName = @Norm, ConcurrencyStamp = CONVERT(nvarchar(36), NEWID()) WHERE Id = @Id;
            UPDATE dbo.SYS_RolePermission SET RoleName = @Name, RoleID = @Id WHERE RoleID = @Id OR (RoleID IS NULL AND RoleName = @Old);
            SELECT 1;
            """;
        using var cmd = new SqlCommand(sql, conn, tx);
        cmd.Parameters.Add("@Id",   SqlDbType.NVarChar, 450).Value = roleId;
        cmd.Parameters.Add("@Name", SqlDbType.VarChar, 40).Value   = newName;
        cmd.Parameters.Add("@Norm", SqlDbType.NVarChar, 256).Value = newName.ToUpperInvariant();
        cmd.Parameters.Add("@Old",  SqlDbType.VarChar, 40).Value   = oldName;
        if (Convert.ToInt32(cmd.ExecuteScalar()) == 0) return RoleChangeResult.Duplicate;
        tx.Commit();
        return RoleChangeResult.Ok;
    }

    // 사용자가 배정된 역할·시스템 역할(SystemRoles)은 지우지 않는다(사용자 결정 10-08·10-09). 권한 행과 알림 규칙의 수신 역할에서도 같이 뺀다
    public RoleChangeResult DeleteRole(string roleId)
    {
        if (SystemRoles.IsSystem(roleId)) return RoleChangeResult.Protected;
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        var name = RoleNameLocked(conn, tx, roleId);
        if (name is null) return RoleChangeResult.NotFound;

        const string sql = """
            IF EXISTS (SELECT 1 FROM dbo.AspNetUserRoles WITH (UPDLOCK, HOLDLOCK) WHERE RoleId = @Id)
            BEGIN SELECT 0; RETURN; END
            DELETE dbo.SYS_RolePermission WHERE RoleID = @Id OR (RoleID IS NULL AND RoleName = @Name);
            UPDATE r SET RecipientRolesJSON = x.NewJson
            FROM   dbo.SYS_NotificationRule r
            CROSS APPLY (SELECT CASE WHEN COUNT(*) = 0 THEN NULL
                                     ELSE N'[' + STRING_AGG(CAST(N'"' + STRING_ESCAPE(j.value, 'json') + N'"' AS nvarchar(max)), N',')
                                                 WITHIN GROUP (ORDER BY CAST(j.[key] AS int)) + N']' END AS NewJson
                         FROM OPENJSON(r.RecipientRolesJSON) j WHERE j.value <> @Id) x
            WHERE  ISJSON(r.RecipientRolesJSON) = 1
              AND  EXISTS (SELECT 1 FROM OPENJSON(r.RecipientRolesJSON) j WHERE j.value = @Id);
            DELETE dbo.AspNetRoleClaims   WHERE RoleId = @Id;
            DELETE dbo.AspNetRoles        WHERE Id = @Id;
            SELECT 1;
            """;
        using var cmd = new SqlCommand(sql, conn, tx);
        cmd.Parameters.Add("@Id",   SqlDbType.NVarChar, 450).Value = roleId;
        cmd.Parameters.Add("@Name", SqlDbType.VarChar, 40).Value   = name;
        if (Convert.ToInt32(cmd.ExecuteScalar()) == 0) return RoleChangeResult.InUse;
        tx.Commit();
        return RoleChangeResult.Ok;
    }

    static string? RoleNameLocked(SqlConnection conn, SqlTransaction tx, string roleId)
    {
        using var cmd = new SqlCommand("SELECT Name FROM dbo.AspNetRoles WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id", conn, tx);
        cmd.Parameters.Add("@Id", SqlDbType.NVarChar, 450).Value = roleId;
        return cmd.ExecuteScalar() as string;
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

    public sealed record CalendarShiftInput(string DayType, string? HolidayName, int? ShiftCount, string? ShiftCode,
        TimeSpan? Start, TimeSpan? End, int? BreakMin, decimal? NetHours, string PlantCode);

    // SYS-005 날짜 수정 — 지우기와 넣기를 한 트랜잭션으로 묶어, 넣다가 실패해도 그 날짜 행이 사라지지 않게 한다
    public void ReplaceCalendarDate(DateTime date, IReadOnlyList<CalendarShiftInput> rows, string actor)
    {
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        using (var del = new SqlCommand("DELETE dbo.SYS_FactoryCalendar WHERE CalendarDate = CAST(@Date AS DATE)", conn, tx))
        {
            del.Parameters.AddWithValue("@Date", date.Date);
            del.ExecuteNonQuery();
        }
        foreach (var r in rows)
            InsertCalendarShift(conn, tx, date, r.DayType, r.HolidayName, r.ShiftCount, r.ShiftCode,
                r.Start, r.End, r.BreakMin, r.NetHours, date.Year, r.PlantCode, actor);
        tx.Commit();
    }

    // SYS-005 일정 생성 — 기간 전체를 한 트랜잭션으로 넣는다. skipExisting 이 꺼져 있으면 그 날짜의 기존 행을 지우고 새로 넣는다
    // (10-08 사용자 결정 — 전에는 기존 행 위에 그대로 더해 같은 날짜·교대가 중복됐다). 반환값 = 넣은 행 수
    public int GenerateCalendar(IEnumerable<(DateTime Date, IReadOnlyList<CalendarShiftInput> Rows)> days, bool skipExisting, string actor)
    {
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        int inserted = 0;
        foreach (var (date, rows) in days)
        {
            using (var pre = new SqlCommand(skipExisting
                ? "SELECT COUNT(1) FROM dbo.SYS_FactoryCalendar WHERE CalendarDate = CAST(@Date AS DATE)"
                : "DELETE dbo.SYS_FactoryCalendar WHERE CalendarDate = CAST(@Date AS DATE)", conn, tx))
            {
                pre.Parameters.AddWithValue("@Date", date.Date);
                if (skipExisting) { if ((int)pre.ExecuteScalar()! > 0) continue; }
                else pre.ExecuteNonQuery();
            }
            foreach (var r in rows)
            {
                InsertCalendarShift(conn, tx, date, r.DayType, r.HolidayName, r.ShiftCount, r.ShiftCode,
                    r.Start, r.End, r.BreakMin, r.NetHours, date.Year, r.PlantCode, actor);
                inserted++;
            }
        }
        tx.Commit();
        return inserted;
    }

    static void InsertCalendarShift(SqlConnection conn, SqlTransaction? tx, DateTime date, string dayType, string? holidayName,
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
        using var cmd = new SqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("@Date",        date.Date);
        cmd.Parameters.AddWithValue("@DayType",     dayType);
        cmd.Parameters.AddWithValue("@HolidayName", (object?)holidayName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ShiftCount",  (object?)shiftCount  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ShiftCode",   (object?)shiftCode   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Start",       (object?)start    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@End",         (object?)end      ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Break",       (object?)breakMin ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Net",         (object?)netHours ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Year",        calendarYear);
        cmd.Parameters.AddWithValue("@Plant",       string.IsNullOrWhiteSpace(plantCode) ? (object)DBNull.Value : plantCode);
        cmd.Parameters.AddWithValue("@CreatedBy",   createdBy);
        cmd.ExecuteNonQuery();
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

    /// <summary>
    /// 인터페이스 모니터 1행 업서트 — 외부 연동 Worker 가 대상마다 실행 뒤 부른다.
    /// 실패 시 LastSyncTS 는 건드리지 않는다(마지막 성공 시각이어야 SYS-Interfaces 의 경과분이 의미 있다).
    /// actor 는 새 행의 CreatedBy·갱신 시 ModifiedBy — 어느 연동이 쓴 행인지 호출자가 밝힌다.
    /// </summary>
    public void UpsertInterfaceMonitor(string code, string name, string endpoint, int maxGapMin,
        bool ok, int? recordCount, string? error, string actor)
    {
        const string sql = """
            UPDATE dbo.SYS_InterfaceMonitor
            SET    InterfaceName   = @Name,
                   Endpoint        = @Ep,
                   MaxGapMinutes   = @Gap,
                   ConnStatus      = CASE WHEN @Ok = 1 THEN 'OK' ELSE 'ERROR' END,
                   LastSyncTS      = CASE WHEN @Ok = 1 THEN SYSDATETIME() ELSE LastSyncTS END,
                   LastRecordCount = CASE WHEN @Ok = 1 THEN @Cnt ELSE LastRecordCount END,
                   LastErrorMsg    = CASE WHEN @Ok = 1 THEN NULL ELSE @Err END,
                   RetryCount      = CASE WHEN @Ok = 1 THEN 0 ELSE ISNULL(RetryCount, 0) + 1 END,
                   ModifiedBy      = @Actor, ModifiedTS = SYSDATETIME()
            WHERE  InterfaceCode = @Code;
            IF @@ROWCOUNT = 0
                INSERT INTO dbo.SYS_InterfaceMonitor
                       (InterfaceCode, InterfaceName, Direction, Endpoint, Protocol, ConnStatus,
                        LastSyncTS, MaxGapMinutes, LastRecordCount, RetryCount, LastErrorMsg, IsEnabled, CreatedBy)
                VALUES (@Code, @Name, 'INBOUND', @Ep, 'REST',
                        CASE WHEN @Ok = 1 THEN 'OK' ELSE 'ERROR' END,
                        CASE WHEN @Ok = 1 THEN SYSDATETIME() ELSE NULL END,
                        @Gap, CASE WHEN @Ok = 1 THEN @Cnt ELSE NULL END,
                        CASE WHEN @Ok = 1 THEN 0 ELSE 1 END,
                        CASE WHEN @Ok = 1 THEN NULL ELSE @Err END, 1, @Actor);
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Code", SqlDbType.VarChar, 20).Value   = code;
        cmd.Parameters.Add("@Name", SqlDbType.NVarChar, 60).Value  = name.Length > 60 ? name[..60] : name;
        cmd.Parameters.Add("@Ep",   SqlDbType.VarChar, 255).Value  = endpoint.Length > 255 ? endpoint[..255] : endpoint;
        cmd.Parameters.Add("@Gap",  SqlDbType.Int).Value           = maxGapMin;
        cmd.Parameters.Add("@Ok",   SqlDbType.Bit).Value           = ok;
        cmd.Parameters.Add("@Cnt",  SqlDbType.Int).Value           = (object?)recordCount ?? DBNull.Value;
        var err = error is null ? null : error.Length > 1000 ? error[..1000] : error;
        cmd.Parameters.Add("@Err",  SqlDbType.NVarChar, 1000).Value = (object?)err ?? DBNull.Value;
        cmd.Parameters.Add("@Actor", SqlDbType.VarChar, 20).Value  = actor;   // CreatedBy VARCHAR(50)
        cmd.ExecuteNonQuery();
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

    /// <summary>SYS-007 조회 조건. 기간은 날짜 단위(From 00:00 ~ To 다음 날 00:00 미만), 나머지는 비우면 조건 없음.</summary>
    public sealed record AuditFilter(DateTime From, DateTime To, string? Search, string? ModuleCode, string? ProcessCode, string? Result);

    /// <summary>
    /// SYS-007 감사 로그 조회 — 조건을 DB 에서 걸고 건수 제한 없이 돌려준다(10-07 — 전에는 최근 200행 안에서만 화면이 걸렀다).
    /// 검색어는 행위자·대상 ID·대상 엔티티·비고 부분 일치. 시각이 없는 행은 기간과 무관하게 포함한다(종전 화면 규칙).
    /// </summary>
    public List<AuditRow> SearchAudit(AuditFilter f)
    {
        const string sql = """
            SELECT LogID, EventTS, ActorUserID, ModuleCode, ScreenCode, ProcessCode,
                   ActionType, TargetEntity, TargetID, Result, IPAddress, Note
            FROM   dbo.SYS_AuditLog
            WHERE  (EventTS IS NULL OR (EventTS >= @From AND EventTS < @To))
              AND  (@Module  IS NULL OR ModuleCode  = @Module)
              AND  (@Process IS NULL OR ProcessCode = @Process)
              AND  (@Result  IS NULL OR Result      = @Result)
              AND  (@SN IS NULL
                    OR ActorUserID  LIKE @SN OR Note     LIKE @SN
                    OR TargetEntity LIKE @SV OR TargetID LIKE @SV)
            ORDER  BY LogID DESC;
            """;
        static object Opt(string? s) => string.IsNullOrWhiteSpace(s) ? DBNull.Value : s.Trim();
        var like = string.IsNullOrWhiteSpace(f.Search) ? null
                 : "%" + f.Search.Trim().Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%";

        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@From",    SqlDbType.DateTime2).Value = f.From.Date;
        cmd.Parameters.Add("@To",      SqlDbType.DateTime2).Value = f.To.Date.AddDays(1);
        cmd.Parameters.Add("@Module",  SqlDbType.VarChar, 10).Value = Opt(f.ModuleCode);
        cmd.Parameters.Add("@Process", SqlDbType.VarChar, 10).Value = Opt(f.ProcessCode);
        cmd.Parameters.Add("@Result",  SqlDbType.VarChar, 10).Value = Opt(f.Result);
        // 행위자·비고는 nvarchar, 대상 엔티티·ID 는 varchar 컬럼 — 같은 검색어를 컬럼 형에 맞춰 두 번 넘긴다
        cmd.Parameters.Add("@SN", SqlDbType.NVarChar, 600).Value = (object?)like ?? DBNull.Value;
        cmd.Parameters.Add("@SV", SqlDbType.VarChar,  600).Value = (object?)like ?? DBNull.Value;
        using var r = cmd.ExecuteReader();
        var list = new List<AuditRow>();
        while (r.Read())
            list.Add(new AuditRow(
                (long)r["LogID"], r["EventTS"] as DateTime?,
                r["ActorUserID"] as string, r["ModuleCode"] as string,
                r["ScreenCode"] as string, r["ProcessCode"] as string,
                r["ActionType"] as string,
                r["TargetEntity"] as string, r["TargetID"] as string,
                r["Result"] as string, r["IPAddress"] as string,
                r["Note"] as string));
        return list;
    }

    /// <summary>SYS-007 「최근 24시간」 KPI — 조회 기간과 무관하게 DB 시각 기준으로 센다.</summary>
    public int CountAuditLast24h()
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand("SELECT COUNT(*) FROM dbo.SYS_AuditLog WHERE EventTS >= DATEADD(HOUR, -24, SYSDATETIME());", conn);
        return (int)cmd.ExecuteScalar();
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
                    (SELECT COUNT(DISTINCT COALESCE(rp.RoleID, rp.RoleName)) FROM dbo.SYS_RolePermission rp WHERE rp.ScreenCode = m.ScreenCode) AS RoleCount
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
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            DECLARE @OldCode varchar(20);
            SELECT @OldCode = ScreenCode FROM dbo.SYS_Screen WITH (UPDLOCK, HOLDLOCK) WHERE ScreenID = @Id;
            IF @OldCode IS NULL THROW 51821, 'Screen was not found.', 1;
            IF @OldCode <> @Code AND EXISTS
                (SELECT 1 FROM dbo.SYS_Screen WHERE ScreenCode = @Code AND ScreenID <> @Id)
                THROW 51822, 'Screen code is already in use.', 1;
            IF @OldCode <> @Code AND EXISTS
                (SELECT 1 FROM dbo.SYS_RolePermission WHERE ScreenCode = @Code)
                THROW 51823, 'Screen code already has role permissions.', 1;

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
            WHERE  ScreenID = @Id;

            UPDATE dbo.SYS_RolePermission
               SET ScreenCode = @Code, ModuleCode = @Module, ProcessCode = @Process,
                   ModifiedBy = @ModifiedBy, ModifiedTS = SYSDATETIME()
             WHERE ScreenCode = @OldCode
               AND (@OldCode <> @Code OR ISNULL(ModuleCode, '') <> @Module
                    OR ISNULL(ProcessCode, '') <> ISNULL(@Process, ''));
            COMMIT TRANSACTION;
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

    public void SetConfig(int configId, string? value, bool isActive, string modifiedBy)
    {
        const string sql = """
            UPDATE dbo.SYS_Config
            SET    ConfigValue = @Value,
                   IsActive    = @IsActive,
                   ModifiedBy  = @ModifiedBy,
                   ModifiedTS  = SYSDATETIME()
            WHERE  ConfigID = @Id
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Id",         SqlDbType.Int).Value           = configId;
        cmd.Parameters.Add("@Value",      SqlDbType.NVarChar, 500).Value = (object?)value ?? DBNull.Value;
        cmd.Parameters.Add("@IsActive",   SqlDbType.Bit).Value           = isActive;
        cmd.Parameters.Add("@ModifiedBy", SqlDbType.VarChar, 20).Value   = modifiedBy;
        cmd.ExecuteNonQuery();
    }

    // 단일 설정키의 값·활성여부 (컬처 결정/언어 스위처 판정용). 없으면 null.
    public (string? Value, bool IsActive)? GetConfigFlag(string key)
    {
        const string sql = "SELECT TOP 1 ConfigValue, ISNULL(IsActive,1) AS IsActive FROM dbo.SYS_Config WHERE ConfigKey = @Key ORDER BY ConfigID;";
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

    public sealed record RolePermissionItem(string ModuleCode, string ScreenCode, string? PermissionLevel);

    /// <summary>
    /// 한 역할의 화면 권한을 한꺼번에 저장한다(SYS-004) — 레벨이 비면 행 삭제, 있으면 MERGE.
    /// 한 트랜잭션이라 중간에 실패하면 전부 되돌린다(10-07 — 전에는 화면마다 따로 저장해 일부만 바뀐 채 남을 수 있었다).
    /// </summary>
    public void SaveRolePermissions(string roleId, string roleName, IEnumerable<RolePermissionItem> items, string modifiedBy)
    {
        const string sql = """
            IF @Level IS NULL
                DELETE dbo.SYS_RolePermission WHERE RoleID = @RoleID AND ScreenCode = @Screen;
            ELSE
                MERGE dbo.SYS_RolePermission WITH (HOLDLOCK) AS tgt
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

        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        using var cmd  = new SqlCommand(sql, conn, tx);
        cmd.Parameters.Add("@RoleID",     SqlDbType.NVarChar, 450).Value = roleId;
        cmd.Parameters.Add("@RoleName",   SqlDbType.VarChar,   40).Value = roleName;
        cmd.Parameters.Add("@ModifiedBy", SqlDbType.VarChar,   20).Value = modifiedBy;
        var pModule = cmd.Parameters.Add("@Module", SqlDbType.VarChar, 10);
        var pScreen = cmd.Parameters.Add("@Screen", SqlDbType.VarChar, 20);
        var pLevel  = cmd.Parameters.Add("@Level",  SqlDbType.VarChar, 10);
        foreach (var it in items)
        {
            pModule.Value = (object?)it.ModuleCode ?? DBNull.Value;
            pScreen.Value = it.ScreenCode;
            pLevel.Value  = string.IsNullOrEmpty(it.PermissionLevel) ? DBNull.Value : it.PermissionLevel;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
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
    /// <summary>다른 웹 사용자(SYS_UserProfile)가 이미 쓰는 사번인지. excludeUserId 는 수정 중인 본인.</summary>
    public bool EmployeeNoExists(string employeeNo, string? excludeUserId = null)
    {
        const string sql = """
            SELECT COUNT(*) FROM dbo.SYS_UserProfile
            WHERE  UPPER(LTRIM(RTRIM(EmployeeNo))) = UPPER(LTRIM(RTRIM(@No)))
              AND  (@Exclude IS NULL OR UserID <> @Exclude)
            """;
        using var conn = _f.OpenConnection();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@No", SqlDbType.VarChar, 20).Value = employeeNo.Trim();
        cmd.Parameters.Add("@Exclude", SqlDbType.NVarChar, 450).Value = (object?)excludeUserId ?? DBNull.Value;
        return (int)cmd.ExecuteScalar()! > 0;
    }

    /// <summary>POP 전용 현장 작업자(MD_Worker)가 쓰는 사번인지 — 같은 사번이면 POP 로그인에서 웹 계정이 이겨 작업자가 막힌다.</summary>
    public bool EmployeeNoUsedByWorker(string employeeNo)
    {
        using var conn = _f.OpenConnection();
        using var cmd = new SqlCommand("SELECT COUNT(*) FROM dbo.MD_Worker WHERE UPPER(LTRIM(RTRIM(EmployeeNo))) = UPPER(LTRIM(RTRIM(@No)))", conn);
        cmd.Parameters.Add("@No", SqlDbType.VarChar, 20).Value = employeeNo.Trim();
        return (int)cmd.ExecuteScalar()! > 0;
    }

    public void CreateProfile(string userId, string employeeNo, string employeeName,
        string? department, string? plantCode, string? defaultShift, string createdBy, string accountStatus = "ACTIVE")
    {
        const string sql = """
            INSERT INTO dbo.SYS_UserProfile
                (UserID, EmployeeNo, EmployeeName, Department, PlantCode, DefaultShift,
                 AccountStatus, FailedLoginCount, CreatedBy, CreatedTS)
            VALUES
                (@UserID, @EmpNo, @EmpName, @Dept, @Plant, @Shift,
                 @Status, 0, @CreatedBy, SYSDATETIME())
            """;
        Exec(sql,
            ("@UserID",    userId),
            ("@EmpNo",     employeeNo),
            ("@EmpName",   employeeName),
            ("@Dept",      (object?)department       ?? DBNull.Value),
            ("@Plant",     (object?)plantCode         ?? DBNull.Value),
            ("@Shift",     (object?)defaultShift      ?? DBNull.Value),
            ("@Status",    accountStatus),
            ("@CreatedBy", createdBy));
    }

    // 프로필 없는 계정은 로그인이 거부되므로(AuthRepository.GetProfileStatus) 기동 시드처럼 관리자가 만든 계정은 활성 프로필을 함께 둔다.
    public void EnsureActiveProfile(string userId, string employeeName, string createdBy)
        => Exec("""
            IF NOT EXISTS (SELECT 1 FROM dbo.SYS_UserProfile WHERE UserID = @UserID)
                INSERT INTO dbo.SYS_UserProfile (UserID, EmployeeName, AccountStatus, FailedLoginCount, CreatedBy, CreatedTS)
                VALUES (@UserID, @EmpName, 'ACTIVE', 0, @CreatedBy, SYSDATETIME())
            """,
            ("@UserID",    userId),
            ("@EmpName",   employeeName),
            ("@CreatedBy", createdBy));

    public void UpdateProfile(string userId, string employeeNo, string employeeName,
        string? department, string? plantCode, string? defaultShift,
        string accountStatus, string modifiedBy)
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

    /// <summary>
    /// 사용자 삭제 전 정리 — 프로필과 Identity 보조 행(역할·클레임·외부 로그인·토큰)을 한 트랜잭션으로 지운다.
    /// 개발·로컬 DB 의 AspNetUserRoles·Claims·Logins·Tokens 에는 외래키가 없어 AspNetUsers 만 지우면 고아 행이 남는다(10-07 확인).
    /// </summary>
    public void DeleteProfile(string userId)
    {
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        using var cmd  = new SqlCommand("""
            DELETE dbo.AspNetUserRoles  WHERE UserId = @UserID;
            DELETE dbo.AspNetUserClaims WHERE UserId = @UserID;
            DELETE dbo.AspNetUserLogins WHERE UserId = @UserID;
            DELETE dbo.AspNetUserTokens WHERE UserId = @UserID;
            DELETE dbo.SYS_UserProfile  WHERE UserID = @UserID;
            """, conn, tx);
        cmd.Parameters.Add("@UserID", SqlDbType.NVarChar, 450).Value = userId;
        cmd.ExecuteNonQuery();
        tx.Commit();
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
