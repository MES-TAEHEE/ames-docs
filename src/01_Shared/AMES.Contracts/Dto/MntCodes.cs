namespace AMES.Contracts.Dto;

/// <summary>
/// 정비 작업지시 유형 — 공통코드 그룹 MWO_TYPE 의 CodeValue.
/// 화면 라벨·콤보는 공통코드에서 읽고, 발행 코드가 쓰는 값만 여기 상수로 둔다
/// (고장 등록 → CM, PM 등록 → PM). PdM 은 MNT-007 수동 발행으로만 생긴다.
/// </summary>
public static class MwoTypeCodes
{
    public const string Group = "MWO_TYPE";
    public const string Pm  = "PM";
    public const string Cm  = "CM";
    public const string PdM = "PdM";
}

/// <summary>
/// 정비 작업지시 완료 결과 — 공통코드 그룹 MWO_RESULT 의 CodeValue.
/// 결과는 MNT_WorkOrder.ChecklistResultsJSON(결과 JSON)·MNT_PMExecution.Result 에 기록된다.
/// 고장 화면의 수리 완료는 결과를 묻지 않고 OK 로 닫는다.
/// </summary>
public static class MwoResultCodes
{
    public const string Group    = "MWO_RESULT";
    public const string Ok       = "OK";
    public const string Partial  = "PARTIAL";
    public const string FollowUp = "FOLLOWUP";
}

/// <summary>
/// 작업지시 점검 항목 결과 — 공통코드 그룹 MNT_RESULT 의 CodeValue(10-15, MNT_WorkOrderTask.Result).
/// 발행 때 PM 템플릿 단계를 복사하며 PENDING 으로 시작하고, MNT-007 에서 착수 뒤 항목마다 기록한다.
/// </summary>
public static class MntResultCodes
{
    public const string Group   = "MNT_RESULT";
    public const string Pass    = "PASS";
    public const string Fail    = "FAIL";
    public const string Na      = "NA";
    public const string Pending = "PENDING";

    /// <summary>점검을 마친 결과(미점검 아님) — 목록의 「작업 n/m」이 센다.</summary>
    public static bool IsChecked(string? r) => r is Pass or Fail or Na;
}

/// <summary>
/// 정비 작업지시 상태 — 공통코드 그룹 MNT_STATUS 의 CodeValue(10-15). 라벨은 공통코드에서, 전이 규칙은 AMES.Data.Services.MwoStatusRules.
/// 발행 ISSUED → 배정 ASSIGNED → 착수 IN_PROGRESS → 완료 COMPLETED(정상 종료), 끝나기 전 어느 단계에서든 종료 CLOSED(작업 없이 닫음, 사유 필수).
/// </summary>
public static class MwoStatusCodes
{
    public const string Group      = "MNT_STATUS";
    public const string Issued     = "ISSUED";
    public const string Assigned   = "ASSIGNED";
    public const string InProgress = "IN_PROGRESS";
    public const string Completed  = "COMPLETED";
    public const string Closed     = "CLOSED";
}
