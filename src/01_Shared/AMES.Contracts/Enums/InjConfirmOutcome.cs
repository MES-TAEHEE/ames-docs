namespace AMES.Contracts.Enums;

/// <summary>Inj04 스캔 확정 결과.</summary>
public enum InjConfirmOutcome
{
    Confirmed,
    NotFound,
    AlreadyConfirmed,
    NgBlocked,
    WrongLine,
    /// <summary>DEFECT — 재작업 대기 중. REWORK 스테이션에서 판정해야 한다.</summary>
    InRework,
    /// <summary>SCRAPPED — 폐기된 LOT.</summary>
    Scrapped,
}
