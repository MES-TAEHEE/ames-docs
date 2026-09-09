using AMES.Contracts.Dto;
using AMES.Pop.Services;
using Xunit;

namespace AMES.Pop.Tests;

public class AndonWorkflowTests
{
    // 실제 DB 를 흉내내는 인메모리 저장소 — 상태 전이 WHERE 절까지 따라 한다.
    sealed class FakeStore : IAndonStore
    {
        public int NextId = 1;
        public string? Status;
        public int AndonId;
        public string? SupNo, SupName, Cause;
        public bool ResolvedCalled;
        public string? ResolveCause;
        public List<(string Dept, string By)> Called = new();
        public List<string[]> CallArgs = new();
        public HashSet<string> Supervisors = new(StringComparer.OrdinalIgnoreCase) { "S001" };
        public List<AndonDeptCallDto> DeptRows = new();
        private int _nextDeptId = 10;

        public List<AndonCauseDto> Causes = new()
        {
            new("EQUIP", "설비고장", "Equipment failure", "MAINT"),
            new("QUALITY", "품질불량", "Quality defect", "QC"),
            new("OTHER", "기타", "Other", null),
        };
        public List<AndonDeptDto> Depts = new()
        {
            new("MAINT", "보전", "Maintenance"),
            new("QC", "품질", "Quality"),
            new("MATERIAL", "자재", "Material"),
        };

        public AndonCallDto? GetOpenForLine(string lineId) => Status is null or "RESOLVED" ? null : new AndonCallDto
        {
            AndonId = AndonId, LineId = lineId, Status = Status, TriggeredAt = new DateTime(2026, 9, 9, 8, 0, 0),
            TriggeredBy = "W001", SupervisorNo = SupNo, SupervisorName = SupName, CauseCode = Cause,
            Depts = DeptRows.Select(Clone).ToList(),
        };

        static AndonDeptCallDto Clone(AndonDeptCallDto d) => new()
        {
            DeptCallId = d.DeptCallId, DeptCode = d.DeptCode, DeptName = d.DeptName, DeptNameEn = d.DeptNameEn,
            CalledAt = d.CalledAt, ArrivedAt = d.ArrivedAt, ArrivedNo = d.ArrivedNo, ArrivedName = d.ArrivedName, AckedAt = d.AckedAt,
        };

        public int Raise(string lineId, string? equipId, string employeeNo)
        {
            AndonId = NextId++; Status = "OPEN"; return AndonId;
        }
        public bool IsLineSupervisor(string lineId, string workerNo) => Supervisors.Contains(workerNo);
        public List<AndonCauseDto> ListCauses() => Causes;
        public List<AndonDeptDto>  ListDepts()  => Depts;

        public void AcknowledgeBySupervisor(int andonId, string workerNo, string? name)
        {
            if (Status != "OPEN") return;
            SupNo = workerNo; SupName = name; Status = "SUP_ACKED";
        }

        public void CallDepts(int andonId, string causeCode, IEnumerable<string> deptCodes, string calledBy)
        {
            var codes = deptCodes.ToArray();
            CallArgs.Add(codes);
            Cause = causeCode; Status = "DEPT_CALLED";
            foreach (var d in codes)
            {
                if (DeptRows.Any(r => r.DeptCode == d)) continue;
                Called.Add((d, calledBy));
                DeptRows.Add(new AndonDeptCallDto
                {
                    DeptCallId = _nextDeptId++, DeptCode = d, DeptName = d, CalledAt = DateTime.Now,
                });
            }
        }

        public void RecordArrival(int deptCallId, string workerNo, string? name)
        {
            var i = DeptRows.FindIndex(r => r.DeptCallId == deptCallId);
            if (i < 0 || DeptRows[i].ArrivedAt is not null) return;
            var r = DeptRows[i];
            DeptRows[i] = new AndonDeptCallDto
            {
                DeptCallId = r.DeptCallId, DeptCode = r.DeptCode, DeptName = r.DeptName, CalledAt = r.CalledAt,
                ArrivedAt = DateTime.Now, ArrivedNo = workerNo, ArrivedName = name,
            };
        }

        public void AckDept(int deptCallId)
        {
            var i = DeptRows.FindIndex(r => r.DeptCallId == deptCallId);
            if (i < 0 || DeptRows[i].ArrivedAt is null) return;
            var r = DeptRows[i];
            DeptRows[i] = new AndonDeptCallDto
            {
                DeptCallId = r.DeptCallId, DeptCode = r.DeptCode, DeptName = r.DeptName, CalledAt = r.CalledAt,
                ArrivedAt = r.ArrivedAt, ArrivedNo = r.ArrivedNo, ArrivedName = r.ArrivedName, AckedAt = DateTime.Now,
            };
        }

