using AMES.Data.Repositories;
using AMES.Web.Components.Shared;

namespace AMES.Web.Services;

public static class InventorySearchRows
{
    public static bool IsFloor(string? location) => string.IsNullOrWhiteSpace(location) || location.Trim() == "0000";
    public static bool IsSemiFinished(string? itemType) => itemType?.Trim().ToUpperInvariant() is "SUB" or "ASSY";
    public static decimal UnassignedQty(IEnumerable<LocationMapStock> inventory)
        => inventory.Where(row => IsFloor(row.LocationNo)).Sum(row => row.Qty);

    public sealed record LotRow(string PartNo, string PartName, string LotNo, string LocationNo,
        DateTime? ReceivedAt, string Unit, decimal Qty, string ItemCategory, string VendorCodes)
    {
        public bool IsFloor => InventorySearchRows.IsFloor(LocationNo);
        public decimal StoredQty => IsFloor ? 0 : Qty;
        public decimal FloorQty => IsFloor ? Qty : 0;
    }

    public static List<LotRow> Lots(IEnumerable<LocationMapStock> inventory,
        IEnumerable<ScmRepository.ItemVendorRow> vendors, bool finishedGoods)
    {
        var byPart = vendors.ToLookup(x => x.ItemNo, StringComparer.OrdinalIgnoreCase);
        return inventory.Where(x => x.Qty > 0 && !string.IsNullOrWhiteSpace(x.LotNo)
                && IsSemiFinished(x.ItemType) == finishedGoods)
            .Select(x => new LotRow(x.PartNo, x.PartName ?? "", x.LotNo!, IsFloor(x.LocationNo) ? "0000" : x.LocationNo,
                x.ReceivedAt, x.Unit, x.Qty, x.ItemCategory,
                !string.IsNullOrWhiteSpace(x.VendorCode) ? x.VendorCode :
                    string.Join(", ", byPart[x.PartNo].Select(v => v.VendorID).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(v => v))))
            .OrderBy(x => x.PartNo, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(x => x.IsFloor)
            .ThenBy(x => x.ReceivedAt ?? DateTime.MaxValue)
            .ThenBy(x => x.LotNo, StringComparer.OrdinalIgnoreCase).ToList();
    }

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
            (vendor.Length == 0 || (!string.IsNullOrWhiteSpace(row.VendorCode) ? Same(row.VendorCode, vendor) : vendorParts.Contains(row.PartNo))) &&
            (itemType.Length == 0 || Same(row.ItemType, itemType)) &&
            (category.Length == 0 || Same(row.ItemCategory, category)) &&
            (query.Length == 0 || new[] {row.PartNo, row.PartName, row.LotNo, IsFloor(row.LocationNo) ? "0000" : row.LocationNo, row.VendorCode}
                .Any(value => value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true))).ToList();
    }

    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
