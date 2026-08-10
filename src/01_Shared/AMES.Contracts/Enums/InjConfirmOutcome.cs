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
}
