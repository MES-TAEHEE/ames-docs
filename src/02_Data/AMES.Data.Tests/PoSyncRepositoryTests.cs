using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Data.Services.PoSync;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

/// <summary>PO Sync 러너 end-to-end — 업서트·모니터 기록·실패/취소 처리. AMES_DEV 통합 테스트, DB 미기동 시 skip.</summary>
public class PoSyncRepositoryTests
{
    const string SrcKey = "ITEST";
    static readonly string Code = PoSyncConfig.InterfaceCodeFor(SrcKey);

    static void Cleanup(AmesConnectionFactory f)
        => Exec(f, "DELETE FROM dbo.SYS_InterfaceMonitor WHERE InterfaceCode = @C;", ("@C", Code));
    const string SoNo   = "ITEST-PO-1";

    static PoSyncSource Source(string cust) => new(SrcKey, "ITEST source", cust, "https://x/y", null, null,
        "7700", "7710", "310471", "1A7700", 30, -60, 0);

    sealed class FakeSource(Func<IReadOnlyList<SrmPoRow>> fetch) : IPoSource
    {
        public (DateOnly From, DateOnly To)? LastWindow;
        public Task<IReadOnlyList<SrmPoRow>> FetchAsync(PoSyncSource s, DateOnly from, DateOnly to, CancellationToken ct)
        {
            LastWindow = (from, to);
            return Task.FromResult(fetch());
        }
    }

    static void CleanupOrders(AmesConnectionFactory f)
        => Exec(f, "DELETE FROM dbo.PP_CustomerOrder WHERE SoNumber = @S;", ("@S", SoNo));

    [SkippableFact]
    public async Task Runner_upserts_rows_and_records_ok()
    {
        var f = TryFactory();
        Skip.If(f is null, "AMES_DEV unreachable");
        var cust = Scalar(f!, "SELECT TOP 1 CustomerID FROM dbo.MD_Customer ORDER BY CustomerID;") as string;
        Skip.If(cust is null, "MD_Customer empty");
        Cleanup(f!); CleanupOrders(f!);
        try
        {
            var fake = new FakeSource(() =>
            [
                new SrmPoRow(SoNo + "-10", "2026-09-01", "2026-09-10", "ITEST-PART", null, null, 100, 0),
                new SrmPoRow(SoNo + "-20", "2026-09-01", "2026-09-10", "ITEST-PART", null, null, -1, 0),   // skipped
            ]);
            var runner = new PoSyncRunner(f!, fake);

            var r = await runner.RunAsync(Source(cust!), CancellationToken.None);
            Assert.True(r.Ok, r.Error);
            Assert.Equal((2, 1, 1, 1, 0), (r.Fetched, r.Mapped, r.Skipped, r.Inserted, r.Updated));
            Assert.Equal(DateOnly.FromDateTime(DateTime.Today).AddDays(-60), fake.LastWindow!.Value.From);
            Assert.Equal(DateOnly.FromDateTime(DateTime.Today).AddDays(0), fake.LastWindow!.Value.To);

            Assert.Equal("Open", Scalar(f!, "SELECT Status FROM dbo.PP_CustomerOrder WHERE SoNumber=@S AND SoLineNo=10;", ("@S", SoNo)));
            Assert.Equal(PoSyncRunner.Actor, Scalar(f!, "SELECT CreatedBy FROM dbo.PP_CustomerOrder WHERE SoNumber=@S AND SoLineNo=10;", ("@S", SoNo)));

            r = await runner.RunAsync(Source(cust!), CancellationToken.None);
            Assert.Equal((0, 1), (r.Inserted, r.Updated));

            var mon = new SysRepository(f!).ListInterfaces().Single(x => x.InterfaceCode == Code);
            Assert.Equal("OK", mon.ConnStatus);
            Assert.Equal(1, mon.LastRecordCount);
            Assert.Equal(PoSyncRunner.Actor, Scalar(f!, "SELECT CreatedBy FROM dbo.SYS_InterfaceMonitor WHERE InterfaceCode=@C;", ("@C", Code)));
        }
        finally { Cleanup(f!); CleanupOrders(f!); }
    }

    [SkippableFact]
    public async Task Runner_records_error_when_fetch_throws_and_for_config_error()
    {
        var f = TryFactory();
        Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f!);
        try
        {
            var runner = new PoSyncRunner(f!, new FakeSource(() => throw new HttpRequestException("boom 500")));
            var r = await runner.RunAsync(Source("ANY"), CancellationToken.None);
            Assert.False(r.Ok);
            Assert.Contains("boom 500", r.Error);
            var mon = new SysRepository(f!).ListInterfaces().Single(x => x.InterfaceCode == Code);
            Assert.Equal("ERROR", mon.ConnStatus);
            Assert.Contains("boom 500", mon.LastErrorMsg);

            var ce = runner.RecordConfigError(new PoSyncConfigError(SrcKey, "ITEST source", $"{PoSyncConfig.GroupUrl} 에 URL 행이 없습니다"));
            Assert.False(ce.Ok);
            mon = new SysRepository(f!).ListInterfaces().Single(x => x.InterfaceCode == Code);
            Assert.Equal(2, mon.RetryCount);
            Assert.Contains("URL", mon.LastErrorMsg);
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public async Task Runner_treats_timeout_cancellation_as_error_when_caller_token_is_live()
    {
        var f = TryFactory();
        Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f!);
        try
        {
            var runner = new PoSyncRunner(f!, new FakeSource(() => throw new TaskCanceledException("timeout")));
            var r = await runner.RunAsync(Source("ANY"), CancellationToken.None);
            Assert.False(r.Ok);
            Assert.Contains("TaskCanceledException", r.Error);
            var mon = new SysRepository(f!).ListInterfaces().Single(x => x.InterfaceCode == Code);
            Assert.Equal("ERROR", mon.ConnStatus);
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public async Task Runner_rethrows_when_caller_token_is_cancelled()
    {
        var f = TryFactory();
        Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f!);
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var runner = new PoSyncRunner(f!, new FakeSource(() => throw new OperationCanceledException(cts.Token)));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(Source("ANY"), cts.Token));
        }
        finally { Cleanup(f!); }
    }
}
