namespace AMES.Contracts.Enums;

/// <summary>IMG-MAIN 라벨 스캔 확정 결과.</summary>
public enum ImgConfirmOutcome
{
    Confirmed,
    NotFound,
    AlreadyConfirmed,
    WrongLine,
    /// <summary>라인에 LOT 품번과 같은 품번의 열린 WO 단계가 없다.</summary>
    NoWoForItem,
}
