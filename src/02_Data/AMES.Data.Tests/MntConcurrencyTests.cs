using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 10-13 사용자 결정 — MNT 완료 직렬화(동시 완료는 한 번만), 번호 채번 범위 잠금, 고장 삭제는 열린 웹 고장만 + 조치 이력까지
public class MntConcurrencyTests
{
    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — creates and removes test failures/work orders";
    const string Actor = "itest-mntcc";
    const string Equip = "INJ-09-01-450";

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
                var (failId, _, _, _) = mnt.InsertFailure(null, Equip, "ELEC", "ITEST 동시 완료", "MINOR", "WEB", DateTime.Now, null, Actor);
                var woId = Convert.ToInt32(Scalar(f!, "SELECT WorkOrderID FROM dbo.MNT_FailureRegister WHERE FailureID = @I", ("@I", failId)));

                var ok = 0; var rejected = 0;
                Parallel.For(0, 4, i =>
                {
                    try
                    {
                        if (i % 2 == 0) mnt.CompleteWorkOrder(woId, "OK", $"조치 {i}", null, 10, DateTime.Now, null, Actor);
                        else            mnt.ResolveFailure(failId, "원인", $"조치 {i}", DateTime.Now, 10, Actor);
                        Interlocked.Increment(ref ok);
                    }
                    catch (InvalidOperationException) { Interlocked.Increment(ref rejected); }
                });

                Assert.Equal(1, ok);
                Assert.Equal(3, rejected);
                Assert.Equal(1, Convert.ToInt32(Scalar(f!, "SELECT COUNT(*) FROM dbo.MNT_FailureAction WHERE FailureID = @I AND ActionType = 'REPAIRED' AND WorkOrderID = @W", ("@I", failId), ("@W", woId))));
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
                else            mnt.CompleteWorkOrder(wos[i], "OK", "조치", null, 5, DateTime.Now, null, Actor);
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
                var (_, failNo, woNo, _) = mnt.InsertFailure(null, Equip, "ELEC", "ITEST 동시 등록", "MINOR", "WEB", DateTime.Now, null, Actor);
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

