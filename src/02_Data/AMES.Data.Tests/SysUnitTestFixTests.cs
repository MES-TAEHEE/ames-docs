using AMES.Data.Repositories;
using AMES.Data.Services;
using Microsoft.Data.SqlClient;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 10-28 SYS 단위테스트 결과서 FAIL 조치 — 권한 상승 방지·설정값 범위·연동 상태 판정·주소 가림·화면 경로 중복·설정 일괄 저장·알림 KPI·감사 공정 코드
public class SysUnitTestFixTests
{
    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — writes and removes test rows";

    // ── SYS004-TC-010 자기 역할 권한 상승 방지 ─────────────────────────────
    [Theory]
    [InlineData(new[] { "ROLE-PRODUCTION" }, "ROLE-PRODUCTION", true)]
    [InlineData(new[] { "ROLE-PRODUCTION", "ROLE-QUALITY" }, "ROLE-QUALITY", true)]
    [InlineData(new[] { "ROLE-PRODUCTION" }, "ROLE-QUALITY", false)]
    [InlineData(new[] { "ROLE-SYSADMIN", "ROLE-PRODUCTION" }, "ROLE-PRODUCTION", false)]
    [InlineData(new[] { "ROLE-SYSADMIN" }, "ROLE-SYSADMIN", false)]
    [InlineData(new string[0], "ROLE-PRODUCTION", false)]
    public void Own_role_is_blocked_unless_admin(string[] mine, string target, bool blocked)
        => Assert.Equal(blocked, RoleSelfEditRules.Blocks(mine, target));

    // ── SYS009-TC-002·004 설정값 형식·범위 ─────────────────────────────────
    [Theory]
    [InlineData("MNT_PM_LEAD_DAYS", "INT", "7", ConfigValueRules.Problem.None, "7")]
    [InlineData("MNT_PM_LEAD_DAYS", "INT", "366", ConfigValueRules.Problem.OutOfRange, null)]
    [InlineData("MNT_PM_LEAD_DAYS", "INT", "-1", ConfigValueRules.Problem.OutOfRange, null)]
    [InlineData("MNT_PM_LEAD_DAYS", "INT", "1.5", ConfigValueRules.Problem.NotNumber, null)]
    [InlineData("MNT_PM_LEAD_DAYS", "INT", "abc", ConfigValueRules.Problem.NotNumber, null)]
    [InlineData("OEE_TARGET_PCT", "DECIMAL", "85.0", ConfigValueRules.Problem.None, "85.0")]
    [InlineData("OEE_TARGET_PCT", "DECIMAL", "85.55", ConfigValueRules.Problem.TooManyDecimals, null)]
    [InlineData("OEE_TARGET_PCT", "DECIMAL", "100.1", ConfigValueRules.Problem.OutOfRange, null)]
    [InlineData("UNKNOWN_INT", "INT", "123456", ConfigValueRules.Problem.None, "123456")]
    [InlineData("UNKNOWN_DEC", "DECIMAL", "1.23456", ConfigValueRules.Problem.TooManyDecimals, null)]
    [InlineData("PP_SO_AUTO_CONFIRM", "bool", "TRUE", ConfigValueRules.Problem.None, "true")]
    [InlineData("PP_SO_AUTO_CONFIRM", "bool", "yes", ConfigValueRules.Problem.NotBool, null)]
    [InlineData("LANGUAGE_DEFAULT", "STRING", "ko-KR", ConfigValueRules.Problem.None, "ko-KR")]
    public void Config_values_are_checked_by_type_and_key(string key, string type, string value, ConfigValueRules.Problem expected, string? normalized)
    {
        var problem = ConfigValueRules.Validate(key, type, value, out var norm);
        Assert.Equal(expected, problem);
        if (expected == ConfigValueRules.Problem.None) Assert.Equal(normalized, norm);
    }

    [Fact]
    public void Plant_timezone_is_hidden_and_ranges_follow_readers()
    {
        Assert.Contains("PLANT_TIMEZONE", ConfigValueRules.HiddenKeys);
        Assert.Equal(new ConfigValueRules.Range(1, 60, 0), ConfigValueRules.RangeOf("PP_LINE_RUN_GAP_MIN", "INT"));
        Assert.Null(ConfigValueRules.RangeOf("LANGUAGE_DEFAULT", "STRING"));
    }

    // ── SYS006-TC-008 정상 → 경고 → 다운 ──────────────────────────────────
    static readonly DateTime Synced = new(2026, 10, 28, 8, 0, 0);

    [Theory]
    [InlineData("OK", 10, 10, "OK")]
    [InlineData("OK", 11, 10, "WARN")]
    [InlineData("OK", 20, 10, "WARN")]
    [InlineData("OK", 21, 10, "DOWN")]
    [InlineData("UP", 999, 0, "UP")]           // 허용 간격 없음 — 시간으로 판정하지 않는다
    [InlineData("ERROR", 1, 10, "ERROR")]      // 기록이 이미 장애면 그대로
    public void Interface_status_turns_warn_then_down_by_gap(string status, int minutes, int gap, string expected)
        => Assert.Equal(expected, MonitorRules.EffectiveInterfaceStatus(status, Synced, minutes, gap));

