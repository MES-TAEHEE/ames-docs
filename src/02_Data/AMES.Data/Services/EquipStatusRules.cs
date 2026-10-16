namespace AMES.Data.Services;

/// <summary>
/// 설비 상태(공통코드 EQUIP_STATUS) 자동 판정의 정본(10-16 사용자 결정). 저장값을 복사해 두지 않고 조회할 때 계산한다.
///   1) 설비를 멈추는 고장이 열려 있으면 DOWN — 안돈 고장(Source ANDON)·심각도 CRITICAL·「라인 정지」(비가동 연결), 상태 OPEN·REGISTERED·IN_PROGRESS
///   2) 아니면 착수된(IN_PROGRESS) 설비 PM 작업지시가 있으면 PM (보전 PM 은 제외 — 10-16)
///   3) 아니면 MNT-001 에서 손으로 넣은 상태 — 가동(RUN)·유휴(IDLE)만 손으로 정한다(DOWN·PM 은 위에서 정해지므로 수동값이면 RUN 으로 본다)
/// MntRepository.ListEquipment(MNT-001·MNT-009·Api)와 대시보드 KPI 가 같은 규칙을 쓴다. 테스트: EquipStatusRulesTests.
/// </summary>
public static class EquipStatusRules
{
    public const string Run  = "RUN";
    public const string Down = "DOWN";
    public const string Pm   = "PM";
    public const string Idle = "IDLE";

    /// <summary>SQL 조건 — 설비를 멈추는 열린 고장(별칭 f = MNT_FailureRegister). StopsEquipment 와 같은 규칙.</summary>
    public const string StopFailureSql =
        "ISNULL(f.Status, 'OPEN') IN ('OPEN', 'REGISTERED', 'IN_PROGRESS') AND (f.Source = 'ANDON' OR f.Severity = 'CRITICAL' OR f.DowntimeID IS NOT NULL)";

    /// <summary>SQL 조건 — 착수된 PM 작업지시(별칭 w = MNT_WorkOrder). 보전 PM(MAINT)은 라인을 멈추지 않는 점검이라 빼낸다(PmRules).</summary>
    public const string PmInProgressSql =
        "w.WoType = 'PM' AND w.Status = 'IN_PROGRESS' AND NOT EXISTS (SELECT 1 FROM dbo.MNT_PMSchedule mp WHERE mp.ActiveWoID = w.WorkOrderID AND mp.PMClass = 'MAINT')";

    /// <summary>MNT-001 에서 손으로 고를 수 있는 상태.</summary>
    public static readonly IReadOnlyList<string> ManualStatuses = [Run, Idle];

    /// <param name="lineStopped">고장이 라인 비가동에 연결돼 있음(MNT_FailureRegister.DowntimeID — 「라인 정지」로 등록, 10-16)</param>
    public static bool StopsEquipment(string? source, string? severity, bool lineStopped = false) =>
        lineStopped
        || string.Equals(source?.Trim(), "ANDON", StringComparison.OrdinalIgnoreCase)
        || string.Equals(severity?.Trim(), "CRITICAL", StringComparison.OrdinalIgnoreCase);

    /// <summary>수동 상태 정규화 — 유휴 계열은 IDLE, 그 밖(가동 계열·DOWN·PM·빈 값)은 RUN.</summary>
    public static string NormalizeManual(string? manual) =>
        manual?.Trim().ToUpperInvariant() is Idle or "SETUP" or "READY" ? Idle : Run;

    public static string Effective(bool stopFailureOpen, bool pmInProgress, string? manual) =>
        stopFailureOpen ? Down : pmInProgress ? Pm : NormalizeManual(manual);
}
