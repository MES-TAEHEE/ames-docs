using AMES.Data.Connection;
using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// SYS-003 화면 기본 권한(SYS_Screen.PermissionCriteria)을 바꾸면 그 화면의 역할 권한이 같은 트랜잭션에서 맞춰진다.
// 전용 화면 코드·역할 ID 만 쓰고 끝에 지운다.
public class SysScreenPermissionTests
{
    const string Code = "ITEST-SCR-PERM";
    const string RoleA = "ITEST-ROLE-A", RoleB = "ITEST-ROLE-B", RoleC = "ITEST-ROLE-C";

    static void Cleanup(AmesConnectionFactory f) => Exec(f, """
        DELETE dbo.SYS_RolePermission WHERE ScreenCode = @C;
        DELETE dbo.SYS_Screen WHERE ScreenCode = @C;
        """, ("@C", Code));

    static void Grant(AmesConnectionFactory f, string role, string level) => Exec(f, """
        INSERT dbo.SYS_RolePermission (RoleID, RoleName, ModuleCode, ScreenCode, PermissionLevel, IsSystemRole, EffectiveTS, CreatedBy, CreatedTS)
        VALUES (@R, @R, 'WEB', @C, @L, 0, SYSDATETIME(), 'itest', SYSDATETIME());
        """, ("@R", role), ("@C", Code), ("@L", level));

    static Dictionary<string, string> Levels(AmesConnectionFactory f)
    {
        using var conn = f.OpenConnection();
        using var cmd = new Microsoft.Data.SqlClient.SqlCommand("SELECT RoleID, PermissionLevel FROM dbo.SYS_RolePermission WHERE ScreenCode = @C", conn);
        cmd.Parameters.AddWithValue("@C", Code);
        using var r = cmd.ExecuteReader();
        var d = new Dictionary<string, string>();
        while (r.Read()) d[r.GetString(0)] = r.GetString(1);
        return d;
    }

    static SysRepository.ScreenRow Screen(SysRepository sys) => sys.ListScreens().Single(s => s.ScreenCode == Code);

    [SkippableFact]
    public void Changing_screen_template_aligns_role_permissions()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f);
        try
        {
            var sys = new SysRepository(f);
            sys.InsertScreen(Code, "WEB", "SYS", null, "ITEST 화면", null, "itest/screen-perm", Code, 999, false, "itest", "___");
            Assert.Equal("___", Screen(sys).PermissionCriteria);
            Grant(f, RoleA, "REA");
            Grant(f, RoleB, "R_A");
            Grant(f, RoleC, "_E_");

            // 승인 기능을 없앤다 — A 는 X, 승인만 있던 자리는 X
            var s = Screen(sys);
            sys.UpdateScreen(s.ScreenId, Code, "WEB", "SYS", null, s.ScreenName, null, s.HRef, s.LidLabel, s.SortOrder, s.IsVisible, "itest", "__X");
            Assert.Equal("__X", Screen(sys).PermissionCriteria);
            Assert.Equal(new Dictionary<string, string> { [RoleA] = "REX", [RoleB] = "R_X", [RoleC] = "_EX" }, Levels(f));

            // 수정 기능도 없앤다 — E 만 있던 역할은 남는 부여가 없어 행이 지워진다
            sys.UpdateScreen(s.ScreenId, Code, "WEB", "SYS", null, s.ScreenName, null, s.HRef, s.LidLabel, s.SortOrder, s.IsVisible, "itest", "_XX");
            Assert.Equal(new Dictionary<string, string> { [RoleA] = "RXX", [RoleB] = "RXX" }, Levels(f));

            // 기능을 다시 켜면 그 자리는 '_'(미부여) — 예전 부여가 되살아나지 않는다
            sys.UpdateScreen(s.ScreenId, Code, "WEB", "SYS", null, s.ScreenName, null, s.HRef, s.LidLabel, s.SortOrder, s.IsVisible, "itest", "___");
            Assert.Equal(new Dictionary<string, string> { [RoleA] = "R__", [RoleB] = "R__" }, Levels(f));

            // 기본 권한을 주지 않으면(null) 그대로 둔다
            sys.UpdateScreen(s.ScreenId, Code, "WEB", "SYS", null, s.ScreenName, null, s.HRef, s.LidLabel, s.SortOrder, s.IsVisible, "itest");
            Assert.Equal("___", Screen(sys).PermissionCriteria);
            Assert.Equal(new Dictionary<string, string> { [RoleA] = "R__", [RoleB] = "R__" }, Levels(f));
        }
        finally { Cleanup(f); }
    }

    // SYS-004 저장은 넘어온 값이 아니라 DB 의 현재 화면 기본 권한으로 다시 맞춘다 — 화면을 연 사이 SYS-003 이 기능을 꺼도 옛 부여가 남지 않는다
    [SkippableFact]
    public void Role_save_uses_current_screen_template()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f);
        try
        {
            var sys = new SysRepository(f);
            sys.InsertScreen(Code, "WEB", "SYS", null, "ITEST 화면", null, "itest/screen-perm", Code, 999, false, "itest", "__X");

            // 오래된 SYS-004 화면이 "REA"(승인 포함)를 보낸다 → 승인 기능이 없으므로 REX
            var saved = sys.SaveRolePermissions(RoleA, RoleA, [new("WEB", Code, "REA")], "itest");
            Assert.Equal("REX", saved[Code]);
            Assert.Equal(new Dictionary<string, string> { [RoleA] = "REX" }, Levels(f));

            // 기능이 없는 글자만 보내면 남는 부여가 없어 행이 지워진다
            saved = sys.SaveRolePermissions(RoleA, RoleA, [new("WEB", Code, "__A")], "itest");
            Assert.False(saved.ContainsKey(Code));
            Assert.Empty(Levels(f));

            // 등록되지 않은 화면 코드는 세 기능 모두 있다고 본다
            saved = sys.SaveRolePermissions(RoleA, RoleA, [new("WEB", "ITEST-SCR-NONE", "RA")], "itest");
            Assert.Equal("R_A", saved["ITEST-SCR-NONE"]);
        }
        finally
        {
            Cleanup(f);
            Exec(f, "DELETE dbo.SYS_RolePermission WHERE ScreenCode = 'ITEST-SCR-NONE';");
        }
    }
}
