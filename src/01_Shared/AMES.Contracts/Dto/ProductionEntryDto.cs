namespace AMES.Contracts.Dto;

/// <summary>IMG-MAIN 우측 "오늘 실적 이력" 한 행 — PR_ProductionResult 하나.</summary>
public sealed class ProductionEntryDto
{
    public required int      ResultId { get; init; }
    public required DateTime EntryAt  { get; init; }
    public required string   ItemNo   { get; init; }
    public string?           WoNumber { get; init; }
    public string?           LotCode  { get; init; }
    public required int      GoodQty  { get; init; }
}
