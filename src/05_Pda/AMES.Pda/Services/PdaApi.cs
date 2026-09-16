using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AMES.Contracts.Dto;

namespace AMES.Pda.Services;

/// <summary>
/// Thin HTTP client wrapper for the AMES.Api endpoints. One instance per
/// process — registered as singleton in MauiProgram. Stamps every request
/// with the current bearer token from AuthState.
/// </summary>
public sealed class PdaApi
{
    public const string SparePartsAreaCode = "SPARE_PARTS_AREA";
    private readonly HttpClient _http;
    private readonly AuthState  _auth;

    public PdaApi(HttpClient http, AuthState auth)
    {
        _http = http;
        _auth = auth;
    }

    // ── Auth ─────────────────────────────────────────────────────────────
    public sealed record LoginReq(string EmployeeNo, string Pin, string TerminalId, string LineId, string ShiftCode);
    public sealed record LoginRes(string Token, int Result, string? Reason,
                                   string? EmployeeNo, string? EmployeeName,
                                   string? LineId, string? ShiftCode, DateTime? ExpiresAt);

    /// <summary>
    /// Login + session fetch in one call. The API hands back an opaque
    /// bearer token; we stamp it locally and immediately call /me so the
    /// caller gets a fully-populated PopSessionDto in one await. Avoids
    /// the timing bug where AuthState.Token wasn't set yet when MeAsync
    /// rebuilt the Authorization header.
    /// Returns null on any auth failure or unreachable API.
    /// </summary>
    public async Task<(string Token, PopSessionDto Session, string? Reason)?> LoginAsync(
        string employeeNo, string pin,
        string terminalId = "PDA-DEV-01",
        string lineId = "LINE-INJ-01",
        string shiftCode = "A")
    {
        HttpResponseMessage resp;
        try
        {
            resp = await _http.PostAsJsonAsync("/api/auth/login",
                new LoginReq(employeeNo, pin, terminalId, lineId, shiftCode));
        }
        catch (Exception)
        {
            return (Token: "", Session: null!, Reason: "Authentication service is unavailable.");
        }

        if (!resp.IsSuccessStatusCode)
            return (Token: "", Session: null!, Reason: await LoginHttpErrorAsync(resp));

        LoginRes? login;
        try
        {
            login = await resp.Content.ReadFromJsonAsync<LoginRes>();
        }
        catch
        {
            return (Token: "", Session: null!, Reason: "Authentication service returned an invalid response.");
        }

        if (login is null || string.IsNullOrEmpty(login.Token))
            return (Token: "", Session: null!, Reason: NormalizeLoginReason(login?.Reason));

        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.Token);

        var meResp = await _http.GetAsync("/api/auth/me");
        if (!meResp.IsSuccessStatusCode)
            return (Token: "", Session: null!, Reason: "Session could not be loaded after sign-in.");

