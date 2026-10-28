using AMES.Data.Repositories;
using AMES.Contracts.Dto;
using Xunit;

namespace AMES.Data.Tests;

/// <summary>같은 라인에 동시에 안돈을 눌러도 열린 안돈은 한 건 — AMES_DEV 가 닿을 때만 돈다. 시험 라인 ID 로만 쓰고 끝에 지운다.</summary>
public class AndonRaiseConcurrencyDbTests
{
    const string Line = "ITEST-ANDON";

    [SkippableFact]
    public void Concurrent_raises_on_one_line_create_a_single_andon()
    {
        var f = AmesDevDb.TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Clean(f!);
        try
        {
            var repo = new AndonRepository(f!);
            var ids = new int[8];
            Parallel.For(0, ids.Length, new ParallelOptions { MaxDegreeOfParallelism = ids.Length },
                i => ids[i] = repo.Raise(Line, null, "MANUAL", "ITEST", null, "ITEST"));

            Assert.Single(ids.Distinct());
            Assert.Equal(1, (int)AmesDevDb.Scalar(f!, "SELECT COUNT(*) FROM dbo.PR_AndonCall WHERE LineID = @L", ("@L", Line))!);
            Assert.Equal(1, (int)AmesDevDb.Scalar(f!, "SELECT COUNT(*) FROM dbo.PP_LineDowntimeLog WHERE LineID = @L", ("@L", Line))!);
        }
        finally { Clean(f!); }
    }

    [SkippableFact]
    public void A_resolved_andon_does_not_block_the_next_one()
    {
        var f = AmesDevDb.TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Clean(f!);
        try
        {
            var repo = new AndonRepository(f!);
            var first = repo.Raise(Line, null, "MANUAL", "ITEST", null, "ITEST");
            AmesDevDb.Exec(f!, "UPDATE dbo.PR_AndonCall SET Status = 'RESOLVED' WHERE AndonID = @A", ("@A", first));
            var second = repo.Raise(Line, null, "MANUAL", "ITEST", null, "ITEST");
            Assert.NotEqual(first, second);
        }
        finally { Clean(f!); }
    }

    [SkippableFact]
    public void Raise_does_not_open_a_second_downtime_when_the_line_is_already_down()
    {
        var f = AmesDevDb.TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Clean(f!);
        try
        {
            // 웹 PP-DTL·MNT-002 라인 정지가 연 비가동 — 안돈이 겹쳐 열면 그 시간을 두 번 센다(10-27)
            AmesDevDb.Exec(f!, """
                INSERT INTO dbo.PP_LineDowntimeLog (LineID, StartTS, ReasonCode, Comment, LoggedBy, CreatedBy, CreatedTS)
                VALUES (@L, DATEADD(minute, -10, SYSDATETIME()), 'MACHINE', N'ITEST open', 'ITEST', 'ITEST', SYSDATETIME());
                """, ("@L", Line));
            var id = new AndonRepository(f!).Raise(Line, null, "MANUAL", "ITEST", null, "ITEST");
            Assert.True(id > 0);
            Assert.Equal(1, (int)AmesDevDb.Scalar(f!, "SELECT COUNT(*) FROM dbo.PP_LineDowntimeLog WHERE LineID = @L", ("@L", Line))!);
            Assert.Equal(0, (int)AmesDevDb.Scalar(f!, "SELECT COUNT(*) FROM dbo.PP_LineDowntimeLog WHERE AndonID = @A", ("@A", id))!);
        }
        finally { Clean(f!); }
    }

