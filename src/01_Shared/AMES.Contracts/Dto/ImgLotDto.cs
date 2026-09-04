namespace AMES.Contracts.Dto;

/// <summary>tbl_Lot + PR_ImgLot 조인 뷰 — IMG 원천 LOT 1건 (1 LOT = 1 EA).</summary>
public sealed class ImgLotDto
{
    public int       LotId            { get; init; }
    public string    LotCode          { get; init; } = string.Empty;
    public string    ItemNo           { get; init; } = string.Empty;
    public string?   ItemName         { get; init; }
    /// <summary>MD_Item.PGN — 라벨 S 토큰 앞부분.</summary>
    public string?   Pgn              { get; init; }
    /// <summary>MD_Item.ALC — 라벨 S 토큰 뒷부분 + 좌상 큰 글자.</summary>
    public string?   Alc              { get; init; }
    /// <summary>MD_Item.MountPos — 차량 장착 위치 FL/FR/RL/RR, 라벨 우상 글자.</summary>
    public string?   MountPos         { get; init; }
    /// <summary>발행 시점 열린 WO 의 수주처 코드(MD_Customer.CustomerCode) — 라벨 V 토큰. 수주 없는 WO 면 null.</summary>
    public string?   CustomerCode     { get; init; }
    public string?   LineId           { get; init; }
    public string?   EquipId          { get; init; }
    /// <summary>RAW(발행됨·미확정) / CONFIRMED(스캔 확정).</summary>
    public string    ConfirmStatus    { get; init; } = "RAW";
    public DateTime? ConfirmedAt      { get; init; }
    /// <summary>확정 시 차감한 원단 롤 LotID. 롤 미장착이면 null.</summary>
    public int?      FabricRollLotId  { get; init; }
    public decimal?  FabricConsumedM  { get; init; }
    public int?      BondSetupId      { get; init; }
    /// <summary>라벨 누적 발행 횟수 (최초 발행 + 재출력).</summary>
    public int       PrintedCount     { get; init; }
    public DateTime  CreatedTS        { get; init; }

    public bool IsConfirmed => ConfirmStatus == "CONFIRMED";
}
