using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

/// <summary>
/// INJ-MAIN 좌측 품번 패널 집계. AMES_DEV 통합 테스트, DB 미기동 시 skip.
/// 시드는 전부 ITEST-DLY- 접두 — WorkOrderRepositoryTests(ITEST-WO-*) 와 병렬 실행돼도 안 겹친다.
/// </summary>
public class InjDailyItemSummaryTests
{
    const string Line    = "LINE-INJ-01";
    const string Station = "ST-INJ-01";
    const string ItemA   = "ITEST-DLY-A";   // BOP 있음, 실적·일정 있음
    const string ItemB   = "ITEST-DLY-B";   // BOP 없음, 실적 있음 → InBop=false
    const string ItemC   = "ITEST-DLY-C";   // BOP 있음, 아무것도 없음 → 0 행
    const string ItemD   = "ITEST-DLY-D";   // BOP 있음, WO 있음, LOT 없음

    static void Cleanup(AmesConnectionFactory f)
    {
        Exec(f, """
            DELETE d FROM dbo.PR_DefectDetail d JOIN dbo.PP_WorkOrder w ON w.WoID = d.WoID WHERE w.ItemNo LIKE 'ITEST-DLY-%';
            DELETE d FROM dbo.PR_DefectDetail d JOIN dbo.tbl_Lot l ON l.LotID = d.LotID WHERE l.ItemNo LIKE 'ITEST-DLY-%';
            DELETE e FROM dbo.PR_InjLot e JOIN dbo.tbl_Lot l ON l.LotID = e.LotID WHERE l.ItemNo LIKE 'ITEST-DLY-%';
            DELETE FROM dbo.tbl_Lot WHERE ItemNo LIKE 'ITEST-DLY-%';
            DELETE s FROM dbo.PP_LineSchedule s JOIN dbo.PP_WorkOrder w ON w.WoID = s.WoID WHERE w.ItemNo LIKE 'ITEST-DLY-%';
            DELETE r FROM dbo.PP_WorkOrderRouting r JOIN dbo.PP_WorkOrder w ON w.WoID = r.WoID WHERE w.ItemNo LIKE 'ITEST-DLY-%';
            DELETE FROM dbo.PP_WorkOrder WHERE ItemNo LIKE 'ITEST-DLY-%';
            DELETE FROM dbo.MD_Bop  WHERE ItemNo LIKE 'ITEST-DLY-%';
            DELETE FROM dbo.MD_Item WHERE ItemNo LIKE 'ITEST-DLY-%';
            """);
    }

    /// <summary>품목 4개, A/C/D 는 ST-INJ-01 BOP. A·B·D 에 LINE-INJ-01 INJ 단계가 Released 인 WO 하나씩.</summary>
    static (int WoA, int WoB, int WoD) Seed(AmesConnectionFactory f)
    {
        Cleanup(f);
        Exec(f, """
            INSERT INTO dbo.MD_Item (ItemNo, ItemName, ActiveFlag, CreatedBy) VALUES
              (@A, N'ITEST daily A', 1, 'ITEST'), (@B, N'ITEST daily B', 1, 'ITEST'),
              (@C, N'ITEST daily C', 1, 'ITEST'), (@D, N'ITEST daily D', 1, 'ITEST');
            INSERT INTO dbo.MD_Bop (BOPID, ItemNo, RoutingType, StepSeq, StationCode, ActiveFlag, CreatedBy) VALUES
              ('ITEST-DLY-BOP-A', @A, 'A', 10, @St, 1, 'ITEST'),
              ('ITEST-DLY-BOP-C', @C, 'A', 10, @St, 1, 'ITEST'),
              ('ITEST-DLY-BOP-D', @D, 'A', 10, @St, 1, 'ITEST');
            """, ("@A", ItemA), ("@B", ItemB), ("@C", ItemC), ("@D", ItemD), ("@St", Station));
        return (AddWo(f, ItemA, "ITEST-DLY-WO-A"), AddWo(f, ItemB, "ITEST-DLY-WO-B"), AddWo(f, ItemD, "ITEST-DLY-WO-D"));
    }

    static int AddWo(AmesConnectionFactory f, string itemNo, string woNumber)
    {
        return (int)Scalar(f, """
            DECLARE @Out TABLE (WoID int);
            INSERT INTO dbo.PP_WorkOrder (WoNumber, ItemNo, OrderQty, OpenQty, CompletedQty, Status, Priority, CreatedBy)
            OUTPUT INSERTED.WoID INTO @Out
            VALUES (@W, @I, 100, 100, 0, 'Released', 5, 'ITEST');
            INSERT INTO dbo.PP_WorkOrderRouting (WoID, StepSeq, ProcessCode, LineID, Status, CompletedQty, CreatedBy)
            SELECT WoID, 1, 'INJ', @L, 'Released', 0, 'ITEST' FROM @Out;
            SELECT WoID FROM @Out;
            """, ("@W", woNumber), ("@I", itemNo), ("@L", Line))!;
    }

