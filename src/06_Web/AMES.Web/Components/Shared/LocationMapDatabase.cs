using AMES.Data.Repositories;
using AMES.Web.Services;

namespace AMES.Web.Components.Shared;

public static class LocationMapDatabase
{
    public static (List<LocationMapArea> Areas, List<LocationMapCell> Locations, List<LocationMapStock> Inventory)
        Load(MasterDataRepository md, WarehouseRepository warehouse, bool finishedGoods)
    {
        var warehouses = warehouse.ListWarehouses().ToDictionary(x => x.WhCode, x => x.WhName ?? x.WhCode, StringComparer.OrdinalIgnoreCase);
        var stock = warehouse.ListUnifiedInventory(false, includeFinishedGoods: true)
            .Select(x => new LocationMapStock(x.LocationNo, x.PartNo, x.PartName, x.LotNo, x.Qty, x.Unit,
                x.WarehouseCode, x.WarehouseName, x.AreaCode, x.AreaName, ReceivedAt:x.ReceivedAt, VendorCode:x.VendorCode));
        var inventory = InventorySearchRows.FromMaster(md.ListItems(), stock)
            .Where(x => x.Qty > 0 && (!finishedGoods || InventorySearchRows.IsSemiFinished(x.ItemType))).ToList();
        var occupied = inventory.Select(x => x.LocationNo).Where(x => !InventorySearchRows.IsFloor(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var locations = md.ListLocations().Where(x => x.ActiveFlag && x.LocationLevel == MasterDataRepository.LocationLevelDetail
            && (!finishedGoods || string.Equals(x.LocationType, "FG", StringComparison.OrdinalIgnoreCase) || occupied.Contains(x.LocationID))).ToList();
        var scopes = locations.Select(x => (x.WhCode, x.AreaCode)).ToHashSet();
        var areas = warehouse.ListWarehouseAreas().Where(x => !string.IsNullOrWhiteSpace(x.WhCode)
                && (!finishedGoods || scopes.Contains((x.WhCode!, x.AreaCode))))
            .Select(x => new LocationMapArea(x.WhCode!, x.WhName ?? x.WhCode!, x.AreaCode, x.AreaName ?? x.AreaCode, x.LocationPrefix, x.StorageMode)).ToList();
        foreach (var location in locations)
            if (!areas.Any(a => a.WarehouseCode == location.WhCode && a.AreaCode == location.AreaCode))
                areas.Add(new LocationMapArea(location.WhCode, warehouses.GetValueOrDefault(location.WhCode, location.WhCode), location.AreaCode, location.AreaCode, null));
        areas = areas.Select(area => area with
        {
            IsLineArea = locations.Any(location => !string.IsNullOrWhiteSpace(location.LineId)
                && string.Equals(location.WhCode, area.WarehouseCode, StringComparison.OrdinalIgnoreCase)
                && string.Equals(location.AreaCode, area.AreaCode, StringComparison.OrdinalIgnoreCase))
        }).ToList();
        var qty = inventory.GroupBy(x => x.LocationNo, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Qty), StringComparer.OrdinalIgnoreCase);
        // Keep the registered spatial coordinates; the future physical layout is not inferred from stock.
        var cells = locations.Select(x => new LocationMapCell(x.LocationID, x.LocationName,
            x.WhCode, warehouses.GetValueOrDefault(x.WhCode, x.WhCode), x.AreaCode,
            areas.FirstOrDefault(a => a.WarehouseCode == x.WhCode && a.AreaCode == x.AreaCode)?.AreaName ?? x.AreaCode,
            x.ZoneCode ?? "", x.ZoneCode, x.Aisle ?? "-", x.Bay ?? "-", x.Slot ?? "-",
            qty.GetValueOrDefault(x.LocationID), qty.GetValueOrDefault(x.LocationID) > 0 ? "STOCKED" : "EMPTY", x.Capacity)).ToList();
        return (areas, cells, inventory);
    }
}
