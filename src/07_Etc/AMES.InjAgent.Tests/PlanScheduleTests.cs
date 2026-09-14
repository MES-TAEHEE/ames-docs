using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Data.Scheduling;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.InjAgent.Tests;

/// <summary>
/// PP-003 계획 확정 → WO 생성 + Release + 마감일 기준 다일·분할 자동 배치 (한 트랜잭션).
/// AMES_DEV 통합 테스트, DB 미기동 시 skip. 라인/스테이션은 개발 시드(LINE-INJ-01, LINE-IMG-01, ST-INJ-01, ST-IMG-01)에 의존.
/// 배치 시작일(startDate)은 먼 미래의 월요일(+400일 이후)로 고정하고, 그 주 월~금 (라인, 일자) 에 ITEST 패턴
/// placeholder 행을 미리 두어 패턴 해석을 고정한다. 그 주에 SYS_FactoryCalendar HOLIDAY 행이 없다고 가정한다.
/// </summary>
// MoldPlanningTests 와 같은 LINE-INJ-01/LINE-IMG-01 + 같은 +400일 주(D0)를 쓰므로 같은 컬렉션으로 묶어 직렬화한다.
[Collection("AMES_DEV plan week")]
public class PlanScheduleTests
{
    static readonly string Conn =
        Environment.GetEnvironmentVariable("AMES_TEST_CONN")
        ?? "Server=98.95.142.192,1433;Database=AMES_DEV;User Id=ames_app;Password=!Dev2026;TrustServerCertificate=True;Encrypt=True;";

    const string Item     = "ITEST-PS-RTA";
    const string Pattern  = "ITEST-PS-PAT";
    const string LineInj  = "LINE-INJ-01";
    const string LineImg  = "LINE-IMG-01";
    static readonly DateTime D0 = NextMonday(DateTime.Today.AddDays(400));
    static readonly DateTime D1 = D0.AddDays(1);
    static readonly DateTime D4 = D0.AddDays(4);   // 금요일
    static readonly DateTime[] Week = Enumerable.Range(0, 5).Select(i => D0.AddDays(i)).ToArray();

    static DateTime NextMonday(DateTime d) => d.Date.AddDays(((int)DayOfWeek.Monday - (int)d.DayOfWeek + 7) % 7);

    static AmesConnectionFactory? TryFactory()
    {
        try { var f = new AmesConnectionFactory(Conn); using var c = f.OpenConnection(); return f; }
        catch { return null; }
    }

