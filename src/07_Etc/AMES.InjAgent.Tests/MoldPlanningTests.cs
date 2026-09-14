using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Data.Services;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.InjAgent.Tests;

/// <summary>
/// 금형 교체 시간의 계획 반영 — AMES_DEV 통합 테스트, DB 미기동 시 skip.
/// 시드: 품번 ITEST-MC-RTA(A 라우팅, INJ 6초/IMG 12초 BOP), 패턴 ITEST-MC-PAT(08~12, 13~18 = 540분),
/// 금형 ITEST-MC-A(기본 20분) / ITEST-MC-B(기본 50분, LINE-INJ-01 오버라이드 15분). 배치 주간은 +400일 뒤 월~금.
/// </summary>
// PlanScheduleTests 와 같은 LINE-INJ-01/LINE-IMG-01 + 같은 +400일 주(D0)를 쓰므로 같은 컬렉션으로 묶어 직렬화한다.
[Collection("AMES_DEV plan week")]
public class MoldPlanningTests
{
    static readonly string Conn =
        Environment.GetEnvironmentVariable("AMES_TEST_CONN")
        ?? "Server=98.95.142.192,1433;Database=AMES_DEV;User Id=ames_app;Password=!Dev2026;TrustServerCertificate=True;Encrypt=True;";

    const string Item    = "ITEST-MC-RTA";
    const string Pattern = "ITEST-MC-PAT";
    const string MoldA   = "ITEST-MC-A";
    const string MoldB   = "ITEST-MC-B";
    const string LineInj = "LINE-INJ-01";
    const string LineImg = "LINE-IMG-01";
    static readonly DateTime D0 = NextMonday(DateTime.Today.AddDays(400));
    static readonly DateTime D1 = D0.AddDays(1);
    static readonly DateTime D4 = D0.AddDays(4);
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

    /// <summary>(라인, 일자)의 모든 행 — EntryType·시각·금형·참조.</summary>
    static List<(string Type, int Start, int End, string? Mold, int? WoId, string? RefType, int? RefId, string? Title)> Rows(AmesConnectionFactory f, string line, DateTime date)
    {
        using var conn = f.OpenConnection();
        using var cmd  = new SqlCommand("""
            SELECT EntryType, ISNULL(StartMin,0) AS StartMin, ISNULL(EndMin,0) AS EndMin, MoldID, WoID, RefType, RefID, Title
            FROM   dbo.PP_LineSchedule WHERE LineID = @L AND ScheduleDate = @D AND ISNULL(EndMin,0) > ISNULL(StartMin,0)
            ORDER  BY StartMin;
            """, conn);
        cmd.Parameters.AddWithValue("@L", line); cmd.Parameters.AddWithValue("@D", date);
        using var rdr = cmd.ExecuteReader();
        var list = new List<(string, int, int, string?, int?, string?, int?, string?)>();
        while (rdr.Read())
            list.Add(((string)rdr["EntryType"], Convert.ToInt32(rdr["StartMin"]), Convert.ToInt32(rdr["EndMin"]),
                      rdr["MoldID"] as string, rdr["WoID"] as int?, rdr["RefType"] as string, rdr["RefID"] as int?, rdr["Title"] as string));
        return list;
    }

