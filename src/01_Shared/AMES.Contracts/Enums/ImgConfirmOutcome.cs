namespace AMES.Contracts.Enums;

/// <summary>IMG-MAIN 라벨 스캔 확정 결과.</summary>
public enum ImgConfirmOutcome
{
    Confirmed,
    NotFound,
    AlreadyConfirmed,
    WrongLine,
    /// <summary>DEFECT — 재작업 대기 중. REWORK 스테이션에서 판정해야 한다.</summary>
    InRework,
    /// <summary>SCRAPPED — 폐기된 LOT.</summary>
    Scrapped,
    /// <summary>스캔한 팔레트가 MD_Pallet 에 없다 — 확정하지 않음(2026-10-21 팔레트 적재).</summary>
    PalletNotFound,
    /// <summary>팔레트가 사용 안 함이거나 출고된 채 회수되지 않았다 — 확정하지 않음.</summary>
    PalletUnavailable,
    /// <summary>팔레트에 다른 품번이 실려 있다(혼적 금지) — 확정하지 않음.</summary>
    PalletMixed,
    /// <summary>팔레트가 적입수량(MD_Item.PalletQty)만큼 찼다 — 새 팔레트를 스캔해야 확정된다.</summary>
    PalletFull,
}
