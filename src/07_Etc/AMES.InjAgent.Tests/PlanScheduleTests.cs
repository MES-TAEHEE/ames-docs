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
            DELETE FROM dbo.PP_LineSchedule WHERE CreatedBy = 'ITEST' AND ScheduleDate BETWEEN @D0 AND @D4;
            DELETE r FROM dbo.PP_WorkOrderRouting r JOIN dbo.PP_WorkOrder w ON w.WoID = r.WoID WHERE w.ItemNo = @I;
            DELETE FROM dbo.PP_WorkOrder     WHERE ItemNo = @I;
            DELETE FROM dbo.PP_CustomerOrder WHERE ItemNo = @I;
            DELETE FROM dbo.MD_Bop           WHERE ItemNo = @I;
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

    static PpRepository.OrderPlan Plan(int soId) =>
        new(soId, new[] { new PpRepository.StepChoice(1, LineInj), new PpRepository.StepChoice(2, LineImg) });

    static PpRepository.ScheduledCreateResult Run(AmesConnectionFactory f, params PpRepository.OrderPlan[] plans) =>
        new PpRepository(f).CreateScheduledWorkOrders(plans, "itest", useNetReq: false, startDate: D0);

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

            // INJ 50 EA × 0.1분 = 5분, IMG 50 EA × 0.2분 = 10분 — 같은 날 INJ 뒤에
            var slots = Slots(f, woId);
            Assert.Equal(new[] { (LineInj, D0, 480, 485, 50m, (string?)"DRAFT", (string?)Pattern), (LineImg, D0, 485, 495, 50m, (string?)"DRAFT", (string?)Pattern) }, slots);
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
            Assert.Equal((D1, 540), (img[0].Date, img[0].Start));    // INJ 의 D1 슬롯(480~540) 뒤
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
            var a = Slots(f, WoIdOf(f, res.Orders[0].WoNumber))[0];
            var b = Slots(f, WoIdOf(f, res.Orders[1].WoNumber))[0];
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
