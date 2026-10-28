using AMES.Data.Repositories;

namespace AMES.Web.Components.Shared;

public static class LocationMapDatabase
{
    public static (List<LocationMapArea> Areas, List<LocationMapCell> Locations, List<LocationMapStock> Inventory)
        Load(MasterDataRepository md, WarehouseRepository warehouse, bool finishedGoods)
    {
        bool Included(string area) => finishedGoods ? area == "FG_AREA" : area != "SPARE_PARTS_AREA";
        var warehouses = warehouse.ListWarehouses().ToDictionary(x => x.WhCode, x => x.WhName ?? x.WhCode, StringComparer.OrdinalIgnoreCase);
        var locations = md.ListLocations().Where(x => x.ActiveFlag
            && x.LocationLevel == MasterDataRepository.LocationLevelDetail && Included(x.AreaCode)).ToList();
        var areas = warehouse.ListWarehouseAreas().Where(x => Included(x.AreaCode) && !string.IsNullOrWhiteSpace(x.WhCode))
            .Select(x => new LocationMapArea(x.WhCode!, x.WhName ?? x.WhCode!, x.AreaCode, x.AreaName ?? x.AreaCode, x.LocationPrefix, x.StorageMode)).ToList();
        if (!finishedGoods)
            foreach (var wh in warehouses)
                if (!areas.Any(x => x.WarehouseCode.Equals(wh.Key, StringComparison.OrdinalIgnoreCase)))
                    areas.Add(new LocationMapArea(wh.Key, wh.Value, "", "", null));
        foreach (var location in locations)
            if (!areas.Any(a => a.WarehouseCode == location.WhCode && a.AreaCode == location.AreaCode))
                areas.Add(new LocationMapArea(location.WhCode, warehouses.GetValueOrDefault(location.WhCode, location.WhCode), location.AreaCode, location.AreaCode, null));
        areas = areas.Select(area => area with
        {
            IsLineArea = locations.Any(location => !string.IsNullOrWhiteSpace(location.LineId)
                && string.Equals(location.WhCode, area.WarehouseCode, StringComparison.OrdinalIgnoreCase)
                && string.Equals(location.AreaCode, area.AreaCode, StringComparison.OrdinalIgnoreCase))
        }).ToList();

        var inventoryRows = finishedGoods ? warehouse.ListUnifiedInventory(true, includeDefects: true)
            : warehouse.ListUnifiedInventory(false, includeDefects: true).Concat(warehouse.ListUnifiedInventory(true, includeDefects: true));
        var inventory = inventoryRows
            .Where(x => Included(x.AreaCode))
            .Select(x => new LocationMapStock(x.LocationNo, x.PartNo, x.PartName, x.LotNo, x.Qty, x.Unit,
                x.WarehouseCode, x.WarehouseName, x.AreaCode, x.AreaName, x.DefectQty)).ToList();
        var qty = inventory.GroupBy(x => x.LocationNo, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Qty), StringComparer.OrdinalIgnoreCase);
        var defects = inventory.GroupBy(x => x.LocationNo, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.DefectQty), StringComparer.OrdinalIgnoreCase);
        // The map displays Aisle (A1, B1...) horizontally and Bay (01, 02...) vertically.
        var cells = locations.Select(x => new LocationMapCell(x.LocationID, x.LocationName,
            x.WhCode, warehouses.GetValueOrDefault(x.WhCode, x.WhCode), x.AreaCode,
            areas.FirstOrDefault(a => a.WarehouseCode == x.WhCode && a.AreaCode == x.AreaCode)?.AreaName ?? x.AreaCode,
            x.ZoneCode ?? "", x.ZoneCode, x.Aisle ?? "-", x.Bay ?? "-", x.Slot ?? "-",
            qty.GetValueOrDefault(x.LocationID), qty.GetValueOrDefault(x.LocationID) > 0 ? "STOCKED" : "EMPTY", x.Capacity,
            defects.GetValueOrDefault(x.LocationID))).ToList();
        return (areas, cells, inventory);
    }
}
