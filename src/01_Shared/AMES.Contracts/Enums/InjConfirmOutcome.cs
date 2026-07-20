namespace AMES.Contracts.Enums;

/// <summary>Inj04 스캔 확정 결과.</summary>
public enum InjConfirmOutcome
{
    Confirmed,
    NotFound,
    AlreadyConfirmed,
    NgBlocked,
    WrongLine,
}
