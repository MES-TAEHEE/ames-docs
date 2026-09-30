namespace AMES.Contracts.Enums;

/// <summary>IMG-MAIN 사출 Core 스캔 → 완제품 LOT 생성 결과.</summary>
public enum ImgCoreOutcome
{
    Created,
    /// <summary>INJ LOT 이 아니다(없는 코드·IMG LotCode 포함).</summary>
    NotFound,
    /// <summary>Core 가 RAW — 사출에서 스캔 확정되지 않았다.</summary>
    CoreUnconfirmed,
    /// <summary>Core 가 NG_BLOCKED·DEFECT.</summary>
    CoreDefect,
    /// <summary>Core 가 SCRAPPED.</summary>
    CoreScrapped,
    /// <summary>이미 다른 완제품 LOT 에 연결됐다.</summary>
    CoreUsed,
    /// <summary>이 라인에 Core 품번의 열린 WO 단계가 없다.</summary>
    NoWoForItem,
}
