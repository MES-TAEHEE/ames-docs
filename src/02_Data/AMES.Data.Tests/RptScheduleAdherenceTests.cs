using AMES.Data.Repositories;
using AMES.Data.Services;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 오늘 전기일 실적을 넣는 테스트가 있어 다른 보고서 합계 테스트와 동시에 돌면 그쪽 합계가 흔들린다 — 단독 실행
[CollectionDefinition(nameof(RptScheduleAdherenceTests), DisableParallelization = true)]
public class RptScheduleAdherenceCollection { }

// RPT 일정 준수 — 실적은 전기일, 먼저 계획된 슬롯부터 채우고, 실적일 = 누적 실적이 목표에 닿은 전기일(10-13 사용자 결정)
[Collection(nameof(RptScheduleAdherenceTests))]
public class RptScheduleAdherenceTests
{
    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — creates and removes test WOs, slots and results";
    const string Actor = "itest-sa";
    const string Line  = "ZT-SA-LINE";

    static readonly DateTime D1 = new(2031, 3, 10), D2 = D1.AddDays(1), D3 = D1.AddDays(2);

    static void Cleanup(Connection.AmesConnectionFactory f) => Exec(f, """
        DELETE dbo.PR_ProductionResult WHERE CreatedBy = @A;
        DELETE dbo.PP_LineSchedule     WHERE CreatedBy = @A;
        DELETE dbo.PP_WorkOrder        WHERE CreatedBy = @A;
        """, ("@A", Actor));

    static int Wo(Connection.AmesConnectionFactory f, string no) => Convert.ToInt32(Scalar(f, """
        INSERT dbo.PP_WorkOrder (WoNumber, ItemNo, Status, CreatedBy) VALUES (@N, '12490-04101', 'Released', @A);
        SELECT CAST(SCOPE_IDENTITY() AS int);
        """, ("@N", no), ("@A", Actor)));

    static void Slot(Connection.AmesConnectionFactory f, int wo, DateTime day, int qty) => Exec(f, """
        INSERT dbo.PP_LineSchedule (LineID, ScheduleDate, WoID, PlannedQty, Status, EntryType, CreatedBy)
        VALUES (@L, @D, @W, @Q, 'DRAFT', 'WO', @A);
        """, ("@L", Line), ("@D", day), ("@W", wo), ("@Q", qty), ("@A", Actor));

    // 실적: 전기일 prodDate, 입력 시각 entryAt(자정 넘김 재현용)
    static void Result(Connection.AmesConnectionFactory f, int wo, DateTime prodDate, DateTime entryAt, int good) => Exec(f, """
        INSERT dbo.PR_ProductionResult (WoID, LineID, ProcessCode, GoodQty, DefectFlag, EntryAt, ProdDate, CreatedBy)
        VALUES (@W, @L, 'INJ', @G, 0, @E, @P, @A);
        """, ("@W", wo), ("@L", Line), ("@G", good), ("@E", entryAt), ("@P", prodDate), ("@A", Actor));

    static Connection.AmesConnectionFactory? WriteFactory()
        => Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null ? null : TryFactory();

    [SkippableFact]
    public void Actual_date_is_the_production_day_the_cumulative_plan_was_met()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        var rpt = new RptRepository(f!);
        Cleanup(f!);
        try
        {
            // WO-A: D1 100 + D3 100. D1 에 100, D3 B교대에 100(마지막 20 은 D4 00:30 — 달력으로는 다음 날)
            var a = Wo(f!, "ZT-SA-A");
            Slot(f!, a, D1, 100); Slot(f!, a, D3, 100);
            Result(f!, a, D1, D1.AddHours(10), 100);
            Result(f!, a, D3, D3.AddHours(18), 80);
            Result(f!, a, D3, D3.AddDays(1).AddMinutes(30), 20);

            // WO-B: D1 50. D1 에 30, D2 에 30 — 하루 늦게 달성
            var b = Wo(f!, "ZT-SA-B");
            Slot(f!, b, D1, 50);
            Result(f!, b, D1, D1.AddHours(9), 30);
            Result(f!, b, D2, D2.AddHours(9), 30);

            ScheduleRow(rpt, D3, D3, a, plan: 100, actual: 100, actualDate: D3);   // 자정 넘긴 실적도 D3 — 전에는 D4(지연 +1)
            ScheduleRow(rpt, D1, D1, a, plan: 100, actual: 100, actualDate: D1);   // 뒤 슬롯 생산은 앞 슬롯 완료일·실적에 안 섞인다
            ScheduleRow(rpt, D1, D3, a, plan: 200, actual: 200, actualDate: D3);
            ScheduleRow(rpt, D1, D1, b, plan: 50,  actual: 60,  actualDate: D2);   // 뒤 슬롯이 없으면 초과분도 이 기간 실적
            ScheduleRow(rpt, D2, D3, b, plan: null, actual: null, actualDate: null); // 기간 안에 슬롯 없음 → 행 없음
        }
        finally { Cleanup(f!); }
    }

    // 앞 슬롯이 덜 찼으면 뒤 날짜 생산이 앞 슬롯부터 채운다(선입선출)
    [SkippableFact]
    public void Earlier_slots_are_filled_first()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        var rpt = new RptRepository(f!);
        Cleanup(f!);
        try
        {
            var c = Wo(f!, "ZT-SA-C");
            Slot(f!, c, D1, 100); Slot(f!, c, D2, 100);
            Result(f!, c, D1, D1.AddHours(10), 60);
            Result(f!, c, D2, D2.AddHours(10), 100);

            ScheduleRow(rpt, D1, D1, c, plan: 100, actual: 100, actualDate: D2);   // D1 슬롯은 D2 에야 채워졌다
            ScheduleRow(rpt, D2, D2, c, plan: 100, actual: 60,  actualDate: D2);   // D2 슬롯은 60 만 채워졌다
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void Line_day_summary_uses_production_days()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        var rpt = new RptRepository(f!);
        Cleanup(f!);
        try
        {
            var today = rpt.ProdToday();
            var d = Wo(f!, "ZT-SA-D");
            Slot(f!, d, today, 10);
            Result(f!, d, today, today.AddDays(1).AddMinutes(30), 7);   // 자정 넘긴 B교대 실적 → 그 근무일

            var row = Assert.Single(rpt.ListScheduleAdherence(3), r => r.LineId == Line);
            Assert.Equal(today, row.Day);
            Assert.Equal(10m, row.PlannedQty);
            Assert.Equal(7m, row.ProducedQty);
            Assert.Equal(70m, row.AdherencePct);
        }
        finally { Cleanup(f!); }
    }

    static void ScheduleRow(RptRepository rpt, DateTime from, DateTime to, int wo, decimal? plan, decimal? actual, DateTime? actualDate)
    {
        var row = rpt.ListScheduleWo(from, to, Line).SingleOrDefault(r => r.WoId == wo);
        if (plan is null) { Assert.Null(row); return; }
        Assert.NotNull(row);
        Assert.Equal(plan, row!.PlanQty);
        Assert.Equal(actual, row.ActualQty);
        Assert.Equal(actualDate, row.ActualDate);
    }
}
