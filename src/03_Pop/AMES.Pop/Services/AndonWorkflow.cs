using AMES.Contracts.Dto;
using AMES.Devices;

namespace AMES.Pop.Services;

internal enum AndonUiState { Ready, Open, SupAcked, DeptCalled, PickDept }

internal enum AndonReject
{
    EmptyBadge, NotSupervisor, CauseRequired, DeptRequired,
    NoPendingDept, NotArrived, DeptAlreadyCalled, UnknownBadge,
}

/// <summary>
/// 안돈 대응 상태 머신. UI·DB 를 모르며 저장 후에는 항상 GetOpenForLine 으로
/// 다시 읽어 Call 을 갱신한다 — 다른 터미널이 같은 안돈을 밀어도 화면이 따라간다.
/// 저장소 예외는 삼키지 않는다(뷰가 토스트로 보여준다). 예외 전 상태를 유지한다.
/// </summary>
internal sealed class AndonWorkflow
{
    private readonly IAndonStore    _store;
    private readonly IBadgeResolver _names;
    private readonly string  _lineId;
    private readonly string? _equipId;
    private readonly string  _operatorNo;

    private readonly HashSet<string> _selectedDepts = new(StringComparer.OrdinalIgnoreCase);
    private bool _deptsTouched;

    public AndonUiState State { get; private set; } = AndonUiState.Ready;
    public AndonCallDto? Call  { get; private set; }
    public IReadOnlyList<AndonCauseDto> Causes { get; private set; } = Array.Empty<AndonCauseDto>();
    public IReadOnlyList<AndonDeptDto>  Depts  { get; private set; } = Array.Empty<AndonDeptDto>();
    public string? SelectedCause { get; private set; }
    public IReadOnlySet<string> SelectedDepts => _selectedDepts;
    public (string No, string? Name)? PendingScan { get; private set; }

    public IReadOnlyList<AndonDeptCallDto> PendingArrivalDepts
        => Call?.Depts.Where(d => !d.IsArrived).ToList() ?? new List<AndonDeptCallDto>();

    public event Action? Changed;
    public event Action<AndonReject>? Rejected;
    public event Action<int>? Resolved;

    public AndonWorkflow(IAndonStore store, IBadgeResolver names, string lineId, string? equipId, string operatorNo)
    {
        _store = store; _names = names; _lineId = lineId; _equipId = equipId; _operatorNo = operatorNo;
    }

    public void Load()
    {
        Call = _store.GetOpenForLine(_lineId);
        PendingScan = null;
        // GetOpenForLine 의 WHERE Status IN (...) 는 DB 콜레이션이 CI 라 대소문자 무관하게 걸리는데
        // 여기 switch 는 원래 CS 였다 — 구 대소문자 행('Open' 등)이 Ready 로 떨어져 안돈 버튼이 죽는다.
        State = Call?.Status?.Trim().ToUpperInvariant() switch
        {
            null          => AndonUiState.Ready,
            "OPEN"        => AndonUiState.Open,
            "SUP_ACKED"   => AndonUiState.SupAcked,
            "DEPT_CALLED" => AndonUiState.DeptCalled,
            _             => AndonUiState.Ready,
        };
        if (State is AndonUiState.SupAcked or AndonUiState.DeptCalled) EnsureMasters();
        if (State == AndonUiState.DeptCalled && AllDeptsAcked(Call))
        {
            // 마지막 AckDept 커밋 후 Resolve 가 실패하면 전 부서 ACK 인데 DEPT_CALLED 로 남을 수 있다 —
            // 재진입할 때마다 그 전이를 마저 끝낸다.
            var id = Call!.AndonId;
            _store.Resolve(id, null);
            Finish(id);
            return;
        }
        if (State == AndonUiState.Ready) { Call = null; ResetSelection(); }
        else SelectedCause ??= Call?.CauseCode;
        Changed?.Invoke();
    }

    public void Raise()
    {
        if (State != AndonUiState.Ready) return;
        // 같은 라인 다른 터미널이 먼저 발동했으면 그걸 복원한다 — 안돈 두 건이 되면 안 된다.
        if (_store.GetOpenForLine(_lineId) is null)
            _store.Raise(_lineId, _equipId, _operatorNo);
        Load();
    }

