namespace AMES.Contracts.Enums;

/// <summary>REWORK 스테이션 판정 결과.</summary>
public enum ReworkOutcome
{
    Done,
    /// <summary>다른 터미널이 먼저 판정했다.</summary>
    AlreadyDecided,
    NotFound,
}