        public void Resolve(int andonId, string? causeCode)
        {
            ResolvedCalled = true; ResolveCause = causeCode; Status = "RESOLVED";
        }
    }

    sealed class FakeNames : IBadgeResolver
    {
        public Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
        {
            ["S001"] = "Sup One", ["M001"] = "Maint One", ["Q001"] = "Qc One", ["M002"] = "Maint Two",
        };
        public string? ResolveName(string workerNo) => Names.TryGetValue(workerNo, out var n) ? n : null;
    }

    static (AndonWorkflow W, FakeStore S, List<AndonReject> R, List<int> Done) Build()
    {
        var s = new FakeStore();
        var w = new AndonWorkflow(s, new FakeNames(), "LINE-INJ-01", "EQ-01", "W001");
        var rejects = new List<AndonReject>();
        var done = new List<int>();
        w.Rejected += rejects.Add;
        w.Resolved += done.Add;
        w.Load();
        return (w, s, rejects, done);
    }

    static AndonWorkflow ToSupAcked(AndonWorkflow w)
    {
        w.Raise();
        w.OnBadgeScan("EOS*S001*Sup One");
        return w;
    }

    [Fact]
    public void Load_without_open_andon_is_ready()
    {
        var (w, _, _, _) = Build();
        Assert.Equal(AndonUiState.Ready, w.State);
        Assert.Null(w.Call);
    }

    [Fact]
    public void Raise_creates_open_andon()
    {
        var (w, s, _, _) = Build();
        w.Raise();
        Assert.Equal(AndonUiState.Open, w.State);
        Assert.Equal("OPEN", s.Status);
        Assert.Equal(s.AndonId, w.Call!.AndonId);
    }

    [Fact]
    public void Raise_restores_existing_open_andon_instead_of_creating_second()
    {
        var (w, s, _, _) = Build();
        s.AndonId = 77; s.Status = "OPEN";
        w.Raise();
        Assert.Equal(77, w.Call!.AndonId);
        Assert.Equal(1, s.NextId);
    }

    [Fact]
    public void Open_rejects_non_supervisor_badge()
    {
        var (w, s, r, _) = Build();
        w.Raise();
        w.OnBadgeScan("EOS*W002*Someone");
        Assert.Equal(AndonUiState.Open, w.State);
        Assert.Equal(new[] { AndonReject.NotSupervisor }, r);
        Assert.Equal("OPEN", s.Status);
    }

    [Fact]
    public void Open_rejects_empty_badge()
    {
        var (w, _, r, _) = Build();
        w.Raise();
        w.OnBadgeScan("   ");
        Assert.Equal(new[] { AndonReject.EmptyBadge }, r);
    }

    [Fact]
    public void Supervisor_badge_acknowledges_and_loads_masters()
    {
        var (w, s, _, _) = Build();
        w.Raise();
        w.OnBadgeScan("EOS*S001*Sup One");
        Assert.Equal(AndonUiState.SupAcked, w.State);
        Assert.Equal("S001", s.SupNo);
        Assert.Equal("Sup One", s.SupName);
        Assert.Equal(3, w.Causes.Count);
        Assert.Equal(3, w.Depts.Count);
    }

    [Fact]
    public void Supervisor_name_falls_back_to_resolver_when_badge_has_none()
    {
        var (w, s, _, _) = Build();
        w.Raise();
        w.OnBadgeScan("S001");
        Assert.Equal("Sup One", s.SupName);
    }

    [Fact]
    public void SelectCause_checks_default_dept_when_user_has_not_touched_depts()
    {
        var (w, _, _, _) = Build();
        ToSupAcked(w);
        w.SelectCause("EQUIP");
        Assert.Equal("EQUIP", w.SelectedCause);
        Assert.Equal(new[] { "MAINT" }, w.SelectedDepts.OrderBy(x => x));
        w.SelectCause("QUALITY");
        Assert.Equal(new[] { "QC" }, w.SelectedDepts.OrderBy(x => x));
        w.SelectCause("OTHER");
        Assert.Empty(w.SelectedDepts);
    }

    [Fact]
    public void SelectCause_keeps_user_chosen_depts()
    {
        var (w, _, _, _) = Build();
        ToSupAcked(w);
        w.ToggleDept("QC");
        w.SelectCause("EQUIP");
        Assert.Equal(new[] { "QC" }, w.SelectedDepts.OrderBy(x => x));
    }

