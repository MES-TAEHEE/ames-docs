using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;
using static AMES.InjAgent.Tests.AmesDevDb;

namespace AMES.InjAgent.Tests;

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
    const string ItemD   = "ITEST-DLY-D";   // BOP 있음, 수동 불량 > 확정 → FINAL 0
    const string ItemE   = "ITEST-DLY-E";   // WO 가 INJ+IMG 두 단계 — IMG 쪽 수동 불량이 INJ 패널에 새면 안 됨

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

    /// <summary>WO 하나에 INJ(StepSeq 1, Line) + IMG(StepSeq 2, LINE-IMG-01) 두 라우팅 단계.</summary>
    static int AddWoWithImgStep(AmesConnectionFactory f, string itemNo, string woNumber)
    {
        return (int)Scalar(f, """
            DECLARE @Out TABLE (WoID int);
            INSERT INTO dbo.PP_WorkOrder (WoNumber, ItemNo, OrderQty, OpenQty, CompletedQty, Status, Priority, CreatedBy)
            OUTPUT INSERTED.WoID INTO @Out
            VALUES (@W, @I, 100, 100, 0, 'Released', 5, 'ITEST');
            INSERT INTO dbo.PP_WorkOrderRouting (WoID, StepSeq, ProcessCode, LineID, Status, CompletedQty, CreatedBy)
            SELECT WoID, 1, 'INJ', @L,   'Released', 0, 'ITEST' FROM @Out
            UNION ALL
            SELECT WoID, 2, 'IMG', @Img, 'Released', 0, 'ITEST' FROM @Out;
            SELECT WoID FROM @Out;
            """, ("@W", woNumber), ("@I", itemNo), ("@L", Line), ("@Img", "LINE-IMG-01"))!;
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

    static void AddDefect(AmesConnectionFactory f, int woId, int? lotId, int qty, string processCode = "INJ")
        => Exec(f, """
            INSERT INTO dbo.PR_DefectDetail (ResultID, WoID, LotID, ProcessCode, DefectCode, Qty, DetectedAt, CreatedBy)
            VALUES (0, @W, @L, @P, 'ITEST', @Q, SYSDATETIME(), 'ITEST');
            """, ("@W", woId), ("@L", (object?)lotId ?? DBNull.Value), ("@P", processCode), ("@Q", qty));

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
            AddLot(f, ItemA, Line, "NG_BLOCKED");
            var ngConfirmed = AddLot(f, ItemA, Line, "NG_CONFIRMED");
            AddDefect(f, woA, ngConfirmed, 1);      // LOT 연결 불량 — 상태에서 이미 셈, 중복 금지
            AddDefect(f, woA, null, 1);             // 수동 불량 — NG 에 더하고 FINAL 에서 뺌
            AddLot(f, ItemA, Line, "CONFIRMED", dayOffset: -1);   // 어제 → 제외
            AddLot(f, ItemA, "LINE-INJ-02", "CONFIRMED");         // 다른 라인 → 제외
            AddPlan(f, woA, Line, 0, 60);
            AddPlan(f, woA, Line, 0, 40);
            AddPlan(f, woA, Line, -1, 999);                       // 어제 일정 → 제외
            AddPlan(f, woA, "LINE-INJ-02", 0, 999);               // 다른 라인 → 제외
            AddPlan(f, woA, Line, 0, 999, entryType: "PM");       // PM 밴드(WoID NULL) → PLAN 집계 제외

            var row = new InjLotRepository(f).GetDailyItemSummary(Line, Station).Single(x => x.ItemNo == ItemA);

            Assert.Equal(100m, row.PlanQty);
            Assert.Equal(9,    row.InputQty);
            Assert.Equal(3,    row.NgQty);        // NG LOT 2 + 수동 1
            Assert.Equal(3,    row.FinalQty);     // CONFIRMED 4 − 수동 1
            Assert.Equal(3,    row.PendingQty);
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

            var rows = new InjLotRepository(f).GetDailyItemSummary(Line, Station)
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

    [SkippableFact]
    public void Summary_clamps_final_at_zero_when_manual_defects_exceed_confirmed()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var (_, _, woD) = Seed(f);
        try
        {
            AddLot(f, ItemD, Line, "CONFIRMED");
            AddDefect(f, woD, null, 3);

            var d = new InjLotRepository(f).GetDailyItemSummary(Line, Station).Single(x => x.ItemNo == ItemD);

            Assert.Equal(1, d.InputQty);
            Assert.Equal(3, d.NgQty);
            Assert.Equal(0, d.FinalQty);
        }
        finally { Cleanup(f); }
    }

    /// <summary>
    /// WO 가 INJ(1단계, LINE-INJ-01)·IMG(2단계, LINE-IMG-01) 두 라우팅 단계를 갖고, 같은 WO 에
    /// ProcessCode='IMG' 수동 불량이 있으면 그 수량이 INJ 패널의 NG/FINAL 에 새면 안 된다.
    /// </summary>
    [SkippableFact]
    public void Summary_counts_only_INJ_process_code_manual_defects()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f);
        try
        {
            var woE = AddWoWithImgStep(f, ItemE, "ITEST-DLY-WO-E");
            AddLot(f, ItemE, Line, "CONFIRMED");
            AddLot(f, ItemE, Line, "CONFIRMED");
            AddDefect(f, woE, null, 1, processCode: "INJ");
            AddDefect(f, woE, null, 5, processCode: "IMG");   // 같은 WO 의 IMG 단계 수동 불량 — INJ 패널에 새면 안 됨

            var row = new InjLotRepository(f).GetDailyItemSummary(Line, Station).Single(x => x.ItemNo == ItemE);

            Assert.Equal(2, row.InputQty);
            Assert.Equal(1, row.NgQty);        // INJ 수동 불량 1건만
            Assert.Equal(1, row.FinalQty);     // 확정 2 − INJ 수동 1 (IMG 5 는 빼지 않음)
        }
        finally { Cleanup(f); }
    }
}