    static void Exec(AmesConnectionFactory f, string sql, params (string, object)[] ps)
    {
        using var conn = f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    static object? Scalar(AmesConnectionFactory f, string sql, params (string, object)[] ps)
    {
        using var conn = f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        return cmd.ExecuteScalar();
    }

    static List<(string Line, DateTime Date, int Start, int End, decimal Qty, string? Status, string? Pattern)> Slots(AmesConnectionFactory f, int woId)
    {
        using var conn = f.OpenConnection();
        using var cmd  = new SqlCommand("""
            SELECT LineID, ScheduleDate, StartMin, EndMin, PlannedQty, Status, PatternID
            FROM   dbo.PP_LineSchedule WHERE WoID = @W ORDER BY ScheduleDate, StartMin, LineID;
            """, conn);
        cmd.Parameters.AddWithValue("@W", woId);
        using var rdr = cmd.ExecuteReader();
        var list = new List<(string, DateTime, int, int, decimal, string?, string?)>();
        while (rdr.Read())
            list.Add(((string)rdr["LineID"], (DateTime)rdr["ScheduleDate"],
                      Convert.ToInt32(rdr["StartMin"]), Convert.ToInt32(rdr["EndMin"]),
                      rdr.GetDecimal(rdr.GetOrdinal("PlannedQty")), rdr["Status"] as string, rdr["PatternID"] as string));
        return list;
    }

    /// <summary>A 라우팅 품목 + BOP(ST-INJ-01 사이클 6초 = 0.1분/EA, ST-IMG-01 사이클 12초 = 0.2분/EA)
    /// + 가동 패턴(08:00~12:00, 휴식, 13:00~18:00 = 540분) + 두 라인 월~금 placeholder.</summary>
    static void Seed(AmesConnectionFactory f)
    {
        Cleanup(f);
        Exec(f, """
            INSERT INTO dbo.MD_Item (ItemNo, ItemName, RoutingType, ActiveFlag, CreatedBy)
            VALUES (@I, N'ITEST plan schedule', 'A', 1, 'ITEST');
            INSERT INTO dbo.MD_Mold (MoldID, MoldName, Status, MoldChangeMin, CreatedBy)
            VALUES ('ITEST-PS-M', N'ITEST plan mold', 'AVAILABLE', 0, 'ITEST');
            INSERT INTO dbo.MD_MoldItem (MoldID, ItemNo, Color, CavitySeq, CavityPos, CavityCount, MoldCategory, ActiveFlag, CreatedBy)
            VALUES ('ITEST-PS-M', @I, 'CBK', 1, 'LH', 1, 'INJECTION', 1, 'ITEST');
            INSERT INTO dbo.MD_Bop (BOPID, ItemNo, RoutingType, StepSeq, StationCode, StdCycleTime, ActiveFlag, CreatedBy)
            VALUES ('ITEST-PS-BOP-10', @I, 'A', 10, 'ST-INJ-01', 6,  1, 'ITEST'),
                   ('ITEST-PS-BOP-20', @I, 'A', 20, 'ST-IMG-01', 12, 1, 'ITEST');
            INSERT INTO dbo.MD_LineTimePattern (PatternID, LineID, PatternName, Status, CreatedBy)
            VALUES (@P, NULL, N'ITEST pattern', 'ACTIVE', 'ITEST');
            INSERT INTO dbo.MD_LineTimeSegment (SegmentID, PatternID, SeqNo, StartMin, EndMin, SegmentState, ShiftCode, CreatedBy)
            VALUES ('ITEST-PS-SEG-1', @P, 1, 480,  720,  'OPERATING', 'A', 'ITEST'),
                   ('ITEST-PS-SEG-2', @P, 2, 720,  780,  'BREAK',     'A', 'ITEST'),
                   ('ITEST-PS-SEG-3', @P, 3, 780,  1080, 'OPERATING', 'A', 'ITEST');
            """, ("@I", Item), ("@P", Pattern));
        foreach (var d in Week)
            Exec(f, """
                INSERT INTO dbo.PP_LineSchedule (LineID, ScheduleDate, PatternID, EntryType, PlannedQty, Status, CreatedBy)
                VALUES (@LI, @D, @P, 'WO', 0, 'DRAFT', 'ITEST'),
                       (@LM, @D, @P, 'WO', 0, 'DRAFT', 'ITEST');
                """, ("@P", Pattern), ("@LI", LineInj), ("@LM", LineImg), ("@D", d));
    }

    static void Cleanup(AmesConnectionFactory f)
    {
        Exec(f, """
            DELETE s FROM dbo.PP_LineSchedule s JOIN dbo.PP_WorkOrder w ON w.WoID = s.WoID WHERE w.ItemNo = @I;
            DELETE s FROM dbo.PP_LineSchedule s JOIN dbo.PP_WorkOrder w ON w.WoID = s.RefID AND s.RefType = 'WO' WHERE w.ItemNo = @I;
            DELETE FROM dbo.PP_LineSchedule WHERE CreatedBy = 'ITEST' AND ScheduleDate BETWEEN @D0 AND @D4;
            DELETE r FROM dbo.PP_WorkOrderRouting r JOIN dbo.PP_WorkOrder w ON w.WoID = r.WoID WHERE w.ItemNo = @I;
            DELETE FROM dbo.PP_WorkOrder     WHERE ItemNo = @I;
            DELETE FROM dbo.PP_CustomerOrder WHERE ItemNo = @I;
            DELETE FROM dbo.MD_Bop           WHERE ItemNo = @I;
            DELETE FROM dbo.MD_MoldItem WHERE MoldID = 'ITEST-PS-M';
            DELETE FROM dbo.MD_Mold     WHERE MoldID = 'ITEST-PS-M';
            DELETE FROM dbo.MD_Item          WHERE ItemNo = @I;
            DELETE FROM dbo.MD_LineTimeSegment WHERE PatternID = @P;
            DELETE FROM dbo.MD_LineTimePattern WHERE PatternID = @P;
            """, ("@I", Item), ("@P", Pattern), ("@D0", D0), ("@D4", D4));
    }

    /// <summary>납기 = D0 + dueOffset 일. 기본 9 → 다음 주 수요일 → 마감일(−3근무일) = 이번 주 금요일 D4.</summary>
    static int SeedSo(AmesConnectionFactory f, string soNo, decimal qty = 50, int dueOffset = 9) => (int)Scalar(f, """
        INSERT INTO dbo.PP_CustomerOrder (SoNumber, SoLineNo, ItemNo, OrderQty, RequestedDeliveryDate, Status, CreatedBy)
        OUTPUT INSERTED.SoID
        VALUES (@S, 1, @I, @Q, @Due, 'Confirmed', 'ITEST');
        """, ("@S", soNo), ("@I", Item), ("@Q", qty), ("@Due", D0.AddDays(dueOffset)))!;

    static int WoIdOf(AmesConnectionFactory f, string woNumber) =>
        (int)Scalar(f, "SELECT WoID FROM dbo.PP_WorkOrder WHERE WoNumber = @W;", ("@W", woNumber))!;

    static PpRepository.OrderPlan Plan(int soId, decimal? qty = null) =>
        new(soId, new[] { new PpRepository.StepChoice(1, LineInj), new PpRepository.StepChoice(2, LineImg) }, qty);

    static PpRepository.ScheduledCreateResult Run(AmesConnectionFactory f, params PpRepository.OrderPlan[] plans) =>
        new PpRepository(f).CreateScheduledWorkOrders(plans, "itest", useNetReq: false, startDate: D0);

    static decimal WoQty(AmesConnectionFactory f, string woNumber) =>
        (decimal)Scalar(f, "SELECT OrderQty FROM dbo.PP_WorkOrder WHERE WoNumber = @W;", ("@W", woNumber))!;

    static PpRepository.PlanLineRow? Candidate(AmesConnectionFactory f, int soId) =>
        new PpRepository(f).ListPlanCandidates("", null, null).FirstOrDefault(r => r.SoId == soId);

    // ── 직접 입력 수량 + 분할 발행 ───────────────────────────────────────────

    [SkippableFact]
    public void Manual_qty_overrides_basis_and_remainder_can_be_issued_later()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f);
        try
        {
            var so = SeedSo(f, "SO-ITEST-PS-Q1", qty: 50);

            var first = Run(f, Plan(so, 20));
            Assert.Equal(20m, WoQty(f, Assert.Single(first.Orders).WoNumber));

            // 부분 발행된 수주는 후보에 남고 기발행·잔량이 보인다
            var cand = Candidate(f, so);
            Assert.NotNull(cand);
            Assert.Equal((20m, 30m, 30m), (cand!.IssuedQty, cand.RemainQty, cand.NetReq));

            var second = Run(f, Plan(so, 30));
            Assert.Equal(30m, WoQty(f, Assert.Single(second.Orders).WoNumber));
            Assert.Null(Candidate(f, so));   // 전량 발행 → 후보에서 빠진다
            Assert.Equal(2, (int)Scalar(f, "SELECT COUNT(*) FROM dbo.PP_WorkOrder WHERE SoID = @S AND Status <> 'Cancelled';", ("@S", so))!);
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Order_basis_defaults_to_remaining_after_partial_issue()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f);
        try
        {
            var so = SeedSo(f, "SO-ITEST-PS-Q2", qty: 50);
            Run(f, Plan(so, 20));

            var res = Run(f, Plan(so));   // 수량 미지정 = 수주량 기준 → 잔량 30

            Assert.Equal(30m, WoQty(f, Assert.Single(res.Orders).WoNumber));
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Manual_qty_may_exceed_order_qty()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f);
        try
        {
            var so  = SeedSo(f, "SO-ITEST-PS-Q3", qty: 50);
            var res = Run(f, Plan(so, 80));

            Assert.Equal(80m, WoQty(f, Assert.Single(res.Orders).WoNumber));
            Assert.Null(Candidate(f, so));   // 잔량 0 이하 → 후보에서 빠진다
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Zero_manual_qty_creates_nothing()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f);
        try
        {
            var so  = SeedSo(f, "SO-ITEST-PS-Q4", qty: 50);
            var res = Run(f, Plan(so, 0));

            Assert.Empty(res.Orders);
            Assert.Equal(0, (int)Scalar(f, "SELECT COUNT(*) FROM dbo.PP_WorkOrder WHERE SoID = @S;", ("@S", so))!);
        }
        finally { Cleanup(f); }
    }

    static void BlockInjAllWeek(AmesConnectionFactory f)
    {
        foreach (var d in Week)
            Exec(f, """
                INSERT INTO dbo.PP_LineSchedule (LineID, ScheduleDate, PatternID, EntryType, StartMin, EndMin, PlannedQty, Title, Status, CreatedBy)
                VALUES (@L, @D, @P, 'PM', 480, 720,  0, N'ITEST PM', 'DRAFT', 'ITEST'),
                       (@L, @D, @P, 'PM', 780, 1080, 0, N'ITEST PM', 'DRAFT', 'ITEST');
                """, ("@L", LineInj), ("@D", d), ("@P", Pattern));
    }

    [SkippableFact]
    public void Releases_wo_stamps_deadline_and_places_steps_from_start_date()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f);
        try
        {
            var so  = SeedSo(f, "SO-ITEST-PS-1");

            var res = Run(f, Plan(so));

            Assert.Equal(1, res.Created);
            var o    = Assert.Single(res.Orders);
            var woId = WoIdOf(f, o.WoNumber);
            Assert.Equal("Released", Scalar(f, "SELECT Status FROM dbo.PP_WorkOrder WHERE WoID = @W;", ("@W", woId)));
            Assert.Equal(D4, (DateTime)Scalar(f, "SELECT ProdDeadline FROM dbo.PP_WorkOrder WHERE WoID = @W;", ("@W", woId))!);
            Assert.Equal(D4, o.Deadline);
            Assert.Equal(LineInj, Scalar(f, "SELECT LineID FROM dbo.PP_WorkOrderRouting WHERE WoID = @W AND StepSeq = 1;", ("@W", woId)));
            Assert.Equal(LineImg, Scalar(f, "SELECT LineID FROM dbo.PP_WorkOrderRouting WHERE WoID = @W AND StepSeq = 2;", ("@W", woId)));

            // INJ 50 EA × 0.1분 = 5분, IMG 50 EA × 0.2분 = 10분 — 같은 날, INJ 첫 슬롯과 같은 시각부터
            var slots = Slots(f, woId);
            var inj = Assert.Single(slots, s => s.Line == LineInj);
            var img = Assert.Single(slots, s => s.Line == LineImg);
            Assert.Equal((D0, 480, 485, 50m), (inj.Date, inj.Start, inj.End, inj.Qty));
            Assert.Equal((D0, 480, 490, 50m), (img.Date, img.Start, img.End, img.Qty));   // IMG 는 INJ 첫 슬롯과 같은 시각부터 (수량 흐름 무관)
            Assert.All(slots, s => { Assert.Equal("DRAFT", s.Status); Assert.Equal(Pattern, s.Pattern); });
            Assert.Empty(o.Shortfalls);
            Assert.Equal(0m, o.LateQty);
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Large_qty_splits_across_days_and_next_step_follows()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f);
        try
        {
            var so  = SeedSo(f, "SO-ITEST-PS-2", qty: 6000);   // INJ 600분 → D0 540 + D1 60, IMG 1200분

            var res = Run(f, Plan(so));

            var o     = Assert.Single(res.Orders);
            var slots = Slots(f, WoIdOf(f, o.WoNumber));
            var inj   = slots.Where(s => s.Line == LineInj).ToList();
            var img   = slots.Where(s => s.Line == LineImg).ToList();
            Assert.Equal(6000m, inj.Sum(s => s.Qty));
            Assert.Equal(5400m, inj.Where(s => s.Date == D0).Sum(s => s.Qty));
            Assert.Equal(600m,  inj.Where(s => s.Date == D1).Sum(s => s.Qty));
            Assert.Equal(6000m, img.Sum(s => s.Qty));
            Assert.Equal((D0, 480), (img[0].Date, img[0].Start));    // INJ 첫 슬롯과 같은 날·같은 시각부터
            Assert.Empty(o.Shortfalls);
            Assert.Equal(0m, o.LateQty);
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Batch_is_scheduled_in_due_date_order_not_input_order()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f);
        try
        {
            var later   = SeedSo(f, "SO-ITEST-PS-3A", dueOffset: 10);
            var earlier = SeedSo(f, "SO-ITEST-PS-3B", dueOffset: 9);

            var res = Run(f, Plan(later), Plan(earlier));

            Assert.Equal(2, res.Created);
            Assert.Equal(earlier, res.Orders[0].SoId);
            var a = Slots(f, WoIdOf(f, res.Orders[0].WoNumber)).First(s => s.Line == LineInj);
            var b = Slots(f, WoIdOf(f, res.Orders[1].WoNumber)).First(s => s.Line == LineInj);
            Assert.Equal((480, 485), (a.Start, a.End));
            Assert.Equal((485, 490), (b.Start, b.End));
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void No_room_until_due_reports_shortfall_but_wo_is_still_released()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f);
        try
        {
            BlockInjAllWeek(f);
            var so  = SeedSo(f, "SO-ITEST-PS-4", dueOffset: 4);   // 납기 = 금요일 D4 → 그 주 안에 INJ 자리 없음

            var res = Run(f, Plan(so));

            var o    = Assert.Single(res.Orders);
            var woId = WoIdOf(f, o.WoNumber);
            Assert.Equal("Released", Scalar(f, "SELECT Status FROM dbo.PP_WorkOrder WHERE WoID = @W;", ("@W", woId)));
            Assert.Equal(new[] { new DeadlinePacker.StepShortfall(1, LineInj, 50m), new DeadlinePacker.StepShortfall(2, LineImg, 50m) }, o.Shortfalls);
            Assert.Empty(Slots(f, woId));
            Assert.Equal(1, res.Short);
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Missing_line_for_required_step_rolls_back_the_whole_batch()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f);
        try
        {
            var so1 = SeedSo(f, "SO-ITEST-PS-5A");
            var so2 = SeedSo(f, "SO-ITEST-PS-5B");
            var bad = new PpRepository.OrderPlan(so2, new[] { new PpRepository.StepChoice(1, LineInj) });   // IMG 단계 누락

            Assert.Throws<InvalidOperationException>(() => Run(f, Plan(so1), bad));

            Assert.Equal(0, (int)Scalar(f, "SELECT COUNT(*) FROM dbo.PP_WorkOrder WHERE ItemNo = @I;", ("@I", Item))!);
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Buffer_workdays_come_from_sys_config_not_the_default()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f);
        Exec(f, "UPDATE dbo.SYS_Config SET ConfigValue = N'2' WHERE ConfigKey = @K;", ("@K", PpRepository.BufferWorkdaysKey));
        try
        {
            var so  = SeedSo(f, "SO-ITEST-PS-6");   // 납기 = 다음 주 수요일 → −2근무일 = 다음 주 월요일

            var res = Run(f, Plan(so));

            Assert.Equal(D0.AddDays(7), Assert.Single(res.Orders).Deadline);
        }
        finally
        {
            Exec(f, "UPDATE dbo.SYS_Config SET ConfigValue = N'3' WHERE ConfigKey = @K;", ("@K", PpRepository.BufferWorkdaysKey));
            Cleanup(f);
        }
    }

    // ── PP-003 그리드 라인 부하 ─────────────────────────────────────────────

    [SkippableFact]
    public void Line_load_pct_is_slot_minutes_over_operating_minutes_for_the_coming_week()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f);
        try
        {
            Run(f, Plan(SeedSo(f, "SO-ITEST-PS-7A")));   // 라인은 "같은 품번의 최근 WO 첫 단계" 로 해석되므로 WO 하나를 먼저 만든다
            SeedSo(f, "SO-ITEST-PS-7B");                  // 후보 그리드에 뜨는 WO 미생성 확정 수주

            var rows = new PpRepository(f).ListPlanCandidates("", null, null);

            var row = Assert.Single(rows, r => r.SoNumber == "SO-ITEST-PS-7B");
            Assert.Equal(LineInj, row.LineId);
            // 독립 재계산: 오늘부터 7일 중 근무일(달력 행 없으면 토·일 제외)의 WO 슬롯 분 ÷ 가동 분
            var lsb   = new LineScheduleRepository(f);
            var today = DateTime.Today;
            int op = 0, wo = 0;
            for (var d = today; d < today.AddDays(PpRepository.LoadWindowDays); d = d.AddDays(1))
            {
                if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                var cap = lsb.GetDayCapacity(row.LineId!, d);
                op += cap.OperatingMin; wo += cap.WoLoadMin;
            }
            Assert.Equal(op > 0 ? (int?)(wo * 100L / op) : null, row.LineLoadPct);
        }
        finally { Cleanup(f); }
    }

    // ── 하루 능력 조회 (다이얼로그 잔여 표시용) — 기존 그대로 ────────────────

    [SkippableFact]
    public void GetDayCapacity_reports_operating_minus_pm_and_wo_load()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f);
        try
        {
            Exec(f, """
                INSERT INTO dbo.PP_LineSchedule (LineID, ScheduleDate, PatternID, EntryType, StartMin, EndMin, PlannedQty, Title, Status, CreatedBy)
                VALUES (@L, @D, @P, 'PM', 480, 540, 0, N'ITEST PM', 'DRAFT', 'ITEST');
                """, ("@L", LineInj), ("@D", D0), ("@P", Pattern));

            var cap = new LineScheduleRepository(f).GetDayCapacity(LineInj, D0);

            Assert.Equal(Pattern, cap.PatternId);
            Assert.Equal(480, cap.DayStart);
            Assert.Equal(480, cap.OperatingMin);     // 240 + 300 − PM 60
            Assert.Equal(0,   cap.WoLoadMin);
            Assert.Equal(480, cap.RemainMin);
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void GetDayCapacity_falls_back_to_active_pattern_when_day_has_no_rows()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f);
        try
        {
            var d2 = D4.AddDays(3);   // placeholder 없는 다음 주 월요일
            var cap = new LineScheduleRepository(f).GetDayCapacity(LineInj, d2);

            Assert.NotNull(cap.PatternId);   // 라인 전용 또는 전역 ACTIVE 패턴 중 하나
            Assert.Equal(0, cap.WoLoadMin);
        }
        finally { Cleanup(f); }
    }
}
