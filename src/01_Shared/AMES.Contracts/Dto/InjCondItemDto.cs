namespace AMES.Contracts.Dto;

/// <summary>MD_InjCondItem 1행 — 사출조건 항목 마스터 (SEOYON ZINJ0150 대응).</summary>
public sealed class InjCondItemDto
{
    public string  ItemCode      { get; init; } = string.Empty;
    public string? ItemName      { get; init; }
    public int?    SetAddress    { get; init; }
    public int?    ActualAddress { get; init; }
    public string  DataType      { get; init; } = "LONG";     // LONG / FLOAT
}
