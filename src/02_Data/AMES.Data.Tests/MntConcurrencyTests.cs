using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 10-13 사용자 결정 — MNT 완료 직렬화(동시 완료는 한 번만), 번호 채번 범위 잠금, 고장 삭제는 열린 웹 고장만 + 조치 이력까지
public class MntConcurrencyTests
{
    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — creates and removes test failures/work orders";
    const string Actor = "itest-mntcc";
    const string Equip = "IMG-EQ-01";

    static void Cleanup(Connection.AmesConnectionFactory f) => Exec(f, """
        DELETE a FROM dbo.MNT_FailureAction a JOIN dbo.MNT_FailureRegister r ON r.FailureID = a.FailureID WHERE r.CreatedBy = @A;
        DELETE dbo.MNT_FailureAction WHERE CreatedBy = @A;
        DELETE dbo.MNT_WorkOrder WHERE CreatedBy = @A;
        DELETE dbo.MNT_FailureRegister WHERE CreatedBy = @A;
        """, ("@A", Actor));

    static Connection.AmesConnectionFactory? WriteFactory()
    {
        if (Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null) return null;
        return TryFactory();
    }

    [SkippableFact]
    public void Concurrent_completions_of_one_work_order_succeed_once()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        var mnt = new MntRepository(f!);
        try
        {
            for (var round = 0; round < 5; round++)
            {
                var (failId, _, _) = mnt.InsertFailure(null, Equip, "ELEC", "ITEST 동시 완료", "MINOR", "WEB", DateTime.Now, null, Actor);
                var woId = Convert.ToInt32(Scalar(f!, "SELECT WorkOrderID FROM dbo.MNT_FailureRegister WHERE FailureID = @I", ("@I", failId)));

                var ok = 0; var rejected = 0;
                Parallel.For(0, 4, i =>
                {
                    try
                    {
                        if (i % 2 == 0) mnt.CompleteWorkOrder(woId, "OK", $"조치 {i}", null, 10, null, DateTime.Now, null, Actor);
                        else            mnt.ResolveFailure(failId, "원인", $"조치 {i}", DateTime.Now, 10, Actor);
                        Interlocked.Increment(ref ok);
                    }
                    catch (InvalidOperationException) { Interlocked.Increment(ref rejected); }
                });

                Assert.Equal(1, ok);
                Assert.Equal(3, rejected);
                Assert.Equal(1, Convert.ToInt32(Scalar(f!, "SELECT COUNT(*) FROM dbo.MNT_FailureAction WHERE FailureID = @I AND ActionType = 'REPAIRED'", ("@I", failId))));
            }
        }
        finally { Cleanup(f!); }
    }

    // 서로 다른 고장을 동시에 수리 완료·작업지시 완료해도 교착 없이 모두 끝난다
    [SkippableFact]
    public void Concurrent_completions_of_different_work_orders_all_succeed()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        var mnt = new MntRepository(f!);
        try
        {
            var ids = Enumerable.Range(0, 8)
                .Select(_ => mnt.InsertFailure(null, Equip, "ELEC", "ITEST 동시 완료(서로 다른 WO)", "MINOR", "WEB", DateTime.Now, null, Actor).FailureId)
                .ToList();
            var wos = ids.Select(id => Convert.ToInt32(Scalar(f!, "SELECT WorkOrderID FROM dbo.MNT_FailureRegister WHERE FailureID = @I", ("@I", id)))).ToList();

            Parallel.For(0, ids.Count, i =>
            {
                if (i % 2 == 0) mnt.ResolveFailure(ids[i], "원인", "조치", DateTime.Now, 5, Actor);
                else            mnt.CompleteWorkOrder(wos[i], "OK", "조치", null, 5, null, DateTime.Now, null, Actor);
            });

            foreach (var id in ids)
                Assert.Equal("RESOLVED", Scalar(f!, "SELECT Status FROM dbo.MNT_FailureRegister WHERE FailureID = @I", ("@I", id)));
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void Concurrent_registrations_get_distinct_numbers()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        var mnt = new MntRepository(f!);
        try
        {
            var results = new System.Collections.Concurrent.ConcurrentBag<(string Fail, string Wo)>();
            Parallel.For(0, 8, _ =>
            {
                var (_, failNo, woNo) = mnt.InsertFailure(null, Equip, "ELEC", "ITEST 동시 등록", "MINOR", "WEB", DateTime.Now, null, Actor);
                results.Add((failNo, woNo));
            });
            Assert.Equal(8, results.Select(r => r.Fail).Distinct().Count());
            Assert.Equal(8, results.Select(r => r.Wo).Distinct().Count());

            var first = results.First().Fail;
            Assert.Throws<MntRepository.DuplicateNumberException>(() =>
                mnt.InsertFailure(first, Equip, "ELEC", "ITEST 중복", "MINOR", "WEB", DateTime.Now, null, Actor));
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void Deleting_a_failure_removes_actions_and_rejects_closed_or_andon_failures()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        var mnt = new MntRepository(f!);
        try
        {
            var (openId, openNo, _) = mnt.InsertFailure(null, Equip, "ELEC", "ITEST 삭제", "MINOR", "WEB", DateTime.Now, null, Actor);
            Exec(f!, "INSERT dbo.MNT_FailureAction (FailureID, ActionType, Description, ActionAt, CreatedBy, CreatedTS) VALUES (@I, 'ARRIVED', N'itest', SYSDATETIME(), @A, SYSDATETIME())",
                ("@I", openId), ("@A", Actor));
            var woId = Convert.ToInt32(Scalar(f!, "SELECT WorkOrderID FROM dbo.MNT_FailureRegister WHERE FailureID = @I", ("@I", openId)));

            var gone = mnt.DeleteFailure(openId);
            Assert.Equal(openNo, gone?.FailureNumber);
            foreach (var (sql, id) in new[] {
                ("SELECT COUNT(*) FROM dbo.MNT_FailureRegister WHERE FailureID = @I", openId),
                ("SELECT COUNT(*) FROM dbo.MNT_FailureAction WHERE FailureID = @I", openId),
                ("SELECT COUNT(*) FROM dbo.MNT_WorkOrder WHERE WorkOrderID = @I", woId) })
                Assert.Equal(0, Convert.ToInt32(Scalar(f!, sql, ("@I", id))));
            Assert.Null(mnt.DeleteFailure(openId));

            var (doneId, _, _) = mnt.InsertFailure(null, Equip, "ELEC", "ITEST 해결 후 삭제", "MINOR", "WEB", DateTime.Now, null, Actor);
            mnt.ResolveFailure(doneId, "원인", "조치", DateTime.Now, 5, Actor);
            var ex = Assert.Throws<MntRepository.FailureNotDeletableException>(() => mnt.DeleteFailure(doneId));
            Assert.Equal(Services.FailureDeleteRules.Block.NotOpen, ex.Reason);

            var (andonId, _, _) = mnt.InsertFailure(null, Equip, "ELEC", "ITEST 안돈", "MINOR", "ANDON", DateTime.Now, null, Actor);
            Assert.Equal(Services.FailureDeleteRules.Block.FromAndon,
                Assert.Throws<MntRepository.FailureNotDeletableException>(() => mnt.DeleteFailure(andonId)).Reason);
        }
        finally { Cleanup(f!); }
    }
}
