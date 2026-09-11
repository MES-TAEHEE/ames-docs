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
