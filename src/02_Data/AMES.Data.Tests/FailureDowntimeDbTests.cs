using AMES.Data.Repositories;
using AMES.Data.Services;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 10-16 — 고장 「라인 정지」 ↔ PP_LineDowntimeLog: 생성·연결·소유 이전·해결/종료 시 닫기·해제·삭제, PP-DTL 잠금
public class FailureDowntimeDbTests
{
    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — creates and removes test failures, work orders and downtime rows";
    const string Actor = "itest-fdt";
    const string Equip = "INJ-850-02";

    static void Cleanup(Connection.AmesConnectionFactory f) => Exec(f, """
        DELETE d FROM dbo.PP_LineDowntimeLog d JOIN dbo.MNT_FailureRegister r ON r.FailureID = d.FailureID WHERE r.CreatedBy = @A;
        DELETE dbo.PP_LineDowntimeLog WHERE CreatedBy = @A;
        DELETE a FROM dbo.MNT_FailureAction a JOIN dbo.MNT_FailureRegister r ON r.FailureID = a.FailureID WHERE r.CreatedBy = @A;
        DELETE dbo.MNT_WorkOrder WHERE CreatedBy = @A;
        DELETE dbo.MNT_FailureRegister WHERE CreatedBy = @A;
        """, ("@A", Actor));

    static object? Fr(Connection.AmesConnectionFactory f, int id, string col) =>
        Scalar(f, $"SELECT {col} FROM dbo.MNT_FailureRegister WHERE FailureID = @I", ("@I", id));

    static object? Dt(Connection.AmesConnectionFactory f, int id, string col) =>
        Scalar(f, $"SELECT {col} FROM dbo.PP_LineDowntimeLog WHERE DowntimeID = @I", ("@I", id));

