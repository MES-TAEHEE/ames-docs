using AMES.Contracts.Dto;
using AMES.Contracts.Enums;
using AMES.Data.Connection;
using AMES.Pop.Services;
using Xunit;

namespace AMES.Pop.Tests;

/// <summary>
/// 안돈·재작업·QC 오프라인(10-28) — DB 가 끊긴 상태(DbHealth 실패 기록)에서 사본·큐만으로 동작하는지. DB 반영은 OfflineReplayDbTests 쪽.
/// </summary>
[Collection("OfflineAuth")]
public sealed class OfflineModulesTests : IDisposable
{
    private const string Line = "LINE-INJ-01";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ames-offmod-" + Guid.NewGuid().ToString("N"));
    private readonly string _origDir = OfflineStore.Dir;

    public OfflineModulesTests()
    {
        OfflineStore.Dir = _dir;
        OfflineWork.ResetForTests();
        OfflineReads.ResetForTests();
        DbHealth.RecordFailure();
    }

    public void Dispose()
    {
        DbHealth.RecordSuccess();
        OfflineStore.Dir = _origDir;
        OfflineWork.ResetForTests();
        OfflineReads.ResetForTests();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed class Names : IBadgeResolver
    {
        public string? ResolveName(string workerNo) => workerNo == "W009" ? "현장 작업자" : null;
    }

    private static void SeedAndonMasters(params string[] supervisors)
    {
        OfflineReads.Put("andon.causes", new List<AndonCauseDto> { new("MACHINE", "설비", "Machine", "MAINT") });
        OfflineReads.Put("andon.depts", new List<AndonDeptDto> { new("MAINT", "보전", "Maint"), new("QUALITY", "품질", "Quality") });
        OfflineReads.Put("andon.severities", new List<AndonSeverityDto> { new("MAJOR", "중", "Major") });
        if (supervisors.Length > 0) OfflineReads.Put("andon.sup." + Line, supervisors.ToList());
    }

    private static AndonWorkflow Workflow()
        => new(new OfflineAwareAndonStore(new ThrowingStore(), Line), new Names(), Line, "EQ-1", "E001");

    // 오프라인에서는 DB 저장소를 부르면 안 된다.
    private sealed class ThrowingStore : IAndonStore
    {
        static Exception No() => new InvalidOperationException("DB store called while offline");
        public AndonCallDto? GetOpenForLine(string lineId) => throw No();
        public int Raise(string lineId, string? equipId, string employeeNo) => throw No();
        public bool IsLineSupervisor(string lineId, string workerNo) => throw No();
        public List<AndonCauseDto> ListCauses() => throw No();
        public List<AndonDeptDto> ListDepts() => throw No();
        public List<AndonSeverityDto> ListSeverities() => throw No();
        public void AcknowledgeBySupervisor(int andonId, string workerNo, string? name, string operatorNo) => throw No();
        public void CallDepts(int andonId, string causeCode, string severity, IEnumerable<string> deptCodes, string calledBy, string operatorNo) => throw No();
        public void RecordArrival(int deptCallId, string workerNo, string? name, string operatorNo) => throw No();
        public void AckDept(int deptCallId, string operatorNo) => throw No();
        public void Resolve(int andonId, string? causeCode, string? severity, string operatorNo) => throw No();
    }

    [Fact]
    public void Andon_runs_the_whole_flow_offline_and_queues_every_step_in_order()
    {
        SeedAndonMasters("S001");
        var wf = Workflow();
        var rejects = new List<AndonReject>();
        var resolved = 0;
        wf.Rejected += rejects.Add;
        wf.Resolved += _ => resolved++;

        Assert.True(wf.Raise());
        Assert.Equal(AndonUiState.Open, wf.State);
        Assert.True(wf.Call!.AndonId < 0);   // 임시 번호 — 복구 때 실제 번호로

        wf.OnBadgeScan("W009");                       // 슈퍼바이저 명단에 없다
        Assert.Contains(AndonReject.NotSupervisor, rejects);
        wf.OnBadgeScan("EOS*S001*김반장");
        Assert.Equal(AndonUiState.SupAcked, wf.State);
        Assert.Equal("김반장", wf.Call!.SupervisorName);

        wf.SelectCause("MACHINE");                    // 기본 부서 MAINT 가 같이 골라진다
        wf.SelectSeverity("MAJOR");
        wf.ToggleDept("QUALITY");
        wf.CallDepts();
        Assert.Equal(AndonUiState.DeptCalled, wf.State);
        Assert.Equal(new[] { "MAINT", "QUALITY" }, wf.Call!.Depts.Select(d => d.DeptCode).OrderBy(x => x));
        Assert.All(wf.Call.Depts, d => Assert.True(d.DeptCallId < 0));

        wf.OnBadgeScan("W009");                       // 두 부서가 기다리니 고른다
        Assert.Equal(AndonUiState.PickDept, wf.State);
        var maint = wf.PendingArrivalDepts.Single(d => d.DeptCode == "MAINT").DeptCallId;
        wf.AssignArrival(maint);
        wf.OnBadgeScan("EOS*W010*품질담당");           // 남은 한 부서는 바로 도착
        Assert.All(wf.Call!.Depts, d => Assert.True(d.IsArrived));

        foreach (var d in wf.Call.Depts.ToList()) wf.AckDept(d.DeptCallId);
        Assert.Equal(1, resolved);
        Assert.Equal(AndonUiState.Ready, wf.State);
        Assert.Null(OfflineAndon.Local(Line));

        var ops = OfflineWork.PendingOps(o => o.Process == OfflineAndon.Process);
        Assert.Equal(new[] { "Raise", "SupAck", "CallDepts", "Arrival", "Arrival", "AckDept", "AckDept", "Resolve" }, ops.Select(o => o.Kind));
        Assert.All(ops, o => Assert.Equal(ops[0].Ref, o.Ref));   // 모두 같은 (임시) 안돈
        Assert.Equal("W009", ops[3].Arg("W"));
        Assert.Equal("현장 작업자", ops[3].Arg("N"));
    }

    [Fact]
    public void Andon_without_a_supervisor_copy_accepts_the_badge_and_continues_a_snapshot_andon()
    {
        SeedAndonMasters();   // 명단 없음 — 일단 받는다
        OfflineReads.Put("andon.open." + Line, new AndonCallDto
        {
            AndonId = 55, LineId = Line, Status = "OPEN", TriggeredAt = DateTime.Now.AddMinutes(-3), TriggeredBy = "E002",
        });
        var wf = Workflow();
        wf.Load();
        Assert.Equal(55, wf.Call!.AndonId);
        Assert.False(wf.Raise());                     // 이미 진행 중 — 새로 만들지 않는다

        wf.OnBadgeScan("EOS*S777*누구");
        Assert.Equal(AndonUiState.SupAcked, wf.State);
        var op = Assert.Single(OfflineWork.PendingOps(o => o.Process == OfflineAndon.Process));
        Assert.Equal(("SupAck", 55), (op.Kind, op.Ref));
    }

    private static ReworkItemDto Defect(int id, string lot) => new()
    {
        DefectId = id, LotId = id * 10, LotCode = lot, ItemNo = "ITEM-A", ItemName = "item", ProcessCode = "INJ", LineId = Line,
        DefectCode = "INJ-D01", DetectedAt = DateTime.Now.AddHours(-1),
    };

    private static PopSessionDto Rwk() => new()
    {
        SessionId = -3, OperatorId = "R001", EmployeeNo = "R001", EmployeeName = "재작업", TerminalId = "ST-RWK-01", LineId = "LINE-RWK-01",
        ShiftCode = "A", AuthMethod = AuthMethod.Pin, StartedAt = DateTime.Now, ExpiresAt = DateTime.Now.AddHours(8),
    };

    [Fact]
    public void Rework_decisions_queue_offline_and_move_the_row_from_pending_to_today()
    {
        OfflineReads.Put("rwk.pending", new List<ReworkItemDto> { Defect(1, "I1AAA0001"), Defect(2, "I1AAA0002") });
        OfflineReads.Put("rwk.decided", new List<ReworkItemDto>());
        OfflineReads.Put("rwk.stats", new ReworkStatsDto { Pending = 2, TodayReworked = 0, TodayScrapped = 0 });

        var row = OfflineRework.FindPendingByLotCode("i1aaa0001");
        Assert.NotNull(row);
        Assert.Equal(ReworkOutcome.Done, OfflineRework.Rework(row!, "C01", "fixed", Rwk(), out _));
        Assert.Equal(ReworkOutcome.AlreadyDecided, OfflineRework.Scrap(row!, "C01", null, Rwk(), out _));
        Assert.Equal(ReworkOutcome.Done, OfflineRework.Scrap(OfflineRework.ListPending().Single(), "C02", null, Rwk(), out _));

        Assert.Empty(OfflineRework.ListPending());
        Assert.Null(OfflineRework.FindPendingByLotCode("I1AAA0001"));
        var today = OfflineRework.ListTodayDecided();
        Assert.Equal(new[] { ("I1AAA0002", "SCRAPPED"), ("I1AAA0001", "REWORKED") }, today.Select(r => (r.LotCode, r.Disposition!)));
        var st = OfflineRework.GetTodayStats();
        Assert.Equal((0, 1, 1), (st.Pending, st.TodayReworked, st.TodayScrapped));
        var ops = OfflineWork.PendingOps(o => o.Process == OfflineRework.Process);
        Assert.Equal(new[] { (1, "Rework", "C01"), (2, "Scrap", "C02") }, ops.Select(o => (o.Ref, o.Kind, o.Arg("Cause")!)));
    }

    [Fact]
    public void Qc_inspection_ncr_and_hold_work_offline()
    {
        OfflineReads.Put("qc.insp.Incoming.30", new List<QcInspectionDto>
        {
            new() { InspectionId = 9, InspectionNo = "INC-1", InspectionType = "Incoming", Verdict = "IN_PROGRESS", InsStartTs = DateTime.Now },
        });
        OfflineReads.Put("qc.hold.Held,Released,Rejected.40", new List<QcHoldDto> { new() { HoldId = 4, HoldNumber = "H-4", Status = "Held" } });
        var s = Rwk();

        var started = OfflineQc.StartInspection("Incoming", "I1AAA0009", 32, s);
        Assert.NotNull(started);
        Assert.True(started!.Queued);
        Assert.True(started.Id < 0);
        var list = OfflineQc.ListInspections("Incoming", 30);
        Assert.Equal(new[] { started.Id, 9 }, list.Select(i => i.InspectionId));

        Assert.True(OfflineQc.FinalizeInspection(started.Id, "PASS", s));
        Assert.True(OfflineQc.FinalizeInspection(9, "FAIL", s));
        Assert.False(OfflineQc.FinalizeInspection(9, "PASS", s));   // 이미 판정
        Assert.Equal(new[] { "PASS", "FAIL" }, OfflineQc.ListInspections("Incoming", 30).Select(i => i.Verdict));

        Assert.True(OfflineQc.CreateNcr("Major", "INSPECTION", "ITEM-A", 3, "HOLD", s) < 0);
        Assert.Single(OfflineQc.ListNcrs("Open,Investigating", 60));

        Assert.True(OfflineQc.ReleaseHold(4, "SCRAP", "bad", s));
        Assert.Equal("Rejected", OfflineQc.ListHolds("Held,Released,Rejected", 40).Single().Status);
        Assert.False(OfflineQc.ReleaseHold(4, "REWORK", "again", s));

        Assert.Equal(new[] { "InspCreate", "InspFinalize", "InspFinalize", "NcrCreate", "HoldRelease" },
                     OfflineWork.PendingOps(o => o.Process == OfflineQc.Process).Select(o => o.Kind));
    }
}
