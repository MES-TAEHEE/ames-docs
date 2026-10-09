using AMES.Data.Connection;
using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// MNT-002 고장 상태는 등록·수정으로 바꾸지 않는다 — 등록은 항상 OPEN + 작업지시 발행, 해결은 수리 완료(ResolveFailure)로만(10-10)
public class FailureStatusFlowTests
{
    const string No = "ITEST-FAIL-1010";

    static void Cleanup(AmesConnectionFactory f) => Exec(f, """
        DELETE a FROM dbo.MNT_FailureAction a JOIN dbo.MNT_FailureRegister r ON r.FailureID = a.FailureID WHERE r.FailureNumber = @N;
        DELETE w FROM dbo.MNT_WorkOrder w JOIN dbo.MNT_FailureRegister r ON r.WorkOrderID = w.WorkOrderID WHERE r.FailureNumber = @N;
        DELETE dbo.MNT_FailureRegister WHERE FailureNumber = @N;
        """, ("@N", No));

    static (string? Fail, DateTime? ResolvedAt, string? Wo) State(AmesConnectionFactory f)
    {
        using var conn = f.OpenConnection();
        using var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
            SELECT r.Status, r.ResolvedAt, w.Status FROM dbo.MNT_FailureRegister r
            LEFT JOIN dbo.MNT_WorkOrder w ON w.WorkOrderID = r.WorkOrderID WHERE r.FailureNumber = @N
            """, conn);
        cmd.Parameters.AddWithValue("@N", No);
        using var rd = cmd.ExecuteReader();
        Assert.True(rd.Read());
        return (rd[0] as string, rd[1] as DateTime?, rd[2] as string);
    }

    [SkippableFact]
    public void Status_changes_only_through_resolve()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f);
        try
        {
            var mnt = new MntRepository(f);
            var rep = DateTime.Today.AddHours(-3);
            var (id, _) = mnt.InsertFailure(No, "IMG-EQ-01", "ELEC", "ITEST 증상", "MINOR", "WEB", rep, null, "itest");
            Assert.Equal(("OPEN", (DateTime?)null, "ISSUED"), State(f));

            // 수정은 내용만 — 상태·해결일시 그대로
            mnt.UpdateFailure(id, No, "IMG-EQ-01", "ELEC", "ITEST 증상 수정", "MAJOR", "WEB", rep, null, "itest");
            Assert.Equal(("OPEN", (DateTime?)null, "ISSUED"), State(f));

            // 수리 완료 — 고장과 작업지시가 같이 닫힌다
            mnt.ResolveFailure(id, "ITEST 원인", "ITEST 조치", rep.AddHours(1), 30, "itest");
            var s = State(f);
            Assert.Equal(("RESOLVED", "COMPLETED"), (s.Fail, s.Wo));
            Assert.NotNull(s.ResolvedAt);

            // 해결된 고장을 수정해도 해결 상태 유지
            mnt.UpdateFailure(id, No, "IMG-EQ-01", "ELEC", "ITEST 증상 재수정", "MAJOR", "WEB", rep, null, "itest");
            Assert.Equal(("RESOLVED", "COMPLETED"), (State(f).Fail, State(f).Wo));
        }
        finally { Cleanup(f); }
    }
}
