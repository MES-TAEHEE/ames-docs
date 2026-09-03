using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.InjAgent.Tests;

/// <summary>
/// PP-003 계획 확정 → WO 생성 + Release + 단계별 라인 스케줄 배치 (한 트랜잭션).
/// AMES_DEV 통합 테스트, DB 미기동 시 skip. 라인/스테이션은 개발 시드(LINE-INJ-01, LINE-IMG-01, ST-INJ-01, ST-IMG-01)에 의존.
/// 스케줄 일자는 먼 미래(+400일)로 잡고, (라인, 일자) 에 ITEST 패턴 placeholder 행을 미리 두어 패턴 해석을 고정한다.
/// </summary>
public class PlanScheduleTests
{
    static readonly string Conn =
        Environment.GetEnvironmentVariable("AMES_TEST_CONN")
        ?? "Server=192.168.2.137,1433;Database=AMES_DEV;User Id=ames_app;Password=!Dev2026;TrustServerCertificate=True;Encrypt=True;Connect Timeout=10;";

    const string Item     = "ITEST-PS-RTA";
    const string Pattern  = "ITEST-PS-PAT";
    const string LineInj  = "LINE-INJ-01";
    const string LineImg  = "LINE-IMG-01";
    static readonly DateTime D0 = DateTime.Today.AddDays(400);
    static readonly DateTime D1 = D0.AddDays(1);

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

    /// <summary>A 라우팅 품목 + BOP(ST-INJ-01 사이클 6초, ST-IMG-01) + 가동 패턴(08:00~12:00, 휴식, 13:00~18:00) + 두 라인 D0/D1 placeholder.</summary>
    static void Seed(AmesConnectionFactory f)
    {
        Cleanup(f);
        Exec(f, """
            INSERT INTO dbo.MD_Item (ItemNo, ItemName, RoutingType, ActiveFlag, CreatedBy)
            VALUES (@I, N'ITEST plan schedule', 'A', 1, 'ITEST');
            INSERT INTO dbo.MD_Bop (BOPID, ItemNo, RoutingType, StepSeq, StationCode, StdCycleTime, ActiveFlag, CreatedBy)
            VALUES ('ITEST-PS-BOP-10', @I, 'A', 10, 'ST-INJ-01', 6, 1, 'ITEST'),
                   ('ITEST-PS-BOP-20', @I, 'A', 20, 'ST-IMG-01', NULL, 1, 'ITEST');
            INSERT INTO dbo.MD_LineTimePattern (PatternID, LineID, PatternName, Status, CreatedBy)
            VALUES (@P, NULL, N'ITEST pattern', 'ACTIVE', 'ITEST');
            INSERT INTO dbo.MD_LineTimeSegment (SegmentID, PatternID, SeqNo, StartMin, EndMin, SegmentState, ShiftCode, CreatedBy)
            VALUES ('ITEST-PS-SEG-1', @P, 1, 480,  720,  'OPERATING', 'A', 'ITEST'),
                   ('ITEST-PS-SEG-2', @P, 2, 720,  780,  'BREAK',     'A', 'ITEST'),
                   ('ITEST-PS-SEG-3', @P, 3, 780,  1080, 'OPERATING', 'A', 'ITEST');
            INSERT INTO dbo.PP_LineSchedule (LineID, ScheduleDate, PatternID, EntryType, PlannedQty, Status, CreatedBy)
            VALUES (@LI, @D0, @P, 'WO', 0, 'DRAFT', 'ITEST'),
                   (@LI, @D1, @P, 'WO', 0, 'DRAFT', 'ITEST'),
                   (@LM, @D0, @P, 'WO', 0, 'DRAFT', 'ITEST'),
                   (@LM, @D1, @P, 'WO', 0, 'DRAFT', 'ITEST');
            """, ("@I", Item), ("@P", Pattern), ("@LI", LineInj), ("@LM", LineImg), ("@D0", D0), ("@D1", D1));
    }

