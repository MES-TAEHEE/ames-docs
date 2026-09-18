using System.Net;
using System.Net.Http.Json;

namespace AMES.Pda.Services;

public sealed class SparePartsApi(HttpClient http, AuthState auth) : PdaApi(http, auth)
{
    public const string AreaCode = SparePartsAreaCode;

    public Task<List<SparePartRow>> MastersAsync(string? query = null)
        => GetRequiredAsync<List<SparePartRow>>(
            "/api/wh/sp/master" + (string.IsNullOrWhiteSpace(query) ? "" : $"?q={Uri.EscapeDataString(query.Trim())}"),
            "Spare part master service is unavailable.");

    public Task<List<InventoryRow>> InventoryAsync(string? query = null)
        => GetRequiredAsync<List<InventoryRow>>(
            "/api/wh/sp/inventory" + (string.IsNullOrWhiteSpace(query) ? "" : $"?q={Uri.EscapeDataString(query.Trim())}"),
            "Spare parts inventory service is unavailable.");

    public async Task<InventoryScanLookupRow?> InventoryScanAsync(string scanText)
    {
        var sparePart = await SpItemAsync(scanText.Trim());
        return sparePart is null
            ? new InventoryScanLookupRow("TEXT", scanText.Trim(), null)
            : new InventoryScanLookupRow("PART", sparePart.EosSpNo, sparePart.PartName);
    }

    public Task<List<InventoryLocationRow>> InventoryLocationsAsync(string eosSpNo)
        => GetRequiredAsync<List<InventoryLocationRow>>(
            $"/api/wh/sp/inventory/locations?eosSpNo={Uri.EscapeDataString(eosSpNo.Trim())}",
            "Spare parts inventory location service is unavailable.");

    public Task<List<LocationRow>> LocationsAsync()
        => GetRequiredAsync<List<LocationRow>>("/api/wh/sp/locations", "Spare parts location service is unavailable.");

    public Task<List<LocationMapItemRow>> LocationItemsAsync(string locationId)
        => GetRequiredAsync<List<LocationMapItemRow>>(
            $"/api/wh/sp/inventory/location/{Uri.EscapeDataString(locationId.Trim())}/items",
            "Spare parts location inventory service is unavailable.");

    public Task<List<LotStatusRow>> LotStatusesAsync(string? query = null)
        => GetRequiredAsync<List<LotStatusRow>>(
            "/api/wh/sp/inventory/lots" + (string.IsNullOrWhiteSpace(query) ? "" : $"?q={Uri.EscapeDataString(query.Trim())}"),
            "Spare parts inventory service is unavailable.");

    public async Task<AdjustmentLocation?> ScanAdjustmentLocationAsync(string barcode)
    {
        Authorize();
        using var response = await _http.GetAsync(
            $"/api/wh/sp/adjust/location?barcode={Uri.EscapeDataString(barcode.Trim())}");
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadServiceErrorAsync(response, "Spare parts adjustment location service is unavailable."));
        if (response.Content.Headers.ContentLength == 0) return null;
        return await response.Content.ReadFromJsonAsync<AdjustmentLocation>();
    }

    public async Task<InboundScanRow?> ScanAdjustAsync(string scanText)
    {
        Authorize();
        using var response = await _http.GetAsync(
            $"/api/wh/sp/adjust/scan?scanText={Uri.EscapeDataString(scanText.Trim())}");
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadServiceErrorAsync(response, "Spare parts adjust scan service is unavailable."));
        return await response.Content.ReadFromJsonAsync<InboundScanRow>();
    }

    public async Task<List<WarehouseTransactionRow>> TransactionsAsync(string? search = null,
        DateTime? dateFrom = null, DateTime? dateTo = null)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(search)) query.Add($"search={Uri.EscapeDataString(search.Trim())}");
        if (dateFrom.HasValue) query.Add($"dateFrom={dateFrom.Value:yyyy-MM-dd}");
        if (dateTo.HasValue) query.Add($"dateTo={dateTo.Value:yyyy-MM-dd}");
        var url = "/api/wh/sp/transactions" + (query.Count == 0 ? "" : "?" + string.Join("&", query));
        return await GetRequiredAsync<List<WarehouseTransactionRow>>(url, "Spare parts transaction service is unavailable.");
    }

    public async Task<SparePartRow?> SpItemAsync(string eosSpNo)
    {
        Authorize();
        using var response = await _http.GetAsync($"/api/wh/sp/item?eosSpNo={Uri.EscapeDataString(eosSpNo.Trim())}");
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadServiceErrorAsync(response, "Spare parts service is unavailable."));
        return await response.Content.ReadFromJsonAsync<SparePartRow>();
    }

    public async Task<SparePartMoveResult> SpReceiveAsync(SparePartMoveReq body)
    {
        Authorize();
        using var response = await _http.PostAsJsonAsync("/api/wh/sp/inbound", body);
        return await ReadSparePartMoveResultAsync(response, "Spare parts inbound failed.");
    }

    public async Task<SparePartMoveResult> SpReleaseAsync(SparePartMoveReq body)
    {
        Authorize();
        using var response = await _http.PostAsJsonAsync("/api/wh/sp/release", body);
        return await ReadSparePartMoveResultAsync(response, "Spare parts release failed.");
    }

    public async Task SpResetTestAsync()
    {
        Authorize();
        using var response = await _http.PostAsync("/api/wh/sp/test/reset", null);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadServiceErrorAsync(response, "Spare parts test reset failed."));
    }
    public async Task<InboundReceiveResult> SpMoveLocationAsync(InboundReceiveReq body)
    {
        Authorize();
        using var response = await _http.PostAsJsonAsync("/api/wh/sp/move-location", body);
        return await ReadInboundReceiveResultAsync(response);
    }

    public async Task<InboundReceiveResult> SpSaveAdjustQtyAsync(AdjustSaveReq body)
    {
        Authorize();
        using var response = await _http.PostAsJsonAsync("/api/wh/sp/adjust/save", body);
        return await ReadInboundReceiveResultAsync(response);
    }

    public async Task<LocationRow?> WhScanLocationAsync(string locationId, string? areaCode = AreaCode)
    {
        try
        {
            Authorize();
            var url = $"/api/wh/location/scan?locationId={Uri.EscapeDataString(locationId.Trim())}";
            var resp = await _http.GetAsync(url);
            if (resp.StatusCode == HttpStatusCode.Unauthorized)
                throw new InvalidOperationException("Session expired. Sign in again.");

            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException(await ReadServiceErrorAsync(resp, "Warehouse location service is unavailable."));

            var row = await resp.Content.ReadFromJsonAsync<LocationRow>();
            EnsureArea(row?.AreaCode, areaCode);
            return row;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            throw new InvalidOperationException("Warehouse location service is unavailable.");
        }
    }
}
