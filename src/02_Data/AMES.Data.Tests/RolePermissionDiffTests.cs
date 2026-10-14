using AMES.Data.Repositories;
using AMES.Data.Services;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 10-14 사용자 결정 — SYS-004 는 역할을 고를 때 읽은 값과 달라진 화면만 저장한다(조회 실패·동시 편집 때 다른 화면 권한을 지우지 않게)
public class RolePermissionDiffTests
{
    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — writes and removes a test role's permission rows";
    const string Role = "ITEST-RBAC-DIFF";

    static Dictionary<string, string> D(params (string K, string V)[] kv) => kv.ToDictionary(x => x.K, x => x.V);

    [Fact]
    public void Only_screens_whose_letters_differ_are_changed()
    {
        var baseline = D(("PP-001", "RE"), ("PP-002", "R"), ("MD-003", "REA"), ("SYS-001", "R"));
        var current  = D(("PP-001", "ER"), ("PP-002", "RE"), ("SYS-001", ""), ("RPT-001", "R"));
        Assert.Equal(new[] { "MD-003", "PP-002", "RPT-001", "SYS-001" }, RolePermissionDiff.Changed(baseline, current));
    }

    [Fact]
    public void Placeholders_and_missing_entries_are_the_same_as_no_grant()
    {
        var baseline = D(("PP-001", "R_X"), ("PP-002", ""));
        var current  = D(("PP-001", "R"));
        Assert.Empty(RolePermissionDiff.Changed(baseline, current));
    }

    [Fact]
    public void Empty_baseline_after_a_failed_load_sends_only_what_the_user_ticked()
    {
        var current = D(("PP-001", "R"));
        Assert.Equal(new[] { "PP-001" }, RolePermissionDiff.Changed(new Dictionary<string, string>(), current));
    }

    // 저장소는 받은 화면만 바꾼다 — 화면이 바뀐 화면만 보내면 나머지 권한 행은 그대로 남는다
    [SkippableFact]
    public void Saving_one_screen_leaves_the_other_screens_of_the_role_untouched()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        var sys = new SysRepository(f!);
        var screens = sys.ListScreens()
            .Where(s => s.PermissionCriteria is { Length: > 0 } c && c[0] == '_' && !ProtectedScreens.IsProtected(s.HRef)
                        && !string.Equals(s.ProcessCode, "PORTAL", StringComparison.OrdinalIgnoreCase))
            .Take(3).ToList();
        Skip.If(screens.Count < 3, "need three non-portal screens");

        Exec(f!, "DELETE dbo.SYS_RolePermission WHERE RoleID = @R;", ("@R", Role));
        try
        {
            sys.SaveRolePermissions(Role, Role, screens.Select(s => new SysRepository.RolePermissionItem(s.ModuleCode, s.ScreenCode, "R")), "itest");
            // 두 번째 화면만 바꾸고(삭제) 그것만 보낸다
            sys.SaveRolePermissions(Role, Role, new[] { new SysRepository.RolePermissionItem(screens[1].ModuleCode, screens[1].ScreenCode, null) }, "itest");

            var left = sys.ListRolePermissions().Where(r => r.RoleId == Role).Select(r => r.ScreenCode!).OrderBy(x => x).ToList();
            Assert.Equal(new[] { screens[0].ScreenCode, screens[2].ScreenCode }.OrderBy(x => x).ToList(), left);
        }
        finally { Exec(f!, "DELETE dbo.SYS_RolePermission WHERE RoleID = @R;", ("@R", Role)); }
    }
}
