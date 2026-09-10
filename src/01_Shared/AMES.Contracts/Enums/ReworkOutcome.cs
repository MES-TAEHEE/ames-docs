namespace AMES.Contracts.Enums;

/// <summary>REWORK 스테이션 판정 결과.</summary>
public enum ReworkOutcome
{
    Done,
    /// <summary>다른 터미널이 먼저 판정했다.</summary>
    AlreadyDecided,
    /// <summary>수리 양품인데 원래 라인에 이 품번의 열린 WO 가 없다 — 폐기는 가능.</summary>
    NoWo,
    NotFound,
}
