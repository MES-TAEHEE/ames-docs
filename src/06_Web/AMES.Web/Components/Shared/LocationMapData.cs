namespace AMES.Web.Components.Shared;

public sealed record LocationMapCell(
    string LocationNo,
    string? LocationName,
    string WarehouseCode,
    string? WarehouseName,
    string AreaCode,
    string? AreaName,
    string ZoneCode,
    string? ZoneName,
    string Column,
    string Row,
    string Floor,
    decimal Qty,
    string Status,
    decimal? Capacity = null,
    decimal DefectQty = 0)
{
    public bool IsFull => AMES.Data.Services.LocationCapacity.IsFull(Qty, Capacity);
}

public sealed record LocationMapStock(
    string LocationNo,
    string PartNo,
    string? PartName,
    string? LotNo,
    decimal Qty,
    string Unit,
    string WarehouseCode = "",
    string WarehouseName = "",
    string AreaCode = "",
    string AreaName = "",
    decimal DefectQty = 0,
    string ItemType = "",
    string ItemCategory = "");

public sealed record LocationMapArea(string WarehouseCode, string WarehouseName, string AreaCode, string AreaName, string? Prefix);
