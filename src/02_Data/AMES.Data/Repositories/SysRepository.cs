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

    public void RememberPortalActor(string email, string actorCode)
    {
        const string sql = """
            DECLARE @OriginalHash binary(32) = HASHBYTES('SHA2_256', CONVERT(nvarchar(900), @Email));
            INSERT dbo.SYS_AuditActorMap (OriginalHash, OriginalValue, ActorCode, MappingKind)
            SELECT @OriginalHash, @Email, @ActorCode, 'PORTAL'
            WHERE NOT EXISTS (SELECT 1 FROM dbo.SYS_AuditActorMap WITH (UPDLOCK, HOLDLOCK) WHERE OriginalHash=@OriginalHash);
            """;
        using var conn = _f.OpenConnection();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Email", SqlDbType.NVarChar, 900).Value = email.Trim().ToLowerInvariant();
        cmd.Parameters.Add("@ActorCode", SqlDbType.VarChar, 20).Value = actorCode;
        cmd.ExecuteNonQuery();
    }

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
        int? SortOrder, bool IsVisible, int RoleCount, string PermissionCriteria = PermissionSlots.Default);

    public sealed record ScreenPermRow(string? RoleId, string? RoleName, string? PermissionLevel);

    public sealed record ConfigRow(int ConfigId, string? ConfigKey, string? ConfigType,
        string? Category, string? ConfigValue, string? CodeName, string? Unit,
        bool IsActive, int? SortOrder);

    public sealed record HealthKpi(int Users, int Roles, int InterfacesOk, int InterfacesDown,
        int AuditLast24h, int NotifLast24h, int NotifFailedLast24h, int ConfigKeys,
        int Screens, int Permissions, long DbRowsApprox)
    {
        /// <summary>집계 기준 시각(DB) — 화면이 「기준 시각」으로 보인다.</summary>
        public DateTime? AsOf { get; init; }
    }

    public sealed record UserSelectRow(string Id, string? UserName, string? Email, string? PhoneNumber, bool IsApproved = true, string? EmployeeNo = null);

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
            ("@N", null, topN), ("@AdminRole", "AspNetUserRoles.RoleId", SystemRoles.AdminId));
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

    /// <summary>
    /// 사용자의 역할을 <paramref name="roleName"/> 하나로(null 이면 역할 없음) 한 트랜잭션으로 바꾼다 — 이름으로 찾은 역할 ID 를 넣는다.
    /// 역할(화면을 연 사이 SYS-002 에서 이름 변경·삭제)이나 사용자가 없으면 아무것도 바꾸지 않고 NotFound.
    /// Identity 의 RemoveFromRoles + AddToRole 두 번 호출은 뒤쪽만 실패하면 역할 없는 계정이 남았다.
    /// </summary>
    public RoleChangeResult SetUserRole(string userId, string? roleName)
    {
        const string sql = """
            SET NOCOUNT ON;
            DECLARE @RoleId nvarchar(450) = NULL;
            IF @Name IS NOT NULL
            BEGIN
                SELECT @RoleId = Id FROM dbo.AspNetRoles WITH (UPDLOCK, HOLDLOCK) WHERE NormalizedName = UPPER(@Name);
                IF @RoleId IS NULL BEGIN SELECT 0; RETURN; END
            END
            IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers WITH (UPDLOCK, HOLDLOCK) WHERE Id = @UserId) BEGIN SELECT 0; RETURN; END
            DELETE dbo.AspNetUserRoles WHERE UserId = @UserId;
            IF @RoleId IS NOT NULL INSERT dbo.AspNetUserRoles (UserId, RoleId) VALUES (@UserId, @RoleId);
            SELECT 1;
            """;
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        using var cmd  = new SqlCommand(sql, conn, tx);
        cmd.Parameters.Add("@UserId", SqlDbType.NVarChar, 450).Value = userId;
        cmd.Parameters.Add("@Name",   SqlDbType.NVarChar, 256).Value = string.IsNullOrWhiteSpace(roleName) ? DBNull.Value : roleName.Trim();
        if (Convert.ToInt32(cmd.ExecuteScalar()) != 1) { tx.Rollback(); return RoleChangeResult.NotFound; }
        tx.Commit();
        return RoleChangeResult.Ok;
    }

    public List<UserSelectRow> ListUsersForSelect()
    {
        const string sql = """
            SELECT u.Id, u.UserName, u.Email, u.PhoneNumber,
                   CAST(CASE WHEN p.UserID IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS IsApproved,
                   NULLIF(p.EmployeeNo, '') AS EmployeeNo
            FROM   dbo.AspNetUsers u
            LEFT JOIN dbo.SYS_UserProfile p ON p.UserID = u.Id
            ORDER  BY UserName;
            """;
        return Query(sql, r => new UserSelectRow(
            (string)r["Id"], r["UserName"] as string,
            r["Email"] as string, r["PhoneNumber"] as string, (bool)r["IsApproved"], r["EmployeeNo"] as string));
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
            ("@RoleID", "SYS_RolePermission.RoleID", (object?)roleId      ?? DBNull.Value),
            ("@RoleName", "SYS_RolePermission.RoleName", roleName),
            ("@Module", "SYS_RolePermission.ModuleCode", moduleCode),
            ("@Screen", "SYS_RolePermission.ScreenCode", screenCode),
            ("@Level", "SYS_RolePermission.PermissionLevel", permissionLevel),
            ("@IsSys", "SYS_RolePermission.IsSystemRole", isSystemRole),
            ("@CreatedBy", "SYS_RolePermission.CreatedBy", createdBy));
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
            ("@Id", "SYS_RolePermission.RolePermissionID", rolePermissionId),
            ("@RoleID", "SYS_RolePermission.RoleID", (object?)roleId ?? DBNull.Value),
            ("@RoleName", "SYS_RolePermission.RoleName", roleName),
            ("@Module", "SYS_RolePermission.ModuleCode", moduleCode),
            ("@Screen", "SYS_RolePermission.ScreenCode", screenCode),
            ("@Level", "SYS_RolePermission.PermissionLevel", permissionLevel),
            ("@IsSys", "SYS_RolePermission.IsSystemRole", isSystemRole),
            ("@ModifiedBy", "SYS_RolePermission.ModifiedBy", modifiedBy));
    }

    public void DeleteRolePerm(int rolePermissionId)
    {
        Exec("DELETE dbo.SYS_RolePermission WHERE RolePermissionID = @Id",
            ("@Id", "SYS_RolePermission.RolePermissionID", rolePermissionId));
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

    public sealed record RoleMemberRow(string UserId, string? UserName, string? Email,
        string? EmployeeNo, string? EmployeeName, string? Department,
        string? AccountStatus, bool LockedOut, bool HasProfile, DateTime? LastLoginTs);

    // SYS-002 인원 수 팝업 — 계정이 지워진 고아 AspNetUserRoles 행은 보일 사람이 없어 뺀다(ListRoles 의 UserCount 는 센다)
    public List<RoleMemberRow> ListRoleMembers(string roleId)
    {
        const string sql = """
            SELECT u.Id, u.UserName, u.Email,
                   p.EmployeeNo, p.EmployeeName, p.Department, p.AccountStatus, p.LastLoginTS,
                   CASE WHEN u.LockoutEnd IS NOT NULL AND u.LockoutEnd > SYSDATETIMEOFFSET()
                        THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS LockedOut,
                   CAST(CASE WHEN p.UserID IS NULL THEN 0 ELSE 1 END AS BIT) AS HasProfile
            FROM   dbo.AspNetUserRoles ur
            JOIN   dbo.AspNetUsers     u ON u.Id = ur.UserId
            LEFT JOIN dbo.SYS_UserProfile p ON p.UserID = u.Id
            WHERE  ur.RoleId = @RoleId
            ORDER  BY p.EmployeeNo, u.UserName;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@RoleId", SqlDbType.NVarChar, 450).Value = roleId;
        using var r = cmd.ExecuteReader();
        var list = new List<RoleMemberRow>();
        while (r.Read())
            list.Add(new RoleMemberRow((string)r["Id"], r["UserName"] as string, r["Email"] as string,
                r["EmployeeNo"] as string, r["EmployeeName"] as string, r["Department"] as string,
                r["AccountStatus"] as string, (bool)r["LockedOut"], (bool)r["HasProfile"],
                r["LastLoginTS"] as DateTime?));
        return list;
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
            ("@A", null, daysAhead), ("@B", null, daysBack));
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
            ("@From", null, from.Date), ("@To", null, to.Date));
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
            ColumnParams.Add(del, "@Date", "SYS_FactoryCalendar.CalendarDate", date.Date);
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
                ColumnParams.Add(pre, "@Date", "SYS_FactoryCalendar.CalendarDate", date.Date);
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
        // 형식·길이 = 컬럼(ColumnParams, 10-12) — 컬럼에 맞지 않으면 저장 전에 DbValueException, 한 줄이라도 막히면 트랜잭션째 되돌아간다.
        // 화면은 휴일 이름을 HolidayNameMax 로 먼저 막는다
        using var cmd = new SqlCommand(sql, conn, tx);
        ColumnParams.Add(cmd, "@Date",        "SYS_FactoryCalendar.CalendarDate", date.Date);
        ColumnParams.Add(cmd, "@DayType",     "SYS_FactoryCalendar.DayType",      dayType);
        ColumnParams.Add(cmd, "@HolidayName", "SYS_FactoryCalendar.HolidayName",  holidayName);
        ColumnParams.Add(cmd, "@ShiftCount",  "SYS_FactoryCalendar.ShiftCount",   shiftCount);
        ColumnParams.Add(cmd, "@ShiftCode",   "SYS_FactoryCalendar.ShiftCode",    shiftCode);
        ColumnParams.Add(cmd, "@Start",       "SYS_FactoryCalendar.StartTime",    start);
        ColumnParams.Add(cmd, "@End",         "SYS_FactoryCalendar.EndTime",      end);
        ColumnParams.Add(cmd, "@Break",       "SYS_FactoryCalendar.BreakMinutes", breakMin);
        ColumnParams.Add(cmd, "@Net",         "SYS_FactoryCalendar.NetWorkHours", netHours);
        ColumnParams.Add(cmd, "@Year",        "SYS_FactoryCalendar.CalendarYear", calendarYear);
        ColumnParams.Add(cmd, "@Plant",       "SYS_FactoryCalendar.PlantCode",    string.IsNullOrWhiteSpace(plantCode) ? null : plantCode);
        ColumnParams.Add(cmd, "@CreatedBy",   "SYS_FactoryCalendar.CreatedBy",    createdBy);
        cmd.ExecuteNonQuery();
    }

    /// <summary>SYS_FactoryCalendar.HolidayName nvarchar(40)</summary>
    public const int HolidayNameMax = 40;

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
            ("@Date", "SYS_FactoryCalendar.CalendarDate", date.Date),
            ("@DayType", "SYS_FactoryCalendar.DayType", dayType),
            ("@HolidayName", "SYS_FactoryCalendar.HolidayName", (object?)holidayName ?? DBNull.Value),
            ("@ModifiedBy", "SYS_FactoryCalendar.ModifiedBy", modifiedBy));
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
            ("@Id", "SYS_FactoryCalendar.FactoryCalendarID", id),
            ("@Start", "SYS_FactoryCalendar.StartTime", (object?)start    ?? DBNull.Value),
            ("@End", "SYS_FactoryCalendar.EndTime", (object?)end      ?? DBNull.Value),
            ("@Break", "SYS_FactoryCalendar.BreakMinutes", (object?)breakMin ?? DBNull.Value),
            ("@Net", "SYS_FactoryCalendar.NetWorkHours", (object?)netHours ?? DBNull.Value),
            ("@ModifiedBy", "SYS_FactoryCalendar.ModifiedBy", modifiedBy));
    }

    public void DeleteCalendarDate(DateTime date)
    {
        Exec("DELETE dbo.SYS_FactoryCalendar WHERE CalendarDate = CAST(@Date AS DATE)",
            ("@Date", "SYS_FactoryCalendar.CalendarDate", date.Date));
    }

    public void DeleteCalendarShift(int id)
    {
        Exec("DELETE dbo.SYS_FactoryCalendar WHERE FactoryCalendarID = @Id", ("@Id", "SYS_FactoryCalendar.FactoryCalendarID", id));
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
        // 주소는 비밀값을 가린 채로만 내보낸다(MonitorRules.MaskEndpoint) — 화면·Api 응답 모두 이 목록을 쓴다
        return Query(sql, r => new InterfaceRow(
            (int)r["InterfaceMonitorID"], r["InterfaceCode"] as string,
            r["InterfaceName"] as string, r["Direction"] as string,
            MonitorRules.MaskEndpoint(r["Endpoint"] as string), r["Protocol"] as string,
            r["ConnStatus"] as string, r["LastSyncTS"] as DateTime?,
            r["MaxGapMinutes"] as int?, r["LastRecordCount"] as int?,
            r["RetryCount"] as int?, r["LastErrorMsg"] as string,
            (bool)r["IsEnabled"], r["MinutesSince"] as int? ?? 0));
    }

    /// <summary>
    /// 인터페이스 모니터 1행 업서트 — 외부 연동 Worker 가 대상마다 실행 뒤 부른다.
    /// 실패 시 LastSyncTS 는 건드리지 않는다(마지막 성공 시각이어야 SYS-Interfaces 의 경과분이 의미 있다).
    /// actor 는 새 행의 CreatedBy·갱신 시 ModifiedBy — 어느 연동이 쓴 행인지 호출자가 밝힌다.
    /// direction 은 공통코드 IF_DIRECTION 값 — 가져오기 Worker 는 INBOUND, PUSH 발송기는 OUTBOUND.
    /// </summary>
    public void UpsertInterfaceMonitor(string code, string name, string endpoint, int maxGapMin,
        bool ok, int? recordCount, string? error, string actor, string direction = "INBOUND")
    {
        const string sql = """
            UPDATE dbo.SYS_InterfaceMonitor
            SET    InterfaceName   = @Name,
                   Direction       = @Dir,
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
                VALUES (@Code, @Name, @Dir, @Ep, 'REST',
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
        cmd.Parameters.Add("@Dir",  SqlDbType.VarChar, 15).Value   = direction;
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
        // 공정 코드는 화면 레지스트리(SYS_Screen.ProcessCode)에서 채운다 — SYS-007 공정 필터가 웹 감사 행도 거르게(화면 코드가 아닌 ACCOUNT 등은 NULL)
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand("""
            INSERT INTO dbo.SYS_AuditLog
                   (EventTS, ActorUserID, ModuleCode, ScreenCode, ProcessCode, ActionType,
                    TargetEntity, TargetID, BeforeValueJSON, AfterValueJSON,
                    IPAddress, Result, Note, CreatedBy, CreatedTS)
            VALUES (SYSDATETIME(), @Actor, @Mod, @Scr,
                    (SELECT TOP (1) s.ProcessCode FROM dbo.SYS_Screen s WHERE s.ScreenCode = @Scr ORDER BY s.ScreenID),
                    @Act, @Ent, @TID, @Before, @After,
                    @IP, @Res, @Note, @Actor, SYSDATETIME())
            """, conn);
        ColumnParams.Add(cmd, "@Actor", "SYS_AuditLog.CreatedBy", actorUserId);
        ColumnParams.Add(cmd, "@Mod", "SYS_AuditLog.ModuleCode", (object?)moduleCode   ?? DBNull.Value);
        ColumnParams.Add(cmd, "@Scr", "SYS_AuditLog.ScreenCode", (object?)screenCode   ?? DBNull.Value);
        ColumnParams.Add(cmd, "@Act", "SYS_AuditLog.ActionType", actionType);
        ColumnParams.Add(cmd, "@Ent", "SYS_AuditLog.TargetEntity", (object?)targetEntity ?? DBNull.Value);
        ColumnParams.Add(cmd, "@TID", "SYS_AuditLog.TargetID", (object?)targetId     ?? DBNull.Value);
        ColumnParams.Add(cmd, "@Before", "SYS_AuditLog.BeforeValueJSON", (object?)beforeJson   ?? DBNull.Value);
        ColumnParams.Add(cmd, "@After", "SYS_AuditLog.AfterValueJSON", (object?)afterJson    ?? DBNull.Value);
        ColumnParams.Add(cmd, "@IP", "SYS_AuditLog.IPAddress", (object?)ipAddress    ?? DBNull.Value);
        ColumnParams.Add(cmd, "@Res", "SYS_AuditLog.Result", result);
        ColumnParams.Add(cmd, "@Note", "SYS_AuditLog.Note", (object?)note         ?? DBNull.Value);
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
            ("@N", null, topN));
    }

    /// <summary>SYS-007 조회 조건. 기간은 날짜 단위(From 00:00 ~ To 다음 날 00:00 미만), 나머지는 비우면 조건 없음.</summary>
    /// <param name="ActorCodes">검색어와 이름이 맞는 행위자 코드(화면이 이름 사전으로 푼 값) — 검색어 조건에 OR 로 더한다.</param>
    public sealed record AuditFilter(DateTime From, DateTime To, string? Search, string? ModuleCode, string? ProcessCode, string? Result,
        IReadOnlyCollection<string>? ActorCodes = null);

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
                    OR TargetEntity LIKE @SV OR TargetID LIKE @SV
                    OR (@Actors IS NOT NULL AND ActorUserID IN (SELECT value FROM STRING_SPLIT(@Actors, NCHAR(31)))))
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
        // 이름으로 찾은 사번 목록 — 구분자는 코드에 나올 수 없는 제어 문자(U+001F)
        var actors = f.ActorCodes is { Count: > 0 } ? string.Join('\u001f', f.ActorCodes) : null;
        cmd.Parameters.Add("@Actors", SqlDbType.NVarChar, -1).Value = (object?)actors ?? DBNull.Value;
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
            ("@EventTypeCode", "SYS_NotificationRule.EventTypeCode", (object?)eventTypeCode      ?? DBNull.Value),
            ("@EventName", "SYS_NotificationRule.EventName", (object?)eventName          ?? DBNull.Value),
            ("@ModuleCode", "SYS_NotificationRule.ModuleCode", (object?)moduleCode         ?? DBNull.Value),
            ("@ProcessCode", "SYS_NotificationRule.ProcessCode", (object?)processCode        ?? DBNull.Value),
            ("@TriggerCondition", "SYS_NotificationRule.TriggerCondition", (object?)triggerCondition   ?? DBNull.Value),
            ("@IsEnabled", "SYS_NotificationRule.IsEnabled", isEnabled),
            ("@ChannelsJSON", "SYS_NotificationRule.ChannelsJSON", (object?)channelsJson       ?? DBNull.Value),
            ("@RecipientRolesJSON", "SYS_NotificationRule.RecipientRolesJSON", (object?)recipientRolesJson ?? DBNull.Value),
            ("@CreatedBy", "SYS_NotificationRule.CreatedBy", createdBy));
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
            ("@Id", "SYS_NotificationRule.NotificationRuleID", id),
            ("@EventTypeCode", "SYS_NotificationRule.EventTypeCode", (object?)eventTypeCode      ?? DBNull.Value),
            ("@EventName", "SYS_NotificationRule.EventName", (object?)eventName          ?? DBNull.Value),
            ("@ModuleCode", "SYS_NotificationRule.ModuleCode", (object?)moduleCode         ?? DBNull.Value),
            ("@ProcessCode", "SYS_NotificationRule.ProcessCode", (object?)processCode        ?? DBNull.Value),
            ("@TriggerCondition", "SYS_NotificationRule.TriggerCondition", (object?)triggerCondition   ?? DBNull.Value),
            ("@IsEnabled", "SYS_NotificationRule.IsEnabled", isEnabled),
            ("@ChannelsJSON", "SYS_NotificationRule.ChannelsJSON", (object?)channelsJson       ?? DBNull.Value),
            ("@RecipientRolesJSON", "SYS_NotificationRule.RecipientRolesJSON", (object?)recipientRolesJson ?? DBNull.Value),
            ("@ModifiedBy", "SYS_NotificationRule.ModifiedBy", modifiedBy));
    }

    public void DeleteNotificationRule(int id)
    {
        Exec("DELETE dbo.SYS_NotificationRule WHERE NotificationRuleID = @Id", ("@Id", "SYS_NotificationRule.NotificationRuleID", id));
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
            ("@N", null, topN));
    }

    // ── SYS-03 Screen Management ───────────────────────────────────────
    public List<ScreenRow> ListScreens(string? moduleCode = null)
    {
        const string sql = """
            SELECT  m.ScreenID, m.ScreenCode, m.ModuleCode, m.ProcessCode, m.SubProcessCode, m.ScreenName, m.ScreenNameEn,
                    m.HRef, m.LidLabel, {CRITERIA} AS PermissionCriteria, m.SortOrder,
                    ISNULL(m.IsVisible, 1) AS IsVisible,
                    (SELECT COUNT(DISTINCT COALESCE(rp.RoleID, rp.RoleName)) FROM dbo.SYS_RolePermission rp WHERE rp.ScreenCode = m.ScreenCode) AS RoleCount
            FROM    dbo.SYS_Screen m
            {WHERE}
            ORDER   BY m.ModuleCode, ISNULL(m.SortOrder, 999), m.ScreenCode;
            """;
        var where = moduleCode is null ? "" : "WHERE m.ModuleCode = @Module";
        // 권한 판정·메뉴·화면 제목이 모두 이 목록을 읽는다 — 마이그레이션 전에 신 Web 이 올라가도 전체 화면이 막히지 않게,
        // 컬럼이 없으면 기본값('___' = 세 기능 모두 있음)으로 읽는다(migrate_sys_screen_permission_criteria.sql)
        var query = sql.Replace("{WHERE}", where)
                       .Replace("{CRITERIA}", HasScreenCriteriaColumn() ? "m.PermissionCriteria" : "'" + PermissionSlots.Default + "'");
        return moduleCode is null
            ? Query(query, MapScreen)
            : Query(query, MapScreen, ("@Module", "SYS_Screen.ModuleCode", moduleCode));
    }

    static bool? _hasScreenCriteria;
    bool HasScreenCriteriaColumn()
    {
        if (_hasScreenCriteria == true) return true;   // 생긴 뒤에는 다시 확인하지 않는다(없을 때만 매번 확인 — 마이그레이션 직후 바로 반영)
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand("SELECT COL_LENGTH('dbo.SYS_Screen', 'PermissionCriteria');", conn);
        _hasScreenCriteria = cmd.ExecuteScalar() is not DBNull and not null;
        return _hasScreenCriteria.Value;
    }

    public void InsertScreen(string screenCode, string moduleCode, string? processCode,
        string? subProcessCode, string screenName, string? screenNameEn, string? href, string? lidLabel,
        int? sortOrder, bool isVisible, string createdBy, string? permissionCriteria = null)
    {
        // 두 화면이 같은 주소를 쓰면 권한 판정·메뉴가 어느 화면을 볼지 흔들린다 — 보호 화면(SYS-003·004)뿐 아니라 모든 주소(51825)
        const string sql = """
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            IF EXISTS (SELECT 1 FROM dbo.SYS_Screen WITH (UPDLOCK, HOLDLOCK) WHERE ScreenCode = @Code)
                THROW 51822, 'Screen code is already in use.', 1;
            IF TRIM('/' FROM ISNULL(@HRef, '')) <> ''
               AND EXISTS (SELECT 1 FROM dbo.SYS_Screen WITH (UPDLOCK, HOLDLOCK) WHERE TRIM('/' FROM ISNULL(HRef, '')) = TRIM('/' FROM @HRef))
                THROW 51825, 'Another screen already uses this path.', 1;
            INSERT INTO dbo.SYS_Screen
                   (ScreenCode, ModuleCode, ProcessCode, SubProcessCode, ScreenName, ScreenNameEn, HRef, LidLabel, PermissionCriteria, SortOrder, IsVisible, CreatedBy)
            VALUES (@Code, @Module, @Process, @SubProc, @Name, @NameEn, @HRef, @Lid, @Perm, @Sort, @Visible, @CreatedBy);
            COMMIT TRANSACTION;
            """;
        Exec(sql,
            ("@Perm", "SYS_Screen.PermissionCriteria", ProtectedScreens.ApplyTemplate(href, permissionCriteria)),
            ("@Code", "SYS_Screen.ScreenCode", screenCode),
            ("@Module", "SYS_Screen.ModuleCode", moduleCode),
            ("@Process", "SYS_Screen.ProcessCode", (object?)processCode    ?? DBNull.Value),
            ("@SubProc", "SYS_Screen.SubProcessCode", (object?)subProcessCode ?? DBNull.Value),
            ("@Name", "SYS_Screen.ScreenName", screenName),
            ("@NameEn", "SYS_Screen.ScreenNameEn", (object?)screenNameEn   ?? DBNull.Value),
            ("@HRef", "SYS_Screen.HRef", (object?)href            ?? DBNull.Value),
            ("@Lid", "SYS_Screen.LidLabel", (object?)lidLabel        ?? DBNull.Value),
            ("@Sort", "SYS_Screen.SortOrder", (object?)sortOrder       ?? DBNull.Value),
            ("@Visible", "SYS_Screen.IsVisible", isVisible),
            ("@CreatedBy", "SYS_Screen.CreatedBy", createdBy));
    }

    /// <remarks>
    /// 화면 기본 권한(permissionCriteria — SYS_Screen.PermissionCriteria)이 바뀌면 그 화면의 역할 권한(SYS_RolePermission)도 같은 트랜잭션에서 맞춘다(10-10 사용자 결정) —
    /// 기능이 없어진(X) 자리는 부여돼 있어도 X, 기능이 생긴 자리는 '_'(미부여), 남는 부여가 없으면 그 행을 지운다. null 이면 기본 권한을 바꾸지 않는다.
    /// </remarks>
    public void UpdateScreen(int screenId, string screenCode, string moduleCode, string? processCode,
        string? subProcessCode, string screenName, string? screenNameEn, string? href, string? lidLabel,
        int? sortOrder, bool isVisible, string modifiedBy, string? permissionCriteria = null)
    {
        // SYS-003·SYS-004 는 코드·주소를 바꿀 수 없고 수정 기능도 끌 수 없다(ProtectedScreens — 잃으면 권한을 다시 줄 화면이 없다)
        var sql = $$"""
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            DECLARE @OldCode varchar(20), @OldHRef varchar(200);
            SELECT @OldCode = ScreenCode, @OldHRef = HRef FROM dbo.SYS_Screen WITH (UPDLOCK, HOLDLOCK) WHERE ScreenID = @Id;
            IF @OldCode IS NULL THROW 51821, 'Screen was not found.', 1;
            IF TRIM('/' FROM ISNULL(@OldHRef, '')) IN ({{ProtectedScreens.SqlList}})
               AND (@OldCode <> @Code OR TRIM('/' FROM ISNULL(@HRef, '')) <> TRIM('/' FROM @OldHRef))
                THROW 51824, 'The code and path of SYS-003/SYS-004 cannot change.', 1;
            IF TRIM('/' FROM ISNULL(@HRef, '')) <> ''
               AND EXISTS (SELECT 1 FROM dbo.SYS_Screen WITH (UPDLOCK, HOLDLOCK) WHERE ScreenID <> @Id AND TRIM('/' FROM ISNULL(HRef, '')) = TRIM('/' FROM @HRef))
                THROW 51825, 'Another screen already uses this path.', 1;
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
                   PermissionCriteria = ISNULL(@Perm, PermissionCriteria),
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

            IF @Perm IS NOT NULL
            BEGIN
                UPDATE rp
                   SET PermissionLevel = x.NewLevel, ModifiedBy = @ModifiedBy, ModifiedTS = SYSDATETIME()
                  FROM dbo.SYS_RolePermission rp
                 CROSS APPLY (SELECT UPPER(ISNULL(rp.PermissionLevel, '')) AS Lv) a
                 CROSS APPLY (SELECT CASE WHEN CHARINDEX('R', a.Lv) > 0 THEN 'R' ELSE '_' END
                                   + CASE WHEN SUBSTRING(@Perm, 2, 1) = 'X' THEN 'X' WHEN CHARINDEX('E', a.Lv) > 0 THEN 'E' ELSE '_' END
                                   + CASE WHEN SUBSTRING(@Perm, 3, 1) = 'X' THEN 'X' WHEN CHARINDEX('A', a.Lv) > 0 THEN 'A' ELSE '_' END AS NewLevel) x
                 WHERE rp.ScreenCode = @Code
                   AND ISNULL(rp.PermissionLevel, '') COLLATE Latin1_General_BIN <> x.NewLevel COLLATE Latin1_General_BIN;
                DELETE dbo.SYS_RolePermission WHERE ScreenCode = @Code AND PermissionLevel COLLATE Latin1_General_BIN NOT LIKE '%[REA]%';
            END
            COMMIT TRANSACTION;
            """;
        Exec(sql,
            ("@Perm", "SYS_Screen.PermissionCriteria", permissionCriteria is null ? (object)DBNull.Value : ProtectedScreens.ApplyTemplate(href, permissionCriteria)),
            ("@Id", "SYS_Screen.ScreenID", screenId),
            ("@Code", "SYS_Screen.ScreenCode", screenCode),
            ("@Module", "SYS_Screen.ModuleCode", moduleCode),
            ("@Process", "SYS_Screen.ProcessCode", (object?)processCode    ?? DBNull.Value),
            ("@SubProc", "SYS_Screen.SubProcessCode", (object?)subProcessCode ?? DBNull.Value),
            ("@Name", "SYS_Screen.ScreenName", screenName),
            ("@NameEn", "SYS_Screen.ScreenNameEn", (object?)screenNameEn   ?? DBNull.Value),
            ("@HRef", "SYS_Screen.HRef", (object?)href            ?? DBNull.Value),
            ("@Lid", "SYS_Screen.LidLabel", (object?)lidLabel        ?? DBNull.Value),
            ("@Sort", "SYS_Screen.SortOrder", (object?)sortOrder       ?? DBNull.Value),
            ("@Visible", "SYS_Screen.IsVisible", isVisible),
            ("@ModifiedBy", "SYS_Screen.ModifiedBy", modifiedBy));
    }

    /// <summary>
    /// 화면과 그 화면 코드의 역할 권한 행을 한 트랜잭션으로 지우고, 지운 권한을 돌려준다(감사 기록용).
    /// 권한 행을 남기면 같은 코드로 새 화면을 등록할 때 옛 권한이 그대로 살아나고, 다른 화면을 그 코드로 바꾸는 것은 막힌다(51823).
    /// </summary>
    public List<ScreenPermRow> DeleteScreen(int screenId, string deletedBy)
    {
        var sql = $$"""
            SET NOCOUNT ON;
            DECLARE @Code varchar(20), @HRef varchar(200);
            SELECT @Code = ScreenCode, @HRef = HRef FROM dbo.SYS_Screen WITH (UPDLOCK, HOLDLOCK) WHERE ScreenID = @Id;
            IF @Code IS NULL RETURN;
            IF TRIM('/' FROM ISNULL(@HRef, '')) IN ({{ProtectedScreens.SqlList}})
                THROW 51826, 'SYS-003/SYS-004 cannot be deleted.', 1;
            DELETE dbo.SYS_RolePermission
            OUTPUT deleted.RoleID, deleted.RoleName, deleted.PermissionLevel
            WHERE  ScreenCode = @Code;
            DELETE dbo.SYS_Screen WHERE ScreenID = @Id;
            """;
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        var removed = new List<ScreenPermRow>();
        using (var cmd = new SqlCommand(sql, conn, tx))
        {
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = screenId;
            using var r = cmd.ExecuteReader();
            while (r.Read()) removed.Add(new(r["RoleID"] as string, r["RoleName"] as string, r["PermissionLevel"] as string));
        }
        tx.Commit();
        return removed;
    }

    private static ScreenRow MapScreen(IDataReader r) => new(
        (int)r["ScreenID"], (string)r["ScreenCode"], (string)r["ModuleCode"],
        r["ProcessCode"] as string, r["SubProcessCode"] as string,
        (string)r["ScreenName"], r["ScreenNameEn"] as string,
        r["HRef"] as string, r["LidLabel"] as string,
        r["SortOrder"] as int?, (bool)r["IsVisible"], (int)r["RoleCount"],
        PermissionSlots.NormalizeTemplate(r["PermissionCriteria"] as string));

    // ── SYS-07 Notification Channels ───────────────────────────────────
    public List<NotifChannelRow> ListNotificationChannels()
    {
        const string sql = """
            SELECT  c.NotificationChannelID, c.UserID,
                    COALESCE(p.EmployeeName, w.EmployeeName, u.UserName, c.UserID) AS UserName,
                    c.Channel, c.Address,
                    ISNULL(c.IsEnabled, 1) AS IsEnabled,
                    c.QuietHoursStart, c.QuietHoursEnd, c.VerifiedAt
            FROM    dbo.SYS_NotificationChannel c
            LEFT JOIN dbo.SYS_UserProfile p ON p.EmployeeNo = c.UserID
            LEFT JOIN dbo.MD_Worker w ON w.EmployeeNo = c.UserID AND p.UserID IS NULL
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

    /// <summary>
    /// 웹(SYS-008)에서 채널 행 등록 — PUSH 는 받지 않는다: PUSH 주소는 FCM 기기 토큰이라 알림 앱이 로그인할 때
    /// NotificationRepository.RegisterDevice 로만 만든다(10-24 사용자 결정).
    /// </summary>
    public void InsertNotificationChannel(string? userId, string channel, string? address,
        bool isEnabled, TimeSpan? quietStart, TimeSpan? quietEnd, string createdBy)
    {
        if (IsPushChannel(channel))
            throw new InvalidOperationException("PUSH devices are registered by the notification app, not on this screen.");
        const string sql = """
            INSERT INTO dbo.SYS_NotificationChannel
                (UserID, Channel, Address, IsEnabled, QuietHoursStart, QuietHoursEnd,
                 CreatedBy, CreatedTS)
            VALUES
                (@UserID, @Channel, @Address, @IsEnabled, @QuietStart, @QuietEnd,
                 @CreatedBy, SYSDATETIME())
            """;
        Exec(sql,
            ("@UserID", "SYS_NotificationChannel.UserID", (object?)userId    ?? DBNull.Value),
            ("@Channel", "SYS_NotificationChannel.Channel", channel),
            ("@Address", "SYS_NotificationChannel.Address", (object?)address   ?? DBNull.Value),
            ("@IsEnabled", "SYS_NotificationChannel.IsEnabled", isEnabled),
            ("@QuietStart", "SYS_NotificationChannel.QuietHoursStart", (object?)quietStart ?? DBNull.Value),
            ("@QuietEnd", "SYS_NotificationChannel.QuietHoursEnd", (object?)quietEnd   ?? DBNull.Value),
            ("@CreatedBy", "SYS_NotificationChannel.CreatedBy", createdBy));
    }

    /// <summary>
    /// 웹(SYS-008)에서 채널 행 수정 — PUSH 행(앱이 등록한 기기)은 수신자·채널·주소를 그대로 두고 수신 여부·방해 금지 시각만 바꾸며,
    /// 다른 채널 행을 PUSH 로 바꾸지 못한다(메일 주소가 FCM 토큰 자리에 들어가는 것을 막는다, 10-24).
    /// </summary>
    public void UpdateNotificationChannel(int id, string? userId, string channel,
        string? address, bool isEnabled, TimeSpan? quietStart, TimeSpan? quietEnd,
        string modifiedBy)
    {
        const string sql = """
            UPDATE dbo.SYS_NotificationChannel
            SET    UserID          = CASE WHEN Channel = @Push OR @Channel = @Push THEN UserID  ELSE @UserID  END,
                   Channel         = CASE WHEN Channel = @Push OR @Channel = @Push THEN Channel ELSE @Channel END,
                   Address         = CASE WHEN Channel = @Push OR @Channel = @Push THEN Address ELSE @Address END,
                   IsEnabled       = @IsEnabled,
                   QuietHoursStart = @QuietStart,
                   QuietHoursEnd   = @QuietEnd,
                   ModifiedBy      = @ModifiedBy,
                   ModifiedTS      = SYSDATETIME()
            WHERE  NotificationChannelID = @Id
            """;
        Exec(sql,
            ("@Id", "SYS_NotificationChannel.NotificationChannelID", id),
            ("@UserID", "SYS_NotificationChannel.UserID", (object?)userId    ?? DBNull.Value),
            ("@Channel", "SYS_NotificationChannel.Channel", channel),
            ("@Address", "SYS_NotificationChannel.Address", (object?)address   ?? DBNull.Value),
            ("@IsEnabled", "SYS_NotificationChannel.IsEnabled", isEnabled),
            ("@QuietStart", "SYS_NotificationChannel.QuietHoursStart", (object?)quietStart ?? DBNull.Value),
            ("@QuietEnd", "SYS_NotificationChannel.QuietHoursEnd", (object?)quietEnd   ?? DBNull.Value),
            ("@ModifiedBy", "SYS_NotificationChannel.ModifiedBy", modifiedBy),
            ("@Push", "SYS_NotificationChannel.Channel", Services.Notify.NotifyRules.Channel));
    }

    public static bool IsPushChannel(string? channel)
        => string.Equals(channel?.Trim(), Services.Notify.NotifyRules.Channel, StringComparison.OrdinalIgnoreCase);

    public void DeleteNotificationChannel(int id)
    {
        Exec("DELETE dbo.SYS_NotificationChannel WHERE NotificationChannelID = @Id",
            ("@Id", "SYS_NotificationChannel.NotificationChannelID", id));
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

    public sealed record ConfigChange(int ConfigId, string? Value, bool IsActive);

    /// <summary>
    /// SYS-009 일괄 저장 — 바뀐 행을 한 트랜잭션으로 쓴다(10-28). 하나라도 실패하면(없어진 행 포함) 전부 되돌리고 예외를 던진다 — 일부만 반영된 채 남지 않는다.
    /// </summary>
    public void SetConfigs(IReadOnlyCollection<ConfigChange> changes, string modifiedBy)
    {
        if (changes.Count == 0) return;
        const string sql = """
            UPDATE dbo.SYS_Config
            SET    ConfigValue = @Value,
                   IsActive    = @IsActive,
                   ModifiedBy  = @ModifiedBy,
                   ModifiedTS  = SYSDATETIME()
            WHERE  ConfigID = @Id
            """;
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        using var cmd  = new SqlCommand(sql, conn, tx);
        var pId     = cmd.Parameters.Add("@Id",         SqlDbType.Int);
        var pValue  = cmd.Parameters.Add("@Value",      SqlDbType.NVarChar, 500);
        var pActive = cmd.Parameters.Add("@IsActive",   SqlDbType.Bit);
        cmd.Parameters.Add("@ModifiedBy", SqlDbType.VarChar, 20).Value = modifiedBy;
        foreach (var c in changes)
        {
            pId.Value     = c.ConfigId;
            pValue.Value  = (object?)c.Value ?? DBNull.Value;
            pActive.Value = c.IsActive;
            if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException($"Config {c.ConfigId} was not found.");
        }
        tx.Commit();
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
    /// 값은 넘어온 부여 글자를 **DB 의 현재 화면 기본 권한(SYS_Screen.PermissionCriteria)** 으로 다시 3자리로 맞춰 쓴다(10-10) —
    /// 화면을 연 사이 SYS-003 에서 기능을 끈 화면에 옛 부여가 저장되지 않게. 저장한 값(화면 코드 → 3자리)을 돌려준다(감사 기록용).
    /// </summary>
    /// <remarks>editorUserId(저장하는 사람의 AspNetUsers.Id)를 주면 그 사람이 이 역할에 속하고 System Administrator 가 아닐 때
    /// 아무것도 쓰지 않고 <see cref="RoleSelfEditException"/> 을 던진다(<see cref="RoleSelfEditRules"/>, 10-28) — 같은 트랜잭션에서 역할 배정을 읽는다.</remarks>
    public Dictionary<string, string> SaveRolePermissions(string roleId, string roleName, IEnumerable<RolePermissionItem> items, string modifiedBy,
        string? editorUserId = null)
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
        if (!string.IsNullOrWhiteSpace(editorUserId))
        {
            var editorRoles = new List<string>();
            using (var q = new SqlCommand("SELECT RoleId FROM dbo.AspNetUserRoles WITH (HOLDLOCK) WHERE UserId = @U;", conn, tx))
            {
                ColumnParams.AddFilter(q, "@U", "AspNetUserRoles.UserId", editorUserId);
                using var rd = q.ExecuteReader();
                while (rd.Read()) editorRoles.Add(rd.GetString(0));
            }
            if (RoleSelfEditRules.Blocks(editorRoles, roleId)) throw new RoleSelfEditException(roleId);
        }
        // 기본 권한을 읽은 뒤 저장이 끝날 때까지 SYS-003 이 바꾸지 못하게 공유 잠금을 쥔다
        var criteria = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var hrefs    = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        using (var q = new SqlCommand("SELECT ScreenCode, PermissionCriteria, HRef FROM dbo.SYS_Screen WITH (HOLDLOCK);", conn, tx))
        using (var rd = q.ExecuteReader())
            while (rd.Read()) { criteria[rd.GetString(0)] = rd.GetString(1); hrefs[rd.GetString(0)] = rd["HRef"] as string; }
        var saved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
            // 관리자의 SYS-003·SYS-004 읽기·수정은 넘어온 값과 관계없이 남긴다(ProtectedScreens)
            var level = PermissionSlots.Compose(criteria.GetValueOrDefault(it.ScreenCode, PermissionSlots.Default),
                ProtectedScreens.ApplyRole(roleId, hrefs.GetValueOrDefault(it.ScreenCode), it.PermissionLevel));
            pLevel.Value  = (object?)level ?? DBNull.Value;
            cmd.ExecuteNonQuery();
            if (level is not null) saved[it.ScreenCode] = level;
        }
        tx.Commit();
        return saved;
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

    // ── SYS-008·SYS-010 알림 KPI ─────────────────────────────────────────
    public sealed record NotificationKpi(int Sent, int Failed, DateTime AsOf);

    /// <summary>
    /// 최근 24시간(DB 시각) 발송·실패 건수 — 이력 전체에서 센다(목록 TOP 과 무관). 발송 = SENT 이고 발송 시각(SentAt)이 24시간 안,
    /// 실패 = 실패 상태(MonitorRules.NotificationFailed)이고 마지막 시도 시각(ModifiedTS, 없으면 CreatedTS)이 24시간 안 — 실패 처리는 SentAt 을 쓰지 않는다.
    /// SYS-008 과 SYS-010 이 같은 값을 보인다. AsOf = 기준 시각(DB).
    /// </summary>
    public NotificationKpi CountNotifications24h()
    {
        var sql = $"""
            DECLARE @now datetime2 = SYSDATETIME(), @from datetime2 = DATEADD(HOUR, -24, SYSDATETIME());
            SELECT (SELECT COUNT(*) FROM dbo.SYS_NotificationHistory
                     WHERE Status = '{MonitorRules.NotificationSent}' AND SentAt >= @from AND SentAt <= @now) AS Sent,
                   (SELECT COUNT(*) FROM dbo.SYS_NotificationHistory
                     WHERE Status IN ({MonitorRules.SqlList(MonitorRules.NotificationFailed)})
                       AND ISNULL(ModifiedTS, CreatedTS) >= @from AND ISNULL(ModifiedTS, CreatedTS) <= @now) AS Failed,
                   @now AS AsOf;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        using var rdr  = cmd.ExecuteReader();
        rdr.Read();
        return new NotificationKpi((int)rdr["Sent"], (int)rdr["Failed"], (DateTime)rdr["AsOf"]);
    }

    // ── SYS-010 System Health ───────────────────────────────────────────
    /// <remarks>인터페이스 정상·다운과 알림 발송·실패는 SYS-006·SYS-008 과 같은 기준(MonitorRules — 시간 초과 경고·다운 포함, CountNotifications24h)으로 센다.</remarks>
    public HealthKpi GetHealth()
    {
        var ifs   = ListInterfaces();
        var eff   = ifs.Select(i => MonitorRules.EffectiveInterfaceStatus(i.ConnStatus, i.LastSyncTs, i.MinutesSinceSync, i.MaxGapMinutes, i.IsEnabled)).ToList();
        var notif = CountNotifications24h();
        const string sql = """
            SELECT
              (SELECT COUNT(*) FROM dbo.AspNetUsers)                                          AS Users,
              (SELECT COUNT(*) FROM dbo.AspNetRoles)                                          AS Roles,
              (SELECT COUNT(*) FROM dbo.SYS_AuditLog          WHERE EventTS >= DATEADD(HOUR,-24,SYSDATETIME())) AS AuditDay,
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
            return new HealthKpi(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0L) { AsOf = notif.AsOf };
        return new HealthKpi(
            (int)rdr["Users"], (int)rdr["Roles"],
            eff.Count(MonitorRules.IsOk), eff.Count(MonitorRules.IsDown),
            (int)rdr["AuditDay"], notif.Sent,
            notif.Failed, (int)rdr["Cfg"],
            (int)rdr["Screens"], (int)rdr["Perms"],
            rdr["RowsApprox"] as long? ?? 0L) { AsOf = notif.AsOf };
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
            ("@UserID", "SYS_UserProfile.UserID", userId),
            ("@EmpNo", "SYS_UserProfile.EmployeeNo", employeeNo),
            ("@EmpName", "SYS_UserProfile.EmployeeName", employeeName),
            ("@Dept", "SYS_UserProfile.Department", (object?)department       ?? DBNull.Value),
            ("@Plant", "SYS_UserProfile.PlantCode", (object?)plantCode         ?? DBNull.Value),
            ("@Shift", "SYS_UserProfile.DefaultShift", (object?)defaultShift      ?? DBNull.Value),
            ("@Status", "SYS_UserProfile.AccountStatus", accountStatus),
            ("@CreatedBy", "SYS_UserProfile.CreatedBy", createdBy));
    }

    // 프로필 없는 계정은 로그인이 거부되므로(AuthRepository.GetProfileStatus) 기동 시드처럼 관리자가 만든 계정은 활성 프로필을 함께 둔다.
    public void EnsureActiveProfile(string userId, string employeeName, string createdBy)
        => Exec("""
            IF NOT EXISTS (SELECT 1 FROM dbo.SYS_UserProfile WHERE UserID = @UserID)
                INSERT INTO dbo.SYS_UserProfile (UserID, EmployeeName, AccountStatus, FailedLoginCount, CreatedBy, CreatedTS)
                VALUES (@UserID, @EmpName, 'ACTIVE', 0, @CreatedBy, SYSDATETIME())
            """,
            ("@UserID", "SYS_UserProfile.UserID", userId),
            ("@EmpName", "SYS_UserProfile.EmployeeName", employeeName),
            ("@CreatedBy", "SYS_UserProfile.CreatedBy", createdBy));

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
            ("@UserID", "SYS_UserProfile.UserID", userId),
            ("@PinHash", "SYS_UserProfile.PinHash", pinHash),
            ("@ModifiedBy", "SYS_UserProfile.ModifiedBy", modifiedBy));
    }

    /// <summary>
    /// 사용자 삭제 전 정리 — 프로필과 Identity 보조 행(역할·클레임·외부 로그인·토큰)을 한 트랜잭션으로 지운다.
    /// 개발·로컬 DB 의 AspNetUserRoles·Claims·Logins·Tokens 에는 외래키가 없어 AspNetUsers 만 지우면 고아 행이 남는다(10-07 확인).
    /// </summary>
    /// <summary>
    /// SYS-001 계정 삭제 — 프로필·Identity 보조 행과 계정(AspNetUsers)까지 한 트랜잭션(10-13). 전에는 프로필만 지운 뒤
    /// UserManager.DeleteAsync 결과를 보지 않아, 계정 삭제가 실패하면 "프로필 없음" 계정이 남은 채 삭제됨으로 기록됐다.
    /// 계정 행이 이미 없으면 false(아무것도 바꾸지 않음).
    /// </summary>
    public bool DeleteUser(string userId)
    {
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        using var cmd  = new SqlCommand("""
            DELETE dbo.AspNetUserRoles  WHERE UserId = @UserID;
            DELETE dbo.AspNetUserClaims WHERE UserId = @UserID;
            DELETE dbo.AspNetUserLogins WHERE UserId = @UserID;
            DELETE dbo.AspNetUserTokens WHERE UserId = @UserID;
            DELETE dbo.SYS_UserProfile  WHERE UserID = @UserID;
            DELETE dbo.AspNetUsers      WHERE Id     = @UserID;
            SELECT @@ROWCOUNT;
            """, conn, tx);
        cmd.Parameters.Add("@UserID", SqlDbType.NVarChar, 450).Value = userId;
        if (Convert.ToInt32(cmd.ExecuteScalar()) == 0) { tx.Rollback(); return false; }
        tx.Commit();
        return true;
    }

    /// <summary>SYS-001 수정 저장 한 건 — 바꿀 것만 채운다(ChangeRole=false 면 역할, PasswordHash/PinHash 가 null 이면 그 값은 그대로).</summary>
    public sealed record UserEditRequest(
        string UserId, string EmployeeNo, string EmployeeName,
        string? Department, string? PlantCode, string? DefaultShift, string AccountStatus,
        bool ChangeRole, string? RoleName, string? PasswordHash, string? PinHash, bool RenewSecurityStamp,
        string Actor);

    public enum UserEditResult { Ok, UserNotFound, RoleMissing }

    // 프로필 컬럼 길이(SYS_UserProfile) — 화면 입력칸 MaxLength 와 저장 전 검사가 같은 값을 쓴다
    public const int EmployeeNoMax = 20, EmployeeNameMax = 50;

    /// <summary>
    /// SYS-001 수정을 한 트랜잭션으로 저장한다 — 역할(AspNetUserRoles) · 프로필(없으면 생성) · PIN · 비밀번호 해시 · 보안 스탬프.
    /// 예전에는 역할 → 비밀번호(Identity) → 프로필 → PIN → 스탬프를 따로 저장해, 프로필 저장이 실패하면(사번 20자 초과 등)
    /// 역할·비밀번호만 바뀐 채 스탬프 갱신·감사 없이 남았다(10-10). 하나라도 실패하면 아무것도 바뀌지 않는다.
    /// 비밀번호는 호출자가 Identity 규칙 검사 후 PasswordHasher 로 만든 해시만 받는다.
    /// </summary>
    public UserEditResult SaveUserEdit(UserEditRequest e)
    {
        if (e.EmployeeNo.Length > EmployeeNoMax || e.EmployeeName.Length > EmployeeNameMax)
            throw new ArgumentException($"EmployeeNo ≤ {EmployeeNoMax}, EmployeeName ≤ {EmployeeNameMax} characters.");

        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            using (var chk = new SqlCommand("SELECT COUNT(*) FROM dbo.AspNetUsers WITH (UPDLOCK, HOLDLOCK) WHERE Id = @UserId;", conn, tx))
            {
                chk.Parameters.Add("@UserId", SqlDbType.NVarChar, 450).Value = e.UserId;
                if (Convert.ToInt32(chk.ExecuteScalar()) != 1) { tx.Rollback(); return UserEditResult.UserNotFound; }
            }

            if (e.ChangeRole)
            {
                using var role = new SqlCommand("""
                    SET NOCOUNT ON;
                    DECLARE @RoleId nvarchar(450) = NULL;
                    IF @Name IS NOT NULL
                    BEGIN
                        SELECT @RoleId = Id FROM dbo.AspNetRoles WITH (UPDLOCK, HOLDLOCK) WHERE NormalizedName = UPPER(@Name);
                        IF @RoleId IS NULL BEGIN SELECT 0; RETURN; END
                    END
                    DELETE dbo.AspNetUserRoles WHERE UserId = @UserId;
                    IF @RoleId IS NOT NULL INSERT dbo.AspNetUserRoles (UserId, RoleId) VALUES (@UserId, @RoleId);
                    SELECT 1;
                    """, conn, tx);
                role.Parameters.Add("@UserId", SqlDbType.NVarChar, 450).Value = e.UserId;
                role.Parameters.Add("@Name",   SqlDbType.NVarChar, 256).Value = string.IsNullOrWhiteSpace(e.RoleName) ? DBNull.Value : e.RoleName.Trim();
                if (Convert.ToInt32(role.ExecuteScalar()) != 1) { tx.Rollback(); return UserEditResult.RoleMissing; }
            }

            using (var prof = new SqlCommand("""
                IF EXISTS (SELECT 1 FROM dbo.SYS_UserProfile WITH (UPDLOCK, HOLDLOCK) WHERE UserID = @UserID)
                    UPDATE dbo.SYS_UserProfile
                    SET    EmployeeNo       = @EmpNo,
                           EmployeeName     = @EmpName,
                           Department       = @Dept,
                           PlantCode        = @Plant,
                           DefaultShift     = @Shift,
                           AccountStatus    = @Status,
                           FailedLoginCount = CASE WHEN UPPER(@Status) = 'ACTIVE' THEN 0 ELSE FailedLoginCount END,
                           PinHash          = COALESCE(@PinHash, PinHash),
                           ModifiedBy       = @Actor,
                           ModifiedTS       = SYSDATETIME()
                    WHERE  UserID = @UserID;
                ELSE
                    INSERT INTO dbo.SYS_UserProfile
                        (UserID, EmployeeNo, EmployeeName, Department, PlantCode, DefaultShift,
                         AccountStatus, FailedLoginCount, PinHash, CreatedBy, CreatedTS)
                    VALUES
                        (@UserID, @EmpNo, @EmpName, @Dept, @Plant, @Shift,
                         @Status, 0, @PinHash, @Actor, SYSDATETIME());
                """, conn, tx))
            {
                prof.Parameters.Add("@UserID",  SqlDbType.NVarChar, 450).Value = e.UserId;
                prof.Parameters.Add("@EmpNo",   SqlDbType.VarChar, EmployeeNoMax).Value     = e.EmployeeNo;
                prof.Parameters.Add("@EmpName", SqlDbType.NVarChar, EmployeeNameMax).Value  = e.EmployeeName;
                prof.Parameters.Add("@Dept",    SqlDbType.VarChar, 30).Value  = (object?)e.Department   ?? DBNull.Value;
                prof.Parameters.Add("@Plant",   SqlDbType.VarChar, 20).Value  = (object?)e.PlantCode    ?? DBNull.Value;
                prof.Parameters.Add("@Shift",   SqlDbType.VarChar, 10).Value  = (object?)e.DefaultShift ?? DBNull.Value;
                prof.Parameters.Add("@Status",  SqlDbType.VarChar, 10).Value  = e.AccountStatus;
                prof.Parameters.Add("@PinHash", SqlDbType.NVarChar, 200).Value = (object?)e.PinHash    ?? DBNull.Value;
                prof.Parameters.Add("@Actor",   SqlDbType.VarChar, 20).Value  = e.Actor;
                prof.ExecuteNonQuery();
            }

            // 비밀번호·보안 스탬프는 Identity 와 같은 컬럼을 직접 쓴다 — 스탬프는 같은지만 비교되므로 형식은 무관하다
            if (e.PasswordHash is not null || e.RenewSecurityStamp)
            {
                using var usr = new SqlCommand("""
                    UPDATE dbo.AspNetUsers
                    SET    PasswordHash     = COALESCE(@Hash, PasswordHash),
                           SecurityStamp    = @Stamp,
                           ConcurrencyStamp = CONVERT(nvarchar(36), NEWID())
                    WHERE  Id = @UserId;
                    """, conn, tx);
                usr.Parameters.Add("@UserId", SqlDbType.NVarChar, 450).Value = e.UserId;
                usr.Parameters.Add("@Hash",   SqlDbType.NVarChar, -1).Value  = (object?)e.PasswordHash ?? DBNull.Value;
                usr.Parameters.Add("@Stamp",  SqlDbType.NVarChar, -1).Value  =
                    Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(20));
                usr.ExecuteNonQuery();
            }

            tx.Commit();
            return UserEditResult.Ok;
        }
        catch { tx.Rollback(); throw; }
    }

    public bool ProfileExists(string userId)
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(
            "SELECT COUNT(1) FROM dbo.SYS_UserProfile WHERE UserID = @UserID", conn);
        ColumnParams.AddFilter(cmd, "@UserID", "SYS_UserProfile.UserID", userId);
        return (int)cmd.ExecuteScalar()! > 0;
    }

    // ── helpers ──────────────────────────────────────────────────────────
    // 저장 매개변수는 값이 들어가는 컬럼("테이블.컬럼")을 함께 적는다 — 형식·길이를 DB 컬럼에서 정하고 저장 전에 길이를 검사한다(ColumnParams, 10-12)
    private void Exec(string sql, params (string Name, string Column, object? Value)[] pars)
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        foreach (var (n, c, v) in pars) ColumnParams.Add(cmd, n, c, v);
        cmd.ExecuteNonQuery();
    }

    private List<T> Query<T>(string sql, Func<IDataReader, T> map, params (string Name, string? Column, object? Value)[] pars)
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        foreach (var (n, c, v) in pars) ColumnParams.AddFilter(cmd, n, c, v);
        using var rdr  = cmd.ExecuteReader();
        var list = new List<T>();
        while (rdr.Read()) list.Add(map(rdr));
        return list;
    }
}
