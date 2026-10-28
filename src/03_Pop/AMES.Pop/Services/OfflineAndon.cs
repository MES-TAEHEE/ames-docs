using AMES.Contracts.Dto;
using AMES.Data.Connection;
using AMES.Pop.Common;

namespace AMES.Pop.Services;

/// <summary>
/// 안돈 오프라인(10-28 사용자 결정 — DB 가 끊겨도 안돈을 발동·대응한다). 화면은 마지막으로 읽은 진행 중 안돈(라인별 사본)에
/// 아직 반영 못 한 안돈 작업을 차례로 얹은 상태를 본다. 작업은 공용 큐(<see cref="OfflineWork"/>)에 올라가 복구 때 발생 시각을
/// 넘겨 같은 저장소 메서드로 반영된다 — 오프라인에서 발동한 안돈은 임시(음수) ID 로 시작하고 반영 때 실제 번호로 바뀐다.
/// 큐에 그 라인의 안돈 작업이 남아 있는 동안은 온라인이어도 로컬 상태를 보여 준다(서버에는 아직 없는 대응을 지우지 않게).
/// </summary>
internal static class OfflineAndon
{
    public const string Process = "ANDON";

    private const string KeyCauses = "andon.causes", KeyDepts = "andon.depts", KeySeverities = "andon.severities";
    private static string KeySup(string line)  => "andon.sup."  + line.ToUpperInvariant();
    private static string KeyOpen(string line) => "andon.open." + line.ToUpperInvariant();

    // ── 사본 ────────────────────────────────────────────────────────────

    /// <summary>INJ·IMG·PNT 사본을 받을 때 같이 — 원인·부서·심각도 코드, 라인 슈퍼바이저, 진행 중 안돈.</summary>
    public static void Warm(string lineId)
    {
        Try(() => OfflineReads.Put(KeyCauses, PopServices.Andon.ListCauses()));
        Try(() => OfflineReads.Put(KeyDepts, PopServices.Andon.ListDepts()));
        Try(() => OfflineReads.Put(KeySeverities, PopServices.Andon.ListSeverities()));
        Try(() => OfflineReads.Put(KeySup(lineId), PopServices.Andon.ListLineSupervisors(lineId)));
        if (!HasPending(lineId)) Try(() => OfflineReads.Put(KeyOpen(lineId), PopServices.Andon.GetOpenForLine(lineId)));
    }

    // 프로시저가 아직 없는 DB(구 스키마)에서도 다른 사본 받기는 계속한다 — 연결 오류만 위로 올린다.
    private static void Try(Action act)
    {
        try { act(); }
        catch (Exception ex) when (!DbHealth.IsConnectionFailure(ex)) { PopLog.Append("offline", "OfflineAndon", $"warm: {ex.Message}"); }
    }

    public static bool HasPending(string lineId)
        => OfflineWork.PendingOps(o => o.Process == Process && SameLine(o.LineId, lineId)).Count > 0;

