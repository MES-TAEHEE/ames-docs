using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AMES.Pda.Services;

public sealed class FinishedGoodsApi(HttpClient http, AuthState auth) : PdaApi(http, auth)
{
    public async Task<AdjustmentLocation?> ScanAdjustmentLocationAsync(string barcode)
    {
        Authorize();
        using var response = await _http.GetAsync(
            $"/api/fg/adjust/location?barcode={Uri.EscapeDataString(barcode.Trim())}");
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadServiceErrorAsync(response, "Finished goods adjustment location service is unavailable."));
        if (response.Content.Headers.ContentLength == 0) return null;
        return await response.Content.ReadFromJsonAsync<AdjustmentLocation>();
    }

    public async Task<List<WarehouseTransactionRow>> TransactionsAsync(string? search = null,
        DateTime? dateFrom = null, DateTime? dateTo = null)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(search)) query.Add($"search={Uri.EscapeDataString(search.Trim())}");
        if (dateFrom.HasValue) query.Add($"dateFrom={dateFrom.Value:yyyy-MM-dd}");
        if (dateTo.HasValue) query.Add($"dateTo={dateTo.Value:yyyy-MM-dd}");
        var url = "/api/fg/transactions" + (query.Count == 0 ? "" : "?" + string.Join("&", query));
        return await GetRequiredAsync<List<WarehouseTransactionRow>>(url, "Finished goods transaction service is unavailable.");
    }

    public async Task FgResetPptTestAsync(string screen)
    {
        Authorize();
        using var response = await _http.PostAsync($"/api/fg/test/ppt-reset/{Uri.EscapeDataString(screen)}", null);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadServiceErrorAsync(response, "FG PPT test reset failed."));
    }

    public async Task<InboundScanRow?> FgScanAdjustAsync(string scanText)
    {
        try
        {
            Authorize();
            var url = $"/api/fg/adjust/scan?scanText={Uri.EscapeDataString(scanText.Trim())}";
            using var resp = await _http.GetAsync(url);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException(await ReadServiceErrorAsync(resp, "Finished goods adjust scan service is unavailable."));
            return await resp.Content.ReadFromJsonAsync<InboundScanRow>();
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            throw new InvalidOperationException("Finished goods adjust scan service is unavailable.");
        }
    }

    public async Task<InboundReceiveResult> FgSaveAdjustQtyAsync(AdjustSaveReq body)
    {
        try
        {
            Authorize();
            using var resp = await _http.PostAsJsonAsync("/api/fg/adjust/save", body);
            return await ReadInboundReceiveResultAsync(resp);
        }
        catch
        {
            return new InboundReceiveResult(false, "Finished goods adjustment service is unavailable.", null);
        }
    }

    public async Task<List<FgStockRow>> FgInventoryAsync(string? q = null)
    {
        Authorize();
        var url = "/api/fg/inventory" + (string.IsNullOrWhiteSpace(q) ? "" : $"?q={Uri.EscapeDataString(q.Trim())}");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            using var response = await _http.GetAsync(url, timeout.Token);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new InvalidOperationException("Your session has expired. Go back and sign in again.");
            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw new InvalidOperationException("You do not have permission to view finished goods inventory.");
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(await ReadServiceErrorAsync(response,
                    $"Inventory service failed. HTTP {(int)response.StatusCode}."));
            return await response.Content.ReadFromJsonAsync<List<FgStockRow>>(cancellationToken: timeout.Token)
                ?? throw new InvalidOperationException("Inventory service returned an invalid response. Press REFRESH to retry.");
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new InvalidOperationException("Inventory request timed out. Check the connection and press REFRESH to retry.", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Inventory could not be loaded. Check the API/DB connection and press REFRESH to retry.", ex);
        }
    }
    public async Task<List<FgQcCompletedRow>> FgQcCompletedAsync()
    {
        Authorize();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            using var response = await _http.GetAsync("/api/fg/qc-completed", timeout.Token);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new InvalidOperationException("Your session has expired. Go back and sign in again.");
            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw new InvalidOperationException("You do not have permission to view QC Waiting.");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<FgQcCompletedRow>>(cancellationToken: timeout.Token)
                ?? throw new InvalidOperationException("QC Waiting returned an invalid response. Press REFRESH to retry.");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException("QC Waiting could not be loaded. Check the API/DB connection and press REFRESH to retry.", ex);
        }
        catch (OperationCanceledException ex)
        {
            throw new InvalidOperationException("QC Waiting timed out. Check the connection and press REFRESH to retry.", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("QC Waiting returned an invalid response. Press REFRESH to retry.", ex);
        }
    }
    public Task<List<FgOrderRow>>   FgOrdersAsync()  => Get<List<FgOrderRow>>("/api/fg/orders");
    public Task<List<FgOrderLineRow>> FgOrderLinesAsync(string shipOrderNumber)
        => Get<List<FgOrderLineRow>>($"/api/fg/orders/{Uri.EscapeDataString(shipOrderNumber)}/lines");
    public async Task<FgOutgoingSlipRow?> FgOutgoingSlipAsync(string barcode)
        => (await GetPickingAsync<List<FgOutgoingSlipRow>>(
            $"/api/fg/release/outgoing-slips/{Uri.EscapeDataString(barcode)}")).FirstOrDefault();
    public Task<List<FgOutgoingSlipLineRow>> FgOutgoingSlipLinesAsync(string barcode)
        => GetPickingAsync<List<FgOutgoingSlipLineRow>>(
            $"/api/fg/release/outgoing-slips/{Uri.EscapeDataString(barcode)}/lines");
    public Task<List<FgHistoryRow>> FgHistoryAsync() => Get<List<FgHistoryRow>>("/api/fg/history");
    public Task<List<FgReturnRow>> FgReturnsAsync() => Get<List<FgReturnRow>>("/api/fg/returns");
    public Task<FgReturnResult> FgReturnScanAsync(string barcode)
        => GetFgReturnResultAsync($"/api/fg/return/scan?barcode={Uri.EscapeDataString(barcode)}");
    public async Task<FgDashboard>  FgDashboardAsync()
        => await GetRequiredAsync<FgDashboard>("/api/fg/dashboard", "Finished goods dashboard service is unavailable.");

    public Task<FgPutAwayResult> FgPutAwayScanAsync(string barcode)
        => GetFgPutAwayResultAsync($"/api/fg/putaway/scan?barcode={Uri.EscapeDataString(barcode)}");
    public Task<FgPutAwayResult> FgValidatePutAwayContainerAsync(string storageMethod, string barcode)
        => GetFgPutAwayResultAsync($"/api/fg/putaway/container?storageMethod={Uri.EscapeDataString(storageMethod)}&barcode={Uri.EscapeDataString(barcode)}");
    public Task<FgPutAwayLocationRow?> FgValidatePutAwayLocationAsync(string locationId, string itemNo, string? customerCode, decimal qty, string? expectedScanType)
        => GetFgPutAwayLocationAsync($"/api/fg/putaway/location?locationId={Uri.EscapeDataString(locationId)}&itemNo={Uri.EscapeDataString(itemNo)}&customerCode={Uri.EscapeDataString(customerCode ?? "")}&qty={qty}&expectedScanType={Uri.EscapeDataString(expectedScanType ?? "")}");
    public Task<FgPutAwayResult> FgConfirmPutAwayAsync(FgPutAwayConfirmReq body)
        => PostFgPutAwayResultAsync("/api/fg/putaway/confirm", body);
    public async Task<FgReleaseLotScanResult> FgReleaseLotScanAsync(FgReleaseLotScanReq body)
    {
        Authorize();
        try
        {
            var response = await _http.PostAsJsonAsync("/api/fg/release/lot/scan", body);
            return await response.Content.ReadFromJsonAsync<FgReleaseLotScanResult>()
                ?? new(false, "SERVICE_ERROR", "Picking service returned an empty response.", null, null);
        }
        catch (Exception ex)
        {
            return new(false, "SERVICE_ERROR", $"Picking service is unavailable. {ex.Message}", null, null);
        }
    }

    public async Task<FgCompleteReleaseResult> FgCompleteReleaseAsync(FgCompleteReleaseReq body)
    {
        Authorize();
        try
        {
            var response = await _http.PostAsJsonAsync("/api/fg/release/complete", body);
            return await response.Content.ReadFromJsonAsync<FgCompleteReleaseResult>()
                ?? new(false, "Picking service returned an empty response.", null);
        }
        catch (Exception ex)
        {
            return new(false, $"Picking service is unavailable. {ex.Message}", null);
        }
    }
    public Task<FgLoadingOrderResult> FgLoadingOrderScanAsync(string barcode)
        => GetFgLoadingOrderResultAsync($"/api/fg/loading/order/scan?barcode={Uri.EscapeDataString(barcode)}");
    public Task<FgLoadingResult> FgLoadingTruckScanAsync(string barcode)
        => GetFgLoadingResultAsync($"/api/fg/loading/truck/scan?barcode={Uri.EscapeDataString(barcode)}");
    public Task<FgLoadingResult> FgLoadingItemScanAsync(string barcode, int? shipmentOrderId)
        => GetFgLoadingResultAsync($"/api/fg/loading/item/scan?barcode={Uri.EscapeDataString(barcode)}{(shipmentOrderId is > 0 ? $"&shipmentOrderId={shipmentOrderId}" : "")}");
    public Task<FgLoadingResult> FgLoadingAsync(FgLoadingReq body)
        => PostFgLoadingResultAsync("/api/fg/loading", body);
    public Task<HttpResponseMessage> FgDeliveryAsync(FgDeliveryReq body) => Post("/api/fg/delivery", body);
    public Task<HttpResponseMessage> FgDayEndAsync  (FgDayEndReq  body)  => Post("/api/fg/dayend",   body);
    public Task<FgReturnResult> FgReturnAsync(FgReturnReq body)
        => PostFgReturnResultAsync("/api/fg/return", body);

    // ── private HTTP helpers ────────────────────────────────────────────
    private async Task<FgReturnResult> GetFgReturnResultAsync(string url)
    {
        Authorize();
        try
        {
            var resp = await _http.GetAsync(url);
            return await ReadFgReturnResultAsync(resp);
        }
        catch (Exception ex)
        {
            return new FgReturnResult(false, ex.Message, null, null);
        }
    }

    private async Task<FgLoadingResult> GetFgLoadingResultAsync(string url)
    {
        Authorize();
        try
        {
            var resp = await _http.GetAsync(url);
            return await ReadFgLoadingResultAsync(resp);
        }
        catch (HttpRequestException)
        {
            return new FgLoadingResult(false, "Truck loading service is unavailable. Check the API and database connection.", null, null, null);
        }
        catch (Exception)
        {
            return new FgLoadingResult(false, "Truck loading service returned an invalid response.", null, null, null);
        }
    }

    private async Task<FgLoadingOrderResult> GetFgLoadingOrderResultAsync(string url)
    {
        Authorize();
        try
        {
            var resp = await _http.GetAsync(url);
            try
            {
                var result = await resp.Content.ReadFromJsonAsync<FgLoadingOrderResult>();
                if (result is not null) return result;
            }
            catch
            {
                // Fall through to a readable HTTP message.
            }

            if (resp.StatusCode == HttpStatusCode.Unauthorized)
                return new FgLoadingOrderResult(false, "Session expired. Sign in again.", null);
            return new FgLoadingOrderResult(resp.IsSuccessStatusCode,
                resp.IsSuccessStatusCode ? "Shipment order loaded." : $"Shipment order service failed. HTTP {(int)resp.StatusCode}.",
                null);
        }
        catch (HttpRequestException)
        {
            return new FgLoadingOrderResult(false, "Truck loading service is unavailable. Check the API and database connection.", null);
        }
        catch (Exception)
        {
            return new FgLoadingOrderResult(false, "Truck loading service returned an invalid response.", null);
        }
    }

    private async Task<FgLoadingResult> PostFgLoadingResultAsync(string url, FgLoadingReq body)
    {
        Authorize();
        try
        {
            var resp = await _http.PostAsJsonAsync(url, body);
            return await ReadFgLoadingResultAsync(resp);
        }
        catch (HttpRequestException)
        {
            return new FgLoadingResult(false, "Truck loading service is unavailable. Check the API and database connection.", null, null, null);
        }
        catch (Exception)
        {
            return new FgLoadingResult(false, "Truck loading service returned an invalid response.", null, null, null);
        }
    }

    private static async Task<FgLoadingResult> ReadFgLoadingResultAsync(HttpResponseMessage resp)
    {
        try
        {
            var result = await resp.Content.ReadFromJsonAsync<FgLoadingResult>();
            if (result is not null) return result;
        }
        catch
        {
            // Fall through to a readable HTTP message.
        }

        if (resp.StatusCode == HttpStatusCode.Unauthorized)
            return new FgLoadingResult(false, "Session expired. Sign in again.", null, null, null);

        return new FgLoadingResult(resp.IsSuccessStatusCode,
            resp.IsSuccessStatusCode ? "Truck loading confirmed." : $"Truck loading service failed. HTTP {(int)resp.StatusCode}.",
            null, null, null);
    }

    private async Task<FgReturnResult> PostFgReturnResultAsync(string url, FgReturnReq body)
    {
        Authorize();
        try
        {
            var resp = await _http.PostAsJsonAsync(url, body);
            return await ReadFgReturnResultAsync(resp);
        }
        catch (Exception ex)
        {
            return new FgReturnResult(false, ex.Message, null, null);
        }
    }

    private static async Task<FgReturnResult> ReadFgReturnResultAsync(HttpResponseMessage resp)
    {
        try
        {
            var result = await resp.Content.ReadFromJsonAsync<FgReturnResult>();
            if (result is not null) return result;
        }
        catch
        {
            // Fall through to a readable HTTP message.
        }

        if (resp.StatusCode == HttpStatusCode.Unauthorized)
            return new FgReturnResult(false, "Session expired. Sign in again.", null, null);

        return new FgReturnResult(resp.IsSuccessStatusCode,
            resp.IsSuccessStatusCode ? "Customer return received." : $"Customer return service failed. HTTP {(int)resp.StatusCode}.",
            null, null);
    }

    private async Task<FgPutAwayResult> GetFgPutAwayResultAsync(string url)
    {
        Authorize();
        try
        {
            var resp = await _http.GetAsync(url);
            var result = await ReadFgPutAwayResultAsync(resp);
            return result ?? new FgPutAwayResult(false, "FG Put-Away service returned an empty response.", null, null, null);
        }
        catch (Exception ex)
        {
            return new FgPutAwayResult(false, ex.Message, null, null, null);
        }
    }

    private async Task<FgPutAwayLocationRow?> GetFgPutAwayLocationAsync(string url)
    {
        Authorize();
        try
        {
            var resp = await _http.GetAsync(url);
            if (resp.IsSuccessStatusCode)
                return await resp.Content.ReadFromJsonAsync<FgPutAwayLocationRow>();

            var result = await ReadFgPutAwayResultAsync(resp);
            return result?.Location is not null
                ? result.Location
                : new FgPutAwayLocationRow("", null, null, null, null, null, 0, 0, 0, null, false,
                    result?.Message ?? "FG location service is unavailable.", "LOCATION", "");
        }
        catch (Exception ex)
        {
            return new FgPutAwayLocationRow("", null, null, null, null, null, 0, 0, 0, null, false, ex.Message, "LOCATION", "");
        }
    }

    private async Task<FgPutAwayResult> PostFgPutAwayResultAsync(string url, FgPutAwayConfirmReq body)
    {
        Authorize();
        try
        {
            var resp = await _http.PostAsJsonAsync(url, body);
            var result = await ReadFgPutAwayResultAsync(resp);
            return result ?? new FgPutAwayResult(false, "FG Put-Away service returned an empty response.", null, null, null);
        }
        catch (Exception ex)
        {
            return new FgPutAwayResult(false, ex.Message, null, null, null);
        }
    }

    private static async Task<FgPutAwayResult?> ReadFgPutAwayResultAsync(HttpResponseMessage resp)
    {
        try
        {
            var result = await resp.Content.ReadFromJsonAsync<FgPutAwayResult>();
            if (result is not null)
                return result;
        }
        catch
        {
            // Fall through to a readable HTTP message.
        }

        if (resp.StatusCode == HttpStatusCode.Unauthorized)
            return new FgPutAwayResult(false, "Session expired. Sign in again.", null, null, null);

        var message = resp.IsSuccessStatusCode
            ? "FG Put-Away completed."
            : $"FG Put-Away service failed. HTTP {(int)resp.StatusCode}.";
        return new FgPutAwayResult(resp.IsSuccessStatusCode, message, null, null, null);
    }

    private async Task<T> GetPickingAsync<T>(string url)
    {
        Authorize();
        try
        {
            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(await ReadServiceErrorAsync(response,
                    $"Picking service failed. HTTP {(int)response.StatusCode}."));
            return await response.Content.ReadFromJsonAsync<T>()
                ?? throw new InvalidOperationException("Picking service returned an empty response.");
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Picking service is unavailable. Check the API/DB connection.", ex);
        }
    }

}
