namespace AMES.Contracts.Enums;

/// <summary>라인 불량 팝업의 LOT 스캔 등록 결과.</summary>
public enum DefectRegisterOutcome
{
    Registered,
    NotFound,
    WrongLine,
    /// <summary>이미 DEFECT 상태 — 재작업 대기열에 있다.</summary>
    AlreadyInRework,
    /// <summary>이미 폐기된 LOT.</summary>
    Scrapped,
}
