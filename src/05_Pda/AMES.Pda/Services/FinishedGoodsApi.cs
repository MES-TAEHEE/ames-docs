using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AMES.Pda.Services;

public sealed class FinishedGoodsApi(HttpClient http, AuthState auth) : PdaApi(http, auth)
{
    public Task<List<AMES.Contracts.Dto.AssyReadyPalletDto>> AssyReadyPalletsAsync()
        =>GetRequiredAsync<List<AMES.Contracts.Dto.AssyReadyPalletDto>>("/api/fg/putaway/ready-pallets",
            "Assy Ready could not be loaded. Please retry.");
    public Task<AMES.Contracts.Dto.PalletReturnDto> ScanEmptyPalletReturnAsync(string barcode)
        => GetRequiredAsync<AMES.Contracts.Dto.PalletReturnDto>($"/api/fg/pallet-return/scan?barcode={Uri.EscapeDataString(barcode)}", "Pallet return service is unavailable.");

    public async Task<AMES.Contracts.Dto.PalletReturnDto> ReceiveEmptyPalletReturnAsync(AMES.Contracts.Dto.PalletReturnRequest body)
    {
        Authorize();
        using var response = await _http.PostAsJsonAsync("/api/fg/pallet-return", body);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadServiceErrorAsync(response, "Pallet return service is unavailable."));
        return await response.Content.ReadFromJsonAsync<AMES.Contracts.Dto.PalletReturnDto>()
            ?? throw new InvalidOperationException("Invalid pallet return response.");
    }

    public Task<List<AMES.Contracts.Dto.ShippingDeliveryListDto>> OpenShippingDeliveriesAsync()
        =>GetRequiredAsync<List<AMES.Contracts.Dto.ShippingDeliveryListDto>>("/api/fg/shipping/deliveries","DCR list service is unavailable.");
    public Task<AMES.Contracts.Dto.ShippingActionResult> ShippingDeliveryAsync(string barcode)
        =>GetRequiredAsync<AMES.Contracts.Dto.ShippingActionResult>($"/api/fg/shipping/delivery?barcode={Uri.EscapeDataString(barcode)}","Shipping service is unavailable.");
    public Task<AMES.Contracts.Dto.ShippingActionResult> LoadShippingDeliveryAsync(string delivery,string pallet,bool allowNonFifo=false)
        =>ShippingPost("load",new AMES.Contracts.Dto.DeliveryLoadRequest(delivery,pallet,allowNonFifo));
    public Task<AMES.Contracts.Dto.ShippingActionResult> UnloadShippingDeliveryAsync(string delivery,string pallet)
        =>ShippingPost("unload",new AMES.Contracts.Dto.DeliveryLoadRequest(delivery,pallet));
    public Task<AMES.Contracts.Dto.ShippingActionResult> CompleteShippingDeliveryAsync(string delivery,bool allowNonFifo=false)
        =>ShippingPost("complete",new AMES.Contracts.Dto.DeliveryCompleteRequest(delivery,allowNonFifo));
    private async Task<AMES.Contracts.Dto.ShippingActionResult> ShippingPost<T>(string path,T body)
    {
        Authorize();using var response=await _http.PostAsJsonAsync("/api/fg/shipping/"+path,body);
        if(!response.IsSuccessStatusCode)throw new InvalidOperationException(await ReadServiceErrorAsync(response,"Shipping service is unavailable."));
        return await response.Content.ReadFromJsonAsync<AMES.Contracts.Dto.ShippingActionResult>()??throw new InvalidOperationException("Invalid shipping response.");
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
    public async Task<List<FgPutAwayWaitingRow>> FgPutAwayWaitingAsync()
    {
        Authorize();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            using var response = await _http.GetAsync("/api/fg/putaway/waiting", timeout.Token);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new InvalidOperationException("Your session has expired. Go back and sign in again.");
            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw new InvalidOperationException("You do not have permission to view Put-Away Waiting.");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<FgPutAwayWaitingRow>>(cancellationToken: timeout.Token)
                ?? throw new InvalidOperationException("Put-Away Waiting returned an invalid response. Press REFRESH to retry.");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException("Put-Away Waiting could not be loaded. Check the API/DB connection and press REFRESH to retry.", ex);
        }
        catch (OperationCanceledException ex)
        {
            throw new InvalidOperationException("Put-Away Waiting timed out. Check the connection and press REFRESH to retry.", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Put-Away Waiting returned an invalid response. Press REFRESH to retry.", ex);
        }
    }
    public Task<List<FgReturnRow>> FgReturnsAsync() => Get<List<FgReturnRow>>("/api/fg/returns");
    public Task<FgReturnResult> FgReturnScanAsync(string barcode)
        => GetFgReturnResultAsync($"/api/fg/return/scan?barcode={Uri.EscapeDataString(barcode)}");
    public async Task<FgDashboard>  FgDashboardAsync()
        => await GetRequiredAsync<FgDashboard>("/api/fg/dashboard", "Finished goods dashboard service is unavailable.");

    public Task<FgPutAwayResult> FgPutAwayScanAsync(string barcode)
        => GetFgPutAwayResultAsync($"/api/fg/putaway/scan?barcode={Uri.EscapeDataString(barcode)}");
    public Task<FgPutAwayLocationRow?> FgValidatePutAwayLocationAsync(string locationId, string itemNo, string? customerCode, decimal qty, string? expectedScanType)
        => GetFgPutAwayLocationAsync($"/api/fg/putaway/location?locationId={Uri.EscapeDataString(locationId)}&itemNo={Uri.EscapeDataString(itemNo)}&customerCode={Uri.EscapeDataString(customerCode ?? "")}&qty={qty}&expectedScanType={Uri.EscapeDataString(expectedScanType ?? "")}");
    public Task<FgPutAwayResult> FgConfirmPutAwayAsync(FgPutAwayConfirmReq body)
        => PostFgPutAwayResultAsync("/api/fg/putaway/confirm", body);
    public Task<FgOutboundUnitResult> FgOutboundScanAsync(string barcode)
        => GetOutboundUnitResultAsync($"/api/fg/outbound/scan?barcode={Uri.EscapeDataString(barcode.Trim())}");
    public Task<List<FgOutboundCustomerRow>> FgOutboundCustomersAsync()
        => GetRequiredAsync<List<FgOutboundCustomerRow>>("/api/fg/outbound/customers", "Customer list could not be loaded. Check the API version and connection.");
    public Task<FgOutboundUnitResult> FgOutboundAsync(FgOutboundUnitReq body)
        => PostOutboundUnitResultAsync("/api/fg/outbound", body);
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

    private async Task<FgOutboundUnitResult> GetOutboundUnitResultAsync(string url)
    {
        Authorize();
        try
        {
            using var response = await _http.GetAsync(url);
            return await ReadOutboundUnitResultAsync(response);
        }
        catch (OperationCanceledException)
        {
            return new FgOutboundUnitResult(false, "Barcode lookup timed out. Check the connection and scan again.", null);
        }
        catch (HttpRequestException)
        {
            return new FgOutboundUnitResult(false, "Cannot reach the Outbound API. Check Wi-Fi, Tailscale and the API server.", null);
        }
    }

    private async Task<FgOutboundUnitResult> PostOutboundUnitResultAsync(string url, FgOutboundUnitReq body)
    {
        Authorize();
        try
        {
            using var response = await _http.PostAsJsonAsync(url, body);
            return await ReadOutboundUnitResultAsync(response);
        }
        catch (OperationCanceledException)
        {
            return new FgOutboundUnitResult(false, "Outbound response timed out. Check transaction history before retrying; the stock may already have been processed.", null);
        }
        catch (HttpRequestException)
        {
            return new FgOutboundUnitResult(false, "Connection lost during outbound. Check transaction history before retrying; the stock may already have been processed.", null);
        }
    }

    private static async Task<FgOutboundUnitResult> ReadOutboundUnitResultAsync(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return new FgOutboundUnitResult(false, await ReadServiceErrorAsync(response, "Outbound request was rejected."), null);
        try
        {
            var result = await response.Content.ReadFromJsonAsync<FgOutboundUnitResult>();
            if (result is not null && !string.IsNullOrWhiteSpace(result.Message)) return result;
        }
        catch
        {
            // Fall through to a readable status message.
        }

        return new FgOutboundUnitResult(false, response.IsSuccessStatusCode
            ? "Outbound API returned an invalid or empty response. Contact an administrator."
            : await ReadServiceErrorAsync(response, $"Outbound API returned HTTP {(int)response.StatusCode}. Check the server status."), null);
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

}