        var session = await meResp.Content.ReadFromJsonAsync<PopSessionDto>();
        return session is null
            ? (Token: "", Session: null!, Reason: "Session response was empty.")
            : (login.Token, session, null);
    }

    private static async Task<string> LoginHttpErrorAsync(HttpResponseMessage resp)
    {
        if ((int)resp.StatusCode >= 500)
            return "Authentication service failed. Check API database connection.";

        var body = "";
        try { body = await resp.Content.ReadAsStringAsync(); }
        catch { /* ignore body read failures */ }

        return string.IsNullOrWhiteSpace(body)
            ? $"Authentication request failed ({(int)resp.StatusCode})."
            : NormalizeLoginReason(body);
    }

    private static string NormalizeLoginReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return "Check Employee No and PIN.";

        var text = reason.Trim();
        if (text.Contains("SqlException", StringComparison.OrdinalIgnoreCase)
            || text.Contains("network-related", StringComparison.OrdinalIgnoreCase)
            || text.Contains("SQL Server", StringComparison.OrdinalIgnoreCase))
            return "Authentication database is unavailable.";

        if (text.Equals("bad pin", StringComparison.OrdinalIgnoreCase)
            || text.Equals("unknown employee", StringComparison.OrdinalIgnoreCase))
            return "Check Employee No and PIN.";

        if (text.Length > 160)
            text = text[..160] + "...";

        return text;
    }

    public async Task LogoutAsync()
    {
        Authorize();
        await _http.PostAsync("/api/auth/logout", null);
    }

    public async Task<PopSessionDto?> MeAsync()
    {
        Authorize();
        var resp = await _http.GetAsync("/api/auth/me");
        return resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<PopSessionDto>() : null;
    }

    public sealed record CodeOption(string CodeValue, string? CodeName, string? CodeNameEn, string? Attribute1)
    {
        public string Name => string.IsNullOrWhiteSpace(CodeNameEn) ? CodeName ?? CodeValue : CodeNameEn;
    }

    public async Task<List<CodeOption>> CodeItemsAsync(string groupCode)
    {
        Authorize();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            using var response = await _http.GetAsync(
                $"/api/sys/code-items/{Uri.EscapeDataString(groupCode)}", timeout.Token);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new InvalidOperationException("Your session has expired. Go back and sign in again.");
            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw new InvalidOperationException("You do not have permission to load this code list.");
            response.EnsureSuccessStatusCode();
            var items = await response.Content.ReadFromJsonAsync<List<CodeOption>>(cancellationToken: timeout.Token);
            if (items is not { Count: > 0 })
                throw new InvalidOperationException($"No active codes are configured for {groupCode}. Contact an administrator.");
            return items;
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException("Code service is unavailable. Check the API connection and reopen this screen.", ex);
        }
        catch (OperationCanceledException ex)
        {
            throw new InvalidOperationException("Code service timed out. Check the API connection and reopen this screen.", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Code service returned an invalid response. Contact an administrator.", ex);
        }
    }

    // ── WH ───────────────────────────────────────────────────────────────
    public sealed record InboundRow(int LotId, string LotCode, string? ItemNo, string? ItemName,
        decimal Qty, string? Vendor, DateTime? ArrivedAt);
    public sealed record InboundScanRow(string ReceiveType, string? Yn, string LotNo, string Barcode,
        string? SourceTable, string? NoteNo, string? CaseBarcode, string? CaseNo, string? InvoiceNo,
        string? ContainerNo, string? PartNo, string? PartName, decimal Qty, string? Unit, string? PoNo,
        int? PoSeq, string? VendorId, string? VendorName, DateTime? ProductionDate, DateTime? DeliveryDate,
        DateTime? ArrivalDate, DateTime? ShipDate, DateTime? PackDate, string? ReceivedLocation,
        string? ReceivedStatus);
    public sealed record InboundDocumentRow(string ReceiveType, int InboundDocumentId,
        string DocumentBarcode, string? DocumentNo, string? VendorId, string? VendorName,
        string? CaseNo, string? InvoiceNo, string? ContainerNo, DateTime? ShipDate,
        DateTime? PackDate, DateTime? DeliveryDate, DateTime? ArrivalDate,
        int TotalBoxes, int ScannedBoxes, string? Yn);
    public sealed record InboundDocumentLineRow(string PartNo, string? PartName,
        int BoxCount, int ScanCount, string? Yn);
    public sealed record InboundDocumentBoxRow(string PartNo, string BoxBarcode,
        string? LotNo, decimal Qty, string? Unit, string? Yn);
    public sealed record InboundDocumentResult(InboundDocumentRow? Document,
        List<InboundDocumentLineRow> Lines, List<InboundDocumentBoxRow> Boxes);
    public sealed record WarehouseTransactionRow(long RowNo, string? LotNo, string? PartNo,
        string? WorkDate, string? WorkTime, string? LocationId, decimal Qty, string Status,
        string Direction, string? WorkerId, string? ReasonCode, string? ReasonNote,
        string? Supervisor, decimal? BeforeQty, decimal? DeltaQty, decimal? AfterQty,
        string? BeforeStatus, string? AfterStatus, string? BeforeLocation, string? AfterLocation,
        string? Source, string? Note, string? Unit = null);
    public sealed record InventoryRow(int InventoryId, string ItemNo, string? ItemName, string LocationId,
        int? LotId, decimal OnHandQty, decimal ReservedQty, DateTime? ExpiryDate,
        string? CarCode = null, string? Unit = null, decimal? MinDays = null, decimal? MinQty = null,
        decimal? MaxDays = null, decimal? MaxQty = null, int LotCount = 0, int LocationCount = 0,
        string? Status = null, string? StatusName = null, DateTime? LastReceivedDate = null,
        string? LotNo = null);
    public sealed record LotStatusRow(int LotId, string LotNo, string? ItemNo, string? ItemName,
        string InventoryStatus, decimal RemainingQty, string? LocationId, DateTime? ProductionDate,
        DateTime? LastChangedAt);
    public sealed record InventoryLocationRow(int RowNo, string ItemNo, string LocationId, string? LocationName,
        string? WarehouseCode, string? WarehouseName, string? AreaCode, string? AreaName,
        string? ZoneCode, string? ZoneName, string? RackX, string? RackY, string? RackZ, decimal Qty);
    public sealed record InventoryScanLookupRow(string SearchKind, string SearchText, string? DisplayText);
    public sealed record LocationRow(string LocationId, string? LocationName, string? Zone, int LineCount, decimal TotalQty,
        string? WarehouseCode = null, string? WarehouseName = null, string? AreaCode = null, string? AreaName = null,
        string? ZoneName = null, string? X = null, string? Y = null, string? Z = null,
        string? PlantCode = null, string? LocationType = null, decimal? Capacity = null, string? Unit = null);
    public sealed record LocationMapItemRow(string LotNo, string? PartNo, string? PartName, decimal Qty, string? Unit,
        string? InventoryStatus, string? WorkDate, string? WorkTime);
    public sealed record InventoryTestChangeResult(bool Success, string Message, string LotNo, decimal Qty);
    public sealed record ReleaseSlipStatusRow(string PickSlipNo, bool Exists, bool IsClosed, int LineCount,
        string? RequestLocation, DateTime? RequestDate, DateTime? CloseDate, string Message);
    public sealed record ReleasePickLineRow(string PickSlipNo, string ItemNo, string? ItemName,
        decimal RequestBoxQty, decimal PickedBoxQty, decimal PickedQty, string? RequestUserId,
        string? SuggestedLocation1, string? SuggestedLocation2, string? SuggestedLocation3, string Status);
    public sealed record ReleaseLotRow(string PickSlipNo, string LotNo, string? ItemNo, string? ItemName,
        decimal Qty, string? Unit, string? LocationNo, string? LocationName, string? ZoneCode,
        string? InvStatus, string? ProdDate, string? RcvDate, bool IsFifoSuggested, bool IsValid,
        string? Message);
    public sealed record ReleaseFifoLotRow(string PickSlipNo, string ItemNo, string LotNo, string? LocationNo,
        decimal Qty, string? ProductionDate);
    public sealed record ReleasePickInput(string LotNo, decimal Qty);
    public sealed record ReleaseCompleteReq(string PickSlipNo, List<ReleasePickInput>? Lots = null,
        string? OutgoingType = null, bool SimulateFailure = false);
    public sealed record ReleaseCompleteResult(bool Success, string Message);
    public sealed record DirectOutgoingLotRow(int LotId, string LotNo, string? ItemNo, string? ItemName,
        decimal Qty, string? Unit, string? LocationId, string InventoryStatus, bool IsValid, string Message);
    public sealed record DirectOutgoingReq(string OutgoingType, string LotNo, decimal Qty,
        string? TargetCode = null, string? Note = null);
    public sealed record DirectOutgoingResult(bool Success, string Message, DirectOutgoingLotRow? Row = null);
    public sealed record OutgoingVendorRow(string VendorId, string? VendorName)
    {
        public string DisplayName => string.IsNullOrWhiteSpace(VendorName)
            ? VendorId
            : $"{VendorId} / {VendorName}";
    }
    public sealed record SparePartRow(string EosSpNo, string? Category,
        string? ApplicableEquipment, string? PartName, string? PartNo, string? Maker,
        string? Vendor, decimal Qty, string? Unit, string? StorageLocation, string? AreaCode,
        string InventoryStatus, bool IsReleaseEligible, string? ImageDataUrl);
    public sealed record SparePartMoveReq(string EosSpNo, int Qty = 1,
        string? LocationId = null, string? Note = null);
    public sealed record SparePartMoveResult(bool Success, string Message, SparePartRow? Row = null);
    public sealed record TransactionRow(long TxnId, DateTime TxnTime, string TxnType, string? ItemNo,
        string? LocationId, decimal QtyBefore, decimal Delta, decimal QtyAfter, string? ReasonCode);

    public sealed record ReceiveReq(string LotCode, decimal Qty, string LocationId);
    public sealed record InboundReceiveReq(string Mode, string Barcode, string LocationId, bool SimulateFailure = false);
    public sealed record InboundCancelReq(string Mode, string Barcode);
    public sealed record AdjustSaveReq(string? Mode, string Barcode, decimal DeltaQty, string ReasonCode,
        string? ReasonNote, bool SimulateFailure = false);
    public sealed record AdjustTestResetResult(bool Success, string Message, string LotNo, decimal Qty);
    public sealed record InboundReceiveResult(bool Success, string Message, InboundScanRow? Row);
    public sealed record PickReq(string PickSlipNo, string LotNo, decimal Qty);
    public sealed record PickResult(bool Success, string Message, ReleaseLotRow? Row);

    public Task<List<InboundRow>>         WhInboundTodayAsync()    => Get<List<InboundRow>>("/api/wh/inbound/today");
    public async Task<List<InventoryRow>> WhInventoryAsync(string? q = null, DateTime? dateFrom = null, DateTime? dateTo = null,
        bool simulateFailure = false, string? areaCode = null)
    {
        if (string.Equals(areaCode, SparePartsAreaCode, StringComparison.OrdinalIgnoreCase))
            return await GetRequiredAsync<List<InventoryRow>>(
                "/api/wh/sp/inventory" + (string.IsNullOrWhiteSpace(q) ? "" : $"?q={Uri.EscapeDataString(q.Trim())}"),
                "Spare parts inventory service is unavailable.");

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
        {
            var sparePart = await SpItemAsync(value);
            return sparePart is null
                ? new InventoryScanLookupRow("TEXT", value, null)
                : new InventoryScanLookupRow("PART", sparePart.EosSpNo, sparePart.PartName);
        }
        return await GetRequiredAsync<InventoryScanLookupRow>(
            $"/api/wh/inventory/scan?scanText={Uri.EscapeDataString(value)}",
            "Inventory scan service is unavailable.");
    }

    public async Task<List<InventoryLocationRow>> WhInventoryLocationsAsync(string itemNo, DateTime? dateFrom = null, DateTime? dateTo = null,
        string? areaCode = null)
    {
        if (string.Equals(areaCode, SparePartsAreaCode, StringComparison.OrdinalIgnoreCase))
            return await GetRequiredAsync<List<InventoryLocationRow>>(
                $"/api/wh/sp/inventory/locations?eosSpNo={Uri.EscapeDataString(itemNo.Trim())}",
                "Spare parts inventory location service is unavailable.");

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
            return await GetRequiredAsync<List<LocationRow>>("/api/wh/sp/locations",
                "Spare parts location service is unavailable.");

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
            return await GetRequiredAsync<List<LocationMapItemRow>>(
                $"/api/wh/sp/inventory/location/{Uri.EscapeDataString(locationId.Trim())}/items",
                "Spare parts location inventory service is unavailable.");

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
            return await GetRequiredAsync<List<LotStatusRow>>(
                "/api/wh/sp/inventory/lots" + (string.IsNullOrWhiteSpace(q) ? "" : $"?q={Uri.EscapeDataString(q.Trim())}"),
                "Spare parts inventory service is unavailable.");

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


    public sealed record AdjustmentStock(string Barcode, string LotNo, string PartNo, string? PartName, decimal Qty, string? Unit);
    public sealed record AdjustmentLocation(string LocationId, List<AdjustmentStock> Items);

    public async Task<AdjustmentLocation?> ScanAdjustmentLocationAsync(string barcode, bool finishedGoods, string? areaCode = null)
    {
        Authorize();
        var path = string.Equals(areaCode, SparePartsAreaCode, StringComparison.OrdinalIgnoreCase)
            ? "/api/wh/sp/adjust/location"
            : $"/api/{(finishedGoods ? "fg" : "wh")}/adjust/location";
        using var response = await _http.GetAsync($"{path}?barcode={Uri.EscapeDataString(barcode.Trim())}");
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadServiceErrorAsync(response, "Adjustment location service is unavailable."));
        // An unknown location returns an empty 200 response; the caller then tries a stock barcode.
        if (response.Content.Headers.ContentLength == 0) return null;
        var json = await response.Content.ReadAsStringAsync();
        var row = string.IsNullOrWhiteSpace(json) ? null
            : System.Text.Json.JsonSerializer.Deserialize<AdjustmentLocation>(json, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        if (row is not null && !finishedGoods && !string.IsNullOrWhiteSpace(areaCode))
            await WhScanLocationAsync(row.LocationId, areaCode);
        return row;
    }

    public async Task<InboundScanRow?> WhScanAdjustAsync(string scanText, string? areaCode = null)
    {
        try
        {
            Authorize();
            var url = string.Equals(areaCode, SparePartsAreaCode, StringComparison.OrdinalIgnoreCase)
                ? $"/api/wh/sp/adjust/scan?scanText={Uri.EscapeDataString(scanText.Trim())}"
                : $"/api/wh/adjust/scan?scanText={Uri.EscapeDataString(scanText.Trim())}";
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
        Authorize();
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(search))
            query.Add($"search={Uri.EscapeDataString(search.Trim())}");
        if (dateFrom.HasValue)
            query.Add($"dateFrom={Uri.EscapeDataString(dateFrom.Value.ToString("yyyy-MM-dd"))}");
        if (dateTo.HasValue)
            query.Add($"dateTo={Uri.EscapeDataString(dateTo.Value.ToString("yyyy-MM-dd"))}");

        var url = finishedGoods ? "/api/fg/transactions"
            : string.Equals(areaCode, SparePartsAreaCode, StringComparison.OrdinalIgnoreCase)
                ? "/api/wh/sp/transactions"
                : "/api/wh/warehouse-transactions";
        if (query.Count > 0)
            url += "?" + string.Join("&", query);

        var rows = await _http.GetFromJsonAsync<List<WarehouseTransactionRow>>(url)
            ?? new List<WarehouseTransactionRow>();
        return finishedGoods || string.Equals(areaCode, SparePartsAreaCode, StringComparison.OrdinalIgnoreCase)
            ? rows
            : await FilterByAreaAsync(rows, row => row.LocationId, areaCode);
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

    public async Task<InboundReceiveResult> SpMoveLocationAsync(InboundReceiveReq body)
    {
        Authorize();
        using var response = await _http.PostAsJsonAsync("/api/wh/sp/move-location", body);
        return await ReadInboundReceiveResultAsync(response);
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

    public async Task<InboundReceiveResult> SpSaveAdjustQtyAsync(AdjustSaveReq body)
    {
        Authorize();
        using var response = await _http.PostAsJsonAsync("/api/wh/sp/adjust/save", body);
        return await ReadInboundReceiveResultAsync(response);
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

    public async Task FgResetPptTestAsync(string screen)
    {
        Authorize();
        using var response = await _http.PostAsync($"/api/fg/test/ppt-reset/{Uri.EscapeDataString(screen)}", null);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadServiceErrorAsync(response, "FG PPT test reset failed."));
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

    public Task<HttpResponseMessage> WhPickAsync   (PickReq    body) => Post("/api/wh/release/pick",      body);

    // ── FG ───────────────────────────────────────────────────────────────
    public sealed record FgStockRow(int StockId, string? StockNumber, string ItemNo, string? ItemName,
        int? LotId, string? LotNo, string? CustomerCode, decimal Qty, string? Unit,
        string? Location, string? Status, DateTime? StockTs);
    public sealed record FgOrderRow(int ShipmentOrderId, string? ShipOrderNumber, string? CustomerCode,
        string? CustomerPo, DateTime? ShipDate, string? CarrierCode, string? DestPlant, string? Status, int LineCount);
    public sealed record FgOrderLineRow(int ShipmentOrderLineId, int ShipmentOrderId, int LineSeq,
        string ItemNo, string? ItemName, decimal OrderedQty, decimal AllocatedQty,
        int? StockId, string? LotNo, string? Location, string? ReservationStatus);
    public sealed record FgOutgoingSlipRow(int OutgoingSlipId, string OutgoingSlipBarcode,
        string? CustomerCode, DateTime? OutgoingDate, string? Destination, string? Status, int LineCount);
    public sealed record FgOutgoingSlipLineRow(int OutgoingSlipLineId, int OutgoingSlipId, int LineSeq,
        string PartNo, string? PartName, decimal RequiredQty, decimal ScannedQty, string? Status);
    public sealed record FgHistoryRow(int LoadingId, string? LoadingNumber, int? ShipmentOrderId,
        string? ShipOrderNumber, string? CustomerCode, string? LicensePlate, string? DriverName,
        DateTime? DepartureTs, string? OTDStatus);
    public sealed record FgDashboard(int OpenOrders, int ReadyToShip, int InTransit, int DeliveredToday,
        int PendingReturns, decimal StockOnHand);
    public sealed record FgQcCompletedRow(int LotId, string LotNo, string? WoNumber, string ItemNo,
        string? ItemName, string? CustomerCode, decimal Qty, string? Unit, DateTime? ProducedAt,
        DateTime? QcPassTs);
    public sealed record FgReturnRow(int ReturnId, string? ReturnNumber, string? CustomerCode,
        string? ItemNo, decimal Qty, string? ReturnReason, string? Status, DateTime? ReceivedAt);
    public sealed record FgReturnScanRow(string Barcode, int StockId, string? StockNumber, int? LotId, string? LotNo,
        int ShipmentOrderId, string? ShipOrderNumber, string CustomerCode,
        string ItemNo, string? ItemName, DateTime ShippedAt, decimal Qty);
    public sealed record FgReturnResult(bool Success, string Message, int? ReturnId, FgReturnScanRow? Row);

    public sealed record FgPutAwayScanRow(int? LotId, string LotNo, int? WoId,
        string ItemNo, string? ItemName, string? CustomerCode, decimal Qty, string? Unit,
        DateTime? MfgDate, DateTime? ExpiryDate, string? QcInspectionNo, DateTime? QcPassTs,
        bool IsQcPassed, bool AlreadyStocked, int? ExistingStockId, string? ExistingLocation,
        string? ExistingStatus, string BarcodeType, string StorageMethod, string NextScanType,
        string NextScanLabel, string? PackSpecId, string Message);
    public sealed record FgPutAwayLocationRow(string LocationId, string? LocationName, string? ZoneCode,
        string? Aisle, string? Bay, string? Slot, decimal Capacity, decimal CurrentQty,
        decimal AvailableQty, string? CurrentCustomerCode, bool IsValid, string Message,
        string ScanType, string ScannedBarcode);
    public sealed record FgPutAwayConfirmReq(string Barcode, string LocationId, string? SuggestedLocation,
        string? OverrideReason, int? PalletCount, int? PalletQty, string? StorageMethod,
        string? ContainerType, string? ContainerBarcode);
    public sealed record FgPutAwayResult(bool Success, string Message, int? StockId, FgPutAwayScanRow? Row,
        FgPutAwayLocationRow? Location);
    public sealed record FgReleaseLotReq(int OutgoingSlipLineId, int StockId, decimal Qty);
    public sealed record FgReleaseLotScanReq(int OutgoingSlipId, string Barcode, List<FgReleaseLotReq>? ScannedLots);
    public sealed record FgReleaseLotScanResult(bool Success, string Code, string Message,
        FgStockRow? Stock, int? OutgoingSlipLineId);
    public sealed record FgCompleteReleaseReq(int OutgoingSlipId, List<FgReleaseLotReq> Lots);
    public sealed record FgCompleteReleaseResult(bool Success, string Message, int? PickId);
    public sealed record FgLoadingTruckRow(string Barcode, string LicensePlate, bool Ready, string Message);
    public sealed record FgLoadingItemRow(int StockId, int ShipmentOrderLineId, int ShipmentOrderId,
        string ShipOrderNumber, string CustomerCode, string ItemNo, string? ItemName,
        string? LotNo, string? StockNumber, decimal Qty, string? Unit, string? Location);
    public sealed record FgLoadingOrderRow(int ShipmentOrderId, string Barcode, string ShipOrderNumber,
        string CustomerCode, DateTime? ShipDate, string? Destination, List<FgLoadingItemRow> Items);
    public sealed record FgLoadingOrderResult(bool Success, string Message, FgLoadingOrderRow? Order);
    public sealed record FgLoadingReq(string TruckBarcode, int ShipmentOrderId, List<int> StockIds);
    public sealed record FgLoadingResult(bool Success, string Message, int? LoadingId,
        FgLoadingTruckRow? Truck, FgLoadingItemRow? Item);
    public sealed record FgDeliveryReq(int ShipmentOrderId, int? LoadingId);
    public sealed record FgDayEndReq(string CloseMode, string? Note);
    public sealed record FgReturnReq(string Barcode, string ReturnReason, string? Note);

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
    {
        Authorize();
        try
        {
            var r = await _http.GetAsync("/api/fg/dashboard");
            if (!r.IsSuccessStatusCode) return new FgDashboard(0,0,0,0,0,0);
            return await r.Content.ReadFromJsonAsync<FgDashboard>() ?? new FgDashboard(0,0,0,0,0,0);
        }
        catch { return new FgDashboard(0,0,0,0,0,0); }
    }

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

    private async Task<T> GetRequiredAsync<T>(string url, string fallback)
    {
        Authorize();
        using var response = await _http.GetAsync(url);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadServiceErrorAsync(response, fallback));
        return await response.Content.ReadFromJsonAsync<T>()
            ?? throw new InvalidOperationException(fallback);
    }

    private async Task<T> Get<T>(string url) where T : new()
    {
        Authorize();
        try
        {
            var resp = await _http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return new T();
            return await resp.Content.ReadFromJsonAsync<T>() ?? new T();
        }
        catch { return new T(); }
    }
    private async Task<HttpResponseMessage> Post<TBody>(string url, TBody body)
    {
        Authorize();
        return await _http.PostAsJsonAsync(url, body);
    }

    // ── helper ──────────────────────────────────────────────────────────
    private static async Task<SparePartMoveResult> ReadSparePartMoveResultAsync(HttpResponseMessage response, string fallback)
    {
        if (!response.IsSuccessStatusCode)
            return new SparePartMoveResult(false, await ReadServiceErrorAsync(response, fallback));

        return await response.Content.ReadFromJsonAsync<SparePartMoveResult>()
               ?? new SparePartMoveResult(false, fallback);
    }

    private static async Task<InboundReceiveResult> ReadInboundReceiveResultAsync(HttpResponseMessage resp)
    {
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
            return new InboundReceiveResult(false, "Session expired. Sign in again.", null);

        if (!resp.IsSuccessStatusCode)
            return new InboundReceiveResult(false, await ReadServiceErrorAsync(resp, "Warehouse service is unavailable."), null);

        return await resp.Content.ReadFromJsonAsync<InboundReceiveResult>()
               ?? new InboundReceiveResult(false, "Warehouse service returned an empty response.", null);
    }

    private static async Task<string> ReadServiceErrorAsync(HttpResponseMessage resp, string fallback)
    {
        string body;
        try
        {
            body = await resp.Content.ReadAsStringAsync();
        }
        catch
        {
            return fallback;
        }

        if (string.IsNullOrWhiteSpace(body))
            return fallback;

        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("detail", out var detail)
                && detail.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(detail.GetString()))
                return detail.GetString()!;

            if (json.RootElement.TryGetProperty("title", out var title)
                && title.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(title.GetString()))
                return title.GetString()!;
        }
        catch
        {
            // Fall back to a short generic message rather than showing raw JSON.
        }

        return fallback;
    }

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

    private static List<T> FilterByArea<T>(IEnumerable<T> rows, Func<T, string?> areaSelector,
        string? areaCode)
    {
        var list = rows.ToList();
        return string.IsNullOrWhiteSpace(areaCode)
            ? list
            : list.Where(row => string.Equals(areaSelector(row), areaCode, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private static void EnsureArea(string? actualAreaCode, string? expectedAreaCode)
    {
        if (string.IsNullOrWhiteSpace(expectedAreaCode)) return;
        if (string.Equals(actualAreaCode, expectedAreaCode, StringComparison.OrdinalIgnoreCase)) return;

        throw new InvalidOperationException(string.Equals(expectedAreaCode, SparePartsAreaCode, StringComparison.OrdinalIgnoreCase)
            ? "This location does not belong to the Spare Parts Area."
            : $"This location does not belong to {expectedAreaCode}.");
    }

    private static List<LocationRow> NormalizeLocationRows(IEnumerable<LocationRow> rows)
    {
        return rows.Select(NormalizeLocationRow).ToList();
    }

    private static LocationRow NormalizeLocationRow(LocationRow row)
    {
        if (!string.IsNullOrWhiteSpace(row.AreaCode)
            && (!string.IsNullOrWhiteSpace(row.X) || !string.IsNullOrWhiteSpace(row.Y) || !string.IsNullOrWhiteSpace(row.Z)))
            return row;

        var fallback = GuessLocationPosition(row);
        return row with
        {
            AreaCode = FirstText(row.AreaCode, row.Zone, fallback.Area),
            AreaName = FirstText(row.AreaName, row.ZoneName, row.LocationName),
            X = FirstText(row.X, fallback.Column),
            Y = FirstText(row.Y, fallback.Row),
            Z = FirstText(row.Z, fallback.Level)
        };
    }

    private static (string Area, string Column, string Row, string Level) GuessLocationPosition(LocationRow row)
    {
        var locationId = row.LocationId?.Trim() ?? "";
        var compact = new string(locationId.Where(char.IsLetterOrDigit).ToArray());
        var digits = new string(compact.Where(char.IsDigit).ToArray());

        var area = !string.IsNullOrWhiteSpace(row.Zone) ? row.Zone.Trim()
            : compact.Length >= 4 ? compact[..4]
            : compact.Length > 0 ? compact
            : "AREA";

        var column = digits.Length >= 2 ? digits[..2] : "1";
        var locationRow = digits.Length >= 4 ? digits.Substring(2, 2) : "1";
        var level = digits.Length >= 6 ? digits.Substring(4, 2) : "1";

        return (area, column, locationRow, level);
    }

    private static string? FirstText(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }

    private void Authorize()
    {
        _http.DefaultRequestHeaders.Authorization =
            _auth.Token is null ? null : new AuthenticationHeaderValue("Bearer", _auth.Token);
    }
}
