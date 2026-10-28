using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 10-28 MNT 단위테스트 결과서 FAIL 조치 — DB 경로(로컬 DB 쓰기, AMES_TEST_CONN 지정 시)
public class MntFailFixes1028DbTests
{
    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — inserts and removes test OEE and PM execution rows";
    const string Actor = "itest-mnt1028";
    const string Equip = "INJ-01-01-1800";

    static void Cleanup(Connection.AmesConnectionFactory f) => Exec(f, """
        DELETE dbo.MNT_OEELog WHERE CreatedBy = @A;
        DELETE dbo.MNT_PMExecution WHERE CreatedBy = @A;
        """, ("@A", Actor));

    // MNT001-TC-002 — 당일 OEE 는 자동 채움 설비 OEE(MNT_OEELog)의 오늘 전기일 행 평균, 이전일 값으로 대체하지 않는다
    [SkippableFact]
    public void Equipment_card_today_oee_averages_only_todays_oee_log_rows()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        var mnt = new MntRepository(f!);
        try
        {
            Cleanup(f!);
            var existing = Scalar(f!, "SELECT COUNT(*) FROM dbo.MNT_OEELog WHERE EquipID = @E AND AggDate >= DATEADD(DAY, -1, dbo.FN_POP_ProdToday())", ("@E", Equip));
            Skip.If(Convert.ToInt32(existing) > 0, "test equipment already has OEE rows for today or yesterday");

            Exec(f!, """
                INSERT dbo.MNT_OEELog (EquipID, AggLevel, AggDate, ShiftCode, OEE, CreatedBy, CreatedTS)
                VALUES (@E, 'SHIFT', DATEADD(DAY, -1, dbo.FN_POP_ProdToday()), 'ZT1', 95.00, @A, SYSDATETIME());
                """, ("@E", Equip), ("@A", Actor));
            Assert.Null(mnt.ListEquipment().Single(e => e.EquipId == Equip).TodayOee);   // 이전일 값만 있으면 비어 있다

            Exec(f!, """
                INSERT dbo.MNT_OEELog (EquipID, AggLevel, AggDate, ShiftCode, OEE, CreatedBy, CreatedTS)
                VALUES (@E, 'SHIFT', dbo.FN_POP_ProdToday(), 'ZT1', 60.00, @A, SYSDATETIME()),
                       (@E, 'SHIFT', dbo.FN_POP_ProdToday(), 'ZT2', 90.00, @A, SYSDATETIME());
                """, ("@E", Equip), ("@A", Actor));
            Assert.Equal(75.00m, mnt.ListEquipment().Single(e => e.EquipId == Equip).TodayOee);
        }
        finally { Cleanup(f!); }
    }

    // MNT010-TC-032 — 건너뜀(종료) 이력은 예정일이 든 달에 조회되고, 완료 이력은 완료일이 든 달에 조회된다
    [SkippableFact]
    public void Pm_execution_range_uses_the_due_date_for_skipped_rounds()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        var mnt = new MntRepository(f!);
        try
        {
            Cleanup(f!);
            Exec(f!, """
                INSERT dbo.MNT_PMExecution (PMPlanNumber, EquipID, PMClass, DueDate, CompletedAt, Result, CreatedBy, CreatedTS)
                VALUES ('ITEST-SKIP', @E, 'MAINT', '2001-01-30', '2001-02-03 10:00', 'CLOSED', @A, SYSDATETIME()),
                       ('ITEST-DONE', @E, 'MAINT', '2001-01-30', '2001-02-03 10:00', 'OK',     @A, SYSDATETIME());
                """, ("@E", Equip), ("@A", Actor));

            var jan = mnt.ListPmExecutions(new DateTime(2001, 1, 1), new DateTime(2001, 1, 31), "MAINT").Select(x => x.PmPlanNumber).ToList();
            var feb = mnt.ListPmExecutions(new DateTime(2001, 2, 1), new DateTime(2001, 2, 28), "MAINT").Select(x => x.PmPlanNumber).ToList();
            Assert.Equal(new[] { "ITEST-SKIP" }, jan);
            Assert.Equal(new[] { "ITEST-DONE" }, feb);
        }
        finally { Cleanup(f!); }
    }
}
