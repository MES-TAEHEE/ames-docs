using AMES.Data.Services;
using AMES.Data.Services.DemandPlan;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests.DemandPlan;

/// <summary>fetch → map → 창 교체 → 모니터. 가짜 소스로 응답을 주입한다. AMES_DEV 통합(모니터·계획 행), DB 미기동 시 skip.</summary>
[Collection("AMES_DEV plan week")]
public class DemandPlanRunnerTests
{
    sealed class FakeSource(Func<SrmMipResponse> make) : IDemandPlanSource
    {
        public Task<SrmMipResponse> FetchAsync(DemandPlanSource s, DateOnly planDate, CancellationToken ct) => Task.FromResult(make());
    }
    static DemandPlanSource Src() => new("ITEST-DPS", "ITEST DP source", "ITEST-DPC", "https://srm.invalid/mip", null, null, "7700", "7710", "310471", "1A7700", 60, null);
    static string Sample() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "DemandPlan", "MIP_PLAN_7700_310471_20260929_api.json"));

    static void Cleanup(AMES.Data.Connection.AmesConnectionFactory f) => Exec(f, """
        DELETE FROM dbo.PP_DemandPlan WHERE CustomerID = 'ITEST-DPC';
        DELETE FROM dbo.PP_DemandPlanBatch WHERE CustomerID = 'ITEST-DPC';
        DELETE FROM dbo.SYS_InterfaceMonitor WHERE InterfaceCode = 'DPSYNC-ITEST-DPS';
        """);

    [SkippableFact]
    public async Task Runner_replaces_window_and_records_ok()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f!);
        try
        {
            var r = await new DemandPlanRunner(f!, new FakeSource(() => SrmMipMapper.Parse(Sample()))).RunAsync(Src(), CancellationToken.None);
            Assert.True(r.Ok, r.Error);
            Assert.Equal((57, 57, 325), (r.Fetched, r.Mapped, r.Cells));
            Assert.Equal(new DateOnly(2026, 9, 29), r.From);
            Assert.Equal(325, Scalar(f!, "SELECT COUNT(*) FROM dbo.PP_DemandPlan WHERE CustomerID = 'ITEST-DPC'"));
            Assert.Equal("OK", Scalar(f!, "SELECT ConnStatus FROM dbo.SYS_InterfaceMonitor WHERE InterfaceCode = 'DPSYNC-ITEST-DPS'"));
            Assert.Equal(1, Scalar(f!, "SELECT COUNT(*) FROM dbo.PP_DemandPlanBatch WHERE CustomerID = 'ITEST-DPC' AND Source = 'SRM' AND SourceKey = 'ITEST-DPS'"));
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public async Task Runner_does_not_replace_window_when_response_has_no_rows()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f!);
        try
        {
            await new DemandPlanRunner(f!, new FakeSource(() => SrmMipMapper.Parse(Sample()))).RunAsync(Src(), CancellationToken.None);
            var empty = """{"success":true,"plan_date":"2026-09-29","dates":{"D0":"2026-09-29","D1":"2026-10-30"},"count":0,"data":[]}""";
            var r = await new DemandPlanRunner(f!, new FakeSource(() => SrmMipMapper.Parse(empty))).RunAsync(Src(), CancellationToken.None);
            Assert.True(r.Ok); Assert.Equal(0, r.Fetched);
            Assert.Equal(325, Scalar(f!, "SELECT COUNT(*) FROM dbo.PP_DemandPlan WHERE CustomerID = 'ITEST-DPC'"));   // 그대로
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public async Task Runner_does_not_replace_window_when_all_rows_filtered_out()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f!);
        try
        {
            await new DemandPlanRunner(f!, new FakeSource(() => SrmMipMapper.Parse(Sample()))).RunAsync(Src(), CancellationToken.None);
            // 행은 2개(Rows>0) 있지만 하나는 PARTNO 가 비었고 하나는 전 날짜 수량이 0 — 매핑 결과 셸(Cells)이 0이다.
            var allFilteredOut = """
                {"success":true,"plan_date":"2026-09-29","dates":{"D0":"2026-09-29","D1":"2026-09-30"},"count":2,"data":[
                    {"PARTNO":"","PARTNM":"blank","UNIT":"EA","PACK_QTY":1,"D0_PR_QTY":5,"D1_PR_QTY":5},
                    {"PARTNO":"ITEST-DP-ZERO","PARTNM":"zero","UNIT":"EA","PACK_QTY":1,"D0_PR_QTY":0,"D1_PR_QTY":0}
                ]}
                """;
            var r = await new DemandPlanRunner(f!, new FakeSource(() => SrmMipMapper.Parse(allFilteredOut))).RunAsync(Src(), CancellationToken.None);
            Assert.True(r.Ok, r.Error);
            Assert.Equal(2, r.Fetched);
            Assert.Equal(0, r.Cells);
            Assert.Equal(325, Scalar(f!, "SELECT COUNT(*) FROM dbo.PP_DemandPlan WHERE CustomerID = 'ITEST-DPC'"));   // 그대로 — Rows>0 이라도 Cells==0 이면 창을 지우면 안 된다
            Assert.Equal("OK", Scalar(f!, "SELECT ConnStatus FROM dbo.SYS_InterfaceMonitor WHERE InterfaceCode = 'DPSYNC-ITEST-DPS'"));
            Assert.NotEmpty(r.Warnings!);
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public async Task Runner_records_error_when_fetch_throws()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f!);
        try
        {
            var r = await new DemandPlanRunner(f!, new FakeSource(() => throw new HttpRequestException("boom"))).RunAsync(Src(), CancellationToken.None);
            Assert.False(r.Ok); Assert.Contains("boom", r.Error);
            Assert.Equal("ERROR", Scalar(f!, "SELECT ConnStatus FROM dbo.SYS_InterfaceMonitor WHERE InterfaceCode = 'DPSYNC-ITEST-DPS'"));
        }
        finally { Cleanup(f!); }
    }
}
