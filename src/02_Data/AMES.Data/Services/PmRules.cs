using AMES.Contracts.Dto;

namespace AMES.Data.Services;

/// <summary>
/// PM 일정 규칙의 정본(10-16 사용자 결정). 테스트: PmRulesTests.
///   · 상태(공통코드 PM_STATUS)는 저장값이 아니라 예정일로 계산한다 — 취소면 CANCELLED, 예정일이 지났으면 OVERDUE,
///     오늘 + N일 이내면 DUE, 그 밖은 OK. 저장은 사용(OK)·취소(CANCELLED)만 하고, 옛 저장값 DUE·OVERDUE·DONE 은 사용으로 본다.
///   · N = SYS_Config MNT_PM_LEAD_DAYS(일, 0~365, 없거나 틀리면 7). 같은 N 으로 PM 작업지시의 「예정」을 가른다:
///     착수 전(발행·배정)이고 그 PM 의 예정일이 오늘 + N일보다 뒤면 예정 — MNT-007 기본 목록·KPI, MNT-009 열린 작업지시,
///     MNT-001 설비의 열린 작업지시 수에서 뺀다(착수는 막지 않는다).
///   · 다음 예정일: 기간(TIME) 주기면 기준일 + 주기로 자동, 그 밖(사이클·주기 없음)은 완료할 때 사람이 넣는다(샷 누적 연동 전까지).
///   · 보전 PM(PM_CLASS MAINT)은 라인을 멈추지 않는 점검이다 — PP-LSB PM 후보와 설비 「예방보전」 상태에서 뺀다.
/// </summary>
public static class PmRules
{
    public const string LeadDaysKey     = "MNT_PM_LEAD_DAYS";
    public const int    DefaultLeadDays = 7;
    public const int    MaxLeadDays     = 365;

    public const string Ok        = "OK";
    public const string Due       = "DUE";
    public const string Overdue   = "OVERDUE";
    public const string Cancelled = "CANCELLED";

    public const string ClassEquip = "EQUIP";
    public const string ClassMaint = "MAINT";

    public static int ParseLeadDays(string? value) =>
        int.TryParse(value?.Trim(), out var n) && n is >= 0 and <= MaxLeadDays ? n : DefaultLeadDays;

    public static bool IsCancelled(string? stored) => string.Equals(stored?.Trim(), Cancelled, StringComparison.OrdinalIgnoreCase);

    /// <summary>저장할 상태 — 사용 OK / 취소 CANCELLED 둘뿐.</summary>
    public static string Stored(bool cancelled) => cancelled ? Cancelled : Ok;

    public static string DisplayStatus(string? stored, DateTime? nextDue, DateTime today, int leadDays) =>
        IsCancelled(stored)            ? Cancelled
        : nextDue is not { } d         ? Ok
        : d.Date < today.Date          ? Overdue
        : d.Date <= today.Date.AddDays(leadDays) ? Due
        : Ok;

    public static bool IsPlanned(string? woStatus, DateTime? pmDue, DateTime today, int leadDays) =>
        MwoStatusRules.IsNotStarted(woStatus) && pmDue is { } d && d.Date > today.Date.AddDays(leadDays);

    public static bool IsMaint(string? pmClass) => string.Equals(pmClass?.Trim(), ClassMaint, StringComparison.OrdinalIgnoreCase);

    /// <summary>기간 주기면 기준일 + 주기, 아니면 null(사람이 다음 예정일을 넣어야 한다).</summary>
    public static DateTime? AutoNextDue(string? cycleBasis, int? cycleValue, DateTime basis) =>
        string.Equals(cycleBasis?.Trim(), "TIME", StringComparison.OrdinalIgnoreCase) && cycleValue is > 0
            ? basis.Date.AddDays(cycleValue.Value) : null;

    public static bool NeedsManualNextDue(string? cycleBasis, int? cycleValue) => AutoNextDue(cycleBasis, cycleValue, DateTime.Today) is null;

    /// <summary>사람이 넣은 다음 예정일은 완료일 뒤여야 한다.</summary>
    public static bool ValidManualNextDue(DateTime? nextDue, DateTime completedAt) => nextDue is { } d && d.Date > completedAt.Date;

    /// <summary>SQL 식 — N(SYS_Config MNT_PM_LEAD_DAYS, ParseLeadDays 와 같은 규칙).</summary>
    public const string LeadDaysSql =
        "ISNULL((SELECT TOP 1 CASE WHEN TRY_CAST(LTRIM(RTRIM(c.ConfigValue)) AS int) BETWEEN 0 AND 365 THEN TRY_CAST(LTRIM(RTRIM(c.ConfigValue)) AS int) END " +
        "FROM dbo.SYS_Config c WHERE c.ConfigKey = 'MNT_PM_LEAD_DAYS' ORDER BY c.ConfigID), 7)";

    /// <summary>SQL 조건 — 「예정」 PM 작업지시(별칭 w = MNT_WorkOrder). IsPlanned 와 같은 규칙.</summary>
    public const string PlannedWoSql =
        "(w.Status IN ('ISSUED', 'ASSIGNED') AND EXISTS (SELECT 1 FROM dbo.MNT_PMSchedule pp WHERE pp.ActiveWoID = w.WorkOrderID " +
        "AND pp.NextDueDate > DATEADD(DAY, " + LeadDaysSql + ", CAST(SYSDATETIME() AS date))))";
}

/// <summary>
/// 작업지시 점검 항목(MNT_WorkOrderTask)과 완료 결과의 규칙(10-16 사용자 결정). 테스트: PmRulesTests.
///   · 미점검(PENDING)이나 불합격(FAIL)이 있으면 정상 완료(OK)는 안 된다 — 부분 완료·후속 조치 필요로 완료하거나 종료한다.
///   · 완료 창의 결과 기본값: 불합격 → 후속 조치 필요, 미점검 → 부분 완료, 그 밖 → 정상 완료.
///   · 불합격 항목이 있는 작업지시를 부분·후속으로 완료하면(고장이 연결돼 있지 않을 때 — 고장은 그 자체로 후속 작업지시를 받는다)
///     불합격 항목을 담은 CM 후속 작업지시를 같은 트랜잭션에서 발행한다.
/// </summary>
public static class MwoChecklistRules
{
    public static bool CanCompleteOk(int pending, int failed) => pending == 0 && failed == 0;

    public static bool Allows(string? result, int pending, int failed) =>
        result != MwoResultCodes.Ok || CanCompleteOk(pending, failed);

    public static string DefaultResult(int pending, int failed) =>
        failed > 0 ? MwoResultCodes.FollowUp : pending > 0 ? MwoResultCodes.Partial : MwoResultCodes.Ok;

    public static bool IssuesFollowUp(string? result, int failed, bool hasOpenFailures) =>
        failed > 0 && !hasOpenFailures && result is MwoResultCodes.Partial or MwoResultCodes.FollowUp;
}
