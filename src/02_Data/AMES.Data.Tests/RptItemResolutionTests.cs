using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 생산 보고서 품번(10-13 사용자 결정) — 실적 = LOT 품번 → WO 단계 품번 → WO 품번, 계획·일정 준수 = WO 단계 품번
[Collection(nameof(RptScheduleAdherenceTests))]
public class RptItemResolutionTests
{
    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — creates and removes test WO, routing, slot, lots and results";
    const string Actor = "itest-item";
    const string Line  = "ZT-ITEM-LINE";
    const string Finished = "12490-04101";       // WO 헤더 품번(완제품 역할)
    const string Core     = "21326B1-PIDHA";     // INJ 단계·LOT 품번(코어 역할)
    const string NoWoItem = "21328A0-PIDHANNB";  // WO 없이 만든 LOT 품번

    static readonly DateTime Day = new(2031, 4, 7);

    static void Cleanup(Connection.AmesConnectionFactory f) => Exec(f, """
        DELETE dbo.PR_DefectDetail      WHERE CreatedBy = @A;
        DELETE dbo.PR_ProductionResult  WHERE CreatedBy = @A;
        DELETE dbo.tbl_Lot              WHERE CreatedBy = @A;
        DELETE dbo.PP_LineSchedule      WHERE CreatedBy = @A;
        DELETE dbo.PP_WorkOrderRouting  WHERE CreatedBy = @A;
        DELETE dbo.PP_WorkOrder         WHERE CreatedBy = @A;
        """, ("@A", Actor));

    static int Lot(Connection.AmesConnectionFactory f, string item, int? wo) => Convert.ToInt32(Scalar(f, """
        INSERT dbo.tbl_Lot (ItemNo, WoID, LineID, ProcessCode, CreatedBy) VALUES (@I, @W, @L, 'INJ', @A);
        SELECT CAST(SCOPE_IDENTITY() AS int);
        """, ("@I", item), ("@W", (object?)wo ?? DBNull.Value), ("@L", Line), ("@A", Actor)));

    static void Result(Connection.AmesConnectionFactory f, int? wo, int lot, int good) => Exec(f, """
        INSERT dbo.PR_ProductionResult (WoID, LotID, LineID, ProcessCode, GoodQty, DefectFlag, EntryAt, ProdDate, CreatedBy)
        VALUES (@W, @Lot, @L, 'INJ', @G, 0, @E, @D, @A);
        """, ("@W", (object?)wo ?? DBNull.Value), ("@Lot", lot), ("@L", Line), ("@G", good), ("@E", Day.AddHours(10)), ("@D", Day), ("@A", Actor));

    [SkippableFact]
    public void Reports_use_lot_items_for_actuals_and_step_items_for_plans()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        var rpt = new RptRepository(f!);
        Cleanup(f!);
        try
        {
            // WO 헤더는 완제품, 이 라인 단계는 코어 — APS·PP-003 이 사출 단계에 코어를 스냅샷하는 모양
            var wo = Convert.ToInt32(Scalar(f!, """
                INSERT dbo.PP_WorkOrder (WoNumber, ItemNo, Status, CreatedBy) VALUES ('ZT-ITEM-WO', @I, 'Released', @A);
                SELECT CAST(SCOPE_IDENTITY() AS int);
                """, ("@I", Finished), ("@A", Actor)));
            Exec(f!, "INSERT dbo.PP_WorkOrderRouting (WoID, StepSeq, ProcessCode, LineID, ItemNo, Status, CreatedBy) VALUES (@W, 10, 'INJ', @L, @I, 'Released', @A)",
                ("@W", wo), ("@L", Line), ("@I", Core), ("@A", Actor));
            Exec(f!, "INSERT dbo.PP_LineSchedule (LineID, ScheduleDate, WoID, PlannedQty, Status, EntryType, CreatedBy) VALUES (@L, @D, @W, 10, 'PUBLISHED', 'WO', @A)",
                ("@L", Line), ("@D", Day), ("@W", wo), ("@A", Actor));

            Result(f!, wo, Lot(f!, Core, wo), 1);                // WO 있는 사출 실적 → 코어
            Result(f!, null, Lot(f!, NoWoItem, null), 1);        // WO 없는 실적 → LOT 품번
            var rawLot = Lot(f!, NoWoItem, null);                // WO 없는 확정 전 LOT 불량(실적 행 없음)
            Exec(f!, "INSERT dbo.PR_DefectDetail (ResultID, WoID, LotID, ProcessCode, Qty, DetectedAt, CreatedBy) VALUES (NULL, NULL, @Lot, 'INJ', 1, @T, @A)",
                ("@Lot", rawLot), ("@T", Day.AddHours(11)), ("@A", Actor));

            var prod = rpt.ListProductionByHour(Day, Line);
            Assert.Equal(new[] { Core, NoWoItem }, prod.Select(r => r.ItemNo).Distinct().OrderBy(x => x).ToArray());
            Assert.All(prod, r => Assert.False(string.IsNullOrEmpty(r.ItemName)));
            Assert.Equal(1, prod.Where(r => r.ItemNo == NoWoItem).Sum(r => r.DefectQty));
            Assert.DoesNotContain(prod, r => r.ItemNo == Finished);

            var plan = Assert.Single(rpt.ListPlanByDate(Day, Line));
            Assert.Equal(Core, plan.ItemNo);                     // 계획도 코어 → 같은 행에 모인다

            var items = rpt.AdhocQuery("PROD", "Item", Day, Day).ToDictionary(r => r.Key);
            Assert.True(items.ContainsKey(Core) && items.ContainsKey(NoWoItem));
            Assert.Equal(2m, items[NoWoItem].M["Production"]);   // 실적 1 + 확정 전 불량 1

            var sched = Assert.Single(rpt.ListScheduleWo(Day, Day, Line));
            Assert.Equal(Core, sched.ItemNo);
        }
        finally { Cleanup(f!); }
    }
}
