using AMES.Web.Components.Shared;
using static AMES.Data.Repositories.WarehouseRepository;

namespace AMES.Web.Services;

public static class FactoryMapView
{
    public static string Code(FactoryMapRegion region)
    {
        if (region.Zone.Code is not null) return NormalizeFactoryMapCode(region.Zone.Code);
        var name = region.Name.Trim().ToUpperInvariant();
        return name.Length is > 0 and <= 3 && name.All(char.IsAsciiLetterUpper) ? name : "";
    }

    public static string NextCode(IEnumerable<FactoryMapRegion> regions)
    {
        var used = regions.Select(Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i <= 18278; i++)
        {
            var code = Alphabet(i);
            if (!used.Contains(code)) return code;
        }
        throw new InvalidOperationException("All region letters are in use.");
    }

    private static string Alphabet(int number)
    {
        var result = "";
        while (number > 0) { number--; result = (char)('A' + number % 26) + result; number /= 26; }
        return result;
    }

    public static string Color(FactoryMapRegion region) => $"hsl({(long)region.Id * 137 % 360} 68% 34%)";

    public static List<LocationMapStock> Stock(IEnumerable<LocationMapStock> stock,
        IEnumerable<AMES.Data.Repositories.MasterDataRepository.LocationRow> locations, FactoryMapRegion region)
    {
        var ids = locations.Where(l => l.ActiveFlag && l.LocationLevel == 2 && Same(l.LocationType, "FG")
            && Same(l.WhCode, region.Zone.Warehouse) && Same(l.AreaCode, region.Zone.Area) && Same(l.ParentLocationID, region.Zone.Parent))
            .Select(l => l.LocationID).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return stock.Where(x => ids.Contains(x.LocationNo)).OrderBy(x => x.PartNo).ThenBy(x => x.ReceivedAt).ThenBy(x => x.LotNo).ToList();
    }

    public static List<LocationMapStock> Examples(FactoryMapRegion region, IEnumerable<LocationMapStock> items, DateTime today)
    {
        var code = Code(region);
        var label = code.Length == 0 ? $"R{region.Id}" : code;
        var parts = items.Where(x => !string.IsNullOrWhiteSpace(x.PartNo)).DistinctBy(x => x.PartNo).OrderBy(x => x.PartNo).ToList();
        if (parts.Count == 0) parts = [new("", "DEMO-FG-001", "Sample finished product", null, 0, "EA")];
        return Enumerable.Range(0, 4).Select(i =>
        {
            var part = parts[(region.Id + i) % parts.Count];
            return new LocationMapStock($"{label}-{i + 1:00}", part.PartNo, part.PartName, $"DEMO-{label}-{i + 1:000}",
                12 + (region.Id + i * 7) % 40, part.Unit, region.Zone.Warehouse, region.Zone.Warehouse,
                region.Zone.Area, region.Name, ItemType: part.ItemType, ItemCategory: part.ItemCategory,
                ReceivedAt: today.AddDays(-8 + i * 2), VendorCode: part.VendorCode);
        }).ToList();
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