    [Fact]
    public void Disabled_or_never_synced_interfaces_keep_recorded_status()
    {
        Assert.Equal("OK", MonitorRules.EffectiveInterfaceStatus("OK", Synced, 500, 10, isEnabled: false));
        Assert.Equal("OK", MonitorRules.EffectiveInterfaceStatus("OK", null, 500, 10));
    }

    // ── SYS006-TC-009 주소 비밀값 가림 ────────────────────────────────────
    [Theory]
    [InlineData("http://192.168.1.68:5220/Service/WEBSRV_INQUERY_PO.ashx", "http://192.168.1.68:5220/Service/WEBSRV_INQUERY_PO.ashx")]
    [InlineData("https://h/api?APIKEY=abc123&CORCD=7700", "https://h/api?APIKEY=***&CORCD=7700")]
    [InlineData("https://h/a?token=t&access_token=x&page=2#top", "https://h/a?token=***&access_token=***&page=2#top")]
    [InlineData("ftp://user:secretpw@host/path", "ftp://user:***@host/path")]
    [InlineData("https://h/a?Password=&x=1", "https://h/a?Password=&x=1")]
    public void Endpoint_secrets_are_masked(string raw, string expected)
        => Assert.Equal(expected, MonitorRules.MaskEndpoint(raw));