    /// <summary>원천 LOT 1건. dayOffset 으로 생성일을 어제(-1)로 밀 수 있다.</summary>
    static int AddLot(AmesConnectionFactory f, string itemNo, string lineId, string status, int dayOffset = 0)
    {
        var code = ("ITEST-DLY-" + Guid.NewGuid().ToString("N"))[..40];
        return (int)Scalar(f, """
            DECLARE @Ts datetime2 = DATEADD(day, @D, SYSDATETIME());
            DECLARE @Out TABLE (LotID int);
            INSERT INTO dbo.tbl_Lot (LotCode, ItemNo, LineID, ProcessCode, BatchSize, RemainingQty, ProducedAt, Status, QualityFlag, CreatedBy, CreatedTS)
            OUTPUT INSERTED.LotID INTO @Out
            VALUES (@Code, @Item, @Line, 'INJ', 1, 1, @Ts, 'RAW', 'PENDING', 'ITEST', @Ts);
            INSERT INTO dbo.PR_InjLot (LotID, ConfirmStatus, CreatedBy, CreatedTS)
            SELECT LotID, @Status, 'ITEST', @Ts FROM @Out;
            SELECT LotID FROM @Out;
            """, ("@Code", code), ("@Item", itemNo), ("@Line", lineId), ("@Status", status), ("@D", dayOffset))!;
    }

    /// <summary>entryType 'PM' 이면 WoID 는 항상 NULL (PM 밴드는 WO 에 안 걸림) 이고 Title 이 채워진다.</summary>
    static void AddPlan(AmesConnectionFactory f, int woId, string lineId, int dayOffset, decimal qty, string entryType = "WO")
        => Exec(f, """
            INSERT INTO dbo.PP_LineSchedule (LineID, ScheduleDate, WoID, PlannedQty, EntryType, Title, Status, CreatedBy)
            VALUES (@L, DATEADD(day, @D, CAST(GETDATE() AS date)), @W, @Q, @E, @T, 'Published', 'ITEST');
            """, ("@L", lineId), ("@D", dayOffset), ("@Q", qty), ("@E", entryType),
                 ("@W", entryType == "PM" ? (object)DBNull.Value : woId),
                 ("@T", entryType == "PM" ? (object)"ITEST PM" : DBNull.Value));

    [SkippableFact]
    public void Summary_counts_today_lots_by_status_and_keeps_identity()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var (woA, _, _) = Seed(f);
        try
        {
            for (var i = 0; i < 3; i++) AddLot(f, ItemA, Line, "RAW");
            for (var i = 0; i < 4; i++) AddLot(f, ItemA, Line, "CONFIRMED");
            AddLot(f, ItemA, Line, "NG_BLOCKED");                 // 로봇 NG 차단
            AddLot(f, ItemA, Line, "DEFECT");                     // 불량 등록 → 재작업 대기
            AddLot(f, ItemA, Line, "SCRAPPED");                   // 폐기 판정
            AddLot(f, ItemA, Line, "CONFIRMED", dayOffset: -1);   // 어제 → 제외
            AddLot(f, ItemA, "LINE-INJ-02", "CONFIRMED");         // 다른 라인 → 제외
            AddPlan(f, woA, Line, 0, 60);
            AddPlan(f, woA, Line, 0, 40);
            AddPlan(f, woA, Line, -1, 999);                       // 어제 일정 → 제외
            AddPlan(f, woA, "LINE-INJ-02", 0, 999);               // 다른 라인 → 제외
            AddPlan(f, woA, Line, 0, 999, entryType: "PM");       // PM 밴드(WoID NULL) → PLAN 집계 제외

            var row = new InjLotRepository(f).GetDailyItemSummary(Line, Station, DateTime.Today).Single(x => x.ItemNo == ItemA);

            Assert.Equal(100m, row.PlanQty);
            Assert.Equal(10,   row.InputQty);     // 오늘 이 라인 LOT 전부
            Assert.Equal(3,    row.NgQty);        // NG_BLOCKED + DEFECT + SCRAPPED
            Assert.Equal(4,    row.FinalQty);     // CONFIRMED
            Assert.Equal(3,    row.PendingQty);   // RAW
            Assert.Equal(row.InputQty, row.FinalQty + row.NgQty + row.PendingQty);
            Assert.True(row.InBop);
            Assert.True(row.HasOpenWo);
            Assert.Equal("ITEST daily A", row.ItemName);
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Summary_lists_bop_items_with_zero_and_appends_non_bop_items_last()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f);
        try
        {
            AddLot(f, ItemB, Line, "RAW");
            AddLot(f, ItemB, Line, "RAW");

            var rows = new InjLotRepository(f).GetDailyItemSummary(Line, Station, DateTime.Today)
                       .Where(x => x.ItemNo.StartsWith("ITEST-DLY-")).ToList();

            var c = rows.Single(x => x.ItemNo == ItemC);
            Assert.True(c.InBop);
            Assert.Equal((0m, 0, 0, 0, 0), (c.PlanQty, c.InputQty, c.NgQty, c.FinalQty, c.PendingQty));
            Assert.False(c.HasOpenWo);

            var b = rows.Single(x => x.ItemNo == ItemB);
            Assert.False(b.InBop);
            Assert.Equal(2, b.InputQty);
            Assert.Equal(2, b.PendingQty);
            Assert.True(b.HasOpenWo);

            Assert.True(rows.IndexOf(b) > rows.IndexOf(c));                         // 미등록은 뒤
            Assert.Equal(rows.Where(x => x.InBop).Select(x => x.ItemNo).OrderBy(x => x),
                         rows.Where(x => x.InBop).Select(x => x.ItemNo));             // BOP 품번은 ItemNo 순
        }
        finally { Cleanup(f); }
    }
}