    // 10-26 — 채번이 카운터 행(SYS_NumberSeq)으로 바뀐 뒤: 많이 몰려도 교착 없이 모두 다른 번호
    // (전에는 그 달 번호 범위를 UPDLOCK·HOLDLOCK 으로 읽어, 전체 테스트를 함께 돌릴 때 8건 중 5건이 1205 로 실패했다)
    [SkippableFact]
    public void Heavy_concurrent_registrations_do_not_deadlock()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        var mnt = new MntRepository(f!);
        try
        {
            var results = new System.Collections.Concurrent.ConcurrentBag<(string Fail, string Wo)>();
            Parallel.For(0, 24, new ParallelOptions { MaxDegreeOfParallelism = 12 }, _ =>
            {
                var (_, failNo, woNo, _) = mnt.InsertFailure(null, Equip, "ELEC", "ITEST 동시 등록(부하)", "MINOR", "WEB", DateTime.Now, null, Actor);
                results.Add((failNo, woNo));
            });
            Assert.Equal(24, results.Select(r => r.Fail).Distinct().Count());
            Assert.Equal(24, results.Select(r => r.Wo).Distinct().Count());
        }
        finally { Cleanup(f!); }
    }

    // 직접 입력한 큰 번호 뒤로 자동 번호가 이어지고, 카운터가 지난달 값이어도 이번 달 실제 최대 번호 다음부터 받는다
    [SkippableFact]
    public void Auto_number_follows_manual_numbers_and_month_rollover()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        var mnt = new MntRepository(f!);
        var saved = ((string?)Scalar(f!, "SELECT PeriodKey FROM dbo.SYS_NumberSeq WHERE SeqCode = 'FAIL'"),
                     Convert.ToInt32(Scalar(f!, "SELECT LastNo FROM dbo.SYS_NumberSeq WHERE SeqCode = 'FAIL'")));
        var ym = AMES.Data.Services.DbClock.Today.ToString("yyMM");
        int Seq(string no) => int.Parse(no[(no.LastIndexOf('-') + 1)..]);
        int MonthMax() => Convert.ToInt32(Scalar(f!, "SELECT ISNULL(MAX(TRY_CAST(SUBSTRING(FailureNumber, 11, 10) AS int)), 0) FROM dbo.MNT_FailureRegister WHERE FailureNumber LIKE @P", ("@P", $"FAIL-{ym}-%")));
        try
        {
            // 카운터는 지운 번호를 다시 쓰지 않으므로(결번 허용) 직접 입력 번호를 카운터·실제 최대 번호 둘 다보다 크게 잡는다
            var counter = saved.Item1 == ym ? saved.Item2 : 0;
            var manual = Math.Max(MonthMax(), counter) + 40;
            mnt.InsertFailure($"FAIL-{ym}-{manual:D3}", Equip, "ELEC", "ITEST 직접 입력 번호", "MINOR", "WEB", DateTime.Now, null, Actor);
            var (_, next, _, _) = mnt.InsertFailure(null, Equip, "ELEC", "ITEST 자동 번호", "MINOR", "WEB", DateTime.Now, null, Actor);
            Assert.Equal(manual + 1, Seq(next));

            // 카운터가 다른 달(지난달)에 큰 값으로 남아 있어도 이번 달은 실제 최대 번호 다음
            Exec(f!, "UPDATE dbo.SYS_NumberSeq SET PeriodKey = '0001', LastNo = 900 WHERE SeqCode = 'FAIL'");
            var expected = MonthMax() + 1;
            var (_, rolled, _, _) = mnt.InsertFailure(null, Equip, "ELEC", "ITEST 달 바뀜", "MINOR", "WEB", DateTime.Now, null, Actor);
            Assert.Equal(expected, Seq(rolled));
            Assert.Equal(ym, (string?)Scalar(f!, "SELECT PeriodKey FROM dbo.SYS_NumberSeq WHERE SeqCode = 'FAIL'"));
        }
        finally
        {
            Cleanup(f!);
            Exec(f!, "UPDATE dbo.SYS_NumberSeq SET PeriodKey = @P, LastNo = @N WHERE SeqCode = 'FAIL'", ("@P", saved.Item1!), ("@N", saved.Item2));
        }
    }

    [SkippableFact]
    public void Deleting_a_failure_removes_actions_and_rejects_closed_or_andon_failures()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        var mnt = new MntRepository(f!);
        try
        {
            var (openId, openNo, _, _) = mnt.InsertFailure(null, Equip, "ELEC", "ITEST 삭제", "MINOR", "WEB", DateTime.Now, null, Actor);
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

            var (doneId, _, _, _) = mnt.InsertFailure(null, Equip, "ELEC", "ITEST 해결 후 삭제", "MINOR", "WEB", DateTime.Now, null, Actor);
            mnt.ResolveFailure(doneId, "원인", "조치", DateTime.Now, 5, Actor);
            var ex = Assert.Throws<MntRepository.FailureNotDeletableException>(() => mnt.DeleteFailure(doneId));
            Assert.Equal(Services.FailureDeleteRules.Block.NotOpen, ex.Reason);

            var (andonId, _, _, _) = mnt.InsertFailure(null, Equip, "ELEC", "ITEST 안돈", "MINOR", "ANDON", DateTime.Now, null, Actor);
            Assert.Equal(Services.FailureDeleteRules.Block.FromAndon,
                Assert.Throws<MntRepository.FailureNotDeletableException>(() => mnt.DeleteFailure(andonId)).Reason);
        }
        finally { Cleanup(f!); }
    }

    // 10-14 사용자 결정 — 부분·후속 완료는 고장을 닫지 않고 후속 작업지시로 넘긴다, 정상 완료만 해결
    [SkippableTheory]
    [InlineData("PARTIAL")]
    [InlineData("FOLLOWUP")]
    public void Partial_completion_keeps_the_failure_open_on_a_follow_up_work_order(string result)
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        var mnt = new MntRepository(f!);
        try
        {
            var (failId, failNo, _, _) = mnt.InsertFailure(null, Equip, "ELEC", "ITEST 부분 완료", "MAJOR", "WEB", DateTime.Now.AddHours(-2), null, Actor);
            var woId = Convert.ToInt32(Scalar(f!, "SELECT WorkOrderID FROM dbo.MNT_FailureRegister WHERE FailureID = @I", ("@I", failId)));

            // 화면 입력 최대 길이(원인 200·조치 500)에 가깝게 — 조치 이력 Description 은 nvarchar(500)
            var o = mnt.CompleteWorkOrder(woId, result, new string('조', 450), new string('원', 200), 30, DateTime.Now.AddHours(-1), null, Actor);

            Assert.NotNull(o.FollowUpWoNumber);
            Assert.Equal("COMPLETED", Scalar(f!, "SELECT Status FROM dbo.MNT_WorkOrder WHERE WorkOrderID = @I", ("@I", woId)));
            Assert.Equal("IN_PROGRESS", Scalar(f!, "SELECT Status FROM dbo.MNT_FailureRegister WHERE FailureID = @I", ("@I", failId)));
            Assert.Equal(DBNull.Value, Scalar(f!, "SELECT ResolvedAt FROM dbo.MNT_FailureRegister WHERE FailureID = @I", ("@I", failId)));
            var newWo = Convert.ToInt32(Scalar(f!, "SELECT WorkOrderID FROM dbo.MNT_FailureRegister WHERE FailureID = @I", ("@I", failId)));
            Assert.NotEqual(woId, newWo);
            Assert.Equal(o.FollowUpWoNumber, Scalar(f!, "SELECT WoNumber FROM dbo.MNT_WorkOrder WHERE WorkOrderID = @I AND Status = 'ISSUED' AND Priority = (SELECT Priority FROM dbo.MNT_WorkOrder WHERE WorkOrderID = @O)",
                ("@I", newWo), ("@O", woId)));
            Assert.Equal(1, Convert.ToInt32(Scalar(f!, "SELECT COUNT(*) FROM dbo.MNT_FailureAction WHERE FailureID = @I AND ActionType = @T AND WorkOrderID = @W", ("@I", failId), ("@T", result), ("@W", woId))));
            Assert.Equal(0, Convert.ToInt32(Scalar(f!, "SELECT COUNT(*) FROM dbo.MNT_FailureAction WHERE FailureID = @I AND ActionType = 'REPAIRED'", ("@I", failId))));

            // 넘어간 고장은 원래 작업지시 상세에도 남고(현재 담당 = 후속 작업지시), 후속 작업지시에도 보인다
            var onOriginal = Assert.Single(mnt.ListFailuresForWo(woId), r => r.FailureId == failId);
            Assert.Equal(newWo, onOriginal.WorkOrderId);
            Assert.Equal(o.FollowUpWoNumber, onOriginal.WoNumber);
            Assert.Single(mnt.ListFailuresForWo(newWo), r => r.FailureId == failId);

            // 작업 이력이 생긴 고장은 지울 수 없다(후속 작업지시가 발행 상태여도)
            Assert.Equal(Services.FailureDeleteRules.Block.NotOpen,
                Assert.Throws<MntRepository.FailureNotDeletableException>(() => mnt.DeleteFailure(failId)).Reason);

            // 고장을 고쳐도 후속 작업지시의 설명(남은 작업)은 그대로 — 고장번호를 바꿔도 원래 작업지시와의 연결은 ID 라 유지
            mnt.UpdateFailure(failId, failNo + "-R", Equip, "ELEC", "ITEST 부분 완료(수정)", "MAJOR", "WEB", DateTime.Now.AddHours(-2), null, Actor);
            Assert.StartsWith(result + " ← ", (string)Scalar(f!, "SELECT ActionDesc FROM dbo.MNT_WorkOrder WHERE WorkOrderID = @I", ("@I", newWo))!);
            Assert.Equal(failNo + "-R", Assert.Single(mnt.ListFailuresForWo(woId), r => r.FailureId == failId).FailureNumber);

            // 후속 작업지시를 정상 완료하면 그때 해결
            var done = mnt.CompleteWorkOrder(newWo, "OK", "부품 교체", null, 20, DateTime.Now, null, Actor);
            Assert.Null(done.FollowUpWoNumber);
            Assert.Equal("RESOLVED", Scalar(f!, "SELECT Status FROM dbo.MNT_FailureRegister WHERE FailureID = @I", ("@I", failId)));
            Assert.Equal(1, Convert.ToInt32(Scalar(f!, "SELECT COUNT(*) FROM dbo.MNT_FailureAction WHERE FailureID = @I AND ActionType = 'REPAIRED' AND WorkOrderID = @W", ("@I", failId), ("@W", newWo))));
            Assert.Equal("RESOLVED", Assert.Single(mnt.ListFailuresForWo(woId), r => r.FailureId == failId).Status);
        }
        finally { Cleanup(f!); }
    }
}
