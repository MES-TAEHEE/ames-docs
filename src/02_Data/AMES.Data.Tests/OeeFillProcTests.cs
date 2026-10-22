using AMES.Data.Repositories;
using AMES.Data.Services;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 10-22 — dbo.SP_PP_LineOEE_Fill(에이전트 작업 "[AMES] Line OEE Fill", dist/migrate_pp_lineoee_fill.sql)이
// OeeRepository.ComputeOee 와 같은 값을 PP_LineOEE 교대 행으로 쓰고, 값이 그대로인 행은 다시 쓰지 않는지.
// 쓰기라서 AMES_TEST_CONN 을 지정했을 때만 돈다. 시험 라인은 MD_Line 에 없어도 된다(대상 = 상태 로그가 있는 라인).
public class OeeFillProcTests
{
    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — writes and removes test state logs, results and OEE snapshots";
    const string Line  = "ITEST-OEEF";
    const string Actor = "itest-oeef";
    static readonly DateTime Day = new(2099, 3, 2);

    [SkippableFact]
    public void Fill_writes_shift_snapshots_matching_ComputeOee_and_rewrites_only_changed_rows()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        Skip.If(Scalar(f!, "SELECT OBJECT_ID('dbo.SP_PP_LineOEE_Fill');") is DBNull or null, "dbo.SP_PP_LineOEE_Fill not applied (dist/migrate_pp_lineoee_fill.sql)");
        var repo = new OeeRepository(f!);

        // 첫 교대(SortOrder) 창 — 프로시저·화면 ShiftWindow 와 같은 해석
        var code = (string?)Scalar(f!, "SELECT TOP (1) CodeValue FROM dbo.MD_CodeItem WHERE GroupCode='WORK_SHIFT' AND ISNULL(UseFlag,1)=1 ORDER BY ISNULL(SortOrder,0), CodeID;");
        var attr = (string?)Scalar(f!, "SELECT TOP (1) Attribute1 FROM dbo.MD_CodeItem WHERE GroupCode='WORK_SHIFT' AND ISNULL(UseFlag,1)=1 ORDER BY ISNULL(SortOrder,0), CodeID;");
        var parsed = ProdCalendar.TryParseWindow(attr, out var sMin, out var eMin);
        Skip.If(code is null || !parsed, "WORK_SHIFT has no usable window");
        if (eMin <= sMin) eMin += 1440;
        var ws = Day.AddMinutes(sMin);
        var we = Day.AddMinutes(eMin);
        Skip.If(eMin - sMin < 90, "first shift shorter than 90 minutes");

        try
        {
            Cleanup(f!);
            // 90분: PLAN-RUN 60분(가동 45 · 정지 15) + 계획 정지 30분, 실적 양품 10 · 불량 3 (WO 없음 → 표준 사이클 없음 → P 100% 간주)
            Exec(f!, """
                INSERT dbo.PP_LineStateLog (LineID, MinuteTS, State, PlanState, RunFlag, CreatedBy, CreatedTS)
                SELECT @L, DATEADD(MINUTE, n, @S),
                       CASE WHEN n < 45 THEN 'RUN' WHEN n < 60 THEN 'DOWN' ELSE 'STOP' END,
                       CASE WHEN n < 60 THEN 'PLAN-RUN' ELSE 'PLAN-STOP' END,
                       CASE WHEN n < 45 THEN 1 ELSE 0 END, @By, SYSDATETIME()
                FROM (SELECT TOP 90 ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS n FROM sys.all_objects) x;
                INSERT dbo.PR_ProductionResult (LineID, GoodQty, EntryAt, CreatedBy) VALUES (@L, 10, DATEADD(MINUTE, 5, @S), @By);
                INSERT dbo.PR_DefectDetail (ResultID, Qty, DetectedAt, CreatedBy) VALUES (SCOPE_IDENTITY(), 3, DATEADD(MINUTE, 6, @S), @By);
                """, ("@L", Line), ("@S", ws), ("@By", Actor));

            var r1 = repo.RunFill(Day, 0, Actor);
            Assert.Equal((1, 1, 0), (r1.Computed, r1.Inserted, r1.Updated));   // 로그가 있는 교대 하나만 행이 생긴다

            var snap     = Assert.Single(repo.GetSnapshots(Day, Day, code, Line));
            var expected = repo.ComputeOee(Line, ws, we, code)!;
            Assert.Equal((90, 30, 45, 15), (snap.LoadingMin, snap.PlannedDownMin, snap.OperatingMin, snap.UnplannedDownMin));
            Assert.Equal((expected.LoadingMin, expected.PlannedDownMin, expected.OperatingMin, expected.UnplannedDownMin),
                         (snap.LoadingMin, snap.PlannedDownMin, snap.OperatingMin, snap.UnplannedDownMin));
            Assert.Equal((13m, 10m), (snap.TotalProducedQty, snap.GoodQty));
            Assert.Equal((expected.TotalProducedQty, expected.GoodQty), (snap.TotalProducedQty, snap.GoodQty));
            // 백분율은 둘 다 소수 2자리 반올림(SQL ROUND 는 .005 를 올리고 C# Math.Round 는 은행가 반올림 — 정확히 .005 인 값만 0.01 차이)
            Assert.Equal(expected.Availability, snap.Availability, 2);
            Assert.Equal(expected.Performance,  snap.Performance,  2);
            Assert.Equal(expected.Quality,      snap.Quality,      2);
            Assert.Equal(expected.Oee,          snap.Oee,          2);
            Assert.Equal((50m, 100m, 76.92m, 38.46m), (snap.Availability, snap.Performance, snap.Quality, snap.Oee));
            Assert.Equal(Actor, snap.CreatedBy);

            // 같은 값이면 다시 쓰지 않는다
            var r2 = repo.RunFill(Day, 0, Actor);
            Assert.Equal((1, 0, 0, 1), (r2.Computed, r2.Inserted, r2.Updated, r2.Unchanged));

            // 실적이 더 들어오면 그 행만 덮어쓴다
            Exec(f!, "INSERT dbo.PR_ProductionResult (LineID, GoodQty, EntryAt, CreatedBy) VALUES (@L, 5, DATEADD(MINUTE, 7, @S), @By);",
                 ("@L", Line), ("@S", ws), ("@By", Actor));
            var r3 = repo.RunFill(Day, 0, Actor);
            Assert.Equal((0, 1), (r3.Inserted, r3.Updated));
            var snap2 = Assert.Single(repo.GetSnapshots(Day, Day, code, Line));
            Assert.Equal((snap.OeeSnapshotId, 18m, 15m), (snap2.OeeSnapshotId, snap2.TotalProducedQty, snap2.GoodQty));

            var mon = repo.GetFillMonitor();
            Assert.NotNull(mon);
            Assert.Equal("OK", mon!.Status);
            Assert.NotNull(mon.LastRunTs);
        }
        finally { Cleanup(f!); }
    }

    static void Cleanup(AMES.Data.Connection.AmesConnectionFactory f) => Exec(f, """
        DELETE dbo.PR_DefectDetail WHERE ResultID IN (SELECT ResultID FROM dbo.PR_ProductionResult WHERE LineID = @L);
        DELETE dbo.PR_ProductionResult WHERE LineID = @L;
        DELETE dbo.PP_LineOEE WHERE LineID = @L;
        DELETE dbo.PP_LineStateLog WHERE LineID = @L;
        """, ("@L", Line));
}
