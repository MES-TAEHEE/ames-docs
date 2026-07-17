namespace AMES.Contracts.Dto;

/// <summary>MD_MoldItemMap 1행 — 금형코드+색상 → 품번·캐비티 (SEOYON APM2120 대응).</summary>
public sealed class MoldItemMapDto
{
    public string  MoldCode  { get; init; } = string.Empty;
    public string  ColorCode { get; init; } = string.Empty;
    public int     CavityNo  { get; init; }
    public string  CavityPos { get; init; } = string.Empty;   // LH / RH
    public string  ItemNo    { get; init; } = string.Empty;
    public string? ItemName  { get; init; }
    public string? MoldId    { get; init; }
}
