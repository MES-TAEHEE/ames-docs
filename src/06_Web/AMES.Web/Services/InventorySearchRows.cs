using AMES.Data.Repositories;
using AMES.Web.Components.Shared;

namespace AMES.Web.Services;

public static class InventorySearchRows
{
    public static List<LocationMapStock> FromMaster(IEnumerable<MasterDataRepository.ItemRow> items, IEnumerable<LocationMapStock> inventory)
    {
        var stock = inventory.ToLookup(x => x.PartNo, StringComparer.OrdinalIgnoreCase);
        return items.SelectMany(item =>
        {
            var rows = stock[item.ItemNo].ToList();
            if (rows.Count == 0)
                rows.Add(new("", item.ItemNo, item.ItemName, null, 0, item.DefaultUOM ?? "EA"));
            return rows.Select(row => row with
            {
                PartName = item.ItemName,
                Unit = string.IsNullOrWhiteSpace(item.DefaultUOM) ? "EA" : item.DefaultUOM,
                ItemType = item.ItemType ?? "",
                ItemCategory = item.ItemCategory ?? ""
            });
        }).ToList();
    }

    public static List<LocationMapStock> Filter(IEnumerable<LocationMapStock> inventory,
        IEnumerable<ScmRepository.ItemVendorRow> vendors, string query, string warehouse, string area,
        string vendor, string itemType, string category)
    {
        var vendorParts = vendors.Where(x => Same(x.VendorID, vendor)).Select(x => x.ItemNo).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return inventory.Where(row =>
            (warehouse.Length == 0 || Same(row.WarehouseCode, warehouse)) &&
            (area.Length == 0 || Same(row.AreaCode, area)) &&
            (vendor.Length == 0 || vendorParts.Contains(row.PartNo)) &&
            (itemType.Length == 0 || Same(row.ItemType, itemType)) &&
            (category.Length == 0 || Same(row.ItemCategory, category)) &&
            (query.Length == 0 || new[] {row.PartNo, row.PartName, row.LotNo, row.LocationNo}
                .Any(value => value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true))).ToList();
    }

    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
