using System.Net;
using System.Net.Http.Json;
using AMES.Contracts.Dto;

namespace AMES.Tablet.Services;

public sealed class TabletInventoryApi(HttpClient http)
{
    public sealed record InventoryRow(
        string LocationNo, string? LocationName, string LineCode,
        string? WarehouseCode, string? AreaCode, string? ZoneCode,
        string? RackX, string? RackY, string? RackZ,
        string? LotNo, string? PartNo, string? PartName, decimal Qty, string Unit,
        string? WarehouseName = null, string? AreaName = null, decimal? Capacity = null, decimal DefectQty = 0, string? StorageMode = null);

    public sealed record InventorySnapshot(IReadOnlyList<InventoryRow> Rows, string? Message, bool SessionExpired = false,
        IReadOnlyList<TabletMapAreaDto>? Areas = null);

    public async Task<InventorySnapshot> LoadAsync()
    {
        try
        {
            using var response = await http.GetAsync("/api/tablet/location-map");
            if (response.StatusCode == HttpStatusCode.NotFound) return await LoadLegacyAsync();
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return new([], "Your session expired. Please sign in again.", true);
            if (!response.IsSuccessStatusCode)
                return new([], $"Inventory could not be loaded (HTTP {(int)response.StatusCode}). Please retry.");
            var map = await response.Content.ReadFromJsonAsync<TabletLocationMapDto>();
            return map?.Areas is null || map.Locations is null || map.Inventory is null
                ? new([], "Location map response was invalid. Please retry.") : FromMap(map);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return new([], "Inventory service is unavailable. Check Tailscale and retry.");
        }
    }

    public static InventorySnapshot FromMap(TabletLocationMapDto map)
    {
        var inventory = map.Inventory.ToLookup(x => x.LocationNo, StringComparer.OrdinalIgnoreCase);
        var rows = new List<InventoryRow>();
        foreach (var location in map.Locations)
        {
            var area = map.Areas.FirstOrDefault(x => Same(x.WarehouseCode, location.WarehouseCode) && Same(x.AreaCode, location.AreaCode));
            foreach (var stock in inventory[location.LocationNo].DefaultIfEmpty())
                rows.Add(new(location.LocationNo, location.LocationName, location.AreaCode,
                    location.WarehouseCode, location.AreaCode, null, location.Column, location.Row, location.Floor,
                    stock?.LotNo, stock?.PartNo, stock?.PartName, stock?.Qty ?? 0, stock?.Unit ?? "",
                    area?.WarehouseName, area?.AreaName, location.Capacity, stock?.DefectQty ?? 0, area?.StorageMode));
        }
        return new(rows, null, Areas: map.Areas);
    }

    // Older server deployments remain usable, but must not guess the configured Area storage mode.
    private async Task<InventorySnapshot> LoadLegacyAsync()
    {
        using var response = await http.GetAsync("/api/tablet/inventory");
        if (response.StatusCode == HttpStatusCode.Unauthorized) return new([], "Your session expired. Please sign in again.", true);
        if (!response.IsSuccessStatusCode) return new([], $"Inventory could not be loaded (HTTP {(int)response.StatusCode}). Please retry.");
        var rows = await response.Content.ReadFromJsonAsync<List<InventoryRow>>() ?? [];
        using var details = await http.GetAsync("/api/wh/locations");
        if (details.StatusCode == HttpStatusCode.Unauthorized) return new([], "Your session expired. Please sign in again.", true);
        if (!details.IsSuccessStatusCode) return new([], "Location details could not be loaded. Please update the server API.");
        var locations = (await details.Content.ReadFromJsonAsync<List<LegacyLocation>>() ?? [])
            .GroupBy(x => x.LocationId, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var filtered = rows.Where(x => locations.ContainsKey(x.LocationNo) && !Same(x.AreaCode, "SPARE_PARTS_AREA"))
            .Select(x => x with { WarehouseName = locations[x.LocationNo].WarehouseName, AreaName = locations[x.LocationNo].AreaName,
                Capacity = locations[x.LocationNo].Capacity }).ToList();
        return new(filtered, null);
    }

    private sealed record LegacyLocation(string LocationId, string? WarehouseName, string? AreaName, decimal? Capacity);
    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
