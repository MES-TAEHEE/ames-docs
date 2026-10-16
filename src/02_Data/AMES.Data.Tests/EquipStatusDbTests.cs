using AMES.Data.Repositories;
using AMES.Data.Services;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 10-16 — MntRepository.ListEquipment 의 SQL 이 EquipStatusRules 대로 상태·근거·열린 작업지시를 계산하는지
public class EquipStatusDbTests
{
    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — creates and removes a test failure and work order";
    const string Actor = "itest-eqstat";
    const string Equip = "INJ-01-01-1800";
    const string Fail  = "ITEST-ES-FAIL";
    const string Wo    = "ITEST-ES-WO";

    static void Cleanup(Connection.AmesConnectionFactory f) => Exec(f, """
        DELETE dbo.MNT_FailureRegister WHERE CreatedBy = @A;
        DELETE dbo.MNT_WorkOrder WHERE CreatedBy = @A;
        """, ("@A", Actor));

    static MntRepository.EquipCardRow Row(MntRepository mnt) => mnt.ListEquipment().Single(e => e.EquipId == Equip);

    [SkippableFact]
    public void Stop_failures_and_started_pm_work_drive_the_equipment_status()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        var mnt = new MntRepository(f!);
        try
        {
            Cleanup(f!);
            var before = Row(mnt);
            Skip.If(before.StopFailureNumber is not null || before.PmWoNumber is not null, "test equipment already has a stop failure or a started PM");
            Assert.Equal(EquipStatusRules.NormalizeManual(before.ManualStatus), before.Status);

            // 멈추지 않는 고장(웹·MAJOR)은 상태를 바꾸지 않는다
            Exec(f!, """
                INSERT dbo.MNT_FailureRegister (FailureNumber, EquipID, Status, Severity, Source, ReportedAt, CreatedBy, CreatedTS)
                VALUES (@N, @E, 'OPEN', 'MAJOR', 'WEB', '2000-01-01', @A, SYSDATETIME())
                """, ("@N", Fail), ("@E", Equip), ("@A", Actor));
            Assert.Equal(before.Status, Row(mnt).Status);

            // 착수된 PM 작업지시 → PM
            Exec(f!, """
                INSERT dbo.MNT_WorkOrder (WoNumber, WoType, EquipID, Status, IssuedAt, StartedAt, CreatedBy, CreatedTS)
                VALUES (@W, 'PM', @E, 'IN_PROGRESS', '2000-01-01', '2000-01-01', @A, SYSDATETIME())
                """, ("@W", Wo), ("@E", Equip), ("@A", Actor));
            var pm = Row(mnt);
            Assert.Equal(EquipStatusRules.Pm, pm.Status);
            Assert.Equal(Wo, pm.PmWoNumber);
            Assert.Equal(Wo, pm.OpenWoNumber);   // 진행 중 작업지시가 대표
            Assert.Equal(before.OpenWoCount + 1, pm.OpenWoCount);

            // CRITICAL 이 되면 PM 보다 앞서 DOWN
            Exec(f!, "UPDATE dbo.MNT_FailureRegister SET Severity = 'CRITICAL' WHERE CreatedBy = @A", ("@A", Actor));
            var down = Row(mnt);
            Assert.Equal(EquipStatusRules.Down, down.Status);
            Assert.Equal(Fail, down.StopFailureNumber);

            // 안돈 고장도 심각도와 관계없이 DOWN
            Exec(f!, "UPDATE dbo.MNT_FailureRegister SET Severity = 'MINOR', Source = 'ANDON' WHERE CreatedBy = @A", ("@A", Actor));
            Assert.Equal(EquipStatusRules.Down, Row(mnt).Status);

            // 고장이 해결되고 작업지시가 끝나면 수동 상태로 돌아간다
            Exec(f!, "UPDATE dbo.MNT_FailureRegister SET Status = 'RESOLVED' WHERE CreatedBy = @A", ("@A", Actor));
            Assert.Equal(EquipStatusRules.Pm, Row(mnt).Status);
            Exec(f!, "UPDATE dbo.MNT_WorkOrder SET Status = 'COMPLETED' WHERE CreatedBy = @A", ("@A", Actor));
            var after = Row(mnt);
            Assert.Equal(before.Status, after.Status);
            Assert.Equal(before.OpenWoCount, after.OpenWoCount);
        }
        finally { Cleanup(f!); }
    }
}