    static void Seed(AmesConnectionFactory f, bool withMoldItems = true)
    {
        Cleanup(f);
        Exec(f, """
            INSERT INTO dbo.MD_Item (ItemNo, ItemName, RoutingType, ActiveFlag, CreatedBy)
            VALUES (@I, N'ITEST mold change', 'A', 1, 'ITEST');
            INSERT INTO dbo.MD_Bop (BOPID, ItemNo, RoutingType, StepSeq, StationCode, StdCycleTime, ActiveFlag, CreatedBy)
            VALUES ('ITEST-MC-BOP-10', @I, 'A', 10, 'ST-INJ-01', 6,  1, 'ITEST'),
                   ('ITEST-MC-BOP-20', @I, 'A', 20, 'ST-IMG-01', 12, 1, 'ITEST');
            INSERT INTO dbo.MD_LineTimePattern (PatternID, LineID, PatternName, Status, CreatedBy)
            VALUES (@P, NULL, N'ITEST mc pattern', 'ACTIVE', 'ITEST');
            INSERT INTO dbo.MD_LineTimeSegment (SegmentID, PatternID, SeqNo, StartMin, EndMin, SegmentState, ShiftCode, CreatedBy)
            VALUES ('ITEST-MC-SEG-1', @P, 1, 480,  720,  'OPERATING', 'A', 'ITEST'),
                   ('ITEST-MC-SEG-2', @P, 2, 720,  780,  'BREAK',     'A', 'ITEST'),
                   ('ITEST-MC-SEG-3', @P, 3, 780,  1080, 'OPERATING', 'A', 'ITEST');
            INSERT INTO dbo.MD_Mold (MoldID, MoldName, Status, MoldChangeMin, CreatedBy)
            VALUES (@A, N'ITEST mold A', 'AVAILABLE', 20, 'ITEST'),
                   (@B, N'ITEST mold B', 'AVAILABLE', 50, 'ITEST');
            INSERT INTO dbo.MD_MoldLine (LineCode, MoldID, UPH, PrepTime, CreatedBy)
            VALUES (@LI, @B, 60, 15, 'ITEST');
            """, ("@I", Item), ("@P", Pattern), ("@A", MoldA), ("@B", MoldB), ("@LI", LineInj));
        if (withMoldItems)
            // NOTE: PK_MD_MoldItem is (MoldID, ItemNo) only (dist/AMES_Schema.sql:441) — no CavitySeq
            // column in the key, so a mold can map to a given item with exactly one row. The brief's
            // original seed had two rows for (MoldA, Item) with CavitySeq 1/2, which violates the PK;
            // collapsed to one row per (MoldID, ItemNo) here (see task-4-report.md for the trace).
            Exec(f, """
                INSERT INTO dbo.MD_MoldItem (MoldID, ItemNo, Color, CavitySeq, CavityPos, CavityCount, MoldCategory, ActiveFlag, CreatedBy)
                VALUES (@A, @I, 'CBK', 1, 'LH', 2, 'INJECTION', 1, 'ITEST'),
                       (@B, @I, 'CBK', 1, 'LH', 1, 'INJECTION', 1, 'ITEST');
                """, ("@I", Item), ("@A", MoldA), ("@B", MoldB));
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
            DELETE FROM dbo.MNT_EquipmentStatus WHERE CreatedBy = 'ITEST';
            DELETE r FROM dbo.PP_WorkOrderRouting r JOIN dbo.PP_WorkOrder w ON w.WoID = r.WoID WHERE w.ItemNo = @I;
            DELETE FROM dbo.PP_WorkOrder     WHERE ItemNo = @I;
            DELETE FROM dbo.PP_CustomerOrder WHERE ItemNo = @I;
            DELETE FROM dbo.MD_Bop           WHERE ItemNo = @I;
            DELETE FROM dbo.MD_Item          WHERE ItemNo = @I;
            DELETE FROM dbo.MD_LineTimeSegment WHERE PatternID = @P;
            DELETE FROM dbo.MD_LineTimePattern WHERE PatternID = @P;
            DELETE FROM dbo.MD_MoldItem WHERE MoldID IN (@A, @B);
            DELETE FROM dbo.MD_MoldLine WHERE MoldID IN (@A, @B);
            DELETE FROM dbo.MD_Mold     WHERE MoldID IN (@A, @B);
            """, ("@I", Item), ("@P", Pattern), ("@A", MoldA), ("@B", MoldB), ("@D0", D0), ("@D4", D4));
    }

    /// <summary>INJ 라인 지정일에 기존 WO 슬롯을 지정 금형으로 넣는다 — 직전 금형 상태를 만든다.</summary>
    static void PreexistingSlot(AmesConnectionFactory f, DateTime date, string mold, int start = 480, int end = 600) =>
        Exec(f, """
            INSERT INTO dbo.PP_LineSchedule (LineID, ScheduleDate, PatternID, EntryType, StartMin, EndMin, PlannedQty, MoldID, Status, CreatedBy)
            VALUES (@L, @D, @P, 'WO', @S, @E, 0, @M, 'DRAFT', 'ITEST');
            """, ("@L", LineInj), ("@D", date), ("@P", Pattern), ("@S", start), ("@E", end), ("@M", mold));

    // ── LineScheduleRepository ────────────────────────────────────────────

    [SkippableFact]
    public void GetDayCapacity_counts_change_block_as_load_and_reports_last_mold()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f);
        try
        {
            PreexistingSlot(f, D0, MoldB, 480, 600);
            Exec(f, """
                INSERT INTO dbo.PP_LineSchedule (LineID, ScheduleDate, PatternID, EntryType, StartMin, EndMin, PlannedQty, MoldID, Title, RefType, Status, CreatedBy)
                VALUES (@L, @D, @P, 'MC', 600, 620, 0, @A, N'ITEST-MC-B→ITEST-MC-A', 'WO', 'DRAFT', 'ITEST');
                """, ("@L", LineInj), ("@D", D0), ("@P", Pattern), ("@A", MoldA));
            PreexistingSlot(f, D0, MoldA, 620, 700);

            var cap = new LineScheduleRepository(f).GetDayCapacity(LineInj, D0);

            Assert.Equal(220, cap.WoLoadMin);        // 120 + 20 + 80
            Assert.Equal(700, cap.LastWoEnd);
            Assert.Equal(MoldA, cap.LastMoldId);
            Assert.Equal(3, cap.Occupied.Count);
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void LineLastMoldBefore_uses_latest_scheduled_day()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f);
        try
        {
            PreexistingSlot(f, D0, MoldB, 480, 600);
            PreexistingSlot(f, D0, MoldA, 600, 700);
            var lsb = new LineScheduleRepository(f);

            Assert.Equal((D0, MoldA), lsb.GetLineLastMoldBefore(LineInj, D1));
            Assert.Equal((D0, MoldA), lsb.GetLineLastMoldBefore(LineInj, D4));
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void LineLastMoldBefore_falls_back_to_mounted_mold_with_min_date()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f);
        try
        {
            Exec(f, """
                INSERT INTO dbo.MNT_EquipmentStatus (EquipID, LineID, Status, MountedMoldID, CreatedBy, CreatedTS, ModifiedTS)
                VALUES (NULL, @L, 'RUN', @B, 'ITEST', SYSDATETIME(), SYSDATETIME());
                """, ("@L", LineInj), ("@B", MoldB));

            var got = new LineScheduleRepository(f).GetLineLastMoldBefore(LineInj, D0);

            Assert.Equal((DateTime.MinValue, MoldB), got);
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void SaveSchedule_keeps_change_blocks_and_mold_ids()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f);
        try
        {
            var lsb = new LineScheduleRepository(f);
            lsb.SaveSchedule(LineInj, D0, Pattern,
                slots:    Array.Empty<(int WoId, int StartMin, int EndMin, decimal Qty, string? MoldId)>(),
                pmBands:  Array.Empty<(int StartMin, int EndMin, string? Title, string? RefType, int? RefId)>(),
                actor:    "itest",
                mcBlocks: new[] { (StartMin: 600, EndMin: 620, Title: (string?)"ITEST-MC-B→ITEST-MC-A", WoRefId: (int?)null, MoldId: (string?)MoldA) });

            var rows = Rows(f, LineInj, D0);
            var mc = Assert.Single(rows);
            Assert.Equal(("MC", 600, 620, MoldA, "WO"), (mc.Type, mc.Start, mc.End, mc.Mold, mc.RefType));

            var back = lsb.GetSchedule(LineInj, D0).Single(r => r.EntryType == "MC");
            Assert.Equal(MoldA, back.MoldId);
        }
        finally { Cleanup(f); }
    }

    // ── MasterDataRepository ──────────────────────────────────────────────

    [SkippableFact]
    public void ListMoldCandidates_reports_line_assignment_and_effective_change_min()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f);
        try
        {
            var cands = new MasterDataRepository(f).ListMoldCandidates(Item, LineInj);

            Assert.Equal(new[] { (MoldA, false, 20), (MoldB, true, 15) },
                         cands.Select(c => (c.MoldId, c.AssignedToLine, c.ChangeMin)).ToArray());   // LH/RH 두 행이라도 금형당 1건
            Assert.Empty(new MasterDataRepository(f).ListMoldCandidates("ITEST-NOPE", LineInj));
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Mold_change_min_round_trips_through_insert_list_update()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f);
        try
        {
            var md = new MasterDataRepository(f);
            md.UpdateMold(MoldA, "ITEST mold A", null, null, null, null, null, null, "AVAILABLE", "itest", moldChangeMin: 25);
            Assert.Equal(25, md.ListMolds().Single(m => m.MoldID == MoldA).MoldChangeMin);

            md.InsertMold("ITEST-MC-C", "ITEST mold C", null, null, null, null, null, null, "AVAILABLE", "itest", moldChangeMin: 7);
            Assert.Equal(7, md.ListMolds().Single(m => m.MoldID == "ITEST-MC-C").MoldChangeMin);
        }
        finally
        {
            Exec(f, "DELETE FROM dbo.MD_Mold WHERE MoldID = 'ITEST-MC-C';");
            Cleanup(f);
        }
    }

    // ── PpRepository.CreateScheduledWorkOrders ────────────────────────────

    static int SeedSo(AmesConnectionFactory f, string soNo, decimal qty = 50, int dueOffset = 9) => (int)Scalar(f, """
        INSERT INTO dbo.PP_CustomerOrder (SoNumber, SoLineNo, ItemNo, OrderQty, RequestedDeliveryDate, Status, CreatedBy)
        OUTPUT INSERTED.SoID
        VALUES (@S, 1, @I, @Q, @Due, 'Confirmed', 'ITEST');
        """, ("@S", soNo), ("@I", Item), ("@Q", qty), ("@Due", D0.AddDays(dueOffset)))!;

    static PpRepository.OrderPlan Plan(int soId) =>
        new(soId, new[] { new PpRepository.StepChoice(1, LineInj), new PpRepository.StepChoice(2, LineImg) });

    static PpRepository.ScheduledCreateResult Run(AmesConnectionFactory f, params PpRepository.OrderPlan[] plans) =>
        new PpRepository(f).CreateScheduledWorkOrders(plans, "itest", useNetReq: false, startDate: D0);

    static int WoIdOf(AmesConnectionFactory f, string woNumber) =>
        (int)Scalar(f, "SELECT WoID FROM dbo.PP_WorkOrder WHERE WoNumber = @W;", ("@W", woNumber))!;

    [SkippableFact]
    public void Rejects_order_whose_item_has_no_mold_and_creates_no_wo()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f, withMoldItems: false);
        try
        {
            var so = SeedSo(f, "ITEST-MC-SO-1");
            var res = Run(f, Plan(so));

            Assert.Empty(res.Orders);
            var rej = Assert.Single(res.Rejected);
            Assert.Equal((so, Item, PpRepository.RejectNoMold), (rej.SoId, rej.ItemNo, rej.Reason));
            Assert.Equal(0, (int)Scalar(f, "SELECT COUNT(*) FROM dbo.PP_WorkOrder WHERE ItemNo = @I;", ("@I", Item))!);
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Inserts_change_block_before_wo_when_line_switches_mold()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f);
        try
        {
            // 직전 금형은 후보에 없는 X — 규칙 ② 로 라인 배정된 B(15분) 가 선택돼 교체가 필요하다
            PreexistingSlot(f, D0, "ITEST-MC-X", 480, 600);
            var so  = SeedSo(f, "ITEST-MC-SO-2", qty: 50);
            var res = Run(f, Plan(so));

            var o = Assert.Single(res.Orders);
            Assert.Empty(res.Rejected);
            int woId = WoIdOf(f, o.WoNumber);
            var rows = Rows(f, LineInj, D0);

            // MC 600~615 → WO 615~620 (50 EA × 0.1분)
            Assert.Contains(rows, r => r.Type == "MC" && r.Start == 600 && r.End == 615 && r.Mold == MoldB
                                     && r.RefType == "WO" && r.RefId == woId && r.Title == "ITEST-MC-X→ITEST-MC-B" && r.WoId is null);
            var wo = Assert.Single(rows, r => r.Type == "WO" && r.WoId == woId);
            Assert.Equal((615, 620, MoldB), (wo.Start, wo.End, wo.Mold));
            var mc = Assert.Single(o.MoldChanges);
            Assert.Equal(("ITEST-MC-X", MoldB, 600, 615), (mc.FromMoldId, mc.ToMoldId, mc.StartMin, mc.EndMin));
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Same_mold_on_line_gets_no_change_block_and_img_step_never_does()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f);
        try
        {
            PreexistingSlot(f, D0, MoldA, 480, 600);
            var so  = SeedSo(f, "ITEST-MC-SO-3", qty: 50);
            var res = Run(f, Plan(so));

            var o = Assert.Single(res.Orders);
            Assert.Empty(o.MoldChanges);
            int woId = WoIdOf(f, o.WoNumber);
            Assert.DoesNotContain(Rows(f, LineInj, D0), r => r.Type == "MC");
            Assert.Equal(MoldA, Rows(f, LineInj, D0).Single(r => r.WoId == woId).Mold);          // 규칙 ① 직전 금형 유지
            Assert.All(Rows(f, LineImg, D0).Where(r => r.WoId == woId), r => Assert.Null(r.Mold)); // IMG 는 금형 없음
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void CancelWo_removes_its_change_block()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f);
        try
        {
            PreexistingSlot(f, D0, "ITEST-MC-X", 480, 600);
            var so  = SeedSo(f, "ITEST-MC-SO-4", qty: 50);
            var res = Run(f, Plan(so));
            int woId = WoIdOf(f, res.Orders.Single().WoNumber);
            Assert.Contains(Rows(f, LineInj, D0), r => r.Type == "MC" && r.RefId == woId);

            new WorkOrderRepository(f).CancelWo(woId, "itest");

            Assert.DoesNotContain(Rows(f, LineInj, D0), r => r.Type == "MC" || r.WoId == woId);
        }
        finally { Cleanup(f); }
    }
}