    [SkippableFact]
    public void Line_stop_opens_links_hands_over_and_closes_the_downtime()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        var mnt = new MntRepository(f!);
        var pp  = new PpRepository(f!);
        try
        {
            Cleanup(f!);
            var line  = (string)Scalar(f!, "SELECT LineID FROM dbo.MD_Equipment WHERE EquipID = @E", ("@E", Equip))!;
            var start = ((DateTime)Scalar(f!, "SELECT SYSDATETIME()")!).AddMinutes(-20);
            Skip.If(Scalar(f!, "SELECT COUNT(*) FROM dbo.PP_LineDowntimeLog WHERE LineID = @L AND ISNULL(EndTS, '9999-12-31') > @S",
                ("@L", line), ("@S", start)) is not 0, "test line already has downtime after the start time");

            // 사용자가 끄면(MAJOR) 비가동 없음 → 수정에서 켜면 그 고장이 연 비가동이 생기고 설비는 중지
            var (a, aNo, aWo, aDt) = mnt.InsertFailure(null, Equip, "ELEC", "ITEST 라인 정지 A", "MAJOR", "WEB", start, null, Actor);
            Assert.Null(aDt.DowntimeId);
            var (_, upd) = mnt.UpdateFailure(a, aNo, Equip, "ELEC", "ITEST 라인 정지 A", "MAJOR", "WEB", start, null, Actor, lineStop: true);
            Assert.True(upd.Created);
            var dt = upd.DowntimeId!.Value;
            Assert.Equal(dt, Fr(f!, a, "DowntimeID"));
            Assert.Equal(a, Dt(f!, dt, "FailureID"));
            Assert.Equal(start, Dt(f!, dt, "StartTS"));
            Assert.Equal(FailureDowntimeRules.ReasonCode, Dt(f!, dt, "ReasonCode"));
            Assert.Equal(EquipStatusRules.Down, mnt.ListEquipment().Single(e => e.EquipId == Equip).Status);

            // 같은 라인의 두 번째 고장은 새로 열지 않고 연결
            var (b, _, bWo, bDt) = mnt.InsertFailure(null, Equip, "MECH", "ITEST 라인 정지 B", "MINOR", "WEB", start.AddMinutes(5), null, Actor, lineStop: true);
            Assert.False(bDt.Created);
            Assert.Equal(dt, bDt.DowntimeId);
            Assert.Equal(1, Scalar(f!, "SELECT COUNT(*) FROM dbo.PP_LineDowntimeLog WHERE LineID = @L AND EndTS IS NULL", ("@L", line)));

            // PP-DTL 에서는 시각·사유를 고칠 수 없다
            Assert.Equal(PpRepository.DowntimeSaveResult.NotEditable,
                pp.UpdateDowntimeEntry(dt, new PpRepository.DowntimeEntry(line, start, null, "QUALITY", null, null), Actor));

            // A 가 해결돼도 B 가 열려 있으니 닫지 않고 B 로 넘긴다
            mnt.ResolveFailure(a, "원인", "조치", start.AddMinutes(10), 10, Actor);
            Assert.Equal(DBNull.Value, Dt(f!, dt, "EndTS"));
            Assert.Equal(b, Dt(f!, dt, "FailureID"));

            // B 를 작업지시 종료로 닫으면 종료 시각으로 닫힌다
            var bWoId = (int)Fr(f!, b, "WorkOrderID")!;
            mnt.CloseWorkOrder(bWoId, "ITEST 종료", Actor);
            var end = Dt(f!, dt, "EndTS") as DateTime?;
            Assert.NotNull(end);
            Assert.Equal((int)(end!.Value - start).TotalMinutes, Dt(f!, dt, "DurationMin"));
            Assert.Equal(dt, Fr(f!, b, "DowntimeID"));   // 지난 기록으로 연결은 남는다
            Assert.NotEqual(EquipStatusRules.Down, mnt.ListEquipment().Single(e => e.EquipId == Equip).Status);
            _ = (aWo, bWo);
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void Critical_is_always_a_line_stop_and_turning_it_off_or_deleting_removes_the_owned_row()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        var mnt = new MntRepository(f!);
        try
        {
            Cleanup(f!);
            var line  = (string)Scalar(f!, "SELECT LineID FROM dbo.MD_Equipment WHERE EquipID = @E", ("@E", Equip))!;
            var start = ((DateTime)Scalar(f!, "SELECT SYSDATETIME()")!).AddMinutes(-20);
            Skip.If(Scalar(f!, "SELECT COUNT(*) FROM dbo.PP_LineDowntimeLog WHERE LineID = @L AND ISNULL(EndTS, '9999-12-31') > @S",
                ("@L", line), ("@S", start)) is not 0, "test line already has downtime after the start time");

            // CRITICAL 은 lineStop 이 꺼져 있어도 기록되고, 수정에서 꺼도 유지된다
            var (c, cNo, _, cDt) = mnt.InsertFailure(null, Equip, "ELEC", "ITEST 치명", "CRITICAL", "WEB", start, null, Actor);
            Assert.True(cDt.Created);
            var (_, keep) = mnt.UpdateFailure(c, cNo, Equip, "ELEC", "ITEST 치명", "CRITICAL", "WEB", start, null, Actor, lineStop: false);
            Assert.Equal(cDt.DowntimeId, keep.DowntimeId);

            // 발생 시각을 고치면 그 고장이 연 열린 행도 따라간다
            mnt.UpdateFailure(c, cNo, Equip, "ELEC", "ITEST 치명", "CRITICAL", "WEB", start.AddMinutes(3), null, Actor, lineStop: true);
            Assert.Equal(start.AddMinutes(3), Dt(f!, cDt.DowntimeId!.Value, "StartTS"));

            // 심각도를 내리고 끄면 그 행을 지우고 연결을 푼다
            mnt.UpdateFailure(c, cNo, Equip, "ELEC", "ITEST 치명 → 경미", "MINOR", "WEB", start.AddMinutes(3), null, Actor, lineStop: false);
            Assert.Null(Dt(f!, cDt.DowntimeId!.Value, "DowntimeID"));
            Assert.Equal(DBNull.Value, Fr(f!, c, "DowntimeID"));

            // 착수 전 고장을 지우면 그 고장이 연 비가동도 지운다
            var (d, _, _, dDt) = mnt.InsertFailure(null, Equip, "ELEC", "ITEST 삭제", "MAJOR", "WEB", start, null, Actor, lineStop: true);
            Assert.True(dDt.Created);
            Assert.NotNull(mnt.DeleteFailure(d, Actor));
            Assert.Null(Dt(f!, dDt.DowntimeId!.Value, "DowntimeID"));

            // 수리 완료는 해결 시각으로 닫는다
            var (r, _, _, rDt) = mnt.InsertFailure(null, Equip, "ELEC", "ITEST 수리", "MAJOR", "WEB", start, null, Actor, lineStop: true);
            mnt.ResolveFailure(r, "원인", "조치", start.AddMinutes(15), 15, Actor);
            Assert.Equal(start.AddMinutes(15), Dt(f!, rDt.DowntimeId!.Value, "EndTS"));
            Assert.Equal(15, Dt(f!, rDt.DowntimeId!.Value, "DurationMin"));
        }
        finally { Cleanup(f!); }
    }
}
