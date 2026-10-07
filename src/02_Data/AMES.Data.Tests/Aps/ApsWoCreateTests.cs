using System.Text.Json;
using AMES.Data.Aps;
using AMES.Data.Aps.Contracts;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests.Aps;

/// <summary>
/// PP-APS 「WO 생성」 — 저장된 실행의 같은 품번 사출 계획 행 → WO(FIFO SoID) + INJ 고정 슬롯(주/야) + 형제 0분 슬롯 + MC + 완제품 단계 슬롯.
/// AMES_DEV 통합 테스트, DB 미기동 시 skip. 라인/스테이션은 개발 시드(LINE-INJ-01, LINE-IMG-01, ST-INJ-01, ST-IMG-01)에 의존하고
/// 라우팅 A 가 INJ→IMG 가 아니면 skip 한다(완제품 단계 판정이 달라진다).
/// 배치 주간은 +400일 뒤 월~금(D0~D4) — (라인, 일자) 에 ITEST 패턴 placeholder 행을 두어 패턴 해석을 고정한다.
/// 패턴: A 08:00-12:00 · 12:00-13:00 휴게 · 13:00-16:00 / B 16:00-24:00 → 주간 480-720·780-960, 야간 960-1440, DayStart 480.
/// 공유 개발 DB 의 LINE-INJ-01 에는 실제 금형 슬롯이 있어 LineLastMoldBefore 가 그 금형을 돌려준다 — D0 전날(일요일)에 ITEST 금형 슬롯을
/// 두어 "직전 금형 = ITEST-APS-M1" 을 고정한다(MC 테스트는 같은 날 앞 슬롯으로 직전 금형을 따로 만든다).
/// </summary>
// PlanScheduleTests·MoldPlanningTests 와 같은 LINE-INJ-01/LINE-IMG-01 + 같은 +400일 주(D0)를 쓰므로 같은 컬렉션으로 묶어 직렬화한다.
[Collection("AMES_DEV plan week")]
public class ApsWoCreateTests
{
    const string ItemA = "ITEST-APS-RTA", ItemB = "ITEST-APS-RTB", ItemC = "ITEST-APS-RTC";   // C 는 금형 없음
    // BOM 규칙(스펙 2026-10-05): 완제품 P1(→ K ×1, K2 ×2) · P2(→ K ×1), 사출 자식 K(금형 M2, 60 UPH) · K2(금형 M3, 120 UPH) — 둘 다 LINE-INJ-01
    const string P1 = "ITEST-APS-RTP1", P2 = "ITEST-APS-RTP2", K = "ITEST-APS-RTK", K2 = "ITEST-APS-RTK2";
    const string Pattern = "ITEST-APS-PAT", ClosedPattern = "ITEST-APS-PATX", Mold = "ITEST-APS-M1", Mold2 = "ITEST-APS-M2", Mold3 = "ITEST-APS-M3";
    const string LineInj = "LINE-INJ-01", LineImg = "LINE-IMG-01", LinePnt = "LINE-PNT-01";
    const string Actor = "ITEST-APS";
    static readonly DateTime D0 = NextMonday(DateTime.Today.AddDays(400));
    static readonly DateTime D1 = D0.AddDays(1);
    static readonly DateOnly D0d = DateOnly.FromDateTime(D0);
    static readonly DateTime D2 = D0.AddDays(2);
    static readonly DateTime D4 = D0.AddDays(4);
    static readonly DateTime Pin = D0.AddDays(-1);   // 직전 금형 고정용 일요일
    static readonly DateTime[] Week = Enumerable.Range(0, 5).Select(i => D0.AddDays(i)).ToArray();
    static int _offset = 1;   // Seed 가 읽는 LINE-INJ-01 의 유효 선행일(MD_ApsLineStage)

    static DateTime NextMonday(DateTime d) => d.Date.AddDays(((int)DayOfWeek.Monday - (int)d.DayOfWeek + 7) % 7);

