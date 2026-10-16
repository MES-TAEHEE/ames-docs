using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 10-13 사용자 결정 — RPT-010 생산·불량 전기일, SYS-001 삭제 한 트랜잭션, MD-032 작업자 사용 안 함
public class OpenItemFixes1013Tests
{
    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — creates and removes a test row";

    // RPT-010 보고서 빌더의 생산 실적·불량 합계는 RPT-001·RPT-003 과 같은 하루(전기일)로 묶여 같은 값이어야 한다(읽기 전용)
    [SkippableFact]
    public void Report_builder_production_and_defects_match_daily_reports()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var rpt = new RptRepository(f);
        var to = rpt.ProdToday(); var from = to.AddDays(-90);

        var prod = rpt.AdhocQuery("PROD", "Date", from, to);
        var (good, defect) = rpt.ProductionTotals(from, to);
        Assert.Equal(good + defect, prod.Sum(r => r.M["Production"]));
        Assert.All(prod, r => Assert.InRange(DateTime.Parse(r.Key), from, to));

        var defects = rpt.AdhocQuery("DEFECT", "Date", from, to);
        Assert.Equal(rpt.ListDefectAgg(from, to).Sum(r => r.Qty), defects.Sum(r => r.M["DefectQty"]));
        Assert.All(defects, r => Assert.InRange(DateTime.Parse(r.Key), from, to));
    }

    [SkippableFact]
    public void Deleting_a_user_removes_the_account_profile_and_roles_together()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, "AMES_TEST_CONN unreachable");
        var id = "itest-" + Guid.NewGuid().ToString("N");
        try
        {
            Exec(f, "INSERT dbo.AspNetUsers (Id, UserName, NormalizedUserName, Email, NormalizedEmail) VALUES (@I, @I, UPPER(@I), @I, UPPER(@I))", ("@I", id));
            Exec(f, "INSERT dbo.SYS_UserProfile (UserID, CreatedBy) VALUES (@I, 'itest')", ("@I", id));
            Exec(f, "INSERT dbo.AspNetUserRoles (UserId, RoleId) VALUES (@I, 'ROLE-OPERATOR')", ("@I", id));
            var sys = new SysRepository(f);

            Assert.True(sys.DeleteUser(id));
            foreach (var (table, col) in new[] { ("AspNetUsers", "Id"), ("SYS_UserProfile", "UserID"), ("AspNetUserRoles", "UserId") })
                Assert.Equal(0, Convert.ToInt32(Scalar(f, $"SELECT COUNT(*) FROM dbo.{table} WHERE {col} = @I", ("@I", id))));
            Assert.False(sys.DeleteUser(id));                       // 이미 없는 계정 — 아무것도 바꾸지 않음
        }
        finally
        {
            Exec(f, "DELETE dbo.AspNetUserRoles WHERE UserId=@I; DELETE dbo.SYS_UserProfile WHERE UserID=@I; DELETE dbo.AspNetUsers WHERE Id=@I;", ("@I", id));
        }
    }

    [SkippableFact]
    public void Deactivated_worker_is_not_recreated_by_a_badge_scan()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, "AMES_TEST_CONN unreachable");
        var no = "ZT" + Random.Shared.Next(100000, 999999);
        try
        {
            var workers = new WorkerRepository(f);
            Assert.True(workers.EnsureRegistered(no, "itest"));
            var id = Convert.ToInt32(Scalar(f, "SELECT WorkerID FROM dbo.MD_Worker WHERE EmployeeNo = @N", ("@N", no)));

            workers.Deactivate(id, "itest");
            Assert.False(workers.EnsureRegistered(no, "itest"));    // 행이 남아 배지 스캔이 새로 만들지 않는다
            Assert.Equal(1, Convert.ToInt32(Scalar(f, "SELECT COUNT(*) FROM dbo.MD_Worker WHERE EmployeeNo = @N", ("@N", no))));
            Assert.False(Convert.ToBoolean(Scalar(f, "SELECT ActiveFlag FROM dbo.MD_Worker WHERE EmployeeNo = @N", ("@N", no))));
            Assert.DoesNotContain(workers.ListAllActive(), w => w.EmployeeNo == no);
        }
        finally { Exec(f, "DELETE dbo.MD_Worker WHERE EmployeeNo = @N", ("@N", no)); }
    }
}
