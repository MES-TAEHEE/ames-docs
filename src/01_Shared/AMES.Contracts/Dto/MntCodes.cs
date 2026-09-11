namespace AMES.Contracts.Dto;

/// <summary>
/// 정비 작업지시 유형 — 공통코드 그룹 MWO_TYPE 의 CodeValue.
/// 화면 라벨·콤보는 공통코드에서 읽고, 발행 코드가 쓰는 값만 여기 상수로 둔다
/// (고장 등록 → CM, PM 등록 → PM). PdM 은 발행 경로가 없어 시연 데이터에만 있다.
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
/// 정비 작업지시 상태 — 공통코드 그룹 없이 고정 어휘. 발행 ISSUED → 착수 IN_PROGRESS → 완료 COMPLETED.
/// OPEN 은 시연 데이터의 동의어(ISSUED 와 같이 취급), CLOSED 는 완료 뒤 마감(발행 경로 없음).
/// </summary>
public static class MwoStatusCodes
{
    public const string Open       = "OPEN";
    public const string Issued     = "ISSUED";
    public const string InProgress = "IN_PROGRESS";
    public const string Completed  = "COMPLETED";
    public const string Closed     = "CLOSED";
}
