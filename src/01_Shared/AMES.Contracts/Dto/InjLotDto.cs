namespace AMES.Contracts.Dto;

/// <summary>tbl_Lot + PR_InjLot(+검사) 조인 뷰 — INJ 원천 LOT 1건.</summary>
public sealed class InjLotDto
{
    public int      LotId            { get; init; }
    public string   LotCode          { get; init; } = string.Empty;
    public string   ItemNo           { get; init; } = string.Empty;
    public string?  ItemName         { get; init; }
    public string?  LineId           { get; init; }
    public string?  EquipId          { get; init; }
    public string?  MoldCode         { get; init; }
    public string?  ColorCode        { get; init; }
    public string?  MoldId           { get; init; }
    public int?     CavityNo         { get; init; }
    public string?  CavityPos        { get; init; }
    public string?  PressType        { get; init; }
    public string   ConfirmStatus    { get; init; } = "RAW";
    public long?    MachineShotCount { get; init; }
    public DateTime CreatedTS        { get; init; }
    public bool?    OverallNg        { get; init; }
    /// <summary>검사 없거나 전항목 OK/PASS면 null — NG 여부는 OverallNg로 판별.</summary>
    public string?  InspectionSummary { get; init; }
}