    private static bool SameLine(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public static List<AndonCauseDto> Causes() => OfflineReads.Get(KeyCauses, () => PopServices.Andon.ListCauses(), new List<AndonCauseDto>());
    public static List<AndonDeptDto> Depts() => OfflineReads.Get(KeyDepts, () => PopServices.Andon.ListDepts(), new List<AndonDeptDto>());
    public static List<AndonSeverityDto> Severities() => OfflineReads.Get(KeySeverities, () => PopServices.Andon.ListSeverities(), new List<AndonSeverityDto>());

    /// <summary>
    /// 오프라인 슈퍼바이저 확인 — 사본 명단에 있으면 통과. 명단을 받은 적이 없으면 받아들인다(일단 받고 복구 뒤 서버 기록을 따른다).
    /// </summary>
    public static bool IsSupervisorOffline(string lineId, string workerNo)
    {
        if (!OfflineReads.Has(KeySup(lineId))) return true;
        return OfflineReads.Cached(KeySup(lineId), new List<string>()).Contains(workerNo, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>진행 중 안돈 — 온라인이고 그 라인에 남은 작업이 없으면 서버를 읽어 사본을 갱신하고, 아니면 사본 + 남은 작업.</summary>
    public static AndonCallDto? GetOpenForLine(string lineId)
    {
        if (!PopDb.IsOffline && !HasPending(lineId))
        {
            try
            {
                var call = PopServices.Andon.GetOpenForLine(lineId);
                OfflineReads.Put(KeyOpen(lineId), call);
                return call;
            }
            catch (Exception ex) when (DbHealth.IsConnectionFailure(ex)) { /* 로컬로 */ }
        }
        return Local(lineId);
    }

    /// <summary>사본 위에 그 라인의 남은 안돈 작업을 차례로 얹은 상태. 종료됐으면 null.</summary>
    public static AndonCallDto? Local(string lineId)
    {
        var snap = OfflineReads.Cached<AndonCallDto?>(KeyOpen(lineId), null);
        var a = snap is null ? null : LocalAndon.From(snap);
        foreach (var op in OfflineWork.PendingOps(o => o.Process == Process && SameLine(o.LineId, lineId)))
            a = Step(a, op);
        return a?.ToDto();
    }

    // 작업 한 건의 효과 — 반영 때 서버 프로시저가 하는 것과 같은 전이(현재 상태가 맞을 때만).
    private static LocalAndon? Step(LocalAndon? a, WorkOp op)
    {
        switch (op.Kind)
        {
            case "Raise":
                return a ?? new LocalAndon
                {
                    AndonId = op.Ref, LineId = op.LineId, EquipId = op.Arg("Equip"), Status = "OPEN",
                    TriggeredAt = op.At, TriggeredBy = op.EmployeeNo,
                };
            case "SupAck" when a is { Status: "OPEN" }:
                a.Status = "SUP_ACKED"; a.SupervisorNo = op.Arg("W"); a.SupervisorName = op.Arg("N"); a.SupervisorAt = op.At;
                return a;
            case "CallDepts" when a is { Status: "SUP_ACKED" or "DEPT_CALLED" }:
                a.Status = "DEPT_CALLED"; a.CauseCode = op.Arg("C"); a.Severity = op.Arg("S");
                var codes = (op.Arg("D") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
                var ids = (op.Arg("Ids") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
                var names = Depts();
                for (var i = 0; i < codes.Length; i++)
                {
                    if (a.Depts.Any(d => string.Equals(d.DeptCode, codes[i], StringComparison.OrdinalIgnoreCase))) continue;
                    var n = names.FirstOrDefault(x => string.Equals(x.Code, codes[i], StringComparison.OrdinalIgnoreCase));
                    a.Depts.Add(new LocalDept
                    {
                        DeptCallId = i < ids.Length && int.TryParse(ids[i], out var id) ? id : 0,
                        DeptCode = codes[i], DeptName = n?.Name ?? codes[i], DeptNameEn = n?.NameEn, CalledAt = op.At,
                    });
                }
                return a;
            case "Arrival" when a is not null:
                if (a.Depts.FirstOrDefault(d => string.Equals(d.DeptCode, op.Arg("D"), StringComparison.OrdinalIgnoreCase)) is { ArrivedAt: null } ar)
                { ar.ArrivedAt = op.At; ar.ArrivedNo = op.Arg("W"); ar.ArrivedName = op.Arg("N"); }
                return a;
            case "AckDept" when a is not null:
                if (a.Depts.FirstOrDefault(d => string.Equals(d.DeptCode, op.Arg("D"), StringComparison.OrdinalIgnoreCase)) is { ArrivedAt: not null, AckedAt: null } ak)
                    ak.AckedAt = op.At;
                return a;
            case "Resolve":
                return null;
        }
        return a;
    }

    // ── 오프라인 작업 ───────────────────────────────────────────────────

    private static WorkOp NewOp(string kind, string lineId, int andonRef, string operatorNo) => new()
    {
        Process = Process, Kind = kind, At = DbClock.Now, LineId = lineId, Ref = andonRef,
        EmployeeNo = operatorNo, OperatorId = operatorNo, LotCode = $"ANDON {lineId}",
    };

    public static int Raise(string lineId, string? equipId, string operatorNo)
    {
        if (Local(lineId) is { } open) return open.AndonId;   // 같은 라인에 한 건만 — 서버 Raise 와 같다
        var op = NewOp("Raise", lineId, OfflineWork.NextTempId(), operatorNo);
        op.Args["Equip"] = equipId;
        OfflineWork.Enqueue(op);
        return op.Ref;
    }

    public static void SupAck(string lineId, int andonId, string workerNo, string? name, string operatorNo)
    {
        var op = NewOp("SupAck", lineId, andonId, operatorNo);
        op.Args["W"] = workerNo; op.Args["N"] = name;
        OfflineWork.Enqueue(op);
    }

    public static void CallDepts(string lineId, int andonId, string cause, string severity, IEnumerable<string> depts, string calledBy, string operatorNo)
    {
        var codes = depts.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var op = NewOp("CallDepts", lineId, andonId, operatorNo);
        op.Args["C"] = cause; op.Args["S"] = severity; op.Args["D"] = string.Join(',', codes); op.Args["By"] = calledBy;
        op.Args["Ids"] = string.Join(',', codes.Select(_ => OfflineWork.NextTempId()));
        OfflineWork.Enqueue(op);
    }

    public static void Arrival(string lineId, int deptCallId, string workerNo, string? name, string operatorNo)
    {
        if (DeptOf(lineId, deptCallId) is not { } d) return;
        var op = NewOp("Arrival", lineId, d.AndonId, operatorNo);
        op.Args["D"] = d.DeptCode; op.Args["W"] = workerNo; op.Args["N"] = name;
        OfflineWork.Enqueue(op);
    }

    public static void AckDept(string lineId, int deptCallId, string operatorNo)
    {
        if (DeptOf(lineId, deptCallId) is not { } d) return;
        var op = NewOp("AckDept", lineId, d.AndonId, operatorNo);
        op.Args["D"] = d.DeptCode;
        OfflineWork.Enqueue(op);
    }

    public static void Resolve(string lineId, int andonId, string? cause, string? severity, string operatorNo)
    {
        var op = NewOp("Resolve", lineId, andonId, operatorNo);
        op.Args["C"] = cause; op.Args["S"] = severity;
        OfflineWork.Enqueue(op);
    }

    // 부서 행은 안돈 안에서 부서 코드로 하나다 — 복구 때도 코드로 찾는다(오프라인에서 호출한 부서는 아직 실제 ID 가 없다).
    private static (int AndonId, string DeptCode)? DeptOf(string lineId, int deptCallId)
    {
        var a = Local(lineId);
        var d = a?.Depts.FirstOrDefault(x => x.DeptCallId == deptCallId);
        return a is null || d is null ? null : (a.AndonId, d.DeptCode);
    }

    // ── 복구 때 반영 ────────────────────────────────────────────────────

    /// <summary>안돈 작업 한 건 반영. 충돌이면 사유, 아니면 null. 반영 뒤 그 라인 사본을 서버 값으로 바꾼다.</summary>
    public static string? Apply(WorkOp op)
    {
        int andonId;
        if (op.Kind == "Raise")
        {
            if (OfflineWork.ResolveId(op.Ref) is int done) andonId = done;   // 이미 반영됐다(재시도)
            else
            {
                andonId = PopServices.Andon.Raise(op.LineId, op.Arg("Equip"), "MANUAL", "INJ-008-MANUAL", null, op.EmployeeNo, op.At);
                OfflineWork.MapTempId(op.Ref, andonId);
            }
        }
        else
        {
            if (OfflineWork.ResolveId(op.Ref) is not int real) return "AndonMissing";   // 발동이 충돌로 빠졌다
            andonId = real;
            var at = (DateTime?)op.At;
            switch (op.Kind)
            {
                case "SupAck":
                    PopServices.Andon.AcknowledgeBySupervisor(andonId, op.Arg("W") ?? "", op.Arg("N"), op.EmployeeNo, at);
                    break;
                case "CallDepts":
                    PopServices.Andon.CallDepts(andonId, op.Arg("C") ?? "", op.Arg("S") ?? "",
                        (op.Arg("D") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries), op.Arg("By") ?? op.EmployeeNo, op.EmployeeNo, at);
                    break;
                case "Arrival":
                {
                    if (PopServices.Andon.FindDeptCall(andonId, op.Arg("D") ?? "") is not int dc) return "DeptCallMissing";
                    PopServices.Andon.RecordArrival(dc, op.Arg("W") ?? "", op.Arg("N"), op.EmployeeNo, at);
                    break;
                }
                case "AckDept":
                {
                    if (PopServices.Andon.FindDeptCall(andonId, op.Arg("D") ?? "") is not int dc) return "DeptCallMissing";
                    PopServices.Andon.AckDept(dc, op.EmployeeNo, at);
                    break;
                }
                case "Resolve":
                    PopServices.Andon.Resolve(andonId, op.Arg("C"), op.Arg("S"), op.EmployeeNo, at);
                    break;
                default:
                    return $"unknown andon op {op.Kind}";
            }
        }
        try { OfflineReads.Put(KeyOpen(op.LineId), PopServices.Andon.GetOpenForLine(op.LineId)); }
        catch (Exception ex) when (!DbHealth.IsConnectionFailure(ex)) { }
        return null;
    }

    // ── 로컬 모델 ───────────────────────────────────────────────────────

    private sealed class LocalAndon
    {
        public int AndonId; public string LineId = ""; public string? EquipId; public string Status = "OPEN";
        public DateTime TriggeredAt; public string TriggeredBy = "";
        public string? SupervisorNo, SupervisorName, CauseCode, Severity; public DateTime? SupervisorAt;
        public List<LocalDept> Depts = new();

        public static LocalAndon From(AndonCallDto d) => new()
        {
            AndonId = d.AndonId, LineId = d.LineId, EquipId = d.EquipId, Status = d.Status.Trim().ToUpperInvariant(),
            TriggeredAt = d.TriggeredAt, TriggeredBy = d.TriggeredBy, SupervisorNo = d.SupervisorNo, SupervisorName = d.SupervisorName,
            SupervisorAt = d.SupervisorAt, CauseCode = d.CauseCode, Severity = d.Severity,
            Depts = d.Depts.Select(x => new LocalDept
            {
                DeptCallId = x.DeptCallId, DeptCode = x.DeptCode, DeptName = x.DeptName, DeptNameEn = x.DeptNameEn,
                CalledAt = x.CalledAt, ArrivedAt = x.ArrivedAt, ArrivedNo = x.ArrivedNo, ArrivedName = x.ArrivedName, AckedAt = x.AckedAt,
            }).ToList(),
        };

        public AndonCallDto ToDto() => new()
        {
            AndonId = AndonId, LineId = LineId, EquipId = EquipId, Status = Status, TriggeredAt = TriggeredAt, TriggeredBy = TriggeredBy,
            SupervisorNo = SupervisorNo, SupervisorName = SupervisorName, SupervisorAt = SupervisorAt, CauseCode = CauseCode, Severity = Severity,
            Depts = Depts.Select(x => new AndonDeptCallDto
            {
                DeptCallId = x.DeptCallId, DeptCode = x.DeptCode, DeptName = x.DeptName, DeptNameEn = x.DeptNameEn,
                CalledAt = x.CalledAt, ArrivedAt = x.ArrivedAt, ArrivedNo = x.ArrivedNo, ArrivedName = x.ArrivedName, AckedAt = x.AckedAt,
            }).ToList(),
        };
    }

    private sealed class LocalDept
    {
        public int DeptCallId; public string DeptCode = "", DeptName = ""; public string? DeptNameEn;
        public DateTime CalledAt; public DateTime? ArrivedAt; public string? ArrivedNo, ArrivedName; public DateTime? AckedAt;
    }
}

/// <summary>
/// 안돈 워크플로의 저장소 — 온라인이면 DB(<see cref="RepoAndonStore"/>), DB 가 끊겼거나 그 라인에 아직 반영 못 한 안돈 작업이 있으면
/// <see cref="OfflineAndon"/> 로 간다. 온라인 호출이 연결 오류로 실패해도 그 자리에서 오프라인 경로로 넘긴다.
/// </summary>
internal sealed class OfflineAwareAndonStore : IAndonStore
{
    private readonly IAndonStore _db;
    private readonly string _lineId;

    public OfflineAwareAndonStore(IAndonStore db, string lineId) { _db = db; _lineId = lineId; }

    private bool Local => PopDb.IsOffline || OfflineAndon.HasPending(_lineId);

    private void Write(Action online, Action offline)
    {
        if (Local) { offline(); return; }
        try { online(); }
        catch (Exception ex) when (DbHealth.IsConnectionFailure(ex)) { offline(); }
    }

    private T Read<T>(Func<T> online, Func<T> offline)
    {
        if (PopDb.IsOffline) return offline();
        try { return online(); }
        catch (Exception ex) when (DbHealth.IsConnectionFailure(ex)) { return offline(); }
    }

    public AndonCallDto? GetOpenForLine(string lineId) => OfflineAndon.GetOpenForLine(lineId);

    public int Raise(string lineId, string? equipId, string employeeNo)
    {
        var id = 0;
        Write(() => id = _db.Raise(lineId, equipId, employeeNo), () => id = OfflineAndon.Raise(lineId, equipId, employeeNo));
        return id;
    }

    public bool IsLineSupervisor(string lineId, string workerNo)
        => Read(() => _db.IsLineSupervisor(lineId, workerNo), () => OfflineAndon.IsSupervisorOffline(lineId, workerNo));

    public List<AndonCauseDto> ListCauses() => OfflineAndon.Causes();
    public List<AndonDeptDto> ListDepts() => OfflineAndon.Depts();
    public List<AndonSeverityDto> ListSeverities() => OfflineAndon.Severities();

    public void AcknowledgeBySupervisor(int andonId, string workerNo, string? name, string operatorNo)
        => Write(() => _db.AcknowledgeBySupervisor(andonId, workerNo, name, operatorNo),
                 () => OfflineAndon.SupAck(_lineId, andonId, workerNo, name, operatorNo));

    public void CallDepts(int andonId, string causeCode, string severity, IEnumerable<string> deptCodes, string calledBy, string operatorNo)
    {
        var codes = deptCodes.ToList();
        Write(() => _db.CallDepts(andonId, causeCode, severity, codes, calledBy, operatorNo),
              () => OfflineAndon.CallDepts(_lineId, andonId, causeCode, severity, codes, calledBy, operatorNo));
    }

    public void RecordArrival(int deptCallId, string workerNo, string? name, string operatorNo)
        => Write(() => _db.RecordArrival(deptCallId, workerNo, name, operatorNo),
                 () => OfflineAndon.Arrival(_lineId, deptCallId, workerNo, name, operatorNo));

    public void AckDept(int deptCallId, string operatorNo)
        => Write(() => _db.AckDept(deptCallId, operatorNo), () => OfflineAndon.AckDept(_lineId, deptCallId, operatorNo));

    public void Resolve(int andonId, string? causeCode, string? severity, string operatorNo)
        => Write(() => _db.Resolve(andonId, causeCode, severity, operatorNo),
                 () => OfflineAndon.Resolve(_lineId, andonId, causeCode, severity, operatorNo));
}
