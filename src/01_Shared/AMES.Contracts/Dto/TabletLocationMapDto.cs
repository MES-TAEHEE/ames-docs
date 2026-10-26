namespace AMES.Contracts.Dto;

public sealed record TabletMapAreaDto(string WarehouseCode, string WarehouseName, string AreaCode, string AreaName, string StorageMode);

public sealed record TabletMapLocationDto(
    string LocationNo, string? LocationName, string WarehouseCode, string AreaCode,
    string? Column, string? Row, string? Floor, decimal? Capacity);

public sealed record TabletMapStockDto(
    string LocationNo, string LotNo, string PartNo, string? PartName,
    decimal Qty, string Unit, decimal DefectQty = 0);

public sealed record TabletLocationMapDto(
    IReadOnlyList<TabletMapAreaDto> Areas,
    IReadOnlyList<TabletMapLocationDto> Locations,
    IReadOnlyList<TabletMapStockDto> Inventory);