    [Fact]
    public void CallDepts_requires_cause_and_at_least_one_dept()
    {
        var (w, s, r, _) = Build();
        ToSupAcked(w);
        w.CallDepts();
        Assert.Equal(AndonReject.CauseRequired, r.Last());
        w.SelectCause("OTHER");
        w.CallDepts();
        Assert.Equal(AndonReject.DeptRequired, r.Last());
        Assert.Empty(s.Called);
        Assert.Equal(AndonUiState.SupAcked, w.State);
    }

    [Fact]
    public void CallDepts_records_rows_and_moves_to_DeptCalled()
    {
        var (w, s, _, _) = Build();
        ToSupAcked(w);
        w.SelectCause("EQUIP");
        w.ToggleDept("QC");
        w.CallDepts();
        Assert.Equal(AndonUiState.DeptCalled, w.State);
        Assert.Equal(new[] { "MAINT", "QC" }, s.Called.Select(c => c.Dept).OrderBy(x => x));
        Assert.All(s.Called, c => Assert.Equal("S001", c.By));
        Assert.Equal(2, w.Call!.Depts.Count);
        Assert.Empty(w.SelectedDepts);
    }

    [Fact]
    public void ResolveSelf_requires_cause_then_resolves_with_it()
    {
        var (w, s, r, done) = Build();
        ToSupAcked(w);
        w.ResolveSelf();
        Assert.Equal(AndonReject.CauseRequired, r.Last());
        Assert.False(s.ResolvedCalled);
        w.SelectCause("OTHER");
        w.ResolveSelf();
        Assert.True(s.ResolvedCalled);
        Assert.Equal("OTHER", s.ResolveCause);
        Assert.Equal(AndonUiState.Ready, w.State);
        Assert.Equal(new[] { s.AndonId }, done);
    }

    static AndonWorkflow ToDeptCalled(AndonWorkflow w, params string[] extraDepts)
    {
        ToSupAcked(w);
        w.SelectCause("EQUIP");
        foreach (var d in extraDepts) w.ToggleDept(d);
        w.CallDepts();
        return w;
    }

    [Fact]
    public void DeptCalled_scan_with_single_pending_dept_records_arrival()
    {
        var (w, s, _, _) = Build();
        ToDeptCalled(w);
        w.OnBadgeScan("EOS*M001*Maint One");
        Assert.Equal(AndonUiState.DeptCalled, w.State);
        var row = Assert.Single(s.DeptRows);
        Assert.Equal("M001", row.ArrivedNo);
        Assert.Equal("Maint One", row.ArrivedName);
    }

    [Fact]
    public void DeptCalled_scan_with_two_pending_depts_asks_which_dept()
    {
        var (w, s, _, _) = Build();
        ToDeptCalled(w, "QC");
        w.OnBadgeScan("EOS*M001*Maint One");
        Assert.Equal(AndonUiState.PickDept, w.State);
        Assert.Equal(("M001", "Maint One"), w.PendingScan!.Value);
        Assert.Equal(2, w.PendingArrivalDepts.Count);
        Assert.All(s.DeptRows, r => Assert.Null(r.ArrivedAt));

        var qc = s.DeptRows.Single(r => r.DeptCode == "QC");
        w.AssignArrival(qc.DeptCallId);
        Assert.Equal(AndonUiState.DeptCalled, w.State);
        Assert.Null(w.PendingScan);
        Assert.Equal("M001", s.DeptRows.Single(r => r.DeptCode == "QC").ArrivedNo);
        Assert.Null(s.DeptRows.Single(r => r.DeptCode == "MAINT").ArrivedAt);
    }

    [Fact]
    public void DeptCalled_rejects_scan_that_is_neither_eos_nor_known()
    {
        // LOT 라벨을 잘못 찍은 경우: EOS 양식도 아니고 등록된 사번도 아니다 — 도착으로 기록하면 안 된다.
        var (w, s, r, _) = Build();
        ToDeptCalled(w);
        w.OnBadgeScan("L26A1I10001");
        Assert.Equal(AndonReject.UnknownBadge, r.Last());
        Assert.All(s.DeptRows, row => Assert.Null(row.ArrivedAt));
    }

    [Fact]
    public void DeptCalled_accepts_number_only_badge_of_known_person()
    {
        var (w, s, _, _) = Build();
        ToDeptCalled(w);
        w.OnBadgeScan("M001");
        var row = Assert.Single(s.DeptRows);
        Assert.Equal("M001", row.ArrivedNo);
        Assert.Equal("Maint One", row.ArrivedName);
    }

    [Fact]
    public void DeptCalled_accepts_eos_badge_of_unknown_person()
    {
        var (w, s, _, _) = Build();
        ToDeptCalled(w);
        w.OnBadgeScan("EOS*X999*Guest");
        var row = Assert.Single(s.DeptRows);
        Assert.Equal("X999", row.ArrivedNo);
        Assert.Equal("Guest", row.ArrivedName);
    }

