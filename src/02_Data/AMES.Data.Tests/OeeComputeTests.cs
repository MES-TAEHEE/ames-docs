using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 10-16 — 상태 로그는 있는데 실적이 한 건도 없는 구간에서 PP-OEE 계산이 DBNull 변환으로 실패하던 결함(RowsWithStd 의 SUM 이 NULL)
public class OeeComputeTests
{
    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — writes and removes test line state logs";
    const string Line = "ITEST-OEEC";
    static readonly DateTime Start = new(2099, 2, 1, 7, 0, 0);

    [SkippableFact]
    public void Window_with_state_logs_but_no_production_computes_with_assumed_performance()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        try
        {
            Exec(f!, "DELETE dbo.PP_LineStateLog WHERE LineID = @L;", ("@L", Line));
            // 60분: 40분 가동, 20분 정지(비계획)
            Exec(f!, """
                INSERT dbo.PP_LineStateLog (LineID, MinuteTS, State, PlanState, RunFlag, CreatedBy, CreatedTS)
                SELECT @L, DATEADD(MINUTE, n, @S), CASE WHEN n < 40 THEN 'RUN' ELSE 'DOWN' END, 'PLAN-RUN', CASE WHEN n < 40 THEN 1 ELSE 0 END, 'itest-oeec', SYSDATETIME()
                FROM (SELECT TOP 60 ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS n FROM sys.all_objects) x;
                """, ("@L", Line), ("@S", Start));

            var r = new OeeRepository(f!).ComputeOee(Line, Start, Start.AddHours(1), null);

            Assert.NotNull(r);
            Assert.Equal((60, 0, 40, 20), (r!.LoadingMin, r.PlannedDownMin, r.OperatingMin, r.UnplannedDownMin));
            Assert.Equal((0m, 100m, 100m), (r.TotalProducedQty, r.Performance, r.Quality));
            Assert.True(r.PerformanceAssumed);
            Assert.Equal(66.67m, r.Availability);
        }
        finally { Exec(f!, "DELETE dbo.PP_LineStateLog WHERE LineID = @L;", ("@L", Line)); }
    }
}
