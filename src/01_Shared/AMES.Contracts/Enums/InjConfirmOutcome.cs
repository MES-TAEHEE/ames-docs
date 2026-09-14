namespace AMES.Contracts.Enums;

/// <summary>Inj04 스캔 확정 결과.</summary>
public enum InjConfirmOutcome
{
    Confirmed,
    NotFound,
    AlreadyConfirmed,
    NgBlocked,
    WrongLine,
    /// <summary>라인에 LOT 품번과 같은 품번의 접수 가능한 WO 가 없다.</summary>
    NoWoForItem,
    /// <summary>DEFECT — 재작업 대기 중. REWORK 스테이션에서 판정해야 한다.</summary>
    InRework,
    /// <summary>SCRAPPED — 폐기된 LOT.</summary>
    Scrapped,
}
