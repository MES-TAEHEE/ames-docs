using AMES.Data.Connection;
using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// SYS-001 수정 저장(SaveUserEdit)은 한 트랜잭션 — 실패하면 역할·프로필·PIN·비밀번호·스탬프 아무것도 바뀌지 않는다.
// SYS-005 날짜 저장(ReplaceCalendarDate)은 DayType 등 문자열을 컬럼 길이 그대로 저장한다(10-09 VarChar(5) 잘림 회귀).
// 전용 가짜 사용자와 2099-01-01 만 쓰고 끝에 지운다.
public class SysUserEditTests
{
    const string UserId = "ITEST-SYS001-USER";
    static readonly DateTime CalDay = new(2099, 1, 1);

    static void Cleanup(AmesConnectionFactory f) => Exec(f, """
        DELETE dbo.AspNetUserRoles WHERE UserId = @U;
        DELETE dbo.SYS_UserProfile WHERE UserID = @U;
        DELETE dbo.AspNetUsers     WHERE Id     = @U;
        DELETE dbo.SYS_FactoryCalendar WHERE CalendarDate = @D;
        """, ("@U", UserId), ("@D", CalDay));

    static void SeedUser(AmesConnectionFactory f) => Exec(f, """
        INSERT dbo.AspNetUsers (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp,
                                ConcurrencyStamp, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount)
        VALUES (@U, 'itest-sys001@ames.local', 'ITEST-SYS001@AMES.LOCAL', 'itest-sys001@ames.local', 'ITEST-SYS001@AMES.LOCAL', 1,
                'OLD-HASH', 'OLD-STAMP', 'OLD-CC', 0, 0, 0, 0);
        INSERT dbo.AspNetUserRoles (UserId, RoleId) VALUES (@U, 'ROLE-OPERATOR');
        """, ("@U", UserId));

    static (string? Hash, string? Stamp, string? Role, string? EmpNo, string? Name, string? Status, string? Pin) State(AmesConnectionFactory f)
    {
        using var conn = f.OpenConnection();
        using var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
            SELECT u.PasswordHash, u.SecurityStamp,
                   (SELECT TOP 1 RoleId FROM dbo.AspNetUserRoles WHERE UserId = u.Id),
                   p.EmployeeNo, p.EmployeeName, p.AccountStatus, p.PinHash
            FROM dbo.AspNetUsers u LEFT JOIN dbo.SYS_UserProfile p ON p.UserID = u.Id
            WHERE u.Id = @U;
            """, conn);
        cmd.Parameters.AddWithValue("@U", UserId);
        using var r = cmd.ExecuteReader();
        Assert.True(r.Read());
        string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        return (S(0), S(1), S(2), S(3), S(4), S(5), S(6));
    }

    static string RoleName(AmesConnectionFactory f, string roleId)
    {
        using var conn = f.OpenConnection();
        using var cmd = new Microsoft.Data.SqlClient.SqlCommand("SELECT Name FROM dbo.AspNetRoles WHERE Id = @R", conn);
        cmd.Parameters.AddWithValue("@R", roleId);
        return (string)cmd.ExecuteScalar()!;
    }

    static SysRepository.UserEditRequest Req(string? role, bool changeRole, string empNo = "ITEST-E01") => new(
        UserId, empNo, "ITEST 사용자", null, null, null, "ACTIVE",
        ChangeRole: changeRole, RoleName: role, PasswordHash: "NEW-HASH", PinHash: "NEW-PIN", RenewSecurityStamp: true, Actor: "itest");

    [SkippableFact]
    public void Edit_saves_role_profile_pin_password_and_stamp_together()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f);
        try
        {
            SeedUser(f);
            var r = new SysRepository(f).SaveUserEdit(Req(RoleName(f, "ROLE-SUPERVISOR"), changeRole: true));

            Assert.Equal(SysRepository.UserEditResult.Ok, r);
            var s = State(f);
            Assert.Equal(("NEW-HASH", "ROLE-SUPERVISOR", "ITEST-E01", "ITEST 사용자", "ACTIVE", "NEW-PIN"),
                         (s.Hash, s.Role, s.EmpNo, s.Name, s.Status, s.Pin));
            Assert.NotEqual("OLD-STAMP", s.Stamp);
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Missing_role_changes_nothing()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f);
        try
        {
            SeedUser(f);
            var r = new SysRepository(f).SaveUserEdit(Req("ITEST no such role", changeRole: true));

            Assert.Equal(SysRepository.UserEditResult.RoleMissing, r);
            Assert.Equal(("OLD-HASH", "OLD-STAMP", "ROLE-OPERATOR", (string?)null), (State(f).Hash, State(f).Stamp, State(f).Role, State(f).EmpNo));
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Too_long_employee_no_changes_nothing()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f);
        try
        {
            SeedUser(f);
            Assert.Throws<ArgumentException>(() =>
                new SysRepository(f).SaveUserEdit(Req(RoleName(f, "ROLE-SUPERVISOR"), changeRole: true, empNo: new string('9', 21))));

            Assert.Equal(("OLD-HASH", "OLD-STAMP", "ROLE-OPERATOR", (string?)null), (State(f).Hash, State(f).Stamp, State(f).Role, State(f).EmpNo));
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Calendar_date_keeps_full_day_type_shift_and_plant()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f);
        try
        {
            new SysRepository(f).ReplaceCalendarDate(CalDay,
                [new SysRepository.CalendarShiftInput("WORKDAY", null, 1, "SHIFT-LONG", TimeSpan.FromHours(7), TimeSpan.FromHours(16), 65, 7.92m, "ITEST-PLANT-20CHAR")],
                "itest");

            using var conn = f.OpenConnection();
            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(
                "SELECT DayType, ShiftCode, PlantCode FROM dbo.SYS_FactoryCalendar WHERE CalendarDate = @D", conn);
            cmd.Parameters.AddWithValue("@D", CalDay);
            using var rd = cmd.ExecuteReader();
            Assert.True(rd.Read());
            Assert.Equal(("WORKDAY", "SHIFT-LONG", "ITEST-PLANT-20CHAR"), (rd.GetString(0), rd.GetString(1), rd.GetString(2)));
        }
        finally { Cleanup(f); }
    }
}