    [Fact]
    public void CancelPick_discards_scan()
    {
        var (w, s, _, _) = Build();
        ToDeptCalled(w, "QC");
        w.OnBadgeScan("M001");
        w.CancelPick();
        Assert.Equal(AndonUiState.DeptCalled, w.State);
        Assert.Null(w.PendingScan);
        Assert.All(s.DeptRows, r => Assert.Null(r.ArrivedAt));
    }

    [Fact]
    public void DeptCalled_scan_with_no_pending_dept_is_rejected()
    {
        var (w, s, r, _) = Build();
        ToDeptCalled(w);
        w.OnBadgeScan("M001");
        w.OnBadgeScan("M002");
        Assert.Equal(AndonReject.NoPendingDept, r.Last());
        Assert.Equal("M001", s.DeptRows[0].ArrivedNo);
    }

    [Fact]
    public void AckDept_before_arrival_is_rejected()
    {
        var (w, s, r, _) = Build();
        ToDeptCalled(w);
        w.AckDept(s.DeptRows[0].DeptCallId);
        Assert.Equal(AndonReject.NotArrived, r.Last());
        Assert.Null(s.DeptRows[0].AckedAt);
    }

    [Fact]
    public void All_depts_acked_resolves_andon()
    {
        var (w, s, _, done) = Build();
        ToDeptCalled(w, "QC");
        var maint = s.DeptRows.Single(r => r.DeptCode == "MAINT").DeptCallId;
        var qc    = s.DeptRows.Single(r => r.DeptCode == "QC").DeptCallId;

        w.OnBadgeScan("M001"); w.AssignArrival(maint);
        w.AckDept(maint);
        Assert.False(s.ResolvedCalled);
        Assert.Equal(AndonUiState.DeptCalled, w.State);

        w.OnBadgeScan("Q001");
        w.AckDept(qc);
        Assert.True(s.ResolvedCalled);
        Assert.Null(s.ResolveCause);
        Assert.Equal(AndonUiState.Ready, w.State);
        Assert.Equal(new[] { s.AndonId }, done);
    }

    [Fact]
    public void Load_restores_DeptCalled_from_store()
    {
        var s = new FakeStore { AndonId = 5, Status = "DEPT_CALLED", SupNo = "S001", Cause = "EQUIP" };
        s.DeptRows.Add(new AndonDeptCallDto { DeptCallId = 1, DeptCode = "MAINT", DeptName = "보전", CalledAt = DateTime.Now });
        var w = new AndonWorkflow(s, new FakeNames(), "LINE-INJ-01", null, "W001");
        w.Load();
        Assert.Equal(AndonUiState.DeptCalled, w.State);
        Assert.Equal("EQUIP", w.SelectedCause);
        Assert.Single(w.Call!.Depts);
        Assert.NotEmpty(w.Depts);
    }

    [Fact]
    public void Load_restores_SupAcked_from_store()
    {
        var s = new FakeStore { AndonId = 5, Status = "SUP_ACKED", SupNo = "S001" };
        var w = new AndonWorkflow(s, new FakeNames(), "LINE-INJ-01", null, "W001");
        w.Load();
        Assert.Equal(AndonUiState.SupAcked, w.State);
        Assert.NotEmpty(w.Causes);
    }

    [Fact]
    public void Load_maps_mixed_case_legacy_status()
    {
        // GetOpenForLine 은 CI 콜레이션 IN 필터라 'Open' 같은 구 대소문자 행도 돌려준다 —
        // Load 는 이를 Ready 로 떨어뜨리면 안 되고 정규화해서 인식해야 한다.
        var s = new FakeStore { AndonId = 9, Status = "Open" };
        var w = new AndonWorkflow(s, new FakeNames(), "LINE-INJ-01", null, "W001");
        w.Load();
        Assert.Equal(AndonUiState.Open, w.State);
        Assert.NotNull(w.Call);
    }

    [Fact]
    public void Additional_call_skips_already_called_dept_and_adds_new_one()
    {
        var (w, s, r, _) = Build();
        ToDeptCalled(w);
        w.ToggleDept("MAINT");
        Assert.Equal(AndonReject.DeptAlreadyCalled, r.Last());
        w.ToggleDept("QC");
        w.CallDepts();
        Assert.Equal(new[] { "MAINT", "QC" }, s.DeptRows.Select(d => d.DeptCode).OrderBy(x => x));
        Assert.Equal(2, s.Called.Count);
        Assert.Equal(new[] { "QC" }, s.CallArgs[1]);
    }