    public void OnBadgeScan(string raw)
    {
        var scan = BadgeScanParser.Parse(raw);
        if (scan.WorkerNo.Length == 0) { Reject(AndonReject.EmptyBadge); return; }

        switch (State)
        {
            case AndonUiState.Open:
                if (Call is null) return;
                if (!_store.IsLineSupervisor(_lineId, scan.WorkerNo)) { Reject(AndonReject.NotSupervisor); return; }
                _store.AcknowledgeBySupervisor(Call.AndonId, scan.WorkerNo, scan.WorkerName ?? _names.ResolveName(scan.WorkerNo));
                Load();
                break;

            case AndonUiState.DeptCalled:
                var pending = PendingArrivalDepts;
                if (pending.Count == 0) { Reject(AndonReject.NoPendingDept); return; }
                var name = scan.WorkerName ?? _names.ResolveName(scan.WorkerNo);
                // EOS 배지이거나 사번이 웹 계정·MD_Worker 에 등록돼 있어야 도착으로 받는다 —
                // 그래야 LOT 라벨을 잘못 찍어도 도착자로 기록되지 않는다(부서 소속은 검증하지 않음).
                if (!scan.IsEosFormat && name is null) { Reject(AndonReject.UnknownBadge); return; }
                if (pending.Count == 1)
                {
                    _store.RecordArrival(pending[0].DeptCallId, scan.WorkerNo, name);
                    Load();
                }
                else
                {
                    PendingScan = (scan.WorkerNo, name);
                    State = AndonUiState.PickDept;
                    Changed?.Invoke();
                }
                break;
        }
    }

    public void SelectCause(string code)
    {
        if (State is not (AndonUiState.SupAcked or AndonUiState.DeptCalled)) return;
        SelectedCause = code;
        if (!_deptsTouched)
        {
            _selectedDepts.Clear();
            var def = Causes.FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase))?.DefaultDeptCode;
            if (def is not null && !IsAlreadyCalled(def)) _selectedDepts.Add(def);
        }
        Changed?.Invoke();
    }

    public void ToggleDept(string code)
    {
        if (State is not (AndonUiState.SupAcked or AndonUiState.DeptCalled)) return;
        if (IsAlreadyCalled(code)) { Reject(AndonReject.DeptAlreadyCalled); return; }
        _deptsTouched = true;
        if (!_selectedDepts.Remove(code)) _selectedDepts.Add(code);
        Changed?.Invoke();
    }

    public void CallDepts()
    {
        if (State is not (AndonUiState.SupAcked or AndonUiState.DeptCalled) || Call is null) return;
        if (SelectedCause is null)  { Reject(AndonReject.CauseRequired); return; }
        if (_selectedDepts.Count == 0) { Reject(AndonReject.DeptRequired); return; }
        // 다른 터미널이 먼저 같은 부서를 호출했을 수 있다 — 선택이 오래됐을 수 있으니 재확인한다.
        var toCall = _selectedDepts.Where(d => !IsAlreadyCalled(d)).ToList();
        if (toCall.Count == 0) { Reject(AndonReject.DeptRequired); return; }
        _store.CallDepts(Call.AndonId, SelectedCause, toCall, Call.SupervisorNo ?? _operatorNo);
        ResetSelection(keepCause: true);
        Load();
    }

    public void ResolveSelf()
    {
        if (State != AndonUiState.SupAcked || Call is null) return;
        if (SelectedCause is null) { Reject(AndonReject.CauseRequired); return; }
        var id = Call.AndonId;
        _store.Resolve(id, SelectedCause);
        Finish(id);
    }

    public void AssignArrival(int deptCallId)
    {
        if (State != AndonUiState.PickDept || PendingScan is not { } scan) return;
        _store.RecordArrival(deptCallId, scan.No, scan.Name);
        Load();
    }

    public void CancelPick()
    {
        if (State != AndonUiState.PickDept) return;
        PendingScan = null;
        State = AndonUiState.DeptCalled;
        Changed?.Invoke();
    }

    public void AckDept(int deptCallId)
    {
        if (State != AndonUiState.DeptCalled || Call is null) return;
        var row = Call.Depts.FirstOrDefault(d => d.DeptCallId == deptCallId);
        if (row is null || !row.IsArrived) { Reject(AndonReject.NotArrived); return; }
        if (row.IsAcked) return;
        _store.AckDept(deptCallId);
        Load(); // Load() 자체가 전 부서 ACK 을 감지하면 Resolve 까지 마친다 (AllDeptsAcked 참고)
    }

    private void Finish(int andonId)
    {
        Call = null;
        State = AndonUiState.Ready;
        ResetSelection();
        Changed?.Invoke();
        Resolved?.Invoke(andonId);
    }

    private void EnsureMasters()
    {
        if (Causes.Count == 0) Causes = _store.ListCauses();
        if (Depts.Count == 0)  Depts  = _store.ListDepts();
    }

    private bool IsAlreadyCalled(string deptCode)
        => Call?.Depts.Any(d => string.Equals(d.DeptCode, deptCode, StringComparison.OrdinalIgnoreCase)) == true;

    private static bool AllDeptsAcked(AndonCallDto? call)
        => call is not null && call.Depts.Count > 0 && call.Depts.All(d => d.IsAcked);

    private void ResetSelection(bool keepCause = false)
    {
        if (!keepCause) SelectedCause = null;
        _selectedDepts.Clear();
        _deptsTouched = false;
        PendingScan = null;
    }

    private void Reject(AndonReject reason) => Rejected?.Invoke(reason);
}
