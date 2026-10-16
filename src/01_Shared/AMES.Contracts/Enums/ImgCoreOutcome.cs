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
    /// <summary>열린 WO 도, 작업자가 고른 완제품 품번도 없어 완제품을 정할 수 없다(반환 품번은 코어). WO 없는 생산은 좌측 선택이 완제품이다.</summary>
    NoFinishedItem,
    /// <summary>작업자가 고른 완제품의 유효 BOM 코어가 스캔한 코어와 다르다.</summary>
    CoreMismatch,
}