    static void Cleanup(AmesConnectionFactory f)
    {
        Exec(f, """
            DELETE s FROM dbo.PP_LineSchedule s JOIN dbo.PP_WorkOrder w ON w.WoID = s.WoID WHERE w.ItemNo = @I;
            DELETE FROM dbo.PP_LineSchedule WHERE CreatedBy = 'ITEST' AND ScheduleDate IN (@D0, @D1);
            DELETE r FROM dbo.PP_WorkOrderRouting r JOIN dbo.PP_WorkOrder w ON w.WoID = r.WoID WHERE w.ItemNo = @I;
            DELETE FROM dbo.PP_WorkOrder     WHERE ItemNo = @I;
            DELETE FROM dbo.PP_CustomerOrder WHERE ItemNo = @I;
            DELETE FROM dbo.MD_Bop           WHERE ItemNo = @I;
            DELETE FROM dbo.MD_Item          WHERE ItemNo = @I;
            DELETE FROM dbo.MD_LineTimeSegment WHERE PatternID = @P;
            DELETE FROM dbo.MD_LineTimePattern WHERE PatternID = @P;
            """, ("@I", Item), ("@P", Pattern), ("@D0", D0), ("@D1", D1));
    }

    static int SeedSo(AmesConnectionFactory f, string soNo, decimal qty = 50) => (int)Scalar(f, """
        INSERT INTO dbo.PP_CustomerOrder (SoNumber, SoLineNo, ItemNo, OrderQty, RequestedDeliveryDate, Status, CreatedBy)
        OUTPUT INSERTED.SoID
        VALUES (@S, 1, @I, @Q, DATEADD(day, 410, CAST(GETDATE() AS date)), 'Confirmed', 'ITEST');
        """, ("@S", soNo), ("@I", Item), ("@Q", qty))!;

    static int WoIdOf(AmesConnectionFactory f, string woNumber) =>
        (int)Scalar(f, "SELECT WoID FROM dbo.PP_WorkOrder WHERE WoNumber = @W;", ("@W", woNumber))!;

    static PpRepository.OrderPlan Plan(int soId, DateTime injDate, DateTime imgDate, int injMin = 60, int imgMin = 30) =>
        new(soId, new[]
        {
            new PpRepository.StepPlan(1, LineInj, injDate, injMin),
            new PpRepository.StepPlan(2, LineImg, imgDate, imgMin),
        });

