namespace AMES.Data.Services.PoSync;

/// <summary>
/// APG_SRM_MM31006.INQUERY 커서 한 행. 프로퍼티명은 커서 컬럼명 그대로(대문자) — JSON 이 그 이름으로 온다.
/// 업서트에 필요한 컬럼만 둔다. 나머지는 역직렬화에서 무시된다.
/// </summary>
public sealed record SrmPoRow(
    string?  PONO,
    string?  PO_DATE,
    string?  PO_DELI_DATE,
    string?  PARTNO,
    string?  LOEKZ,
    string?  ELIKZ,
    decimal? PO_QTY,
    decimal? DELI_QTY);