    [SkippableFact]
    public void Maintenance_call_issues_a_cm_work_order_and_resolve_completes_it()
    {
        var f = AmesDevDb.TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Clean(f!);
        try
        {
            var repo = new AndonRepository(f!);
            var id = repo.Raise(Line, null, "MANUAL", "ITEST", null, "ITEST");
            AmesDevDb.Exec(f!, "UPDATE dbo.PR_AndonCall SET Status = 'SUP_ACKED' WHERE AndonID = @A", ("@A", id));   // 부서 호출은 슈퍼바이저 확인 뒤
            repo.CallDepts(id, "MACHINE", "CRITICAL", new[] { AndonDeptCodes.Maint }, "ITEST", "ITEST");

            // 고장 1행 + 보전 작업지시(CM·출처 ANDON·고장번호·HIGH·ISSUED)가 연결된다(10-28)
            var wo = AmesDevDb.Scalar(f!, """
                SELECT CONCAT(w.WoType, '|', w.SourceType, '|', IIF(w.SourceRefID = fr.FailureNumber, 'ref', 'x'), '|', w.Priority, '|', w.Status)
                FROM   dbo.MNT_FailureRegister fr JOIN dbo.MNT_WorkOrder w ON w.WorkOrderID = fr.WorkOrderID
                WHERE  fr.Source = 'ANDON' AND fr.AndonRefID = @R;
                """, ("@R", id.ToString()));
            Assert.Equal("CM|ANDON|ref|HIGH|ISSUED", wo);

            // 같은 안돈의 보전 재호출은 고장·작업지시를 더 만들지 않는다
            AmesDevDb.Exec(f!, "UPDATE dbo.PR_AndonCall SET Status = 'SUP_ACKED' WHERE AndonID = @A", ("@A", id));
            repo.CallDepts(id, "MACHINE", "CRITICAL", new[] { AndonDeptCodes.Maint }, "ITEST", "ITEST");
            Assert.Equal(1, (int)AmesDevDb.Scalar(f!, "SELECT COUNT(*) FROM dbo.MNT_WorkOrder WHERE SourceType = 'ANDON' AND SourceRefID IN (SELECT FailureNumber FROM dbo.MNT_FailureRegister WHERE AndonRefID = @R)", ("@R", id.ToString()))!);

            repo.Resolve(id, null, null, "ITEST");
            var done = AmesDevDb.Scalar(f!, """
                SELECT CONCAT(fr.Status, '|', w.Status, '|', IIF(w.CompletedAt IS NULL, 'no', 'at'), '|', JSON_VALUE(w.ChecklistResultsJSON, '$.Result'))
                FROM   dbo.MNT_FailureRegister fr JOIN dbo.MNT_WorkOrder w ON w.WorkOrderID = fr.WorkOrderID
                WHERE  fr.Source = 'ANDON' AND fr.AndonRefID = @R;
                """, ("@R", id.ToString()));
            Assert.Equal("RESOLVED|COMPLETED|at|OK", done);
        }
        finally { Clean(f!); }
    }

    static void Clean(AMES.Data.Connection.AmesConnectionFactory f)
        => AmesDevDb.Exec(f, """
            DELETE w FROM dbo.MNT_WorkOrder w JOIN dbo.MNT_FailureRegister fr ON fr.WorkOrderID = w.WorkOrderID
              JOIN dbo.PR_AndonCall a ON fr.Source = 'ANDON' AND fr.AndonRefID = CAST(a.AndonID AS varchar(24)) WHERE a.LineID = @L;
            DELETE fa FROM dbo.MNT_FailureAction fa JOIN dbo.MNT_FailureRegister fr ON fr.FailureID = fa.FailureID
              JOIN dbo.PR_AndonCall a ON fr.Source = 'ANDON' AND fr.AndonRefID = CAST(a.AndonID AS varchar(24)) WHERE a.LineID = @L;
            DELETE fr FROM dbo.MNT_FailureRegister fr
              JOIN dbo.PR_AndonCall a ON fr.Source = 'ANDON' AND fr.AndonRefID = CAST(a.AndonID AS varchar(24)) WHERE a.LineID = @L;
            DELETE dbo.PR_AndonDeptCall WHERE AndonID IN (SELECT AndonID FROM dbo.PR_AndonCall WHERE LineID = @L);
            DELETE dbo.PR_AndonPush WHERE AndonID IN (SELECT AndonID FROM dbo.PR_AndonCall WHERE LineID = @L);
            DELETE dbo.PP_LineDowntimeLog WHERE LineID = @L;
            DELETE dbo.PR_AndonCall WHERE LineID = @L;
            """, ("@L", Line));
}