    [Fact]
    public void Store_exception_propagates_and_keeps_state()
    {
        var (w, s, _, _) = Build();
        ToSupAcked(w);
        var throwing = new ThrowingStore(s);
        var w2 = new AndonWorkflow(throwing, new FakeNames(), "LINE-INJ-01", null, "W001");
        w2.Load();
        w2.SelectCause("EQUIP");
        Assert.Throws<InvalidOperationException>(() => w2.CallDepts());
        Assert.Equal(AndonUiState.SupAcked, w2.State);
    }

    sealed class ThrowingStore : IAndonStore
    {
        private readonly FakeStore _inner;
        public ThrowingStore(FakeStore inner) => _inner = inner;
        public AndonCallDto? GetOpenForLine(string lineId) => _inner.GetOpenForLine(lineId);
        public int Raise(string lineId, string? equipId, string employeeNo) => _inner.Raise(lineId, equipId, employeeNo);
        public bool IsLineSupervisor(string lineId, string workerNo) => _inner.IsLineSupervisor(lineId, workerNo);
        public List<AndonCauseDto> ListCauses() => _inner.ListCauses();
        public List<AndonDeptDto> ListDepts() => _inner.ListDepts();
        public void AcknowledgeBySupervisor(int andonId, string workerNo, string? name) => _inner.AcknowledgeBySupervisor(andonId, workerNo, name);
        public void CallDepts(int andonId, string causeCode, IEnumerable<string> deptCodes, string calledBy) => throw new InvalidOperationException("db down");
        public void RecordArrival(int deptCallId, string workerNo, string? name) => _inner.RecordArrival(deptCallId, workerNo, name);
        public void AckDept(int deptCallId) => _inner.AckDept(deptCallId);
        public void Resolve(int andonId, string? causeCode) => _inner.Resolve(andonId, causeCode);
    }

    [Fact]
    public void Load_resolves_stranded_andon_when_every_dept_is_already_acked()
    {
        // AckDept 커밋 직후 Resolve 가 예외로 죽으면 전 부서 ACK 인데 상태만 DEPT_CALLED 로 남을 수 있다.
        // 재진입(Load) 이 그 전이를 마저 끝내야 한다.
        var s = new FakeStore { AndonId = 9, Status = "DEPT_CALLED", SupNo = "S001", Cause = "EQUIP" };
        s.DeptRows.Add(new AndonDeptCallDto
        {
            DeptCallId = 1, DeptCode = "MAINT", DeptName = "보전", CalledAt = DateTime.Now,
            ArrivedAt = DateTime.Now, ArrivedNo = "M001", ArrivedName = "Maint One", AckedAt = DateTime.Now,
        });
        var w = new AndonWorkflow(s, new FakeNames(), "LINE-INJ-01", null, "W001");
        var done = new List<int>();
        w.Resolved += done.Add;

        w.Load();

        Assert.True(s.ResolvedCalled);
        Assert.Null(s.ResolveCause);
        Assert.Equal(AndonUiState.Ready, w.State);
        Assert.Equal(new[] { 9 }, done);
    }

    [Fact]
    public void CallDepts_drops_stale_selection_already_called_by_another_terminal()
    {
        // 같은 안돈을 보는 두 터미널이 둘 다 SupAcked 에서 같은 기본 부서(MAINT)를 골라 둔 채,
        // 한쪽이 먼저 호출하면 다른 쪽은 재로딩 후에도 이미 호출된 선택을 그대로 들고 있다.
        var s = new FakeStore();
        var w1 = new AndonWorkflow(s, new FakeNames(), "LINE-INJ-01", "EQ-01", "W001");
        w1.Load();
        ToSupAcked(w1);

        var w2 = new AndonWorkflow(s, new FakeNames(), "LINE-INJ-01", "EQ-01", "W002");
        var rejects2 = new List<AndonReject>();
        w2.Rejected += rejects2.Add;
        w2.Load();
        w2.SelectCause("EQUIP"); // 기본 부서 = MAINT (아직 아무도 호출 안 함)

        w1.SelectCause("EQUIP");
        w1.CallDepts(); // MAINT 호출, DEPT_CALLED 로 전이
        Assert.Equal(AndonUiState.DeptCalled, w1.State);
        Assert.Single(s.CallArgs);

        w2.Load(); // Call 갱신되지만 w2 의 선택(MAINT)은 그대로 남는다
        w2.CallDepts();

        Assert.Equal(AndonReject.DeptRequired, rejects2.Last());
        Assert.Single(s.CallArgs); // 스토어 CallDepts 재호출 없음
    }
}
