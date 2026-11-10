using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// SYS-001 외부 포탈 사용(11-10) — 내부 계정에 협력업체 하나를 연결하면 그 이메일로 SCM_PortalVendorUser 행이 생기고(InternalUserID),
// 포탈 조회는 내부 계정 해시·상태·사번을 함께 돌려준다. 같은 이메일의 진짜 외부 사용자가 있으면 거부, 해제·계정 삭제는 행을 지운다.
// SCM 외부 사용자 관리의 수정·잠금 해제·삭제·비밀번호 변경은 연결 행을 건드리지 않는다. 전용 가짜 사용자만 쓰고 끝에 지운다.
public class PortalInternalLinkTests
{
    const string UserId = "ITEST-PORTAL-LINK";
    const string Email  = "itest-portal-link@ames.local";

    static void Cleanup(AmesConnectionFactory f) => Exec(f, """
        DELETE dbo.SCM_PortalVendorUser WHERE UserID = @E OR InternalUserID = @U;
        DELETE dbo.SYS_UserProfile WHERE UserID = @U;
        DELETE dbo.AspNetUsers     WHERE Id     = @U;
        """, ("@U", UserId), ("@E", Email));

    static void Seed(AmesConnectionFactory f) => Exec(f, """
        INSERT dbo.AspNetUsers (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp,
                                ConcurrencyStamp, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount)
        VALUES (@U, @E, UPPER(@E), @E, UPPER(@E), 1, 'INTERNAL-HASH', 'STAMP', 'CC', 0, 0, 0, 0);
        INSERT dbo.SYS_UserProfile (UserID, EmployeeNo, EmployeeName, AccountStatus, FailedLoginCount, CreatedBy, CreatedTS)
        VALUES (@U, 'ITPL01', N'포탈 연결 시험', 'ACTIVE', 0, 'ITEST', SYSDATETIME());
        """, ("@U", UserId), ("@E", Email));

    static (string A, string B) TwoActiveVendors(AmesConnectionFactory f)
    {
        using var conn = f.OpenConnection();
        using var cmd = new SqlCommand("SELECT TOP 2 VendorID FROM dbo.MD_Vendor WHERE ISNULL(ActiveFlag,1)=1 ORDER BY VendorID", conn);
        using var r = cmd.ExecuteReader();
        var ids = new List<string>();
        while (r.Read()) ids.Add(r.GetString(0));
        Skip.If(ids.Count < 2, "Need two active vendors");
        return (ids[0], ids[1]);
    }

    [SkippableFact]
    public void Linking_creates_a_portal_row_that_reads_the_internal_account_and_unlinking_removes_it()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var sys = new SysRepository(f!); var scm = new ScmRepository(f!);
        var (v1, v2) = TwoActiveVendors(f!);
        Cleanup(f!);
        try
        {
            Seed(f!);
            Assert.Equal(SysRepository.UserEditResult.Ok, sys.SetUserPortalLink(UserId, v1, "ITEST"));

            var row = scm.FindPortalUser(Email);
            Assert.NotNull(row);
            Assert.True(row!.IsInternal);
            Assert.Equal(UserId, row.InternalUserID);
            Assert.Equal(v1, row.VendorID);
            Assert.Equal("INTERNAL-HASH", row.EffectivePasswordHash);   // 로그인·비밀번호 버전은 내부 계정 해시
            Assert.Equal("ACTIVE", row.InternalStatus);
            Assert.Equal("ITPL01", row.InternalEmployeeNo);
            Assert.Equal(v1, sys.ListUsers(int.MaxValue).Single(u => u.UserId == UserId).PortalVendorId);

            // 업체 바꾸기 — 수정 저장 트랜잭션에 실어서
            var edit = new SysRepository.UserEditRequest(UserId, "ITPL01", "포탈 연결 시험", null, null, null, "ACTIVE",
                ChangeRole: false, RoleName: null, PasswordHash: null, PinHash: null, RenewSecurityStamp: false, Actor: "ITEST",
                ChangePortal: true, PortalVendorId: v2);
            Assert.Equal(SysRepository.UserEditResult.Ok, sys.SaveUserEdit(edit));
            Assert.Equal(v2, scm.FindPortalUser(Email)!.VendorID);

            // SCM 외부 사용자 관리의 변경은 연결 행을 건드리지 않는다
            scm.ChangePortalPassword(Email, "EXTERNAL-HASH", "ITEST");
            Assert.Equal(SysRepository.InternalPortalPasswordMarker, scm.FindPortalUser(Email)!.PasswordHash);
            Assert.ThrowsAny<InvalidOperationException>(() => scm.DeletePortalUser(Email, scm.FindPortalUser(Email)!));

            // 해제
            Assert.Equal(SysRepository.UserEditResult.Ok, sys.SaveUserEdit(edit with { PortalVendorId = null }));
            Assert.Null(scm.FindPortalUser(Email));
            Assert.Null(sys.ListUsers(int.MaxValue).Single(u => u.UserId == UserId).PortalVendorId);
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void Linking_is_refused_when_a_real_external_user_owns_the_email_and_user_delete_removes_the_link()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var sys = new SysRepository(f!); var scm = new ScmRepository(f!);
        var (v1, _) = TwoActiveVendors(f!);
        Cleanup(f!);
        try
        {
            Seed(f!);
            scm.InsertPortalUser(Email, v1, null, "EXTERNAL-HASH", true, "ITEST");
            Assert.Equal(SysRepository.UserEditResult.PortalEmailTaken, sys.SetUserPortalLink(UserId, v1, "ITEST"));
            Assert.False(scm.FindPortalUser(Email)!.IsInternal);   // 외부 사용자 행은 그대로
            Assert.Equal(SysRepository.UserEditResult.PortalVendorInactive, sys.SetUserPortalLink(UserId, "NO-SUCH-VENDOR", "ITEST"));
            Exec(f!, "DELETE dbo.SCM_PortalVendorUser WHERE UserID = @E", ("@E", Email));

            Assert.Equal(SysRepository.UserEditResult.Ok, sys.SetUserPortalLink(UserId, v1, "ITEST"));
            Assert.True(sys.DeleteUser(UserId));
            Assert.Null(scm.FindPortalUser(Email));
        }
        finally { Cleanup(f!); }
    }
}