    [SkippableFact]
    public void Releases_wo_and_places_each_step_on_its_own_line_and_date()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f);
        try
        {
            var so = SeedSo(f, "SO-ITEST-PS-1");

            var res = new PpRepository(f).CreateScheduledWorkOrders(new[] { Plan(so, D0, D1) }, "itest", useNetReq: false);

            Assert.Single(res.Created);
            Assert.Empty(res.Unplaced);
            var woId = WoIdOf(f, res.Created[0]);
            Assert.Equal("Released", Scalar(f, "SELECT Status FROM dbo.PP_WorkOrder WHERE WoID = @W;", ("@W", woId)));
            Assert.Equal(LineInj, Scalar(f, "SELECT LineID FROM dbo.PP_WorkOrderRouting WHERE WoID = @W AND StepSeq = 1;", ("@W", woId)));
            Assert.Equal(LineImg, Scalar(f, "SELECT LineID FROM dbo.PP_WorkOrderRouting WHERE WoID = @W AND StepSeq = 2;", ("@W", woId)));

            var slots = Slots(f, woId);
            Assert.Equal(2, slots.Count);
            Assert.Equal((LineInj, D0, 480, 540, 50m, "DRAFT", Pattern), slots[0]);
            Assert.Equal((LineImg, D1, 480, 510, 50m, "DRAFT", Pattern), slots[1]);
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Same_day_next_step_starts_after_previous_step_ends()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f);
        try
        {
            var so  = SeedSo(f, "SO-ITEST-PS-2");
            var res = new PpRepository(f).CreateScheduledWorkOrders(new[] { Plan(so, D0, D0) }, "itest", useNetReq: false);

            var slots = Slots(f, WoIdOf(f, res.Created[0]));
            Assert.Equal((LineInj, D0, 480, 540), (slots[0].Line, slots[0].Date, slots[0].Start, slots[0].End));
            Assert.Equal((LineImg, D0, 540, 570), (slots[1].Line, slots[1].Date, slots[1].Start, slots[1].End));
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Rows_in_one_batch_queue_on_the_same_line_and_date()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f);
        try
        {
            var so1 = SeedSo(f, "SO-ITEST-PS-3A");
            var so2 = SeedSo(f, "SO-ITEST-PS-3B");
            var res = new PpRepository(f).CreateScheduledWorkOrders(
                new[] { Plan(so1, D0, D1), Plan(so2, D0, D1) }, "itest", useNetReq: false);

            Assert.Equal(2, res.Created.Count);
            var a = Slots(f, WoIdOf(f, res.Created[0]))[0];
            var b = Slots(f, WoIdOf(f, res.Created[1]))[0];
            Assert.Equal((480, 540), (a.Start, a.End));
            Assert.Equal((540, 600), (b.Start, b.End));
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Step_without_room_is_reported_unplaced_but_wo_is_still_released()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f);
        try
        {
            // INJ 라인 D0 의 가동 시간을 PM 밴드로 전부 막는다
            Exec(f, """
                INSERT INTO dbo.PP_LineSchedule (LineID, ScheduleDate, PatternID, EntryType, StartMin, EndMin, PlannedQty, Title, Status, CreatedBy)
                VALUES (@L, @D, @P, 'PM', 480, 720,  0, N'ITEST PM', 'DRAFT', 'ITEST'),
                       (@L, @D, @P, 'PM', 780, 1080, 0, N'ITEST PM', 'DRAFT', 'ITEST');
                """, ("@L", LineInj), ("@D", D0), ("@P", Pattern));
            var so  = SeedSo(f, "SO-ITEST-PS-4");

            var res = new PpRepository(f).CreateScheduledWorkOrders(new[] { Plan(so, D0, D1) }, "itest", useNetReq: false);

            Assert.Single(res.Created);
            var woId = WoIdOf(f, res.Created[0]);
            Assert.Equal("Released", Scalar(f, "SELECT Status FROM dbo.PP_WorkOrder WHERE WoID = @W;", ("@W", woId)));
            Assert.Equal(new[] { (res.Created[0], 1) }, res.Unplaced.Select(u => (u.WoNumber, u.StepSeq)).ToArray());
            var slots = Slots(f, woId);
            Assert.Single(slots);
            Assert.Equal(LineImg, slots[0].Line);
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
            var bad = new PpRepository.OrderPlan(so2, new[] { new PpRepository.StepPlan(1, LineInj, D0, 60) });   // IMG 단계 누락

            Assert.Throws<InvalidOperationException>(() =>
                new PpRepository(f).CreateScheduledWorkOrders(new[] { Plan(so1, D0, D1), bad }, "itest", useNetReq: false));

            Assert.Equal(0, (int)Scalar(f, "SELECT COUNT(*) FROM dbo.PP_WorkOrder WHERE ItemNo = @I;", ("@I", Item))!);
        }
        finally { Cleanup(f); }
    }

    // ── 하루 능력 조회 (다이얼로그 잔여 표시용) ─────────────────────────────

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
            var d2 = D1.AddDays(1);
            var cap = new LineScheduleRepository(f).GetDayCapacity(LineInj, d2);

            Assert.NotNull(cap.PatternId);   // 라인 전용 또는 전역 ACTIVE 패턴 중 하나
            Assert.Equal(0, cap.WoLoadMin);
        }
        finally { Cleanup(f); }
    }
}