    // ── DB(로컬) ─────────────────────────────────────────────────────────
    [SkippableFact]
    public void Screen_path_and_code_duplicates_are_refused_for_every_screen()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        var sys = new SysRepository(f!);
        var other = sys.ListScreens().First(s => !string.IsNullOrWhiteSpace(s.HRef) && !ProtectedScreens.IsProtected(s.HRef));
        Exec(f!, "DELETE dbo.SYS_Screen WHERE ScreenCode IN ('ITEST-1028A','ITEST-1028B');");
        try
        {
            int Number(Action a) => Assert.Throws<SqlException>(a).Number;
            // 다른 화면 주소(앞뒤 / 차이 무시)
            Assert.Equal(51825, Number(() => sys.InsertScreen("ITEST-1028A", other.ModuleCode, null, null, "itest", null, "/" + other.HRef!.Trim('/') + "/", null, 1, false, "itest")));
            sys.InsertScreen("ITEST-1028A", other.ModuleCode, null, null, "itest", null, "itest/1028a", null, 1, false, "itest");
            // 같은 코드
            Assert.Equal(51822, Number(() => sys.InsertScreen("ITEST-1028A", other.ModuleCode, null, null, "itest", null, "itest/1028b", null, 1, false, "itest")));
            sys.InsertScreen("ITEST-1028B", other.ModuleCode, null, null, "itest", null, "itest/1028b", null, 1, false, "itest");
            var b = sys.ListScreens().Single(s => s.ScreenCode == "ITEST-1028B");
            Assert.Equal(51825, Number(() => sys.UpdateScreen(b.ScreenId, b.ScreenCode, b.ModuleCode, null, null, "itest", null, "itest/1028a", null, 1, false, "itest")));
            // 주소 없는 화면끼리는 막지 않는다
            sys.UpdateScreen(b.ScreenId, b.ScreenCode, b.ModuleCode, null, null, "itest", null, null, null, 1, false, "itest");
        }
        finally { Exec(f!, "DELETE dbo.SYS_Screen WHERE ScreenCode IN ('ITEST-1028A','ITEST-1028B');"); }
    }

    [SkippableFact]
    public void Saving_permissions_of_own_role_is_refused_without_writing()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        var sys = new SysRepository(f!);
        const string role = "ITEST-RBAC-SELF", user = "ITEST-USER-SELF";
        var scr = sys.ListScreens().First(s => !ProtectedScreens.IsProtected(s.HRef) && !string.Equals(s.ProcessCode, "PORTAL", StringComparison.OrdinalIgnoreCase));
        void Clean() => Exec(f!, "DELETE dbo.SYS_RolePermission WHERE RoleID = @R; DELETE dbo.AspNetUserRoles WHERE UserId = @U;", ("@R", role), ("@U", user));
        Clean();
        try
        {
            Exec(f!, "INSERT dbo.AspNetUserRoles (UserId, RoleId) VALUES (@U, @R);", ("@U", user), ("@R", role));
            var items = new[] { new SysRepository.RolePermissionItem(scr.ModuleCode, scr.ScreenCode, "R") };
            Assert.Throws<RoleSelfEditException>(() => sys.SaveRolePermissions(role, role, items, "itest", user));
            Assert.Equal(0, (int)Scalar(f!, "SELECT COUNT(*) FROM dbo.SYS_RolePermission WHERE RoleID = @R", ("@R", role))!);
            // 관리자는 자기 역할도 고칠 수 있다
            Exec(f!, "INSERT dbo.AspNetUserRoles (UserId, RoleId) VALUES (@U, @A);", ("@U", user), ("@A", SystemRoles.AdminId));
            sys.SaveRolePermissions(role, role, items, "itest", user);
            Assert.Equal(1, (int)Scalar(f!, "SELECT COUNT(*) FROM dbo.SYS_RolePermission WHERE RoleID = @R", ("@R", role))!);
        }
        finally { Clean(); }
    }

    [SkippableFact]
    public void Config_batch_save_is_all_or_nothing()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        var sys = new SysRepository(f!);
        var row = sys.ListConfig().First(r => r.ConfigKey == "LOG_RETENTION_DAYS");
        var before = Scalar(f!, "SELECT CONCAT(ConfigValue, '|', ModifiedBy, '|', ModifiedTS) FROM dbo.SYS_Config WHERE ConfigID = @I", ("@I", row.ConfigId));
        Assert.ThrowsAny<Exception>(() => sys.SetConfigs(new[]
        {
            new SysRepository.ConfigChange(row.ConfigId, "999", row.IsActive),
            new SysRepository.ConfigChange(-1, "1", true),   // 없는 행 — 전체 되돌림
        }, "itest"));
        Assert.Equal(before, Scalar(f!, "SELECT CONCAT(ConfigValue, '|', ModifiedBy, '|', ModifiedTS) FROM dbo.SYS_Config WHERE ConfigID = @I", ("@I", row.ConfigId)));
    }

    [SkippableFact]
    public void Notification_kpi_counts_the_whole_history_and_failures_by_last_attempt()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        var sys = new SysRepository(f!);
        const string ev = "ITEST_1028";
        void Clean() => Exec(f!, "DELETE dbo.SYS_NotificationHistory WHERE EventTypeCode = @E;", ("@E", ev));
        Clean();
        try
        {
            var k0 = sys.CountNotifications24h();
            // 실패 처리는 SentAt 을 쓰지 않는다 — 마지막 시도 시각(ModifiedTS)으로 센다. 25시간 전 실패는 세지 않는다
            Exec(f!, """
                INSERT dbo.SYS_NotificationHistory (EventTypeCode, RecipientUserID, Channel, Status, SentAt, CreatedBy, CreatedTS, ModifiedTS)
                VALUES (@E, 'itest', 'PUSH', 'SENT',   SYSDATETIME(), 'itest', SYSDATETIME(), SYSDATETIME()),
                       (@E, 'itest', 'PUSH', 'FAILED', NULL,          'itest', DATEADD(HOUR,-30,SYSDATETIME()), SYSDATETIME()),
                       (@E, 'itest', 'PUSH', 'ERROR',  NULL,          'itest', SYSDATETIME(), NULL),
                       (@E, 'itest', 'PUSH', 'FAILED', NULL,          'itest', DATEADD(HOUR,-26,SYSDATETIME()), DATEADD(HOUR,-25,SYSDATETIME()));
                """, ("@E", ev));
            var k1 = sys.CountNotifications24h();
            Assert.Equal(k0.Sent + 1, k1.Sent);
            Assert.Equal(k0.Failed + 2, k1.Failed);
            var h = sys.GetHealth();
            Assert.Equal(k1.Sent, h.NotifLast24h);
            Assert.Equal(k1.Failed, h.NotifFailedLast24h);
        }
        finally { Clean(); }
    }

    [SkippableFact]
    public void Audit_rows_take_the_process_code_of_the_screen_and_keep_the_result()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        var sys = new SysRepository(f!);
        var scr = sys.ListScreens().First(s => !string.IsNullOrEmpty(s.ProcessCode));
        void Clean() => Exec(f!, "DELETE dbo.SYS_AuditLog WHERE ActorUserID = 'ITEST-1028';");
        Clean();
        try
        {
            sys.InsertAuditLog(scr.ModuleCode, scr.ScreenCode, "DENIED", "ITEST", "x", null, null, "ITEST-1028", result: "FAIL");
            sys.InsertAuditLog("SYS", "ACCOUNT", "LOGIN_FAIL", "AspNetUsers", "x", null, null, "ITEST-1028", result: "FAIL");
            var rows = sys.SearchAudit(new SysRepository.AuditFilter(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1), null, null, null, "FAIL",
                new[] { "ITEST-1028" })).Where(r => r.ActorUserId == "ITEST-1028").ToList();
            Assert.Equal(2, rows.Count);
            Assert.Equal(scr.ProcessCode, rows.Single(r => r.ActionType == "DENIED").ProcessCode);
            Assert.Null(rows.Single(r => r.ActionType == "LOGIN_FAIL").ProcessCode);
            // 이름 사전으로 푼 코드 목록만으로도 찾는다(검색어가 코드와 달라도)
            var byName = sys.SearchAudit(new SysRepository.AuditFilter(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1), "홍길동-없는-이름", null, null, null,
                new[] { "ITEST-1028" }));
            Assert.Equal(2, byName.Count(r => r.ActorUserId == "ITEST-1028"));
        }
        finally { Clean(); }
    }
}
