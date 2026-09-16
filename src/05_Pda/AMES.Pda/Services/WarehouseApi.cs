using System.Net;
using System.Net.Http.Json;

namespace AMES.Pda.Services;

public sealed class WarehouseApi(HttpClient http, AuthState auth, SparePartsApi spareParts,
    FinishedGoodsApi fgApi) : PdaApi(http, auth)
{
    public Task<List<InboundRow>>         WhInboundTodayAsync()    => Get<List<InboundRow>>("/api/wh/inbound/today");
    public async Task<List<InventoryRow>> WhInventoryAsync(string? q = null, DateTime? dateFrom = null, DateTime? dateTo = null,
        bool simulateFailure = false, string? areaCode = null)
    {
        if (string.Equals(areaCode, SparePartsAreaCode, StringComparison.OrdinalIgnoreCase))
            return await spareParts.InventoryAsync(q);

        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(q)) query.Add($"q={Uri.EscapeDataString(q.Trim())}");
        if (dateFrom.HasValue) query.Add($"dateFrom={dateFrom.Value:yyyy-MM-dd}");
        if (dateTo.HasValue) query.Add($"dateTo={dateTo.Value:yyyy-MM-dd}");
        if (!string.IsNullOrWhiteSpace(areaCode)) query.Add($"areaCode={Uri.EscapeDataString(areaCode.Trim())}");
        if (simulateFailure) query.Add("simulateFailure=true");
        var url = "/api/wh/inventory" + (query.Count == 0 ? "" : "?" + string.Join("&", query));
        return await GetRequiredAsync<List<InventoryRow>>(url, "Inventory service is unavailable.");
    }

    public async Task<InventoryTestChangeResult> WhToggleInventoryTestQtyAsync()
    {
        Authorize();
        using var response = await _http.PostAsync("/api/wh/inventory/test/toggle-qty", null);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadServiceErrorAsync(response, "Inventory refresh test failed."));
        return await response.Content.ReadFromJsonAsync<InventoryTestChangeResult>()
               ?? new InventoryTestChangeResult(false, "Inventory refresh test returned no result.", "", 0);
    }

    public async Task<InventoryScanLookupRow?> WhInventoryScanAsync(string? scanText, string? areaCode = null)
    {
        if (string.IsNullOrWhiteSpace(scanText))
            return null;

        var value = scanText.Trim();
        if (string.Equals(areaCode, SparePartsAreaCode, StringComparison.OrdinalIgnoreCase))
            return await spareParts.InventoryScanAsync(value);
        return await GetRequiredAsync<InventoryScanLookupRow>(
            $"/api/wh/inventory/scan?scanText={Uri.EscapeDataString(value)}",
            "Inventory scan service is unavailable.");
    }

    public async Task<List<InventoryLocationRow>> WhInventoryLocationsAsync(string itemNo, DateTime? dateFrom = null, DateTime? dateTo = null,
        string? areaCode = null)
    {
        if (string.Equals(areaCode, SparePartsAreaCode, StringComparison.OrdinalIgnoreCase))
            return await spareParts.InventoryLocationsAsync(itemNo);

        var query = new List<string> { $"itemNo={Uri.EscapeDataString(itemNo.Trim())}" };
        if (dateFrom.HasValue) query.Add($"dateFrom={dateFrom.Value:yyyy-MM-dd}");
        if (dateTo.HasValue) query.Add($"dateTo={dateTo.Value:yyyy-MM-dd}");
        if (!string.IsNullOrWhiteSpace(areaCode)) query.Add($"areaCode={Uri.EscapeDataString(areaCode.Trim())}");
        return await GetRequiredAsync<List<InventoryLocationRow>>(
            "/api/wh/inventory/locations?" + string.Join("&", query),
            "Inventory location service is unavailable.");
    }
    public async Task<List<LocationRow>> WhLocationsAsync(string? areaCode = null)
    {
        if (string.Equals(areaCode, SparePartsAreaCode, StringComparison.OrdinalIgnoreCase))
            return await spareParts.LocationsAsync();

        return FilterByArea(
            NormalizeLocationRows(await GetRequiredAsync<List<LocationRow>>("/api/wh/locations", "Location service is unavailable.")),
            row => row.AreaCode, areaCode);
    }
    public async Task<List<LocationRow>> WhLocationMapAsync()
    {
        return NormalizeLocationRows(
            await GetRequiredAsync<List<LocationRow>>("/api/wh/locations", "Location map service is unavailable."));
    }
    public async Task<List<LocationMapItemRow>> WhLocationMapItemsAsync(string locationId, DateTime? dateFrom = null, DateTime? dateTo = null,
        string? areaCode = null)
    {
        if (string.Equals(areaCode, SparePartsAreaCode, StringComparison.OrdinalIgnoreCase))
            return await spareParts.LocationItemsAsync(locationId);

        async Task<List<LocationMapItemRow>> QueryApiAsync()
        {
            var query = new List<string>();
            if (dateFrom.HasValue)
                query.Add($"dateFrom={Uri.EscapeDataString(dateFrom.Value.ToString("yyyy-MM-dd"))}");
            if (dateTo.HasValue)
                query.Add($"dateTo={Uri.EscapeDataString(dateTo.Value.ToString("yyyy-MM-dd"))}");

            var url = $"/api/wh/inventory/location/{Uri.EscapeDataString(locationId.Trim())}";
            if (query.Count > 0)
                url += "?" + string.Join("&", query);

            return await Get<List<LocationMapItemRow>>(url);
        }

        return await QueryApiAsync();
    }
    public async Task<LocationRow?> WhScanLocationAsync(string locationId, string? areaCode = null)
    {
        if (string.Equals(areaCode, SparePartsAreaCode, StringComparison.OrdinalIgnoreCase))
            return await spareParts.WhScanLocationAsync(locationId);

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
    public async Task<ReleaseSlipStatusRow?> WhReleaseSlipStatusAsync(string pickSlipNo)
    {
        Authorize();
        try
        {
            var url = $"/api/wh/release/schedule/{Uri.EscapeDataString(pickSlipNo.Trim())}/status";
            var resp = await _http.GetAsync(url);
            return resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<ReleaseSlipStatusRow>() : null;
        }
        catch
        {
            return null;
        }
    }
    public Task<List<ReleasePickLineRow>> WhReleaseLinesAsync(string pickSlipNo)
        => Get<List<ReleasePickLineRow>>($"/api/wh/release/schedule/{Uri.EscapeDataString(pickSlipNo)}/lines");
    public async Task<List<ReleaseFifoLotRow>> WhReleaseFifoLotsAsync(string pickSlipNo, string? areaCode = null)
        => await FilterByAreaAsync(
            await Get<List<ReleaseFifoLotRow>>($"/api/wh/release/schedule/{Uri.EscapeDataString(pickSlipNo)}/fifo-lots"),
            row => row.LocationNo, areaCode);
    public async Task<ReleaseLotRow?> WhReleaseLotAsync(string pickSlipNo, string lotNo, string? areaCode = null)
    {
        Authorize();
        try
        {
            var url = $"/api/wh/release/lot?pickSlipNo={Uri.EscapeDataString(pickSlipNo)}&lotNo={Uri.EscapeDataString(lotNo)}";
            var resp = await _http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;
            var row = await resp.Content.ReadFromJsonAsync<ReleaseLotRow>();
            if (row is not null && !string.IsNullOrWhiteSpace(areaCode))
                await WhScanLocationAsync(row.LocationNo ?? "", areaCode);
            return row;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
    public async Task<List<LotStatusRow>> WhLotStatusesAsync(string? q = null, string? areaCode = null)
    {
        if (string.Equals(areaCode, SparePartsAreaCode, StringComparison.OrdinalIgnoreCase))
            return await spareParts.LotStatusesAsync(q);

        var rows = await GetRequiredAsync<List<LotStatusRow>>(
            "/api/wh/inventory/lots" + (string.IsNullOrWhiteSpace(q) ? "" : $"?q={Uri.EscapeDataString(q.Trim())}"),
            "Inventory lot service is unavailable.");
        return await FilterByAreaAsync(rows, row => row.LocationId, areaCode);
    }
    public Task<HttpResponseMessage> WhReleaseCompleteAsync(ReleaseCompleteReq body)
        => Post("/api/wh/release/complete", body);
    public Task<List<OutgoingVendorRow>> WhOutgoingVendorsAsync()
        => Get<List<OutgoingVendorRow>>("/api/wh/release/outgoing/vendors");
    public async Task<DirectOutgoingLotRow?> WhDirectOutgoingLotAsync(string lotNo, string? areaCode = null)
    {
        Authorize();
        var resp = await _http.GetAsync($"/api/wh/release/outgoing/lot?lotNo={Uri.EscapeDataString(lotNo.Trim())}");
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadServiceErrorAsync(resp, "Warehouse outgoing service is unavailable."));
        var row = await resp.Content.ReadFromJsonAsync<DirectOutgoingLotRow>();
        if (row is not null && !string.IsNullOrWhiteSpace(areaCode))
            await WhScanLocationAsync(row.LocationId ?? "", areaCode);
        return row;
    }
    public Task<HttpResponseMessage> WhDirectOutgoingAsync(DirectOutgoingReq body)
        => Post("/api/wh/release/outgoing", body);
    public Task<List<TransactionRow>>     WhTransactionsAsync(int days = 7) => Get<List<TransactionRow>>($"/api/wh/transactions?days={days}");

    public Task<HttpResponseMessage> WhReceiveAsync(ReceiveReq body) => Post("/api/wh/inbound/receive", body);
    public async Task<InboundDocumentResult> WhInboundDocumentAsync(string mode, string barcode)
    {
        Authorize();
        var url = $"/api/wh/inbound/document?mode={Uri.EscapeDataString(mode.Trim())}&barcode={Uri.EscapeDataString(barcode.Trim())}";
        var resp = await _http.GetAsync(url);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadServiceErrorAsync(resp, "Warehouse document service is unavailable."));

        return await resp.Content.ReadFromJsonAsync<InboundDocumentResult>()
            ?? new InboundDocumentResult(null, [], []);
    }

    public async Task<InboundScanRow?> WhScanInboundAsync(string mode, string barcode)
    {
        try
        {
            Authorize();
            var url = $"/api/wh/inbound/scan?mode={Uri.EscapeDataString(mode.Trim())}&barcode={Uri.EscapeDataString(barcode.Trim())}";
            var resp = await _http.GetAsync(url);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException(await ReadServiceErrorAsync(resp, "Warehouse scan service is unavailable."));

            return await resp.Content.ReadFromJsonAsync<InboundScanRow>();
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            throw new InvalidOperationException("Warehouse scan service is unavailable.");
        }
    }


    public async Task<AdjustmentLocation?> ScanAdjustmentLocationAsync(string barcode, bool finishedGoods, string? areaCode = null)
    {
        if (string.Equals(areaCode, SparePartsAreaCode, StringComparison.OrdinalIgnoreCase))
            return await spareParts.ScanAdjustmentLocationAsync(barcode);
        if (finishedGoods)
            return await fgApi.ScanAdjustmentLocationAsync(barcode);

        Authorize();
        using var response = await _http.GetAsync(
            $"/api/wh/adjust/location?barcode={Uri.EscapeDataString(barcode.Trim())}");
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadServiceErrorAsync(response, "Adjustment location service is unavailable."));
        // An unknown location returns an empty 200 response; the caller then tries a stock barcode.
        if (response.Content.Headers.ContentLength == 0) return null;
        var json = await response.Content.ReadAsStringAsync();
        var row = string.IsNullOrWhiteSpace(json) ? null
            : System.Text.Json.JsonSerializer.Deserialize<AdjustmentLocation>(json, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        if (row is not null && !string.IsNullOrWhiteSpace(areaCode))
            await WhScanLocationAsync(row.LocationId, areaCode);
        return row;
    }

    public async Task<InboundScanRow?> WhScanAdjustAsync(string scanText, string? areaCode = null)
    {
        if (string.Equals(areaCode, SparePartsAreaCode, StringComparison.OrdinalIgnoreCase))
            return await spareParts.ScanAdjustAsync(scanText);

        try
        {
            Authorize();
            var url = $"/api/wh/adjust/scan?scanText={Uri.EscapeDataString(scanText.Trim())}";
            var resp = await _http.GetAsync(url);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException(await ReadServiceErrorAsync(resp, "Warehouse adjust scan service is unavailable."));

            var row = await resp.Content.ReadFromJsonAsync<InboundScanRow>();
            if (row is not null && !string.IsNullOrWhiteSpace(areaCode))
                await WhScanLocationAsync(row.ReceivedLocation ?? "", areaCode);
            return row;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            throw new InvalidOperationException("Warehouse adjust scan service is unavailable.");
        }
    }

    public async Task<List<WarehouseTransactionRow>> WhWarehouseTransactionsAsync(string? search = null, DateTime? dateFrom = null,
        DateTime? dateTo = null, bool finishedGoods = false, string? areaCode = null)
    {
        if (finishedGoods)
            return await fgApi.TransactionsAsync(search, dateFrom, dateTo);
        if (string.Equals(areaCode, SparePartsAreaCode, StringComparison.OrdinalIgnoreCase))
            return await spareParts.TransactionsAsync(search, dateFrom, dateTo);

        Authorize();
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(search))
            query.Add($"search={Uri.EscapeDataString(search.Trim())}");
        if (dateFrom.HasValue)
            query.Add($"dateFrom={Uri.EscapeDataString(dateFrom.Value.ToString("yyyy-MM-dd"))}");
        if (dateTo.HasValue)
            query.Add($"dateTo={Uri.EscapeDataString(dateTo.Value.ToString("yyyy-MM-dd"))}");

        var url = "/api/wh/warehouse-transactions";
        if (query.Count > 0)
            url += "?" + string.Join("&", query);

        var rows = await _http.GetFromJsonAsync<List<WarehouseTransactionRow>>(url)
            ?? new List<WarehouseTransactionRow>();
        return await FilterByAreaAsync(rows, row => row.LocationId, areaCode);
    }

    public async Task<InboundReceiveResult> WhReceiveInboundAsync(InboundReceiveReq body)
    {
        try
        {
            Authorize();
            var resp = await _http.PostAsJsonAsync("/api/wh/inbound/receive-lot", body);
            return await ReadInboundReceiveResultAsync(resp);
        }
        catch
        {
            return new InboundReceiveResult(false, "Warehouse receive service is unavailable.", null);
        }
    }

    public async Task<InboundReceiveResult> WhMoveInboundLocationAsync(InboundReceiveReq body)
    {
        try
        {
            Authorize();
            var resp = await _http.PostAsJsonAsync("/api/wh/inbound/move-location", body);
            return await ReadInboundReceiveResultAsync(resp);
        }
        catch
        {
            return new InboundReceiveResult(false, "Warehouse location service is unavailable.", null);
        }
    }

    public async Task<InboundReceiveResult> WhCancelInboundAsync(InboundCancelReq body)
    {
        try
        {
            Authorize();
            var resp = await _http.PostAsJsonAsync("/api/wh/inbound/cancel", body);
            return await ReadInboundReceiveResultAsync(resp);
        }
        catch
        {
            return new InboundReceiveResult(false, "Warehouse cancel service is unavailable.", null);
        }
    }

    public async Task<InboundReceiveResult> WhSaveAdjustQtyAsync(AdjustSaveReq body)
    {
        try
        {
            Authorize();
            var resp = await _http.PostAsJsonAsync("/api/wh/adjust/save", body);
            return await ReadInboundReceiveResultAsync(resp);
        }
        catch
        {
            return new InboundReceiveResult(false, "Warehouse adjustment service is unavailable.", null);
        }
    }

    public async Task WhResetSimpleInboundTestAsync()
    {
        Authorize();
        using var response = await _http.PostAsync("/api/wh/inbound/test/simple-reset", null);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadServiceErrorAsync(response, "Inbound test reset failed."));
    }

    public async Task WhResetPptTestAsync(string screen)
    {
        Authorize();
        using var response = await _http.PostAsync($"/api/wh/test/ppt-reset/{Uri.EscapeDataString(screen)}", null);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadServiceErrorAsync(response, "PPT test reset failed."));
    }

    public async Task WhResetHistoryTestAsync()
    {
        Authorize();
        using var response = await _http.PostAsync("/api/wh/transactions/test/reset", null);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadServiceErrorAsync(response, "Transaction test reset failed."));
    }

    public async Task<AdjustTestResetResult> WhResetAdjustTestAsync()
    {
        Authorize();
        using var response = await _http.PostAsync("/api/wh/adjust/test/reset", null);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadServiceErrorAsync(response, "Adjust test reset failed."));
        return await response.Content.ReadFromJsonAsync<AdjustTestResetResult>()
               ?? new AdjustTestResetResult(false, "Adjust test reset returned no result.", "", 0);
    }

    public Task<HttpResponseMessage> WhPickAsync   (PickReq    body) => Post("/api/wh/release/pick",      body);

    private async Task<List<T>> FilterByAreaAsync<T>(IEnumerable<T> rows, Func<T, string?> locationSelector,
        string? areaCode)
    {
        var list = rows.ToList();
        if (string.IsNullOrWhiteSpace(areaCode)) return list;

        var locationIds = (await WhLocationsAsync(areaCode))
            .Select(row => row.LocationId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return list.Where(row => locationIds.Contains(locationSelector(row) ?? "")).ToList();
    }

}
