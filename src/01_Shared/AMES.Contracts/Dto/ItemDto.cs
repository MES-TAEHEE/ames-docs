using AMES.Contracts.Enums;

namespace AMES.Contracts.Dto;

/// <summary>
/// DTO for dbo.MD_Item rows passed between Data and UI layers.
/// Mirrors the table columns; FK targets stay as string codes (natural keys).
/// </summary>
public sealed class ItemDto
{
    public required string ItemNo { get; init; }
    public required string ItemName { get; init; }
    public string? ItemNameEN { get; init; }
    public ItemType ItemType { get; init; }
    public string? ItemCategory { get; init; }
    public string? DefaultUOM { get; init; }
    public string? RoutingType { get; init; }    // 'A' / 'B' / null
    public decimal? MinStock { get; init; }
    public decimal? SafetyStock { get; init; }
    public decimal? UnitCost { get; init; }
    public string? CustItemNoSAV { get; init; }
    public string? CustItemNoGEO { get; init; }
    public bool ActiveFlag { get; init; } = true;
}
