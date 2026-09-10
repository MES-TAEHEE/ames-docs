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
    /// <summary>DEFECT — 재작업 대기 중. REWORK 스테이션에서 판정해야 한다.</summary>
    InRework,
    /// <summary>SCRAPPED — 폐기된 LOT.</summary>
    Scrapped,
}
