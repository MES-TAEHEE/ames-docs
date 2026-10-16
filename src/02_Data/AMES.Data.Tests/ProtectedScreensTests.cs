using AMES.Data.Repositories;
using AMES.Data.Services;
using Microsoft.Data.SqlClient;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// SYS-003·SYS-004 보호 — 관리자의 읽기·수정은 고정, 두 화면의 수정 기능·코드·주소·삭제는 막는다(10-12)
public class ProtectedScreensTests
{
    [Theory]
    [InlineData("sys/rbac",    true)]
    [InlineData("/sys/screens/", true)]
    [InlineData("SYS/RBAC",    true)]
    [InlineData("sys/users",   false)]
    [InlineData(null,          false)]
    public void IsProtected_by_path(string? href, bool expected)
        => Assert.Equal(expected, ProtectedScreens.IsProtected(href));

    [Theory]
    [InlineData(SystemRoles.AdminId, "sys/rbac",  'R', true)]
    [InlineData(SystemRoles.AdminId, "sys/rbac",  'E', true)]
    [InlineData(SystemRoles.AdminId, "sys/rbac",  'A', false)]   // 승인은 고정 대상이 아니다
    [InlineData(SystemRoles.AdminId, "sys/users", 'R', false)]
    [InlineData(SystemRoles.SupervisorId, "sys/rbac", 'R', false)]
    public void IsLocked_only_admin_read_edit_on_protected(string roleId, string href, char slot, bool expected)
        => Assert.Equal(expected, ProtectedScreens.IsLocked(roleId, href, slot));

    [Theory]
    [InlineData(SystemRoles.AdminId, "sys/rbac",  "",   "RE")]
    [InlineData(SystemRoles.AdminId, "sys/rbac",  "A",  "REA")]
    [InlineData(SystemRoles.AdminId, "sys/users", "",   "")]
    [InlineData(SystemRoles.OperatorId, "sys/rbac", "", "")]
    public void ApplyRole_keeps_admin_read_edit(string roleId, string href, string granted, string expected)
        => Assert.Equal(expected, ProtectedScreens.ApplyRole(roleId, href, granted));

    [Theory]
    [InlineData("sys/rbac",    "_XX", "__X")]   // 수정 기능은 끌 수 없다, 승인은 그대로
    [InlineData("sys/screens", "_X_", "___")]
    [InlineData("sys/users",   "_XX", "_XX")]
    public void ApplyTemplate_keeps_edit_feature(string href, string template, string expected)
        => Assert.Equal(expected, ProtectedScreens.ApplyTemplate(href, template));

    // 저장소 거부 — 모두 쓰기 전에 THROW 하므로 DB 는 바뀌지 않는다
    [SkippableFact]
    public void Repository_refuses_changing_or_deleting_protected_screens()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var sys = new SysRepository(f);
        var rbac = sys.ListScreens().First(s => ProtectedScreens.IsProtected(s.HRef) && s.HRef!.Trim('/') == "sys/rbac");
        var before = Scalar(f, "SELECT CONCAT(ScreenCode, '|', HRef, '|', PermissionCriteria, '|', ModifiedTS) FROM dbo.SYS_Screen WHERE ScreenID = @I", ("@I", rbac.ScreenId));

        int Number(Action a) => Assert.Throws<SqlException>(a).Number;
        Assert.Equal(51824, Number(() => sys.UpdateScreen(rbac.ScreenId, "ITEST-1012", rbac.ModuleCode, rbac.ProcessCode, rbac.SubProcessCode,
            rbac.ScreenName, rbac.ScreenNameEn, rbac.HRef, rbac.LidLabel, rbac.SortOrder, rbac.IsVisible, "itest")));
        Assert.Equal(51824, Number(() => sys.UpdateScreen(rbac.ScreenId, rbac.ScreenCode, rbac.ModuleCode, rbac.ProcessCode, rbac.SubProcessCode,
            rbac.ScreenName, rbac.ScreenNameEn, "sys/rbac-moved", rbac.LidLabel, rbac.SortOrder, rbac.IsVisible, "itest")));
        Assert.Equal(51826, Number(() => sys.DeleteScreen(rbac.ScreenId, "itest")));
        Assert.Equal(51825, Number(() => sys.InsertScreen("ITEST-1012", rbac.ModuleCode, rbac.ProcessCode, null,
            "itest", null, "sys/rbac", null, null, false, "itest")));

        Assert.Equal(before, Scalar(f, "SELECT CONCAT(ScreenCode, '|', HRef, '|', PermissionCriteria, '|', ModifiedTS) FROM dbo.SYS_Screen WHERE ScreenID = @I", ("@I", rbac.ScreenId)));
        Assert.Equal(0, (int)Scalar(f, "SELECT COUNT(*) FROM dbo.SYS_Screen WHERE ScreenCode = 'ITEST-1012'")!);
    }
}
