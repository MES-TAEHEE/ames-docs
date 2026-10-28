namespace AMES.Contracts.Enums;

/// <summary>라인 불량 팝업의 LOT 스캔 등록 결과.</summary>
public enum DefectRegisterOutcome
{
    Registered,
    NotFound,
    WrongLine,
    /// <summary>이미 DEFECT 상태 — 재작업 대기열에 있다.</summary>
    AlreadyInRework,
    /// <summary>이미 폐기된 LOT.</summary>
    Scrapped,
    /// <summary>IMG 가 완제품의 코어로 이미 쓴 사출 LOT — 완제품은 양품인데 코어만 폐기되는 모순을 막는다.</summary>
    UsedAsCore,
    /// <summary>이미 출하된 LOT(재고로 돌아오지 않음) — 출하품 불량은 고객 반품 검사에서 다룬다.</summary>
    Shipped,
}
