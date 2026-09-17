using AMES.Data.Connection;
using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

/// <summary>SYS_InterfaceMonitor 업서트 — 모든 연동 Worker 가 공유하는 기록 규칙. AMES_DEV 통합 테스트, DB 미기동 시 skip.</summary>
public class SysRepositoryTests
{
    const string Code  = "ITEST-MONITOR";
    const string Actor = "ITEST-ACTOR";

    static void Cleanup(AmesConnectionFactory f)
        => Exec(f, "DELETE FROM dbo.SYS_InterfaceMonitor WHERE InterfaceCode = @C;", ("@C", Code));

    static (object? CreatedBy, object? ModifiedBy) Stamps(AmesConnectionFactory f)
        => (Scalar(f, "SELECT CreatedBy  FROM dbo.SYS_InterfaceMonitor WHERE InterfaceCode = @C;", ("@C", Code)),
            Scalar(f, "SELECT ModifiedBy FROM dbo.SYS_InterfaceMonitor WHERE InterfaceCode = @C;", ("@C", Code)));

    [SkippableFact]
    public void UpsertInterfaceMonitor_inserts_then_updates_and_keeps_last_sync_on_error()
    {
        var f = TryFactory();
        Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f!);
        try
        {
            var sys = new SysRepository(f!);

            sys.UpsertInterfaceMonitor(Code, "ITEST", "https://x/y", 30, ok: true, recordCount: 12, error: null, actor: Actor);
            var row = sys.ListInterfaces().Single(r => r.InterfaceCode == Code);
            Assert.Equal("OK", row.ConnStatus);
            Assert.Equal(12, row.LastRecordCount);
            Assert.Equal(0, row.RetryCount);
            Assert.NotNull(row.LastSyncTs);
            Assert.Equal("INBOUND", row.Direction);
            Assert.Equal("REST", row.Protocol);
            Assert.Equal(30, row.MaxGapMinutes);
            var firstSync = row.LastSyncTs;

            sys.UpsertInterfaceMonitor(Code, "ITEST", "https://x/y", 30, ok: false, recordCount: null, error: new string('e', 1200), actor: Actor);
            row = sys.ListInterfaces().Single(r => r.InterfaceCode == Code);
            Assert.Equal("ERROR", row.ConnStatus);
            Assert.Equal(1, row.RetryCount);
            Assert.Equal(1000, row.LastErrorMsg!.Length);
            Assert.Equal(firstSync, row.LastSyncTs);
            Assert.Equal(12, row.LastRecordCount);

            sys.UpsertInterfaceMonitor(Code, "ITEST", "https://x/y", 30, ok: false, recordCount: null, error: "again", actor: Actor);
            Assert.Equal(2, sys.ListInterfaces().Single(r => r.InterfaceCode == Code).RetryCount);

            sys.UpsertInterfaceMonitor(Code, "ITEST", "https://x/z", 60, ok: true, recordCount: 0, error: null, actor: Actor);
            row = sys.ListInterfaces().Single(r => r.InterfaceCode == Code);
            Assert.Equal("OK", row.ConnStatus);
            Assert.Equal(0, row.RetryCount);
            Assert.Null(row.LastErrorMsg);
            Assert.Equal("https://x/z", row.Endpoint);
            Assert.Equal(60, row.MaxGapMinutes);
            Assert.Equal(1, sys.ListInterfaces().Count(r => r.InterfaceCode == Code));
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void UpsertInterfaceMonitor_stamps_the_callers_actor_on_insert_and_update()
    {
        var f = TryFactory();
        Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f!);
        try
        {
            var sys = new SysRepository(f!);

            sys.UpsertInterfaceMonitor(Code, "ITEST", "", 0, ok: false, recordCount: null, error: "x", actor: Actor);
            Assert.Equal((Actor, (object?)DBNull.Value), Stamps(f!));

            sys.UpsertInterfaceMonitor(Code, "ITEST", "", 0, ok: true, recordCount: 1, error: null, actor: "ITEST-OTHER");
            Assert.Equal((Actor, (object?)"ITEST-OTHER"), Stamps(f!));
        }
        finally { Cleanup(f!); }
    }
}
