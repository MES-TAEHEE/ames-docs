using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// MD-21-D — 템플릿이 PM 일정에 쓰이는 동안은 그 템플릿의 점검 단계도 지워지지 않는다
public class PmTemplateStepDeleteTests
{
    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — creates and removes a test PM template, step and schedule";
    const string Tpl = "ITEST-PMT";
    const string Plan = "ITEST-PMT-PLAN";

    static void Cleanup(Connection.AmesConnectionFactory f) => Exec(f, """
        DELETE dbo.MNT_PMSchedule WHERE PMPlanNumber = @P;
        DELETE dbo.MD_PmTemplateStep WHERE PMTemplateID = @T;
        DELETE dbo.MD_PmTemplate WHERE PMTemplateID = @T;
        """, ("@T", Tpl), ("@P", Plan));

    [SkippableFact]
    public void Step_of_a_used_template_is_kept_until_the_schedule_is_cancelled()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        var md = new MasterDataRepository(f!);
        Cleanup(f!);
        try
        {
            Exec(f!, "INSERT dbo.MD_PmTemplate (PMTemplateID, CreatedBy) VALUES (@T, 'itest')", ("@T", Tpl));
            var step = md.InsertPmTemplateStep(Tpl, 10, "nozzle", null, null, null, null, "itest");
            Assert.Empty(md.PmTemplateStepUsages(step));

            Exec(f!, "INSERT dbo.MNT_PMSchedule (PMPlanNumber, ChecklistID, Status, CreatedBy) VALUES (@P, @T, 'OK', 'itest')", ("@P", Plan), ("@T", Tpl));
            Assert.Contains(md.PmTemplateStepUsages(step), u => u.Kind == "PM");
            Assert.Contains(md.DeletePmTemplateStep(step), u => u.Kind == "PM");
            Assert.NotNull(Scalar(f!, "SELECT 1 FROM dbo.MD_PmTemplateStep WHERE PMStepID = @S", ("@S", step)));

            // 일정을 취소하면 더는 쓰이는 것이 아니므로 지워진다
            Exec(f!, "UPDATE dbo.MNT_PMSchedule SET Status = 'CANCELLED' WHERE PMPlanNumber = @P", ("@P", Plan));
            Assert.Empty(md.DeletePmTemplateStep(step));
            Assert.Null(Scalar(f!, "SELECT 1 FROM dbo.MD_PmTemplateStep WHERE PMStepID = @S", ("@S", step)));
        }
        finally { Cleanup(f!); }
    }
}