    // +400일 주에는 SYS_FactoryCalendar 행이 없다고 가정 — WorkdayCalendar 는 토·일만 휴일로 본다
    static DateTime PlusWorkdays(DateTime d, int n)
    {
        while (n > 0) { d = d.AddDays(1); if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) n--; }
        return d;
    }

    static string RoutingA(AmesConnectionFactory f) => Scalar(f, """
        SELECT STRING_AGG(ProcessCode, ',') WITHIN GROUP (ORDER BY StepSeq)
        FROM   dbo.MD_RoutingStep WHERE RoutingType = 'A' AND ISNULL(ActiveFlag,1) = 1;
        """) as string ?? "";

    static AmesConnectionFactory Ready()
    {
        var f = TryFactory();
        Skip.If(f is null, "AMES_DEV unreachable");
        Skip.If(RoutingA(f!) != "INJ,IMG", "dev routing A is not INJ→IMG");
        return f!;
    }

    static void Seed(AmesConnectionFactory f)
    {
        Cleanup(f);
        // 패턴은 전역(LineID NULL)이지만 유효 기간을 테스트 주로 묶어 공유 DB 의 다른 라인·오늘 패턴 해석에 끼어들지 않게 한다
        Exec(f, """
            INSERT INTO dbo.MD_Item (ItemNo, ItemName, RoutingType, ActiveFlag, CreatedBy)
            VALUES (@A, N'ITEST APS A', 'A', 1, @By), (@B, N'ITEST APS B', 'A', 1, @By), (@C, N'ITEST APS C', 'A', 1, @By);
            INSERT INTO dbo.MD_Bop (BOPID, ItemNo, RoutingType, StepSeq, StationCode, StdCycleTime, ActiveFlag, CreatedBy)
            VALUES ('ITEST-APS-BOP-A10', @A, 'A', 10, 'ST-INJ-01', 6,  1, @By), ('ITEST-APS-BOP-A20', @A, 'A', 20, 'ST-IMG-01', 12, 1, @By),
                   ('ITEST-APS-BOP-B10', @B, 'A', 10, 'ST-INJ-01', 6,  1, @By), ('ITEST-APS-BOP-B20', @B, 'A', 20, 'ST-IMG-01', 12, 1, @By),
                   ('ITEST-APS-BOP-C10', @C, 'A', 10, 'ST-INJ-01', 6,  1, @By), ('ITEST-APS-BOP-C20', @C, 'A', 20, 'ST-IMG-01', 12, 1, @By);
            INSERT INTO dbo.MD_LineTimePattern (PatternID, LineID, PatternName, Status, EffectiveFrom, EffectiveTo, CreatedBy)
            VALUES (@P, NULL, N'ITEST aps pattern', 'ACTIVE', @Pin, @D4, @By),
                   (@PX, NULL, N'ITEST aps closed', 'INACTIVE', @Pin, @D4, @By);
            INSERT INTO dbo.MD_LineTimeSegment (SegmentID, PatternID, SeqNo, StartMin, EndMin, SegmentState, ShiftCode, CreatedBy)
            VALUES ('ITEST-APS-SEG-1', @P, 1, 480,  720,  'OPERATING', 'A', @By),
                   ('ITEST-APS-SEG-2', @P, 2, 720,  780,  'BREAK',     'A', @By),
                   ('ITEST-APS-SEG-3', @P, 3, 780,  960,  'OPERATING', 'A', @By),
                   ('ITEST-APS-SEG-4', @P, 4, 960,  1440, 'OPERATING', 'B', @By),
                   ('ITEST-APS-SEG-X', @PX, 1, 480, 960,  'BREAK',     'A', @By);
            INSERT INTO dbo.MD_Mold (MoldID, MoldName, Status, CavityCount, MoldChangeMin, CreatedBy)
            VALUES (@M, N'ITEST aps mold', 'AVAILABLE', 2, 20, @By);
            INSERT INTO dbo.MD_MoldLine (LineCode, MoldID, UPH, PrepTime, CreatedBy)
            VALUES (@LI, @M, 120, 15, @By);   -- 금형 M 은 캐비티 2 에 품번 A·B 가 같이 찍힌다 → 유효 UPH = 120 × 1 ÷ 2 = 60(ApsUph.Effective, 2026-10-07 UPH 통일)
            INSERT INTO dbo.MD_MoldItem (MoldID, ItemNo, Color, CavitySeq, CavityPos, CavityCount, MoldCategory, ActiveFlag, CreatedBy)
            VALUES (@M, @A, 'CBK', 1, 'LH', 2, 'INJECTION', 1, @By),
                   (@M, @B, 'CBK', 2, 'RH', 2, 'INJECTION', 1, @By);
            IF NOT EXISTS (SELECT 1 FROM dbo.MD_ApsLineStage WHERE LineID = @LI)
                INSERT INTO dbo.MD_ApsLineStage (LineID, OffsetDays, UseStock, CreatedBy) VALUES (@LI, 1, 1, @By);
            """, ("@A", ItemA), ("@B", ItemB), ("@C", ItemC), ("@P", Pattern), ("@PX", ClosedPattern), ("@M", Mold),
                 ("@LI", LineInj), ("@By", Actor), ("@Pin", Pin), ("@D4", D4));
        _offset = Convert.ToInt32(Scalar(f, "SELECT OffsetDays FROM dbo.MD_ApsLineStage WHERE LineID = @LI;", ("@LI", LineInj)));
        ApsPatternConfig.Apply(f, null, (LineInj, Pattern));   // APS 는 PP_LineSchedule 저장 패턴이 아니라 이 지정을 읽는다(2026-10-06) — 기본 패턴은 비워 둔다
        foreach (var d in Week)
            Exec(f, """
                INSERT INTO dbo.PP_LineSchedule (LineID, ScheduleDate, PatternID, EntryType, PlannedQty, Status, CreatedBy)
                VALUES (@LI, @D, @P, 'WO', 0, 'DRAFT', @By), (@LM, @D, @P, 'WO', 0, 'DRAFT', @By);
                """, ("@P", Pattern), ("@LI", LineInj), ("@LM", LineImg), ("@D", d), ("@By", Actor));
        PreexistingSlot(f, Pin, Mold, 480, 481);
    }

    static void Cleanup(AmesConnectionFactory f)
    {
        ApsPatternConfig.Restore(f);   // 테스트 패턴을 지우기 전에 — MD_ApsLineStage.PatternID FK
        Exec(f, """
            DELETE s FROM dbo.PP_LineSchedule s JOIN dbo.PP_WorkOrder w ON w.WoID = s.WoID WHERE w.ItemNo IN (@A, @B, @C, @P1, @P2);
            DELETE s FROM dbo.PP_LineSchedule s JOIN dbo.PP_WorkOrder w ON w.WoID = s.RefID AND s.RefType = 'WO' WHERE w.ItemNo IN (@A, @B, @C, @P1, @P2);
            DELETE FROM dbo.PP_LineSchedule WHERE CreatedBy = @By AND ScheduleDate BETWEEN @Pin AND @D4;
            DELETE x FROM dbo.PP_ApsRunWo    x JOIN dbo.PP_ApsRun r ON r.RunID = x.RunID WHERE r.CreatedBy = @By;
            DELETE l FROM dbo.PP_ApsPlanLine l JOIN dbo.PP_ApsRun r ON r.RunID = l.RunID WHERE r.CreatedBy = @By;
            DELETE FROM dbo.PP_ApsRun WHERE CreatedBy = @By;
            DELETE r FROM dbo.PP_WorkOrderRouting r JOIN dbo.PP_WorkOrder w ON w.WoID = r.WoID WHERE w.ItemNo IN (@A, @B, @C, @P1, @P2);
            DELETE FROM dbo.PP_WorkOrder     WHERE ItemNo IN (@A, @B, @C, @P1, @P2);
            DELETE FROM dbo.PP_CustomerOrder WHERE ItemNo IN (@A, @B, @C, @P1, @P2);
            DELETE FROM dbo.MD_Bop           WHERE ItemNo IN (@A, @B, @C, @P1, @P2);
            DELETE FROM dbo.MD_MoldItem WHERE MoldID IN (@M, @M2, @M3);
            DELETE FROM dbo.MD_MoldLine WHERE MoldID IN (@M, @M2, @M3);
            DELETE FROM dbo.MD_Mold     WHERE MoldID IN (@M, @M2, @M3);
            DELETE FROM dbo.MD_Item     WHERE ItemNo IN (@A, @B, @C, @P1, @P2, @K, @K2);
            DELETE FROM dbo.MD_LineTimeSegment WHERE PatternID IN (@P, @PX);
            DELETE FROM dbo.MD_LineTimePattern WHERE PatternID IN (@P, @PX);
            DELETE FROM dbo.MD_ApsLineStage WHERE CreatedBy = @By;
            """, ("@A", ItemA), ("@B", ItemB), ("@C", ItemC), ("@P1", P1), ("@P2", P2), ("@K", K), ("@K2", K2),
                 ("@P", Pattern), ("@PX", ClosedPattern), ("@M", Mold), ("@M2", Mold2), ("@M3", Mold3), ("@By", Actor),
                 ("@Pin", Pin), ("@D4", D4));
    }

    static int SeedRun(AmesConnectionFactory f) => (int)Scalar(f, """
        INSERT INTO dbo.PP_ApsRun (LineID, BaseDate, Days, IncludeOpen, Status, SettingsJson, BundleJson, ResultJson, WarningCount, CreatedBy)
        OUTPUT INSERTED.RunID
        VALUES (@L, @D, 5, 0, 'Saved', '{}', '{}', '{}', 0, @By);
        """, ("@L", LineInj), ("@D", D0), ("@By", Actor))!;

    static int SeedLine(AmesConnectionFactory f, int runId, string kind, string item, string line, DateTime date,
                        decimal day, decimal night, bool sameItem, decimal supply = 0) => (int)Scalar(f, """
        INSERT INTO dbo.PP_ApsPlanLine (RunID, Kind, ItemNo, LineID, PlanDate, Demand, Supply, Requirement, PlanDay, PlanNight, Stock, Locked, Status, SameItem)
        OUTPUT INSERTED.PlanLineID
        VALUES (@R, @K, @I, @L, @D, 0, @Sup, 0, @Day, @Night, 0, 0, 'ok', @Same);
        """, ("@R", runId), ("@K", kind), ("@I", item), ("@L", line), ("@D", date), ("@Day", day), ("@Night", night), ("@Same", sameItem), ("@Sup", supply))!;

    /// <summary>저장본 BundleJson 에 BOM 간선만 싣는다 — WO 생성이 부모↔사출 자식 관계·QtyPer 를 여기서 읽는다(스펙 2026-10-05 §1).</summary>
    static void SeedBundleBom(AmesConnectionFactory f, int runId, params BomEdge[] edges) =>
        Exec(f, "UPDATE dbo.PP_ApsRun SET BundleJson = @J WHERE RunID = @R;",
             ("@J", JsonSerializer.Serialize(new PlanBundle { Bom = edges.ToList() }, ApsJson.Options)), ("@R", runId));

    /// <summary>BOM 규칙 픽스처 — 완제품 P1·P2(라우팅 A, INJ ST-INJ-01 · IMG ST-IMG-01), 사출 자식 K(M2 60 UPH)·K2(M3 120 UPH), 교체 15분.</summary>
    static void SeedBomParents(AmesConnectionFactory f) => Exec(f, """
        INSERT INTO dbo.MD_Item (ItemNo, ItemName, ItemType, InjFlag, RoutingType, ActiveFlag, CreatedBy)
        VALUES (@P1, N'ITEST APS P1', 'ASSY', 0, 'A', 1, @By), (@P2, N'ITEST APS P2', 'ASSY', 0, 'A', 1, @By),
               (@K,  N'ITEST APS K',  'SUB',  1, NULL, 1, @By), (@K2, N'ITEST APS K2', 'SUB',  1, NULL, 1, @By);
        INSERT INTO dbo.MD_Bop (BOPID, ItemNo, RoutingType, StepSeq, StationCode, StdCycleTime, ActiveFlag, CreatedBy)
        VALUES ('ITEST-APS-BOP-P110', @P1, 'A', 10, 'ST-INJ-01', 6, 1, @By), ('ITEST-APS-BOP-P120', @P1, 'A', 20, 'ST-IMG-01', 12, 1, @By),
               ('ITEST-APS-BOP-P210', @P2, 'A', 10, 'ST-INJ-01', 6, 1, @By), ('ITEST-APS-BOP-P220', @P2, 'A', 20, 'ST-IMG-01', 12, 1, @By);
        INSERT INTO dbo.MD_Mold (MoldID, MoldName, Status, CavityCount, MoldChangeMin, CreatedBy)
        VALUES (@M2, N'ITEST aps mold 2', 'AVAILABLE', 1, 20, @By), (@M3, N'ITEST aps mold 3', 'AVAILABLE', 1, 20, @By);
        INSERT INTO dbo.MD_MoldLine (LineCode, MoldID, UPH, PrepTime, CreatedBy) VALUES (@LI, @M2, 60, 15, @By), (@LI, @M3, 120, 15, @By);
        INSERT INTO dbo.MD_MoldItem (MoldID, ItemNo, Color, CavitySeq, CavityPos, CavityCount, MoldCategory, ActiveFlag, CreatedBy)
        VALUES (@M2, @K, 'CBK', 1, 'LH', 1, 'INJECTION', 1, @By), (@M3, @K2, 'CBK', 1, 'LH', 1, 'INJECTION', 1, @By);
        """, ("@P1", P1), ("@P2", P2), ("@K", K), ("@K2", K2), ("@M2", Mold2), ("@M3", Mold3), ("@LI", LineInj), ("@By", Actor));

    /// <summary>기존 WO 의 INJ 슬롯(완제품 품번) — BOM 규칙 부모 차감 픽스처.</summary>
    static void WoSlot(AmesConnectionFactory f, int woId, string line, DateTime date, int start, int end, decimal qty) => Exec(f, """
        INSERT INTO dbo.PP_LineSchedule (LineID, ScheduleDate, WoID, PatternID, EntryType, StartMin, EndMin, PlannedQty, Status, CreatedBy)
        VALUES (@L, @D, @W, @P, 'WO', @S, @E, @Q, 'DRAFT', @By);
        """, ("@L", line), ("@D", date), ("@W", woId), ("@P", Pattern), ("@S", start), ("@E", end), ("@Q", qty), ("@By", Actor));

    static decimal SumQty(AmesConnectionFactory f, int woId, string line)
        => Convert.ToDecimal(Scalar(f, "SELECT ISNULL(SUM(PlannedQty),0) FROM dbo.PP_LineSchedule WHERE WoID = @W AND LineID = @L;", ("@W", woId), ("@L", line)));
    static string Steps(AmesConnectionFactory f, int woId)
        => (string)Scalar(f, "SELECT STRING_AGG(ProcessCode + '@' + ISNULL(LineID,'-'), ',') WITHIN GROUP (ORDER BY StepSeq) FROM dbo.PP_WorkOrderRouting WHERE WoID = @W;", ("@W", woId))!;

    /// <summary>ASM 행(완제품 라인, 기본 LINE-IMG-01) + INJ 행(LINE-INJ-01) 한 쌍. 반환 = INJ 행 PlanLineID.</summary>
    static int SeedInj(AmesConnectionFactory f, int runId, string item, DateTime date, decimal day, decimal night,
                       bool sameItem = true, string asmLine = LineImg)
    {
        SeedLine(f, runId, "ASM", item, asmLine, date, 0, 0, sameItem);
        return SeedLine(f, runId, "INJ", item, LineInj, date, day, night, sameItem);
    }

    static int SeedSo(AmesConnectionFactory f, string soNo, string item, decimal qty, int dueOffset) => (int)Scalar(f, """
        INSERT INTO dbo.PP_CustomerOrder (SoNumber, SoLineNo, ItemNo, OrderQty, ShippedQty, RequestedDeliveryDate, Status, CreatedBy)
        OUTPUT INSERTED.SoID
        VALUES (@S, 1, @I, @Q, 0, @Due, 'Confirmed', @By);
        """, ("@S", soNo), ("@I", item), ("@Q", qty), ("@Due", D0.AddDays(dueOffset)), ("@By", Actor))!;

    /// <summary>INJ 라인 지정일에 WO 없는 기존 슬롯을 지정 금형으로 넣는다 — 직전 금형·점유 상태를 만든다.</summary>
    static void PreexistingSlot(AmesConnectionFactory f, DateTime date, string mold, int start, int end) =>
        Exec(f, """
            INSERT INTO dbo.PP_LineSchedule (LineID, ScheduleDate, PatternID, EntryType, StartMin, EndMin, PlannedQty, MoldID, Status, CreatedBy)
            VALUES (@L, @D, @P, 'WO', @S, @E, 0, @M, 'DRAFT', @By);
            """, ("@L", LineInj), ("@D", date), ("@P", Pattern), ("@S", start), ("@E", end), ("@M", mold), ("@By", Actor));

    sealed record Slot(string Type, int Start, int End, string? Mold, int? WoId, string? RefType, int? RefId, string? Title, decimal Qty);

    /// <summary>(라인, 일자)의 WO 슬롯(0분 형제 슬롯 포함)과 MC 행 — placeholder·기존 슬롯(WoID 없음·WO) 는 제외.</summary>
    static List<Slot> Rows(AmesConnectionFactory f, string line, DateTime date)
    {
        using var conn = f.OpenConnection();
        using var cmd  = new SqlCommand("""
            SELECT EntryType, ISNULL(StartMin,0) AS StartMin, ISNULL(EndMin,0) AS EndMin, MoldID, WoID, RefType, RefID, Title, ISNULL(PlannedQty,0) AS Qty
            FROM   dbo.PP_LineSchedule
            WHERE  LineID = @L AND ScheduleDate = @D AND (WoID IS NOT NULL OR EntryType = 'MC')
            ORDER  BY StartMin, ScheduleID;
            """, conn);
        cmd.Parameters.AddWithValue("@L", line); cmd.Parameters.AddWithValue("@D", date);
        using var rdr = cmd.ExecuteReader();
        var list = new List<Slot>();
        while (rdr.Read())
            list.Add(new Slot((string)rdr["EntryType"], Convert.ToInt32(rdr["StartMin"]), Convert.ToInt32(rdr["EndMin"]),
                              rdr["MoldID"] as string, rdr["WoID"] as int?, rdr["RefType"] as string, rdr["RefID"] as int?,
                              rdr["Title"] as string, Convert.ToDecimal(rdr["Qty"])));
        return list;
    }

    static string RunStatus(AmesConnectionFactory f, int runId)
        => (string)Scalar(f, "SELECT Status FROM dbo.PP_ApsRun WHERE RunID = @R;", ("@R", runId))!;

    static int WoCount(AmesConnectionFactory f, string item)
        => (int)Scalar(f, "SELECT COUNT(*) FROM dbo.PP_WorkOrder WHERE ItemNo = @I;", ("@I", item))!;

    static PpRepository.ApsWoResult Run(AmesConnectionFactory f, int runId, int[] ids, bool dryRun = false)
        => new PpRepository(f).CreateApsWorkOrders(runId, ids, Actor, dryRun);

    /// <summary>2026-10-07 미리보기 슬롯 편집: dryRun 도 편집을 적용해 보여 주고(행은 롤백), 실제 실행은 PP_LineSchedule 행을 목표 자리로 옮긴다.</summary>
    [SkippableFact]
    public void Slot_edits_are_applied_in_dry_run_and_moved_in_the_real_run()
    {
        var f = Ready();
        Seed(f);
        try
        {
            SeedSo(f, "ITEST-APS-SO-1", ItemA, 200, dueOffset: 9);
            int run = SeedRun(f);
            int pl  = SeedInj(f, run, ItemA, D0, day: 120, night: 0);   // 유효 UPH 60(120 × 1캐비티 ÷ 2) → 480-600
            var pp  = new PpRepository(f);

            var plain = pp.CreateApsWorkOrders(run, new[] { pl }, Actor, dryRun: true);
            var inj   = plain.Orders.Single().Placements.Single(p => p.LineId == LineInj);
            Assert.Equal((480, 600), (inj.StartMin, inj.EndMin));
            var o = plain.Orders.Single();
            var edit = new ApsSlotEdit(o.ItemNo, o.PlanDate, o.SoId, inj.StepSeq, LineInj, D0d, 480, 600, LineInj, D0d, 780, 900, null);

            var dry = pp.CreateApsWorkOrders(run, new[] { pl }, Actor, dryRun: true, edits: new[] { edit });
            Assert.True(Assert.Single(dry.Edits).Applied);
            Assert.Equal((780, 900), dry.Orders.Single().Placements.Where(p => p.LineId == LineInj).Select(p => (p.StartMin, p.EndMin)).Single());
            Assert.Empty(Rows(f, LineInj, D0));

            var real = pp.CreateApsWorkOrders(run, new[] { pl }, Actor, dryRun: false, edits: new[] { edit });
            Assert.True(Assert.Single(real.Edits).Applied);
            var slots = Rows(f, LineInj, D0).Where(r => r.Type == "WO" && r.WoId is not null).ToList();
            Assert.Equal(new[] { (780, 900, 120m) }, slots.Select(r => (r.Start, r.End, r.Qty)).ToArray());
            Assert.Equal($"INJ@{LineInj},IMG@{LineImg}", Steps(f, real.Orders.Single().WoId));
        }
        finally { Cleanup(f); }
    }

    /// <summary>편집 거부 — 휴게를 가로지르면 OutsideBands, 금형 UPH 가 없는 라인으로 옮기면 NoUph, 배치가 달라진 슬롯은 NotFound. 거부돼도 나머지 생성은 그대로.</summary>
    [SkippableFact]
    public void Invalid_slot_edits_are_skipped_with_a_reason()
    {
        var f = Ready();
        Seed(f);
        try
        {
            SeedSo(f, "ITEST-APS-SO-1", ItemA, 200, dueOffset: 9);
            int run = SeedRun(f);
            int pl  = SeedInj(f, run, ItemA, D0, day: 120, night: 0);
            var pp  = new PpRepository(f);
            var o   = pp.CreateApsWorkOrders(run, new[] { pl }, Actor, dryRun: true).Orders.Single();
            var inj = o.Placements.Single(p => p.LineId == LineInj);
            ApsSlotEdit E(string nl, int ns, int ne, int os = 480, int oe = 600) => new(o.ItemNo, o.PlanDate, o.SoId, inj.StepSeq, LineInj, D0d, os, oe, nl, D0d, ns, ne, null);

            var r = pp.CreateApsWorkOrders(run, new[] { pl }, Actor, dryRun: true,
                        edits: new[] { E(LineInj, 700, 820), E(LinePnt, 480, 600), E(LineInj, 780, 900, os: 481) });

            Assert.Equal(new[] { ApsSlotEditRules.ReasonOutsideBands, ApsSlotEditRules.ReasonNoUph, ApsSlotEditRules.ReasonNotFound },
                         r.Edits.Select(e => e.Reason).ToArray());
            Assert.Equal(0, r.EditsApplied);
            Assert.Equal((480, 600), r.Orders.Single().Placements.Where(p => p.LineId == LineInj).Select(p => (p.StartMin, p.EndMin)).Single());
        }
        finally { Cleanup(f); }
    }

    /// <summary>2026-10-07 교대 모델: 계획 행의 교대별 수량이 그 교대 밴드 앞에서부터 놓인다(3교대 A/B/C). 교대 사이를 넘기지 않는다.</summary>
    [SkippableFact]
    public void Places_each_shift_quantity_in_its_own_shift_bands()
    {
        var f = Ready();
        Seed(f);
        try
        {
            Exec(f, "INSERT INTO dbo.MD_LineTimeSegment (SegmentID, PatternID, SeqNo, StartMin, EndMin, SegmentState, ShiftCode, CreatedBy) VALUES ('ITEST-APS-SEG-5', @P, 5, 0, 120, 'OPERATING', 'C', @By);", ("@P", Pattern), ("@By", Actor));
            SeedSo(f, "ITEST-APS-SO-1", ItemA, 500, dueOffset: 9);
            int run = SeedRun(f);
            int pl  = SeedInj(f, run, ItemA, D0, day: 60, night: 90);   // 파생 호환값
            Exec(f, "INSERT INTO dbo.PP_ApsPlanLineShift (PlanLineID, ShiftCode, Qty) VALUES (@L, 'A', 60), (@L, 'B', 30), (@L, 'C', 60);", ("@L", pl));

            var r = Run(f, run, new[] { pl });

            // 유효 UPH 60: A 60개 = 60분 → 480-540, B 30개 = 30분 → 960-990, C 60개 = 60분 → 0-60
            Assert.Equal(new[] { (480, 540, 60m), (960, 990, 30m), (0, 60, 60m) },
                         Rows(f, LineInj, D0).Where(x => x.Type == "WO" && x.WoId is not null).OrderBy(x => x.Start == 0 ? 2000 : x.Start).Select(x => (x.Start, x.End, x.Qty)).ToArray());
            Assert.Empty(r.Orders.Single().Shortfalls.Where(sf => sf.LineId == LineInj));
        }
        finally { Exec(f, "DELETE FROM dbo.MD_LineTimeSegment WHERE SegmentID = 'ITEST-APS-SEG-5';"); Cleanup(f); }
    }

    /// <summary>교대 1개뿐인 패턴(Review Focus 1): 교대 행이 없으면 PlanDay + PlanNight 가 그 교대 하나로 복원돼 전량 그 교대 밴드에 놓인다.</summary>
    [SkippableFact]
    public void Places_everything_in_the_only_shift()
    {
        var f = Ready();
        Seed(f);
        try
        {
            Exec(f, "DELETE FROM dbo.MD_LineTimeSegment WHERE SegmentID = 'ITEST-APS-SEG-4';");   // B 교대 제거 → A 만
            SeedSo(f, "ITEST-APS-SO-1", ItemA, 500, dueOffset: 9);
            int run = SeedRun(f);
            int pl  = SeedInj(f, run, ItemA, D0, day: 120, night: 60);   // 교대 행 없음 → Restore: A = 120 + 60(교대 하나면 합산)

            var r = Run(f, run, new[] { pl });

            Assert.Equal(new[] { (480, 660, 180m) },
                         Rows(f, LineInj, D0).Where(x => x.Type == "WO" && x.WoId is not null).Select(x => (x.Start, x.End, x.Qty)).ToArray());
            Assert.Empty(r.Orders.Single().Shortfalls.Where(sf => sf.LineId == LineInj));
        }
        finally { Cleanup(f); }
    }

    /// <summary>2026-10-07 미리보기 보드: 라인 × 날짜 능력은 WO 생성과 같은 APS 패턴 규칙 — 사출 라인은 지정 패턴, 완제품 라인은 설정이 없으면 자동 해석(PP_LineSchedule 저장 패턴), 사출 라인 미설정은 거부.</summary>
    [SkippableFact]
    public void Day_boards_use_the_same_pattern_rule_as_wo_creation()
    {
        var f = Ready();
        Seed(f);
        try
        {
            var pp = new PpRepository(f);
            var boards = pp.ListApsDayBoards(new[] { LineInj }, new[] { LineInj, LineImg }, new[] { D0, D1 });

            Assert.Equal(4, boards.Count);
            var inj = boards.Single(b => b.LineId == LineInj && b.Date == D0).Capacity;
            Assert.Equal(Pattern, inj.PatternId);
            Assert.Equal(480, inj.DayStart);
            Assert.Equal(new[] { (480, 720), (780, 960), (960, 1440) }, inj.OperatingBands.Select(b => (b.StartMin, b.EndMin)).ToArray());
            Assert.Equal(Pattern, boards.Single(b => b.LineId == LineImg && b.Date == D1).Capacity.PatternId);   // IMG 는 APS 설정 없음 → placeholder 행의 패턴

            ApsPatternConfig.Apply(f, null, (LineInj, null));
            var ex = Assert.Throws<ApsConfigurationException>(() => pp.ListApsDayBoards(new[] { LineInj }, new[] { LineInj }, new[] { D0 }));
            Assert.Contains(ex.Lines, l => l.Contains(LineInj));
        }
        finally { Cleanup(f); }
    }

    /// <summary>2026-10-06: 사출 라인에 APS 가동 시간 패턴(라인 지정·기본 패턴)이 없으면 WO 를 하나도 만들지 않고 ApsConfigurationException — 실행도 Saved 그대로.</summary>
    [SkippableFact]
    public void Rejects_whole_run_when_an_injection_line_has_no_aps_pattern()
    {
        var f = Ready();
        Seed(f);
        try
        {
            SeedSo(f, "ITEST-APS-SO-1", ItemA, 100, dueOffset: 9);
            int run = SeedRun(f);
            int pl  = SeedInj(f, run, ItemA, D0, day: 120, night: 60);
            ApsPatternConfig.Apply(f, null, (LineInj, null));   // 라인 지정도 기본 패턴도 없음 — PP_LineSchedule 의 주간 placeholder 패턴은 APS 가 보지 않는다

            var ex = Assert.Throws<ApsConfigurationException>(() => Run(f, run, new[] { pl }));

            Assert.Contains(ex.Lines, l => l.Contains(LineInj));
            Assert.Equal(0, Convert.ToInt32(Scalar(f, "SELECT COUNT(*) FROM dbo.PP_WorkOrder WHERE ItemNo = @I;", ("@I", ItemA))));
            Assert.Equal("Saved", Scalar(f, "SELECT Status FROM dbo.PP_ApsRun WHERE RunID = @R;", ("@R", run)));
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Creates_fifo_so_split_wos_with_inj_slots_finished_step_slots_and_releases_run()
    {
        var f = Ready();
        Seed(f);
        try
        {
            int so1 = SeedSo(f, "ITEST-APS-SO-1", ItemA, 100, dueOffset: 9);
            int so2 = SeedSo(f, "ITEST-APS-SO-2", ItemA, 50,  dueOffset: 10);
            int run = SeedRun(f);
            int pl  = SeedInj(f, run, ItemA, D0, day: 120, night: 60);

            var res = Run(f, run, new[] { pl });

            Assert.False(res.DryRun);
            Assert.Empty(res.Rejected);
            Assert.Equal((0, 0), (res.SkippedExisting, res.BomRuleExcluded));
            Assert.Equal(new (int?, decimal)[] { (so1, 100m), (so2, 50m), (null, 30m) },
                         res.Orders.Select(o => (o.SoId, o.Qty)).ToArray());
            Assert.All(res.Orders, o => Assert.Equal(pl, o.PlanLineId));

            var deadline = PlusWorkdays(D0, _offset);
            Assert.All(res.Orders, o => Assert.Equal((DateTime?)deadline, o.Deadline));
            Assert.Equal((DateTime?)D0.AddDays(9), res.Orders[0].DueDate);
            Assert.Equal((DateTime?)deadline,      res.Orders[2].DueDate);   // 수주 없는 재고 보충 WO 는 납기 = 생산 마감

            Assert.Equal(3, (int)Scalar(f, "SELECT COUNT(*) FROM dbo.PP_WorkOrder WHERE ItemNo = @I AND Status = 'Released' AND ProdDeadline = @DL;",
                                        ("@I", ItemA), ("@DL", deadline))!);
            Assert.Equal(1, (int)Scalar(f, "SELECT COUNT(*) FROM dbo.PP_WorkOrder WHERE ItemNo = @I AND SoID IS NULL;", ("@I", ItemA))!);
            // Release 단계 행: WO 3건 모두 INJ 단계 = 계획 행 라인, IMG 단계 = 같은 실행 ASM 행의 라인
            Assert.Equal(3, (int)Scalar(f, "SELECT COUNT(*) FROM dbo.PP_WorkOrderRouting r JOIN dbo.PP_WorkOrder w ON w.WoID = r.WoID WHERE w.ItemNo = @I AND r.ProcessCode = 'INJ' AND r.LineID = @L;",
                                        ("@I", ItemA), ("@L", LineInj))!);
            Assert.Equal(3, (int)Scalar(f, "SELECT COUNT(*) FROM dbo.PP_WorkOrderRouting r JOIN dbo.PP_WorkOrder w ON w.WoID = r.WoID WHERE w.ItemNo = @I AND r.ProcessCode = 'IMG' AND r.LineID = @L;",
                                        ("@I", ItemA), ("@L", LineImg))!);

            // INJ: 주간 120분 = 480-600, 야간 60분 = 960-1020 을 조각(100·50·30) 순서대로 잘라 실었다
            var inj = Rows(f, LineInj, D0);
            Assert.Equal(new[] { (480, 580, 100m), (580, 600, 20m), (960, 990, 30m), (990, 1020, 30m) },
                         inj.Select(r => (r.Start, r.End, r.Qty)).ToArray());
            Assert.All(inj, r => Assert.Equal(Mold, r.Mold));
            Assert.Equal(res.Orders[0].WoId, inj[0].WoId);
            Assert.Equal(res.Orders[1].WoId, inj[1].WoId);
            Assert.Equal(res.Orders[1].WoId, inj[2].WoId);
            Assert.Equal(res.Orders[2].WoId, inj[3].WoId);
            Assert.Equal(180m, inj.Sum(r => r.Qty));

            // 완제품(IMG) 단계: 사출 마지막 슬롯(1020) 뒤부터 BOP 12초/EA — 100 EA 20분, 50 EA 10분, 30 EA 6분
            var img = Rows(f, LineImg, D0);
            Assert.Equal(new[] { (1020, 1040, 100m), (1040, 1050, 50m), (1050, 1056, 30m) },
                         img.Select(r => (r.Start, r.End, r.Qty)).ToArray());
            Assert.All(img, r => Assert.Null(r.Mold));
            Assert.All(res.Orders, o => Assert.Empty(o.Shortfalls));
            Assert.All(res.Orders, o => Assert.Equal(0m, o.LateQty));
            Assert.All(res.Orders, o => Assert.Empty(o.MoldChanges));                          // 직전 금형(일요일 고정) = 같은 금형

            Assert.Equal(3, (int)Scalar(f, "SELECT COUNT(*) FROM dbo.PP_ApsRunWo WHERE RunID = @R AND PlanLineID = @P;", ("@R", run), ("@P", pl))!);
            Assert.Equal(1, (int)Scalar(f, "SELECT COUNT(*) FROM dbo.PP_ApsRunWo WHERE RunID = @R AND SoID IS NULL AND Qty = 30;", ("@R", run))!);
            Assert.Equal(res.Orders[0].WoId, (int)Scalar(f, "SELECT WoID FROM dbo.PP_ApsPlanLine WHERE PlanLineID = @P;", ("@P", pl))!);
            Assert.Equal(ApsRepository.StatusReleased, RunStatus(f, run));
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Sibling_item_gets_zero_minute_slot_at_representative_start_and_rep_occupies_max_sibling_minutes()
    {
        var f = Ready();
        Seed(f);
        try
        {
            int run = SeedRun(f);
            int plA = SeedInj(f, run, ItemA, D0, day: 120, night: 0);
            int plB = SeedInj(f, run, ItemB, D0, day: 150, night: 0);

            var res = Run(f, run, new[] { plA, plB });

            Assert.Equal(2, res.Created);
            var a = res.Orders.Single(o => o.ItemNo == ItemA);
            var b = res.Orders.Single(o => o.ItemNo == ItemB);
            Assert.Null(a.SoId); Assert.Null(b.SoId);

            var inj = Rows(f, LineInj, D0);
            var rep = Assert.Single(inj, r => r.WoId == a.WoId);
            Assert.Equal((480, 630, Mold, 120m), (rep.Start, rep.End, rep.Mold, rep.Qty));     // 분 = 형제 최대 150 EA ÷ 60 UPH, 수량은 자기 것
            var sib = Assert.Single(inj, r => r.WoId == b.WoId);
            Assert.Equal((480, 480, (string?)null, 150m), (sib.Start, sib.End, sib.Mold, sib.Qty));
            Assert.Single(new LineScheduleRepository(f).GetDayCapacity(LineInj, D0).Occupied);   // 0분 슬롯은 능력에 안 잡힌다
            Assert.Empty(a.Shortfalls); Assert.Empty(b.Shortfalls);
            Assert.Equal(2, Rows(f, LineImg, D0).Count);
            Assert.Equal(ApsRepository.StatusReleased, RunStatus(f, run));
        }
        finally { Cleanup(f); }
    }

    /// 형제의 단위는 금형 × 색상 — 같은 금형이라도 MD_MoldItem.Color 가 다르면 따로 찍으므로 둘 다 실제 시간을 받고 0분 슬롯은 없다.
    [SkippableFact]
    public void Same_mold_different_color_items_are_not_siblings_and_each_takes_its_own_time()
    {
        var f = Ready();
        Seed(f);
        try
        {
            Exec(f, "UPDATE dbo.MD_MoldItem SET Color = 'YGU' WHERE MoldID = @M AND ItemNo = @B;", ("@M", Mold), ("@B", ItemB));
            int run = SeedRun(f);
            int plA = SeedInj(f, run, ItemA, D0, day: 120, night: 0);
            int plB = SeedInj(f, run, ItemB, D0, day: 150, night: 0);

            var res = Run(f, run, new[] { plA, plB });

            Assert.Equal(2, res.Created);
            var a = res.Orders.Single(o => o.ItemNo == ItemA);
            var b = res.Orders.Single(o => o.ItemNo == ItemB);
            var inj = Rows(f, LineInj, D0).Where(r => r.Type == "WO").ToList();
            var rowsA = inj.Where(r => r.WoId == a.WoId).ToList();
            var rowsB = inj.Where(r => r.WoId == b.WoId).ToList();
            // 색상이 갈리면 각 품번이 그 색상 패밀리의 유일한 품번 → 캐비티 2개를 다 쓴다 → 유효 UPH 120(= 계획 ApsRepository 와 같은 규칙, 2026-10-07 UPH 통일)
            Assert.Equal(60, rowsA.Sum(r => r.End - r.Start));                  // 120 EA ÷ 120 UPH
            Assert.Equal(75, rowsB.Sum(r => r.End - r.Start));                  // 150 EA ÷ 120 UPH — 자기 시간, 0분 슬롯 없음
            Assert.All(inj, r => { Assert.True(r.End > r.Start); Assert.Equal(Mold, r.Mold); });
            Assert.Equal(480, rowsA.Min(r => r.Start));                         // CBK(A) 가 먼저, YGU(B) 는 그 뒤에 이어진다
            Assert.True(rowsB.Min(r => r.Start) >= rowsA.Max(r => r.End));
            Assert.Empty(a.Shortfalls); Assert.Empty(b.Shortfalls);
            Assert.Equal(inj.Count, new LineScheduleRepository(f).GetDayCapacity(LineInj, D0).Occupied.Count);
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Representative_keeps_each_shift_in_its_band_and_writes_all_time_reserved_for_siblings()
    {
        var f = Ready();
        Seed(f);
        try
        {
            // 대표 A(주 10 · 야 50) < 형제 B(주 300 · 야 20): 주간 300분은 두 구간(480-720 · 780-840)에 걸치고 대표 수량 10 은 첫 구간에,
            // 남은 구간도 0 수량으로 써서 형제 몫 시간을 남긴다. 대표 야간 50 은 주간이 아니라 야간 구간(960-1010)에 싣는다.
            int run = SeedRun(f);
            int plA = SeedInj(f, run, ItemA, D0, day: 10,  night: 50);
            int plB = SeedInj(f, run, ItemB, D0, day: 300, night: 20);

            var res = Run(f, run, new[] { plA, plB });

            var a = res.Orders.Single(o => o.ItemNo == ItemA);
            var b = res.Orders.Single(o => o.ItemNo == ItemB);
            var inj = Rows(f, LineInj, D0);
            Assert.Equal(new[] { (480, 720, 10m), (780, 840, 0m), (960, 1010, 50m) },
                         inj.Where(r => r.WoId == a.WoId).Select(r => (r.Start, r.End, r.Qty)).ToArray());
            Assert.All(inj.Where(r => r.WoId == a.WoId), r => Assert.Equal(Mold, r.Mold));
            Assert.Equal(new[] { (480, 480, 300m), (960, 960, 20m) },                        // 형제 주/야 0분 슬롯 = 대표 주/야 첫 슬롯 시작
                         inj.Where(r => r.WoId == b.WoId).Select(r => (r.Start, r.End, r.Qty)).ToArray());
            Assert.All(inj.Where(r => r.WoId == b.WoId), r => Assert.Null(r.Mold));

            var cap = new LineScheduleRepository(f).GetDayCapacity(LineInj, D0);
            Assert.Equal(3, cap.Occupied.Count);
            Assert.Equal(350, cap.WoLoadMin);                                                 // 주간 300 + 야간 50 — DB 가 점유를 전부 안다
            Assert.Empty(a.Shortfalls); Assert.Empty(b.Shortfalls);
        }
        finally { Cleanup(f); }
    }

    // ── PP-LSB 「적용」(SaveSchedule) 과 APS 형제 0분 슬롯 — 보드는 0분 행을 싣지 않으므로 SaveSchedule 이 지우기 전에 읽어 되살린다 ──

    /// <summary>형제 픽스처(대표 A 주간 120 · 형제 B 주간 150, D0) — A 480-630 슬롯 + B 480 0분 슬롯.</summary>
    static (PpRepository.ApsWoOrder Rep, PpRepository.ApsWoOrder Sib) SiblingDay(AmesConnectionFactory f)
    {
        int run = SeedRun(f);
        int plA = SeedInj(f, run, ItemA, D0, day: 120, night: 0);
        int plB = SeedInj(f, run, ItemB, D0, day: 150, night: 0);
        var res = Run(f, run, new[] { plA, plB });
        return (res.Orders.Single(o => o.ItemNo == ItemA), res.Orders.Single(o => o.ItemNo == ItemB));
    }

    static void Apply(AmesConnectionFactory f, params (int WoId, int StartMin, int EndMin, decimal Qty, string? MoldId)[] slots)
        => new LineScheduleRepository(f).SaveSchedule(LineInj, D0, Pattern, slots,
                                                      Array.Empty<(int, int, string?, string?, int?)>(), Actor);

    static int DayRowCount(AmesConnectionFactory f, string where) => (int)Scalar(f,
        $"SELECT COUNT(*) FROM dbo.PP_LineSchedule WHERE LineID = @L AND ScheduleDate = @D AND {where};", ("@L", LineInj), ("@D", D0))!;

    [SkippableFact]
    public void Apply_with_only_the_representative_slot_keeps_the_sibling_zero_minute_row()
    {
        var f = Ready();
        Seed(f);
        try
        {
            var (a, b) = SiblingDay(f);

            Apply(f, (a.WoId, 480, 630, 120m, Mold));                                            // 보드가 넘기는 그대로 — 형제 0분 행은 없다

            var inj = Rows(f, LineInj, D0);
            Assert.All(inj.GroupBy(r => r.WoId), g => Assert.Single(g));                        // WO 마다 정확히 한 행
            var rep = Assert.Single(inj, r => r.WoId == a.WoId);
            Assert.Equal(("WO", 480, 630, Mold, 120m), (rep.Type, rep.Start, rep.End, rep.Mold, rep.Qty));
            var sib = Assert.Single(inj, r => r.WoId == b.WoId);
            Assert.Equal(("WO", 480, 480, (string?)null, 150m), (sib.Type, sib.Start, sib.End, sib.Mold, sib.Qty));
            Assert.Equal(2, DayRowCount(f, "PatternID = '" + Pattern + "' AND Status = 'DRAFT'"));
            Assert.Equal(0, DayRowCount(f, "WoID IS NULL"));
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Apply_with_a_real_slot_for_the_sibling_wo_replaces_its_zero_minute_row()
    {
        var f = Ready();
        Seed(f);
        try
        {
            var (a, b) = SiblingDay(f);

            Apply(f, (a.WoId, 480, 630, 120m, Mold), (b.WoId, 630, 700, 150m, Mold));

            var sib = Assert.Single(Rows(f, LineInj, D0), r => r.WoId == b.WoId);
            Assert.Equal((630, 700, 150m), (sib.Start, sib.End, sib.Qty));
            Assert.Equal(0, DayRowCount(f, "ISNULL(EndMin,0) <= ISNULL(StartMin,0)"));
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Apply_with_no_board_slots_keeps_the_sibling_row_and_writes_no_placeholder()
    {
        var f = Ready();
        Seed(f);
        try
        {
            var (_, b) = SiblingDay(f);

            Apply(f);

            var sib = Assert.Single(Rows(f, LineInj, D0));
            Assert.Equal((b.WoId, 480, 480, 150m), ((int)sib.WoId!, sib.Start, sib.End, sib.Qty));
            Assert.Equal(1, DayRowCount(f, "1 = 1"));                                             // placeholder 행 없음
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Inserts_mold_change_block_before_representative_when_line_last_mold_differs()
    {
        var f = Ready();
        Seed(f);
        try
        {
            PreexistingSlot(f, D0, "ITEST-APS-X", 480, 600);
            int run = SeedRun(f);
            int pl  = SeedInj(f, run, ItemA, D0, day: 60, night: 0);

            var res = Run(f, run, new[] { pl });

            var o  = Assert.Single(res.Orders);
            var mc = Assert.Single(o.MoldChanges);
            Assert.Equal(("ITEST-APS-X", Mold, 600, 615), (mc.FromMoldId, mc.ToMoldId, mc.StartMin, mc.EndMin));   // PrepTime 15분
            var rows = Rows(f, LineInj, D0);
            Assert.Contains(rows, r => r.Type == "MC" && r.Start == 600 && r.End == 615 && r.Mold == Mold
                                    && r.RefType == "WO" && r.RefId == o.WoId && r.Title == "ITEST-APS-X→ITEST-APS-M1" && r.WoId is null);
            var wo = Assert.Single(rows, r => r.Type == "WO" && r.WoId == o.WoId);
            Assert.Equal((615, 675, Mold), (wo.Start, wo.End, wo.Mold));
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Mold_change_takes_the_first_free_minutes_and_production_follows_it_even_before_the_line_tail()
    {
        var f = Ready();
        Seed(f);
        try
        {
            // 480-600 이 비어 있고 600-700 에 다른 금형 슬롯 — MC 는 꼬리(700) 뒤가 아니라 첫 빈 자리 480-495, 생산은 그 뒤 495-555
            PreexistingSlot(f, D0, "ITEST-APS-X", 600, 700);
            int run = SeedRun(f);
            int pl  = SeedInj(f, run, ItemA, D0, day: 60, night: 0);

            var res = Run(f, run, new[] { pl });

            var o  = Assert.Single(res.Orders);
            var mc = Assert.Single(o.MoldChanges);
            Assert.Equal(("ITEST-APS-X", Mold, 480, 495), (mc.FromMoldId, mc.ToMoldId, mc.StartMin, mc.EndMin));
            var rows = Rows(f, LineInj, D0);
            Assert.Equal(new[] { ("MC", 480, 495, (int?)null), ("WO", 495, 555, (int?)o.WoId) },
                         rows.Select(r => (r.Type, r.Start, r.End, r.WoId)).ToArray());
            Assert.Empty(o.Shortfalls);
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Night_only_representative_gets_its_mold_change_at_the_start_of_the_night_band()
    {
        var f = Ready();
        Seed(f);
        try
        {
            PreexistingSlot(f, D0, "ITEST-APS-X", 480, 600);
            int run = SeedRun(f);
            int pl  = SeedInj(f, run, ItemA, D0, day: 0, night: 60);

            var res = Run(f, run, new[] { pl });

            var o  = Assert.Single(res.Orders);
            var mc = Assert.Single(o.MoldChanges);
            Assert.Equal((960, 975), (mc.StartMin, mc.EndMin));                                  // 주간(600~)이 아니라 야간 첫 자리
            Assert.Equal(new[] { ("MC", 960, 975), ("WO", 975, 1035) },
                         Rows(f, LineInj, D0).Select(r => (r.Type, r.Start, r.End)).ToArray());
            Assert.Empty(o.Shortfalls);
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Mold_change_retries_the_night_window_when_the_day_has_no_room()
    {
        var f = Ready();
        Seed(f);
        try
        {
            // 주간이 다른 금형 슬롯으로 꽉 찬 날(D0: 주 0 · 야 60, D1: 주 60 · 야 60) — MC 는 야간 첫 자리 960-975, WO 는 975 부터.
            // D1 의 주간 60 은 금형이 안 바뀐 주간에 싣지 않고 Shortfall
            foreach (var d in new[] { D0, D1 })
            {
                PreexistingSlot(f, d, "ITEST-APS-X", 480, 720);
                PreexistingSlot(f, d, "ITEST-APS-X", 780, 960);
            }
            int run = SeedRun(f);
            int pl0 = SeedInj(f, run, ItemA, D0, day: 0,  night: 60);
            int pl1 = SeedInj(f, run, ItemA, D1, day: 60, night: 60);

            var res = Run(f, run, new[] { pl0, pl1 });

            var o0 = res.Orders.Single(o => o.PlanLineId == pl0);
            var o1 = res.Orders.Single(o => o.PlanLineId == pl1);
            Assert.Equal((960, 975), (Assert.Single(o0.MoldChanges).StartMin, o0.MoldChanges[0].EndMin));
            Assert.Equal((960, 975), (Assert.Single(o1.MoldChanges).StartMin, o1.MoldChanges[0].EndMin));
            Assert.Equal(new[] { ("MC", 960, 975, 0m), ("WO", 975, 1035, 60m) },
                         Rows(f, LineInj, D0).Select(r => (r.Type, r.Start, r.End, r.Qty)).ToArray());
            Assert.Equal(new[] { ("MC", 960, 975, 0m), ("WO", 975, 1035, 60m) },
                         Rows(f, LineInj, D1).Select(r => (r.Type, r.Start, r.End, r.Qty)).ToArray());
            Assert.Empty(o0.Shortfalls);
            Assert.Equal(60m, Assert.Single(o1.Shortfalls, s => s.LineId == LineInj).Qty);
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Mold_change_skips_an_exact_fit_gap_so_production_follows_it_immediately()
    {
        var f = Ready();
        Seed(f);
        try
        {
            // 주간 잔여 [480-495] ∪ [600-720] ∪ [780-960], X 가 495-600 — 교체 15분만 들어가는 480-495 는 쓰지 않는다
            PreexistingSlot(f, D0, "ITEST-APS-X", 495, 600);
            int run = SeedRun(f);
            int pl  = SeedInj(f, run, ItemA, D0, day: 60, night: 0);

            var res = Run(f, run, new[] { pl });

            var o = Assert.Single(res.Orders);
            Assert.Equal((600, 615), (Assert.Single(o.MoldChanges).StartMin, o.MoldChanges[0].EndMin));
            Assert.Equal(new[] { ("MC", 600, 615), ("WO", 615, 675) },
                         Rows(f, LineInj, D0).Select(r => (r.Type, r.Start, r.End)).ToArray());   // 480-495 에는 아무것도 없다
            Assert.Empty(o.Shortfalls);
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Mold_change_with_no_room_for_production_after_it_is_not_written_and_quantity_is_short()
    {
        var f = Ready();
        Seed(f);
        try
        {
            // 주간 빈자리 945-960 = 교체 15분뿐 — MC 뒤 생산 0분이면 MC 를 쓰지 않고 주간 수량 전량 Shortfall(야간으로도 안 넘긴다)
            PreexistingSlot(f, D0, "ITEST-APS-X", 480, 720);
            PreexistingSlot(f, D0, "ITEST-APS-X", 780, 945);
            int run = SeedRun(f);
            int pl  = SeedInj(f, run, ItemA, D0, day: 60, night: 0);

            var res = Run(f, run, new[] { pl });

            var o = Assert.Single(res.Orders);
            Assert.Empty(o.MoldChanges);
            Assert.Empty(Rows(f, LineInj, D0));                                                  // MC 행도 WO 슬롯도 없다
            Assert.Equal(60m, Assert.Single(o.Shortfalls, s => s.LineId == LineInj).Qty);
            Assert.Equal(60m, Assert.Single(o.Shortfalls, s => s.LineId == LineImg).Qty);
            Assert.Empty(Rows(f, LineImg, D0));
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Mold_change_with_no_room_in_any_window_writes_no_production_and_the_whole_day_is_short()
    {
        var f = Ready();
        Seed(f);
        try
        {
            // 남은 빈틈이 전부 10분(주간 710-720 · 950-960, 야간 1430-1440) < 교체 15분 — 새 금형으로 생산하면 안 되므로 MC 도 생산도 없다.
            // 종전에는 교체분 자리조차 없으면 빈틈 20분·10분에 새 금형 생산을 실었다
            PreexistingSlot(f, D0, "ITEST-APS-X", 480, 710);
            PreexistingSlot(f, D0, "ITEST-APS-X", 780, 950);
            PreexistingSlot(f, D0, "ITEST-APS-X", 960, 1430);
            int run = SeedRun(f);
            int pl  = SeedInj(f, run, ItemA, D0, day: 60, night: 60);

            var res = Run(f, run, new[] { pl });

            var o = Assert.Single(res.Orders);
            Assert.Empty(o.MoldChanges);
            Assert.Empty(Rows(f, LineInj, D0));                                                  // MC 행도 WO 슬롯도 없다
            Assert.Empty(Rows(f, LineImg, D0));
            Assert.Equal(120m, Assert.Single(o.Shortfalls, s => s.LineId == LineInj).Qty);        // 그 날 사출 수량 전량
            Assert.Equal(120m, Assert.Single(o.Shortfalls, s => s.LineId == LineImg).Qty);
            Assert.Equal("Released", (string)Scalar(f, "SELECT Status FROM dbo.PP_WorkOrder WHERE WoID = @W;", ("@W", o.WoId))!);   // PP-LSB 미배치 목록
            Assert.Equal(o.WoId, (int)Scalar(f, "SELECT WoID FROM dbo.PP_ApsPlanLine WHERE PlanLineID = @P;", ("@P", pl))!);
            Assert.Equal(ApsRepository.StatusReleased, RunStatus(f, run));                       // WO 가 연결됐고 남은 대상이 없다
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Rejects_item_without_mold_and_continues_with_others_leaving_run_saved()
    {
        var f = Ready();
        Seed(f);
        try
        {
            int run = SeedRun(f);
            int plC = SeedInj(f, run, ItemC, D0, day: 40, night: 0);
            int plA = SeedInj(f, run, ItemA, D0, day: 60, night: 0);

            var res = Run(f, run, new[] { plC, plA });

            var rej = Assert.Single(res.Rejected);
            Assert.Equal((plC, ItemC, D0, PpRepository.RejectNoMold), (rej.PlanLineId, rej.ItemNo, rej.PlanDate.ToDateTime(TimeOnly.MinValue), rej.Reason));
            Assert.Equal(ItemA, Assert.Single(res.Orders).ItemNo);
            Assert.Equal(0, WoCount(f, ItemC));
            Assert.Equal(DBNull.Value, Scalar(f, "SELECT WoID FROM dbo.PP_ApsPlanLine WHERE PlanLineID = @P;", ("@P", plC)));
            Assert.Equal(ApsRepository.StatusSaved, RunStatus(f, run));   // 거부 행이 WoID 없이 남아 Saved — 금형 등록 후 재실행
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Rejects_line_when_chosen_mold_has_no_uph_on_that_line()
    {
        var f = Ready();
        Seed(f);
        try
        {
            Exec(f, "DELETE FROM dbo.MD_MoldLine WHERE MoldID = @M;", ("@M", Mold));   // 후보는 남고(규칙 ③) 라인 UPH 만 없다
            int run = SeedRun(f);
            int pl  = SeedInj(f, run, ItemA, D0, day: 60, night: 0);

            var res = Run(f, run, new[] { pl });

            var rej = Assert.Single(res.Rejected);
            Assert.Equal((pl, PpRepository.RejectNoUph), (rej.PlanLineId, rej.Reason));
            Assert.Empty(res.Orders);
            Assert.Equal(0, WoCount(f, ItemA));
            Assert.Equal(ApsRepository.StatusSaved, RunStatus(f, run));
        }
        finally { Cleanup(f); }
    }

    /// 스펙 2026-10-05: BOM 규칙 사출 행 → 완제품 WO(INJ 단계 = 자식 슬롯, IMG 단계 = DeadlinePacker).
    /// K(150 = P1 100 + P2 50, 공급 100:50 비율) · K2(200 → P1 ×2 = 100). P1 = max(100, 100) = 100, P2 = 50.
    [SkippableFact]
    public void Bom_rule_rows_create_parent_work_orders_with_one_injection_slot_per_child_and_a_finished_step()
    {
        var f = Ready();
        Seed(f);
        try
        {
            SeedBomParents(f);
            int run = SeedRun(f);
            SeedBundleBom(f, run, new BomEdge(P1, K, 1), new BomEdge(P1, K2, 2), new BomEdge(P2, K, 1));
            var d1 = PlusWorkdays(D0, _offset);
            SeedLine(f, run, "ASM", P1, LineImg, d1, 0, 0, false, supply: 100);
            SeedLine(f, run, "ASM", P2, LineImg, d1, 0, 0, false, supply: 50);
            int plK  = SeedLine(f, run, "INJ", K,  LineInj, D0, 150, 0, false);
            int plK2 = SeedLine(f, run, "INJ", K2, LineInj, D0, 200, 0, false);

            var res = Run(f, run, new[] { plK, plK2 });

            Assert.Empty(res.Rejected);
            Assert.Equal(0, res.BomRuleExcluded);
            Assert.Equal(2, res.Created);
            var p1 = Assert.Single(res.Orders, o => o.ItemNo == P1);
            var p2 = Assert.Single(res.Orders, o => o.ItemNo == P2);
            Assert.Equal((100m, (int?)null), (p1.Qty, p1.SoId));
            Assert.Equal(50m, p2.Qty);
            Assert.Equal($"INJ@{LineInj},IMG@{LineImg}", Steps(f, p1.WoId));
            Assert.Equal($"INJ@{LineInj},IMG@{LineImg}", Steps(f, p2.WoId));

            var inj = Rows(f, LineInj, D0).Where(r => r.Type == "WO").ToList();
            var p1K  = Assert.Single(inj, r => r.WoId == p1.WoId && r.Mold == Mold2);     // K 몫 100 EA ÷ 60 UPH = 100분, 대표
            var p1K2 = inj.Where(r => r.WoId == p1.WoId && r.Mold == Mold3).ToList();     // K2 몫 200 EA ÷ 120 UPH = 100분 — K 그룹(150분) 뒤라 휴게(720~780)를 넘어 둘로 갈린다
            Assert.Equal((100, 100m), (p1K.End - p1K.Start, p1K.Qty));
            Assert.Equal((100, 200m), (p1K2.Sum(r => r.End - r.Start), p1K2.Sum(r => r.Qty)));
            Assert.All(p1K2, r => Assert.True(r.Start >= p1K.End + 50));                   // P2 의 K(50분) 뒤에 온다
            // 같은 자식 품번 K 가 두 부모(P1·P2)로 나뉜 조각은 형제(다른 품번 동시 취출)가 아니라 **같은 금형이 차례로 찍는 양**이다 — 사용자 결정 2026-10-06
            // (전에는 0분 형제 행으로 들어가 사출 부하가 조각 수만큼 과소 계산됐다): P2 의 K 50 EA ÷ 60 UPH = 50분, P1 의 K 뒤에 이어서, 금형도 기록
            var p2K = Assert.Single(inj, r => r.WoId == p2.WoId);
            Assert.Equal((p1K.End, 50, Mold2, 50m), (p2K.Start, p2K.End - p2K.Start, p2K.Mold, p2K.Qty));
            Assert.Equal(2, Rows(f, LineInj, D0).Count(r => r.Type == "MC"));              // M1 → M2, M2 → M3

            Assert.Equal(100m, SumQty(f, p1.WoId, LineImg));                                  // IMG = 모든 자식이 실은 몫(100, 200÷2) 의 최소
            Assert.Equal(50m,  SumQty(f, p2.WoId, LineImg));
            Assert.Empty(p1.Shortfalls); Assert.Empty(p2.Shortfalls);

            Assert.Equal(p1.WoId, (int)Scalar(f, "SELECT WoID FROM dbo.PP_ApsPlanLine WHERE PlanLineID = @P;", ("@P", plK))!);   // 품번순 첫 부모
            Assert.Equal(p1.WoId, (int)Scalar(f, "SELECT WoID FROM dbo.PP_ApsPlanLine WHERE PlanLineID = @P;", ("@P", plK2))!);
            Assert.Equal(3, (int)Scalar(f, "SELECT COUNT(*) FROM dbo.PP_ApsRunWo WHERE RunID = @R;", ("@R", run))!);   // (K,P1,100) (K,P2,50) (K2,P1,200)
            Assert.Equal(200m, Convert.ToDecimal(Scalar(f, "SELECT Qty FROM dbo.PP_ApsRunWo WHERE PlanLineID = @P AND WoID = @W;", ("@P", plK2), ("@W", p1.WoId))));
            Assert.Equal(ApsRepository.StatusReleased, RunStatus(f, run));
        }
        finally { Cleanup(f); }
    }

    /// BOM 규칙 WO 수량은 완제품 포장 단위(MD_Item.BoxQty, 없으면 APS_SETTING.ROUND_TO)로 올린다(10-06 사용자 요청) — 비율 분배로 생기는 2~3개짜리 WO 를 막는다.
    /// P1 BoxQty 30: 100 → 120, 자식 슬롯도 120 × QtyPer. P2 는 BoxQty 없음 → ROUND_TO(dev 5) → 50 그대로.
    [SkippableFact]
    public void Bom_rule_parent_quantity_rounds_up_to_the_parent_pack_size()
    {
        var f = Ready();
        Seed(f);
        try
        {
            SeedBomParents(f);
            Exec(f, "UPDATE dbo.MD_Item SET BoxQty = 30 WHERE ItemNo = @P1;", ("@P1", P1));
            int run = SeedRun(f);
            SeedBundleBom(f, run, new BomEdge(P1, K, 1), new BomEdge(P1, K2, 2), new BomEdge(P2, K, 1));
            var d1 = PlusWorkdays(D0, _offset);
            SeedLine(f, run, "ASM", P1, LineImg, d1, 0, 0, false, supply: 100);
            SeedLine(f, run, "ASM", P2, LineImg, d1, 0, 0, false, supply: 50);
            int plK  = SeedLine(f, run, "INJ", K,  LineInj, D0, 150, 0, false);
            int plK2 = SeedLine(f, run, "INJ", K2, LineInj, D0, 200, 0, false);

            var res = Run(f, run, new[] { plK, plK2 });

            var p1 = Assert.Single(res.Orders, o => o.ItemNo == P1);
            var p2 = Assert.Single(res.Orders, o => o.ItemNo == P2);
            Assert.Equal((120m, 50m), (p1.Qty, p2.Qty));
            var inj = Rows(f, LineInj, D0).Where(r => r.Type == "WO" && r.WoId == p1.WoId).ToList();
            Assert.Equal(120m, inj.Where(r => r.Mold == Mold2).Sum(r => r.Qty));   // K ×1
            Assert.Equal(240m, inj.Where(r => r.Mold == Mold3).Sum(r => r.Qty));   // K2 ×2 — 120분이 휴게를 건너 두 구간에 실릴 수 있다
            Assert.Empty(p1.Shortfalls);
            Assert.Equal(120m, SumQty(f, p1.WoId, LineImg));
        }
        finally { Cleanup(f); }
    }

    [Theory]
    [InlineData(100, 30, 120)] [InlineData(120, 30, 120)] [InlineData(2.4, 5, 5)] [InlineData(0, 5, 0)] [InlineData(7, 1, 7)] [InlineData(7.2, 0, 8)]
    public void RoundUpToPack_rounds_up_to_the_pack_multiple_and_treats_non_positive_pack_as_one(double qty, int pack, double expected)
        => Assert.Equal((decimal)expected, PpRepository.RoundUpToPack((decimal)qty, pack));

    /// 그 날·그 라인에 이미 있는 완제품 품번 슬롯(데모 WO)은 QtyPer 로 자식 몫에서 빠진 뒤 부모 단위로 차감된다 — P1 30 이 있으면 P1 = max(100−30, 100−30) = 70.
    [SkippableFact]
    public void Existing_parent_item_slots_on_the_child_line_reduce_the_parent_order()
    {
        var f = Ready();
        Seed(f);
        try
        {
            SeedBomParents(f);
            int oldWo = (int)Scalar(f, """
                INSERT INTO dbo.PP_WorkOrder (WoNumber, ItemNo, OrderQty, OpenQty, RoutingType, Status, CreatedBy, CreatedTS)
                OUTPUT INSERTED.WoID VALUES ('ITEST-APS-WOOLD', @P1, 30, 30, 'A', 'Released', @By, SYSDATETIME());
                """, ("@P1", P1), ("@By", Actor))!;
            WoSlot(f, oldWo, LineInj, D0, 480, 510, 30);
            int run = SeedRun(f);
            SeedBundleBom(f, run, new BomEdge(P1, K, 1), new BomEdge(P1, K2, 2), new BomEdge(P2, K, 1));
            var d1 = PlusWorkdays(D0, _offset);
            SeedLine(f, run, "ASM", P1, LineImg, d1, 0, 0, false, supply: 100);
            SeedLine(f, run, "ASM", P2, LineImg, d1, 0, 0, false, supply: 50);
            int plK  = SeedLine(f, run, "INJ", K,  LineInj, D0, 150, 0, false);
            int plK2 = SeedLine(f, run, "INJ", K2, LineInj, D0, 200, 0, false);

            var res = Run(f, run, new[] { plK, plK2 });

            Assert.Equal(70m, Assert.Single(res.Orders, o => o.ItemNo == P1).Qty);
            Assert.Equal(50m, Assert.Single(res.Orders, o => o.ItemNo == P2).Qty);
        }
        finally { Cleanup(f); }
    }

    /// 자식에 활성 금형이 없으면 그 자식 행은 NoMold 거부(부모 WO 없음), 간선이 없는 자식 행은 NoParent 거부 — 둘 다 실행은 Saved.
    [SkippableFact]
    public void Bom_rule_child_without_mold_or_parent_edge_is_rejected()
    {
        var f = Ready();
        Seed(f);
        try
        {
            SeedBomParents(f);
            int run = SeedRun(f);
            SeedBundleBom(f, run, new BomEdge(P1, ItemC, 1));                                 // C 는 금형 없음
            SeedLine(f, run, "ASM", P1, LineImg, PlusWorkdays(D0, _offset), 0, 0, false, supply: 100);
            int plC = SeedLine(f, run, "INJ", ItemC, LineInj, D0, 40, 0, false);
            int plK = SeedLine(f, run, "INJ", K,     LineInj, D0, 40, 0, false);           // 간선 없음
            int plA = SeedInj(f, run, ItemA, D0, day: 60, night: 0);

            var res = Run(f, run, new[] { plC, plK, plA });

            Assert.Equal(2, res.Rejected.Count);
            Assert.Contains(res.Rejected, r => r.PlanLineId == plC && r.Reason == PpRepository.RejectNoMold);
            Assert.Contains(res.Rejected, r => r.PlanLineId == plK && r.Reason == PpRepository.RejectNoParent);
            Assert.Equal(ItemA, Assert.Single(res.Orders).ItemNo);
            Assert.Equal(0, WoCount(f, P1));
            Assert.Equal(ApsRepository.StatusSaved, RunStatus(f, run));
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Rerun_skips_lines_with_wo_releases_when_no_target_remains_then_rejects_released_run()
    {
        var f = Ready();
        Seed(f);
        try
        {
            int run = SeedRun(f);
            int pl0 = SeedInj(f, run, ItemA, D0, day: 60, night: 0);
            int pl1 = SeedInj(f, run, ItemA, D1, day: 60, night: 0);

            var first = Run(f, run, new[] { pl0 });
            Assert.Equal(1, first.Created);
            Assert.Equal(ApsRepository.StatusSaved, RunStatus(f, run));   // pl1 이 남아 있다

            var second = Run(f, run, new[] { pl0, pl1 });
            Assert.Equal((1, 1), (second.Created, second.SkippedExisting));
            Assert.Equal(pl1, second.Orders.Single().PlanLineId);
            Assert.Empty(second.Orders.Single().MoldChanges);              // D0 마지막 금형 = 같은 금형
            Assert.Equal(ApsRepository.StatusReleased, RunStatus(f, run));
            Assert.Equal(2, WoCount(f, ItemA));

            var ex = Assert.Throws<InvalidOperationException>(() => Run(f, run, new[] { pl0, pl1 }));
            Assert.Contains("released", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(2, WoCount(f, ItemA));
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Subtracts_quantity_already_scheduled_that_day_and_skips_fully_covered_lines()
    {
        var f = Ready();
        Seed(f);
        try
        {
            int w0 = (int)Scalar(f, """
                INSERT INTO dbo.PP_WorkOrder (WoNumber, ItemNo, OrderQty, OpenQty, RoutingType, Status, CreatedBy)
                OUTPUT INSERTED.WoID VALUES ('ITEST-APS-W0', @I, 50, 50, 'A', 'Released', @By);
                """, ("@I", ItemA), ("@By", Actor))!;
            Exec(f, """
                INSERT INTO dbo.PP_LineSchedule (LineID, ScheduleDate, PatternID, EntryType, WoID, StartMin, EndMin, PlannedQty, MoldID, Status, CreatedBy)
                VALUES (@L, @D, @P, 'WO', @W, 480, 530, 50, @M, 'DRAFT', @By);
                """, ("@L", LineInj), ("@D", D0), ("@P", Pattern), ("@W", w0), ("@M", Mold), ("@By", Actor));

            int run = SeedRun(f);
            int pl  = SeedInj(f, run, ItemA, D0, day: 120, night: 0);   // 120 − 기존 50 = 70

            var res = Run(f, run, new[] { pl });

            var o = Assert.Single(res.Orders);
            Assert.Equal(70m, o.Qty);
            Assert.Empty(o.MoldChanges);                                  // 직전 금형 = 같은 금형
            var slot = Assert.Single(Rows(f, LineInj, D0), r => r.WoId == o.WoId);
            Assert.Equal((530, 600, 70m), (slot.Start, slot.End, slot.Qty));

            int run2 = SeedRun(f);
            int plCovered = SeedInj(f, run2, ItemA, D0, day: 50, night: 0);   // 그 날 슬롯 120 ≥ 50 → WO 없음
            var res2 = Run(f, run2, new[] { plCovered });
            Assert.Equal((0, 1), (res2.Created, res2.SkippedExisting));
            Assert.Equal(2, WoCount(f, ItemA));
            Assert.Equal(ApsRepository.StatusSaved, RunStatus(f, run2));
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Existing_day_slots_above_plan_day_are_carried_into_night_so_wo_never_exceeds_total_minus_existing()
    {
        var f = Ready();
        Seed(f);
        try
        {
            int w0 = (int)Scalar(f, """
                INSERT INTO dbo.PP_WorkOrder (WoNumber, ItemNo, OrderQty, OpenQty, RoutingType, Status, CreatedBy)
                OUTPUT INSERTED.WoID VALUES ('ITEST-APS-W0', @I, 80, 80, 'A', 'Released', @By);
                """, ("@I", ItemA), ("@By", Actor))!;
            Exec(f, """
                INSERT INTO dbo.PP_LineSchedule (LineID, ScheduleDate, PatternID, EntryType, WoID, StartMin, EndMin, PlannedQty, MoldID, Status, CreatedBy)
                VALUES (@L, @D, @P, 'WO', @W, 480, 560, 80, @M, 'DRAFT', @By);
                """, ("@L", LineInj), ("@D", D0), ("@P", Pattern), ("@W", w0), ("@M", Mold), ("@By", Actor));

            int run = SeedRun(f);
            int pl  = SeedInj(f, run, ItemA, D0, day: 50, night: 60);   // (50 + 60) − 기존 주간 80 = 30 → 주간 0 · 야간 30

            var res = Run(f, run, new[] { pl });

            var o = Assert.Single(res.Orders);
            Assert.Equal(30m, o.Qty);
            var slot = Assert.Single(Rows(f, LineInj, D0), r => r.WoId == o.WoId);
            Assert.Equal((960, 990, 30m), (slot.Start, slot.End, slot.Qty));
        }
        finally { Cleanup(f); }
    }

    [Theory]
    [InlineData(50, 60, 80, 0,  0,  30)]   // 주간 초과 30 → 야간에서 뺀다
    [InlineData(50, 60, 0,  80, 30, 0)]    // 야간 초과 20 → 주간에서 뺀다
    [InlineData(50, 60, 70, 50, 0,  0)]    // 둘 다 덮임
    [InlineData(50, 60, 20, 10, 30, 50)]   // 초과 없음
    public void SubtractScheduled_carries_one_shifts_excess_into_the_other(decimal planDay, decimal planNight, decimal exDay, decimal exNight,
                                                                           decimal day, decimal night)
    {
        Assert.Equal(new[] { day, night }, PpRepository.SubtractScheduled(new[] { planDay, planNight }, new[] { exDay, exNight }));   // 2교대 = 종전 규칙 그대로
    }

    [SkippableFact]
    public void Full_day_band_yields_shortfall_instead_of_spilling_into_night()
    {
        var f = Ready();
        Seed(f);
        try
        {
            // Review Focus 3: 주간 구간(480-720 · 780-960)이 이미 다 찬 날 — 주간 수량은 야간으로 넘기지 않고 ShortMin>0 → Shortfall
            PreexistingSlot(f, D0, Mold, 480, 720);
            PreexistingSlot(f, D0, Mold, 780, 960);
            int run = SeedRun(f);
            int pl  = SeedInj(f, run, ItemA, D0, day: 60, night: 0);

            var res = Run(f, run, new[] { pl });

            var o  = Assert.Single(res.Orders);
            var sf = Assert.Single(o.Shortfalls, s => s.LineId == LineInj);
            Assert.Equal(60m, sf.Qty);
            Assert.Equal(1, res.Short);
            Assert.DoesNotContain(o.Placements, p => p.LineId == LineInj && p.EndMin > p.StartMin);   // INJ 슬롯에 실린 분 0
            Assert.DoesNotContain(Rows(f, LineInj, D0), r => r.WoId == o.WoId && r.Start >= 960);   // 야간(960~)으로 넘기지 않는다
            Assert.Empty(o.MoldChanges);                                                           // 직전 금형 = 같은 금형
            Assert.Equal("Released", (string)Scalar(f, "SELECT Status FROM dbo.PP_WorkOrder WHERE WoID = @W;", ("@W", o.WoId))!);   // WO 는 Released — PP-LSB 미배치 목록에서 수동 배치
            // 완제품 단계는 사출이 실은 수량(0)까지만 — IMG 슬롯 없음, 나머지 전량은 IMG 단계 Shortfall
            Assert.DoesNotContain(o.Placements, p => p.LineId == LineImg);
            Assert.DoesNotContain(Rows(f, LineImg, D0), r => r.WoId == o.WoId);
            Assert.Equal(60m, Assert.Single(o.Shortfalls, s => s.LineId == LineImg).Qty);
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Finished_step_quantity_is_capped_at_the_injection_quantity_actually_placed()
    {
        var f = Ready();
        Seed(f);
        try
        {
            // 주간 빈자리 930-960 = 30분 → 60 UPH 로 30 EA 만 사출 — 완제품(IMG) 도 30 EA 만, 나머지 30 은 두 단계 모두 Shortfall
            PreexistingSlot(f, D0, Mold, 480, 720);
            PreexistingSlot(f, D0, Mold, 780, 930);
            int run = SeedRun(f);
            int pl  = SeedInj(f, run, ItemA, D0, day: 60, night: 0);

            var res = Run(f, run, new[] { pl });

            var o = Assert.Single(res.Orders);
            Assert.Equal(new[] { (930, 960, 30m) },
                         Rows(f, LineInj, D0).Where(r => r.WoId == o.WoId).Select(r => (r.Start, r.End, r.Qty)).ToArray());
            Assert.Equal(new[] { (960, 966, 30m) },                                               // 사출 끝(960) 뒤 12초 × 30 EA
                         Rows(f, LineImg, D0).Where(r => r.WoId == o.WoId).Select(r => (r.Start, r.End, r.Qty)).ToArray());
            Assert.Equal(30m, o.Placements.Where(p => p.LineId == LineImg).Sum(p => p.Qty));
            Assert.Equal(30m, Assert.Single(o.Shortfalls, s => s.LineId == LineInj).Qty);
            Assert.Equal(30m, Assert.Single(o.Shortfalls, s => s.LineId == LineImg).Qty);
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Sibling_on_full_day_band_is_short_too_and_never_lands_in_night()
    {
        var f = Ready();
        Seed(f);
        try
        {
            PreexistingSlot(f, D0, Mold, 480, 720);
            PreexistingSlot(f, D0, Mold, 780, 960);
            int run = SeedRun(f);
            int plA = SeedInj(f, run, ItemA, D0, day: 60, night: 0);
            int plB = SeedInj(f, run, ItemB, D0, day: 90, night: 0);

            var res = Run(f, run, new[] { plA, plB });

            var a = res.Orders.Single(o => o.ItemNo == ItemA);
            var b = res.Orders.Single(o => o.ItemNo == ItemB);
            Assert.Equal(60m, Assert.Single(a.Shortfalls, s => s.LineId == LineInj).Qty);
            Assert.Equal(90m, Assert.Single(b.Shortfalls, s => s.LineId == LineInj).Qty);       // 대표가 시간을 못 받았으니 형제도 못 찍는다
            Assert.Equal(2, res.Short);
            Assert.DoesNotContain(Rows(f, LineInj, D0), r => r.WoId == a.WoId || r.WoId == b.WoId);   // 0분 슬롯도 야간 위치에 두지 않는다
            Assert.DoesNotContain(Rows(f, LineImg, D0), r => r.WoId == a.WoId || r.WoId == b.WoId);   // 사출 0 → 완제품 슬롯도 없다
        }
        finally { Cleanup(f); }
    }

    /// <summary>완제품 라인도 라인 지정 → DEFAULT_PATTERN 순(사용자 지적 2026-10-06 "라인에 설정 안 했으면 기본 패턴을 따라야") — 기본 패턴이 가동 구간 없는 패턴이면
    /// IMG 단계는 PP_LineSchedule 저장 패턴이 있어도 전량 Shortfall, 사출(LINE-INJ-01 라인 지정)은 그대로 배치된다.</summary>
    [SkippableFact]
    public void Finished_step_follows_default_pattern_when_its_line_has_no_setting()
    {
        var f = Ready();
        Seed(f);
        try
        {
            Exec(f, "UPDATE dbo.MD_LineTimePattern SET Status = 'ACTIVE' WHERE PatternID = @PX;", ("@PX", ClosedPattern));
            ApsPatternConfig.Apply(f, ClosedPattern, (LineInj, Pattern));   // 기본 = 가동 없음, 사출 라인만 라인 지정
            int run = SeedRun(f);
            int pl  = SeedInj(f, run, ItemA, D0, day: 60, night: 0);

            var res = Run(f, run, new[] { pl });

            var o = Assert.Single(res.Orders);
            Assert.Contains(o.Placements, p => p.LineId == LineInj);                       // 사출은 라인 지정 패턴으로 배치
            Assert.DoesNotContain(o.Placements, p => p.LineId == LineImg);                 // 완제품 단계는 기본 패턴(가동 없음)을 따라 자리 없음
            Assert.Equal(60m, Assert.Single(o.Shortfalls, s => s.LineId == LineImg).Qty);
            Assert.DoesNotContain(Rows(f, LineImg, D0), r => r.WoId == o.WoId);
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Closed_line_day_places_nothing_and_reports_whole_quantity_short()
    {
        var f = Ready();
        Seed(f);
        try
        {
            // LINE-INJ-01 의 APS 패턴을 OPERATING 구간이 없는 패턴으로(ACTIVE 로 올려서) — 비가동일은 주간으로 보지 않고 전량 Shortfall.
            // 2026-10-06 부터 그 날 PP_LineSchedule 의 PatternID 는 APS 가 보지 않으므로 APS 라인 지정으로 건다
            Exec(f, "UPDATE dbo.MD_LineTimePattern SET Status = 'ACTIVE' WHERE PatternID = @PX;", ("@PX", ClosedPattern));
            ApsPatternConfig.Apply(f, null, (LineInj, ClosedPattern));
            int run = SeedRun(f);
            int pl  = SeedInj(f, run, ItemA, D2, day: 60, night: 30);

            var res = Run(f, run, new[] { pl });

            var o = Assert.Single(res.Orders);
            Assert.Equal(90m, o.Qty);
            Assert.Equal(90m, Assert.Single(o.Shortfalls, s => s.LineId == LineInj).Qty);
            Assert.Equal(90m, Assert.Single(o.Shortfalls, s => s.LineId == LineImg).Qty);
            Assert.Empty(o.Placements);                                                            // 사출 0 → 완제품 슬롯도 없다
            Assert.Empty(o.MoldChanges);
            Assert.DoesNotContain(Rows(f, LineInj, D2), r => r.WoId == o.WoId);
            Assert.DoesNotContain(Rows(f, LineImg, D2), r => r.WoId == o.WoId);
            Assert.Equal("Released", (string)Scalar(f, "SELECT Status FROM dbo.PP_WorkOrder WHERE WoID = @W;", ("@W", o.WoId))!);
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void All_selected_lines_already_linked_marks_run_released_without_creating()
    {
        var f = Ready();
        Seed(f);
        try
        {
            // Review Focus 4: 전 대상이 SkippedExisting(WoID 보유) 이어도 남은 대상이 없고 연결 WO 가 있으면 Released
            int run = SeedRun(f);
            int pl  = SeedInj(f, run, ItemA, D0, day: 60, night: 0);
            Exec(f, "UPDATE dbo.PP_ApsPlanLine SET WoID = 999999 WHERE PlanLineID = @P;", ("@P", pl));   // PP_ApsPlanLine.WoID 에는 FK 가 없다

            var res = Run(f, run, new[] { pl });

            Assert.Equal((0, 1), (res.Created, res.SkippedExisting));
            Assert.Empty(res.Orders);
            Assert.Equal(0, WoCount(f, ItemA));
            Assert.Equal(ApsRepository.StatusReleased, RunStatus(f, run));
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void DryRun_reports_the_same_result_as_the_real_run_but_rolls_back_everything()
    {
        var f = Ready();
        Seed(f);
        try
        {
            int so1 = SeedSo(f, "ITEST-APS-SO-D", ItemA, 100, dueOffset: 9);
            int run = SeedRun(f);
            int pl  = SeedInj(f, run, ItemA, D0, day: 120, night: 60);

            var dry = Run(f, run, new[] { pl }, dryRun: true);

            Assert.True(dry.DryRun);
            Assert.Equal(new (int?, decimal)[] { (so1, 100m), (null, 80m) }, dry.Orders.Select(o => (o.SoId, o.Qty)).ToArray());
            Assert.Equal(180m, dry.Orders.SelectMany(o => o.Placements).Where(p => p.LineId == LineInj).Sum(p => p.Qty));
            Assert.Equal(0, WoCount(f, ItemA));
            Assert.Equal(0, (int)Scalar(f, "SELECT COUNT(*) FROM dbo.PP_ApsRunWo WHERE RunID = @R;", ("@R", run))!);
            Assert.Equal(DBNull.Value, Scalar(f, "SELECT WoID FROM dbo.PP_ApsPlanLine WHERE PlanLineID = @P;", ("@P", pl)));
            Assert.Empty(Rows(f, LineInj, D0));
            Assert.Empty(Rows(f, LineImg, D0));
            Assert.Equal(ApsRepository.StatusSaved, RunStatus(f, run));

            var real = Run(f, run, new[] { pl });

            Assert.False(real.DryRun);
            Assert.Equal(dry.Orders.Count, real.Orders.Count);
            for (int i = 0; i < dry.Orders.Count; i++)
            {
                var (d, r) = (dry.Orders[i], real.Orders[i]);
                Assert.Equal((d.PlanLineId, d.ItemNo, d.PlanDate, d.SoId, d.Qty, d.Deadline, d.DueDate),
                             (r.PlanLineId, r.ItemNo, r.PlanDate, r.SoId, r.Qty, r.Deadline, r.DueDate));
                Assert.Equal(d.Placements, r.Placements);
                Assert.Equal(d.Shortfalls, r.Shortfalls);
                Assert.Equal(d.MoldChanges, r.MoldChanges);
            }
            Assert.Equal((dry.SkippedExisting, dry.BomRuleExcluded, dry.RejectedCount), (real.SkippedExisting, real.BomRuleExcluded, real.RejectedCount));
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Release_failure_rolls_back_every_wo_and_slot_of_the_call()
    {
        var f = Ready();
        Seed(f);
        try
        {
            // B(D1)의 ASM 행 라인이 IMG 라인이 아니다 → B 의 ReleaseCore 검증 실패(IMG 단계에 PNT 라인). 그룹은 계획일 순이라
            // A(D0)의 WO·INJ/IMG 슬롯·PP_ApsRunWo·계획 행 WoID 가 먼저 다 쓰인 뒤 실패한다 — 그것까지 전부 롤백(스펙 §9)
            int run = SeedRun(f);
            int plA = SeedInj(f, run, ItemA, D0, day: 60, night: 0);
            int plB = SeedInj(f, run, ItemB, D1, day: 30, night: 0, asmLine: LinePnt);

            var ex = Assert.Throws<InvalidOperationException>(() => Run(f, run, new[] { plA, plB }));
            Assert.Contains(LinePnt, ex.Message);                                                  // B(D1) 단계에서 실패했다

            Assert.Equal((0, 0), (WoCount(f, ItemA), WoCount(f, ItemB)));
            Assert.Equal(0, (int)Scalar(f, "SELECT COUNT(*) FROM dbo.PP_ApsRunWo WHERE RunID = @R;", ("@R", run))!);
            Assert.Equal(0, (int)Scalar(f, "SELECT COUNT(*) FROM dbo.PP_ApsPlanLine WHERE RunID = @R AND WoID IS NOT NULL;", ("@R", run))!);
            Assert.Empty(Rows(f, LineInj, D0));
            Assert.Empty(Rows(f, LineImg, D0));
            Assert.Empty(Rows(f, LineInj, D1));
            Assert.Equal(ApsRepository.StatusSaved, RunStatus(f, run));

            // 대조: 같은 실행에서 A 만 고르면 D0 의 WO·슬롯이 실제로 쓰인다 — 위 빈 결과는 롤백 때문이다
            var ok = Run(f, run, new[] { plA });
            Assert.Equal(1, ok.Created);
            Assert.NotEmpty(Rows(f, LineInj, D0));
            Assert.NotEmpty(Rows(f, LineImg, D0));
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Rejects_released_run_before_touching_anything()
    {
        var f = Ready();
        Seed(f);
        try
        {
            int run = SeedRun(f);
            int pl  = SeedInj(f, run, ItemA, D0, day: 60, night: 0);
            Exec(f, "UPDATE dbo.PP_ApsRun SET Status = 'Released' WHERE RunID = @R;", ("@R", run));

            Assert.Throws<InvalidOperationException>(() => Run(f, run, new[] { pl }));
            Assert.Equal(0, WoCount(f, ItemA));
        }
        finally { Cleanup(f); }
    }
}

/// <summary>교대별 기존 슬롯 차감(2026-10-07) — 어떤 교대의 초과분은 다음 교대부터, 끝까지 가면 앞 교대에서 뺀다. 순수 함수.</summary>
public class ApsShiftSubtractTests
{
    [Fact]
    public void Excess_in_one_shift_is_taken_from_the_following_shifts_in_order()
    {
        Assert.Equal(new[] { 0m, 30m, 60m }, PpRepository.SubtractScheduled(new[] { 50m, 60m, 60m }, new[] { 80m, 0m, 0m }));   // A 초과 30 → B 에서
        Assert.Equal(new[] { 50m, 0m, 10m }, PpRepository.SubtractScheduled(new[] { 50m, 60m, 60m }, new[] { 0m, 110m, 0m }));  // B 초과 50 → C 에서
        Assert.Equal(new[] { 20m, 0m, 0m }, PpRepository.SubtractScheduled(new[] { 50m, 60m, 60m }, new[] { 0m, 0m, 150m }));   // 마지막 교대 초과 → 앞으로 거슬러
        Assert.Equal(new[] { 0m, 0m }, PpRepository.SubtractScheduled(new[] { 50m, 60m }, new[] { 200m, 0m }));
        Assert.Equal(new[] { 50m, 60m }, PpRepository.SubtractScheduled(new[] { 50m, 60m }, new[] { 0m, 0m }));
    }
}
