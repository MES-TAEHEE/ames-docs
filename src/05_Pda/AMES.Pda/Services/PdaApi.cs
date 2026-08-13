using System.Data;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Pda.Services;

/// <summary>
/// Thin HTTP client wrapper for the AMES.Api endpoints. One instance per
/// process — registered as singleton in MauiProgram. Stamps every request
/// with the current bearer token from AuthState.
/// </summary>
public sealed class PdaApi
{
    private const string WhLocationCorcd = "5010";
    private const string WhLocationBizcd = "5011";

    private readonly HttpClient _http;
    private readonly AuthState  _auth;
    private readonly AmesConnectionFactory _db;

    public PdaApi(HttpClient http, AuthState auth, AmesConnectionFactory db)
    {
        _http = http;
        _auth = auth;
        _db = db;
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
        string shiftCode = "DAY")
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

    // ── WH ───────────────────────────────────────────────────────────────
    public sealed record InboundRow(int LotId, string LotCode, string? ItemNo, string? ItemName,
        decimal Qty, string? Vendor, DateTime? ArrivedAt);
    public sealed record Wh001ScheduleInboundItem(int ScheduleItemId, string PurchaseOrderNo, int? PurchaseOrderLineNo,
        string? SupplierName, string? MaterialNo, string? MaterialName, string? CarCode, string? UnitOfMeasure,
        decimal PurchaseOrderQty, decimal ReceivedQty, decimal RemainingQty, DateTime? ExpectedArrivalDate,
        DateTime? PurchaseOrderCreatedDate, string ReceiptStatus);
    public sealed record InboundScanRow(string ReceiveType, string? Yn, string LotNo, string Barcode,
        string? SourceTable, string? NoteNo, string? CaseBarcode, string? CaseNo, string? InvoiceNo,
        string? ContainerNo, string? PartNo, string? PartName, decimal Qty, string? Unit, string? PoNo,
        int? PoSeq, string? VendorId, string? VendorName, DateTime? ProductionDate, DateTime? DeliveryDate,
        DateTime? ArrivalDate, DateTime? ShipDate, DateTime? PackDate, string? ReceivedLocation,
        string? ReceivedStatus);
    public sealed record WarehouseTransactionRow(long RowNo, string? LotNo, string? PartNo,
        string? WorkDate, string? WorkTime, string? LocationId, decimal Qty, string Status,
        string Direction, string? WorkerId, string? ReasonCode, string? ReasonNote,
        string? Supervisor, decimal? BeforeQty, decimal? DeltaQty, decimal? AfterQty,
        string? BeforeStatus, string? AfterStatus, string? BeforeLocation, string? AfterLocation,
        string? Source, string? Note);
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
        string? PlantCode = null, string? LocationType = null, decimal? Capacity = null);
    public sealed record LocationMapItemRow(string LotNo, string? PartNo, string? PartName, decimal Qty, string? Unit,
        string? InventoryStatus, string? WorkDate, string? WorkTime);
    public sealed record Wh001ScheduleReleaseItem(string PickSlipNo, string? DestinationLocation, DateTime? RequiredDate,
        string? RequiredTime, DateTime? PrintedAt, DateTime? ClosedAt, string? ClosedBy,
        int MaterialLineCount, decimal RequestedBoxQty, decimal PickedBoxQty, decimal RequestedQty, decimal PickedQty,
        string PickStatus, string? FirstMaterialNo, string? FirstMaterialName, string? SuggestedPickLocation,
        string? SuggestedPickZone);
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
    public sealed record ReleaseCompleteReq(string PickSlipNo, List<ReleasePickInput>? Lots = null);
    public sealed record ReleaseCompleteResult(bool Success, string Message);
    public sealed record TransactionRow(long TxnId, DateTime TxnTime, string TxnType, string? ItemNo,
        string? LocationId, decimal QtyBefore, decimal Delta, decimal QtyAfter, string? ReasonCode);

    public sealed record ReceiveReq(string LotCode, decimal Qty, string LocationId);
    public sealed record InboundReceiveReq(string Mode, string Barcode, string LocationId);
    public sealed record InboundCancelReq(string Mode, string Barcode);
    public sealed record InboundAdjustReq(string Mode, string Barcode, decimal DeltaQty, string ReasonCode,
        string? ReasonNote, string SupervisorPin);
    public sealed record AdjustSaveReq(string? Mode, string Barcode, decimal DeltaQty, string ReasonCode,
        string? ReasonNote, string SupervisorPin);
    public sealed record InboundReceiveResult(bool Success, string Message, InboundScanRow? Row);
    public sealed record AdjustReq(string ItemNo, string LocationId, decimal Delta, string ReasonCode, string? Note);
    public sealed record PickReq(string PickSlipNo, string LotNo, decimal Qty);
    public sealed record PickResult(bool Success, string Message, ReleaseLotRow? Row);

    public Task<List<InboundRow>>         WhInboundTodayAsync()    => Get<List<InboundRow>>("/api/wh/inbound/today");
    public async Task<List<Wh001ScheduleInboundItem>> Wh001ScheduleInboundAsync(int? year = null, int? quarter = null, string? vendorId = null)
    {
        var args = new List<string>();
        if (year.HasValue) args.Add($"year={year.Value}");
        if (quarter.HasValue) args.Add($"quarter={quarter.Value}");
        if (!string.IsNullOrWhiteSpace(vendorId)) args.Add($"vendorId={Uri.EscapeDataString(vendorId)}");

        var query = args.Count == 0 ? "" : "?" + string.Join("&", args);
        return await Get<List<Wh001ScheduleInboundItem>>("/api/wh/schedule/inbound" + query);
    }
    public async Task<List<InventoryRow>> WhInventoryAsync(string? q = null, DateTime? dateFrom = null, DateTime? dateTo = null)
    {
        try
        {
            return await QueryWhInventoryDbAsync(q, dateFrom, dateTo);
        }
        catch
        {
            return await Get<List<InventoryRow>>("/api/wh/inventory" + (string.IsNullOrEmpty(q) ? "" : $"?q={Uri.EscapeDataString(q)}"));
        }
    }

    public async Task<InventoryScanLookupRow?> WhInventoryScanAsync(string? scanText)
    {
        if (string.IsNullOrWhiteSpace(scanText))
            return null;

        var value = scanText.Trim();
        try
        {
            return await QueryWhInventoryScanDbAsync(value);
        }
        catch
        {
            return new InventoryScanLookupRow("TEXT", value, null);
        }
    }

    public async Task<List<InventoryLocationRow>> WhInventoryLocationsAsync(string itemNo, DateTime? dateFrom = null, DateTime? dateTo = null)
    {
        try
        {
            return await QueryWhInventoryLocationsDbAsync(itemNo, dateFrom, dateTo);
        }
        catch
        {
            return new List<InventoryLocationRow>();
        }
    }
    public async Task<List<LocationRow>> WhLocationsAsync()
    {
        try
        {
            return NormalizeLocationRows(await QueryWhLocationsDbAsync());
        }
        catch
        {
            try
            {
                return NormalizeLocationRows(await Get<List<LocationRow>>("/api/wh/locations"));
            }
            catch
            {
                return new List<LocationRow>();
            }
        }
    }
    public async Task<List<LocationRow>> WhLocationMapAsync()
    {
        try
        {
            return NormalizeLocationRows(await QueryWhLocationMapDbAsync());
        }
        catch
        {
            try
            {
                return NormalizeLocationRows(await Get<List<LocationRow>>("/api/wh/locations"));
            }
            catch
            {
                return new List<LocationRow>();
            }
        }
    }
    public async Task<List<LocationMapItemRow>> WhLocationMapItemsAsync(string locationId, DateTime? dateFrom = null, DateTime? dateTo = null)
    {
        try
        {
            return await QueryWhLocationMapItemsDbAsync(locationId, dateFrom, dateTo);
        }
        catch
        {
            return new List<LocationMapItemRow>();
        }
    }
    public async Task<LocationRow?> WhScanLocationAsync(string locationId)
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

            return await resp.Content.ReadFromJsonAsync<LocationRow>();
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
    public async Task<string?> WhLocationTestBarcodeAsync()
    {
        try
        {
            return await QueryWhLocationTestBarcodeDbAsync();
        }
        catch
        {
            return (await WhLocationsAsync()).FirstOrDefault()?.LocationId;
        }
    }
    public Task<List<Wh001ScheduleReleaseItem>> Wh001ScheduleReleaseAsync() =>
        Get<List<Wh001ScheduleReleaseItem>>("/api/wh/schedule/release");
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
    public Task<List<ReleaseFifoLotRow>> WhReleaseFifoLotsAsync(string pickSlipNo)
        => Get<List<ReleaseFifoLotRow>>($"/api/wh/release/schedule/{Uri.EscapeDataString(pickSlipNo)}/fifo-lots");
    public async Task<ReleaseLotRow?> WhReleaseLotAsync(string pickSlipNo, string lotNo)
    {
        Authorize();
        try
        {
            var url = $"/api/wh/release/lot?pickSlipNo={Uri.EscapeDataString(pickSlipNo)}&lotNo={Uri.EscapeDataString(lotNo)}";
            var resp = await _http.GetAsync(url);
            return resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<ReleaseLotRow>() : null;
        }
        catch
        {
            return null;
        }
    }
    public async Task<List<LotStatusRow>> WhLotStatusesAsync(string? q = null)
    {
        try
        {
            await using var conn = _db.CreateConnection();
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("""
                SELECT TOP 300 L.LotID, COALESCE(L.LotCode,CONCAT('LOT-',L.LotID)) AS LotNo,
                       L.ItemNo,I.ItemName,
                       COALESCE(NULLIF(L.InventoryStatus,''),CASE WHEN W.InventoryID IS NULL THEN 'CREATED' WHEN COALESCE(W.OnHandQty,0)<=0 THEN 'RELEASED' WHEN NULLIF(W.LocationID,'') IS NULL THEN 'RECEIVED' ELSE 'STORED' END) AS InventoryStatus,
                       COALESCE(L.RemainingQty,W.OnHandQty,0) AS RemainingQty,W.LocationID,L.ProducedAt,
                       COALESCE(L.ModifiedTS,L.CreatedTS) AS LastChangedAt
                FROM dbo.tbl_Lot L
                LEFT JOIN dbo.MD_Item I ON I.ItemNo=L.ItemNo
                OUTER APPLY (SELECT TOP (1) X.InventoryID,X.OnHandQty,X.LocationID FROM dbo.WH_Inventory X WHERE X.LotID=L.LotID ORDER BY X.InventoryID DESC) W
                WHERE @Q='' OR L.LotCode LIKE '%'+@Q+'%' OR L.ItemNo LIKE '%'+@Q+'%' OR I.ItemName LIKE '%'+@Q+'%'
                ORDER BY COALESCE(L.ModifiedTS,L.CreatedTS) DESC,L.LotID DESC;
                """, conn);
            cmd.Parameters.AddWithValue("@Q", q?.Trim() ?? "");
            await using var rdr = await cmd.ExecuteReaderAsync();
            var rows = new List<LotStatusRow>();
            while (await rdr.ReadAsync())
                rows.Add(new LotStatusRow(
                    rdr.GetInt32(rdr.GetOrdinal("LotID")), GetString(rdr,"LotNo") ?? "",
                    GetString(rdr,"ItemNo"), GetString(rdr,"ItemName"), GetString(rdr,"InventoryStatus") ?? "CREATED",
                    GetDecimal(rdr,"RemainingQty"), GetString(rdr,"LocationID"), GetDate(rdr,"ProducedAt"), GetDate(rdr,"LastChangedAt")));
            return rows;
        }
        catch
        {
            return await Get<List<LotStatusRow>>("/api/wh/inventory/lots" + (string.IsNullOrWhiteSpace(q) ? "" : $"?q={Uri.EscapeDataString(q)}"));
        }
    }
    public Task<HttpResponseMessage> WhReleaseCompleteAsync(ReleaseCompleteReq body)
        => Post("/api/wh/release/complete", body);
    public Task<List<TransactionRow>>     WhTransactionsAsync(int days = 7) => Get<List<TransactionRow>>($"/api/wh/transactions?days={days}");

    public Task<HttpResponseMessage> WhReceiveAsync(ReceiveReq body) => Post("/api/wh/inbound/receive", body);
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

    public async Task<InboundScanRow?> WhScanAdjustAsync(string scanText)
    {
        try
        {
            Authorize();
            var url = $"/api/wh/adjust/scan?scanText={Uri.EscapeDataString(scanText.Trim())}";
            var resp = await _http.GetAsync(url);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException(await ReadServiceErrorAsync(resp, "Warehouse adjust scan service is unavailable."));

            return await resp.Content.ReadFromJsonAsync<InboundScanRow>();
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

    public async Task<List<WarehouseTransactionRow>> WhWarehouseTransactionsAsync(string? search = null, DateTime? dateFrom = null, DateTime? dateTo = null)
    {
        try
        {
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

            return await _http.GetFromJsonAsync<List<WarehouseTransactionRow>>(url)
                ?? new List<WarehouseTransactionRow>();
        }
        catch
        {
            return new List<WarehouseTransactionRow>();
        }
    }

    public async Task<string?> WhInboundTestBarcodeAsync(string mode)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = new SqlCommand("""
            SELECT TOP (1) L.LotCode
            FROM dbo.tbl_Lot L
            LEFT JOIN dbo.WH_Inventory I
                ON I.LotID = L.LotID
               AND COALESCE(I.Status, 'Received') <> 'Canceled'
               AND COALESCE(I.OnHandQty, 0) > 0
            WHERE (@Mode = N'' OR UPPER(COALESCE(L.ProcessCode, N'')) = @Mode)
            ORDER BY CASE WHEN I.InventoryID IS NULL THEN 0 ELSE 1 END, L.LotID DESC;
            """, conn);
        cmd.Parameters.Add("@Mode", SqlDbType.NVarChar, 10).Value =
            string.IsNullOrWhiteSpace(mode) ? "" : mode.Trim().ToUpperInvariant();
        var value = await cmd.ExecuteScalarAsync();
        return value == DBNull.Value ? null : Convert.ToString(value);
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

    public Task<InboundReceiveResult> WhAdjustInboundQtyAsync(InboundAdjustReq body) =>
        WhSaveAdjustQtyAsync(new AdjustSaveReq(
            body.Mode,
            body.Barcode,
            body.DeltaQty,
            body.ReasonCode,
            body.ReasonNote,
            body.SupervisorPin));

    public Task<HttpResponseMessage> WhAdjustAsync (AdjustReq  body) => Post("/api/wh/inventory/adjust", body);
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
    public sealed record FgReturnScanRow(string Barcode, string? StockNumber, string? LotNo,
        int ShipmentOrderId, string? ShipOrderNumber, string CustomerCode,
        string ItemNo, string? ItemName, DateTime ShippedAt);
    public sealed record FgReturnResult(bool Success, string Message, int? ReturnId, FgReturnScanRow? Row);

    public sealed record FgPutAwayReq(int WoId, string ItemNo, decimal Qty, string ActualLoc, int PalletCount);
    public sealed record FgPutAwayScanRow(int? LotId, string LotNo, int? WoId, string? WoNumber,
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
    public sealed record FgPickReq(int ShipmentOrderId, int StockId, decimal Qty);
    public sealed record FgLoadingReq(int ShipmentOrderId, string LicensePlate, string DriverName, string DockNo, string? SealNo);
    public sealed record FgDeliveryReq(int ShipmentOrderId, int? LoadingId);
    public sealed record FgDayEndReq(string CloseMode, string? Note);
    public sealed record FgReturnReq(string Barcode, string ReturnReason);

    public Task<List<FgStockRow>>   FgInventoryAsync(string? q = null)
        => Get<List<FgStockRow>>("/api/fg/inventory" + (string.IsNullOrEmpty(q) ? "" : $"?q={Uri.EscapeDataString(q)}"));
    public Task<List<FgQcCompletedRow>> FgQcCompletedAsync() => Get<List<FgQcCompletedRow>>("/api/fg/qc-completed");
    public Task<List<FgOrderRow>>   FgOrdersAsync()  => Get<List<FgOrderRow>>("/api/fg/orders");
    public Task<List<FgOrderLineRow>> FgOrderLinesAsync(string shipOrderNumber)
        => Get<List<FgOrderLineRow>>($"/api/fg/orders/{Uri.EscapeDataString(shipOrderNumber)}/lines");
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

    public Task<HttpResponseMessage> FgPutAwayAsync (FgPutAwayReq body)  => Post("/api/fg/putaway",  body);
    public Task<FgPutAwayResult> FgPutAwayScanAsync(string barcode)
        => GetFgPutAwayResultAsync($"/api/fg/putaway/scan?barcode={Uri.EscapeDataString(barcode)}");
    public Task<FgPutAwayLocationRow?> FgSuggestPutAwayLocationAsync(string itemNo, string? customerCode, decimal qty)
        => GetFgPutAwayLocationAsync($"/api/fg/putaway/suggest-location?itemNo={Uri.EscapeDataString(itemNo)}&customerCode={Uri.EscapeDataString(customerCode ?? "")}&qty={qty}");
    public Task<FgPutAwayLocationRow?> FgValidatePutAwayLocationAsync(string locationId, string itemNo, string? customerCode, decimal qty, string? expectedScanType)
        => GetFgPutAwayLocationAsync($"/api/fg/putaway/location?locationId={Uri.EscapeDataString(locationId)}&itemNo={Uri.EscapeDataString(itemNo)}&customerCode={Uri.EscapeDataString(customerCode ?? "")}&qty={qty}&expectedScanType={Uri.EscapeDataString(expectedScanType ?? "")}");
    public Task<FgPutAwayResult> FgConfirmPutAwayAsync(FgPutAwayConfirmReq body)
        => PostFgPutAwayResultAsync("/api/fg/putaway/confirm", body);
    public Task<HttpResponseMessage> FgPickAsync    (FgPickReq    body)  => Post("/api/fg/pick",     body);
    public Task<HttpResponseMessage> FgLoadingAsync (FgLoadingReq body)  => Post("/api/fg/loading",  body);
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
    private static string EscapeLikePattern(string value)
    {
        return value
            .Replace(@"\", @"\\")
            .Replace("%", @"\%")
            .Replace("_", @"\_")
            .Replace("[", @"\[");
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

    private async Task<List<WarehouseTransactionRow>> QueryWhWarehouseTransactionsDbAsync(string? search, DateTime? dateFrom, DateTime? dateTo)
    {
        var rows = new List<WarehouseTransactionRow>();
        var searchFilter = string.IsNullOrWhiteSpace(search) ? null : $"%{EscapeLikePattern(search.Trim())}%";
        var fromFilter = dateFrom?.Date ?? DateTime.Today.AddDays(-30);
        var toFilter = dateTo?.Date ?? DateTime.Today;

        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();

        if (await ProcedureExistsAsync(conn, "dbo", "WH_PDA_TRANSACTION_LIST"))
        {
            await using var proc = new SqlCommand("[dbo].[WH_PDA_TRANSACTION_LIST]", conn)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 15
            };
            proc.Parameters.Add("@SearchText", SqlDbType.NVarChar, 120).Value =
                string.IsNullOrWhiteSpace(search) ? DBNull.Value : search.Trim();
            proc.Parameters.Add("@DateFrom", SqlDbType.Date).Value = fromFilter;
            proc.Parameters.Add("@DateTo", SqlDbType.Date).Value = toFilter;

            await using var procRdr = await proc.ExecuteReaderAsync();
            while (await procRdr.ReadAsync())
                rows.Add(ReadWarehouseTransactionRow(procRdr));

            return rows;
        }

        var hasWms2030 = await TableExistsAsync(conn, "SIS_TEST", "WMS2030");
        var sql = hasWms2030
            ? """
                SELECT
                    ROW_NUMBER() OVER (ORDER BY H.SEQNO DESC) AS ROW_NO,
                    H.LOTNO,
                    CAST(NULL AS nvarchar(50)) AS PARTNO,
                    H.STD_DATE AS WDATE,
                    CAST(NULL AS nvarchar(20)) AS WTIME,
                    H.LOCATION_NO,
                    COALESCE(H.QTY, 0) AS QTY,
                    CASE
                        WHEN H.INV_STATUS = N'IC' THEN N'Cancel'
                        WHEN H.INV_STATUS LIKE N'I%' THEN N'In'
                        WHEN H.INV_STATUS LIKE N'O%' THEN N'Out'
                        ELSE N'Loss'
                    END AS STATUS,
                    CASE
                        WHEN H.INV_STATUS LIKE N'I%' THEN N'IN'
                        WHEN H.INV_STATUS LIKE N'O%' THEN N'OUT'
                        ELSE N'OTHER'
                    END AS DIRECTION,
                    CAST(NULL AS nvarchar(80)) AS WORKER_ID,
                    CAST(NULL AS nvarchar(60)) AS REASON_CODE,
                    CAST(NULL AS nvarchar(1000)) AS REASON_NOTE,
                    CAST(NULL AS nvarchar(40)) AS SUPERVISOR,
                    CAST(NULL AS decimal(18,3)) AS BEFORE_QTY,
                    CAST(NULL AS decimal(18,3)) AS DELTA_QTY,
                    CAST(NULL AS decimal(18,3)) AS AFTER_QTY,
                    CAST(NULL AS nvarchar(30)) AS BEFORE_STATUS,
                    H.INV_STATUS AS AFTER_STATUS,
                    CAST(NULL AS nvarchar(60)) AS BEFORE_LOCATION,
                    H.LOCATION_NO AS AFTER_LOCATION,
                    N'WMS2030' AS SOURCE,
                    H.INV_STATUS AS NOTE
                FROM SIS_TEST.WMS2030 H
                WHERE (@IN_SEARCH IS NULL OR H.LOTNO LIKE @IN_SEARCH ESCAPE N'\')
                  AND COALESCE(TRY_CONVERT(date, H.STD_DATE, 23), TRY_CONVERT(date, H.STD_DATE, 112), CONVERT(date, SYSDATETIME())) >= @IN_DATE_FROM
                  AND COALESCE(TRY_CONVERT(date, H.STD_DATE, 23), TRY_CONVERT(date, H.STD_DATE, 112), CONVERT(date, SYSDATETIME())) < DATEADD(day, 1, @IN_DATE_TO)
                ORDER BY ROW_NO;
                """
            : """
                ;WITH Logs AS
                (
                    SELECT
                        1 AS SORT_BUCKET,
                        H.LOTNO,
                        H.PARTNO,
                        COALESCE(NULLIF(H.STOCK_DATE, N''), NULLIF(H.STD_DATE, N''), CONVERT(nvarchar(10), H.INSERT_DATE, 23)) AS WDATE,
                        COALESCE(NULLIF(H.STOCK_TIME, N''), CONVERT(nvarchar(8), H.INSERT_DATE, 108)) AS WTIME,
                        CAST(NULL AS nvarchar(30)) AS LOCATION_NO,
                        COALESCE(H.QTY, 0) AS QTY,
                        N'In' AS STATUS,
                        N'IN' AS DIRECTION,
                        COALESCE(NULLIF(H.USER_ID, N''), NULLIF(H.INSERT_ID, N''), NULLIF(H.UPDATE_ID, N'')) AS WORKER_ID,
                        CAST(NULL AS nvarchar(60)) AS REASON_CODE,
                        CAST(NULL AS nvarchar(1000)) AS REASON_NOTE,
                        CAST(NULL AS nvarchar(40)) AS SUPERVISOR,
                        CAST(NULL AS decimal(18,3)) AS BEFORE_QTY,
                        COALESCE(H.QTY, 0) AS DELTA_QTY,
                        COALESCE(H.QTY, 0) AS AFTER_QTY,
                        CAST(NULL AS nvarchar(30)) AS BEFORE_STATUS,
                        H.INV_STATUS AS AFTER_STATUS,
                        CAST(NULL AS nvarchar(60)) AS BEFORE_LOCATION,
                        CAST(NULL AS nvarchar(60)) AS AFTER_LOCATION,
                        N'WMS2010' AS SOURCE,
                        H.INV_STATUS AS NOTE,
                        COALESCE(H.INSERT_DATE, H.UPDATE_DATE, SYSUTCDATETIME()) AS SORT_TS
                    FROM SIS_TEST.WMS2010 H
                    WHERE (@IN_SEARCH IS NULL OR H.LOTNO LIKE @IN_SEARCH ESCAPE N'\' OR H.PARTNO LIKE @IN_SEARCH ESCAPE N'\')

                    UNION ALL

                    SELECT
                        2 AS SORT_BUCKET,
                        S.LOTNO,
                        S.PARTNO,
                        COALESCE(NULLIF(S.WORK_DATE, N''), NULLIF(S.RCV_DATE, N''), CONVERT(nvarchar(10), S.INSERT_DATE, 23)) AS WDATE,
                        COALESCE(NULLIF(S.WORK_TIME, N''), CONVERT(nvarchar(8), S.INSERT_DATE, 108)) AS WTIME,
                        S.LOCATION_NO,
                        COALESCE(S.QTY, 0) AS QTY,
                        CASE
                            WHEN S.INV_STATUS = N'IC' THEN N'Cancel'
                            WHEN S.INV_STATUS LIKE N'I%' THEN N'In'
                            WHEN S.INV_STATUS LIKE N'O%' THEN N'Out'
                            ELSE N'Loss'
                        END AS STATUS,
                        CASE
                            WHEN S.INV_STATUS LIKE N'I%' THEN N'IN'
                            WHEN S.INV_STATUS LIKE N'O%' THEN N'OUT'
                            ELSE N'OTHER'
                        END AS DIRECTION,
                        COALESCE(NULLIF(S.USER_ID, N''), NULLIF(S.UPDATE_ID, N''), NULLIF(S.INSERT_ID, N'')) AS WORKER_ID,
                        CAST(NULL AS nvarchar(60)) AS REASON_CODE,
                        CAST(NULL AS nvarchar(1000)) AS REASON_NOTE,
                        CAST(NULL AS nvarchar(40)) AS SUPERVISOR,
                        CAST(NULL AS decimal(18,3)) AS BEFORE_QTY,
                        COALESCE(S.QTY, 0) AS DELTA_QTY,
                        COALESCE(S.QTY, 0) AS AFTER_QTY,
                        CAST(NULL AS nvarchar(30)) AS BEFORE_STATUS,
                        S.INV_STATUS AS AFTER_STATUS,
                        CAST(NULL AS nvarchar(60)) AS BEFORE_LOCATION,
                        S.LOCATION_NO AS AFTER_LOCATION,
                        N'WMS2020' AS SOURCE,
                        S.INV_STATUS AS NOTE,
                        COALESCE(S.UPDATE_DATE, S.INSERT_DATE, SYSUTCDATETIME()) AS SORT_TS
                    FROM SIS_TEST.WMS2020 S
                    WHERE (@IN_SEARCH IS NULL OR S.LOTNO LIKE @IN_SEARCH ESCAPE N'\' OR S.PARTNO LIKE @IN_SEARCH ESCAPE N'\')
                      AND NOT EXISTS
                      (
                          SELECT 1
                          FROM SIS_TEST.WMS2010 H
                          WHERE H.LOTNO = S.LOTNO
                      )

                    UNION ALL

                    SELECT
                        3 AS SORT_BUCKET,
                        L.LotCode AS LOTNO,
                        A.ItemNo AS PARTNO,
                        CONVERT(nvarchar(10), A.CreatedTS, 23) AS WDATE,
                        CONVERT(nvarchar(8), A.CreatedTS, 108) AS WTIME,
                        A.LocationID AS LOCATION_NO,
                        COALESCE(A.QtyAfter, 0) AS QTY,
                        N'Adjust' AS STATUS,
                        N'ADJ' AS DIRECTION,
                        COALESCE(A.ApprovedBy, A.RequestedBy, A.CreatedBy) AS WORKER_ID,
                        A.ReasonCode AS REASON_CODE,
                        A.ReasonNote AS REASON_NOTE,
                        COALESCE(A.ApprovedBy, A.RequestedBy, A.CreatedBy) AS SUPERVISOR,
                        A.QtyBefore AS BEFORE_QTY,
                        A.Delta AS DELTA_QTY,
                        A.QtyAfter AS AFTER_QTY,
                        N'QTY BEFORE' AS BEFORE_STATUS,
                        N'QTY AFTER' AS AFTER_STATUS,
                        A.LocationID AS BEFORE_LOCATION,
                        A.LocationID AS AFTER_LOCATION,
                        N'WH_InventoryAdjust' AS SOURCE,
                        CONCAT(A.ReasonCode, N' ', CASE WHEN A.Delta > 0 THEN N'+' ELSE N'' END, CONVERT(nvarchar(40), A.Delta)) AS NOTE,
                        COALESCE(A.CreatedTS, SYSUTCDATETIME()) AS SORT_TS
                    FROM dbo.WH_InventoryAdjust A
                    LEFT JOIN dbo.tbl_Lot L
                           ON L.LotID = A.LotID
                    WHERE (@IN_SEARCH IS NULL
                        OR L.LotCode LIKE @IN_SEARCH ESCAPE N'\'
                        OR A.ItemNo LIKE @IN_SEARCH ESCAPE N'\'
                        OR A.LocationID LIKE @IN_SEARCH ESCAPE N'\'
                        OR A.ReasonCode LIKE @IN_SEARCH ESCAPE N'\')
                )
                SELECT TOP (200)
                    ROW_NUMBER() OVER (ORDER BY SORT_TS DESC, SORT_BUCKET DESC) AS ROW_NO,
                    LOTNO,
                    PARTNO,
                    WDATE,
                    WTIME,
                    LOCATION_NO,
                    QTY,
                    STATUS,
                    DIRECTION,
                    WORKER_ID,
                    REASON_CODE,
                    REASON_NOTE,
                    SUPERVISOR,
                    BEFORE_QTY,
                    DELTA_QTY,
                    AFTER_QTY,
                    BEFORE_STATUS,
                    AFTER_STATUS,
                    BEFORE_LOCATION,
                    AFTER_LOCATION,
                    SOURCE,
                    NOTE
                FROM Logs
                WHERE SORT_TS >= @IN_DATE_FROM
                  AND SORT_TS < DATEADD(day, 1, @IN_DATE_TO)
                ORDER BY ROW_NO;
                """;

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@IN_SEARCH", SqlDbType.NVarChar, 120).Value =
            (object?)searchFilter ?? DBNull.Value;
        cmd.Parameters.Add("@IN_DATE_FROM", SqlDbType.Date).Value = fromFilter;
        cmd.Parameters.Add("@IN_DATE_TO", SqlDbType.Date).Value = toFilter;

        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            rows.Add(ReadWarehouseTransactionRow(rdr));
        }

        return rows;
    }

    private async Task<List<LocationRow>> QueryWhLocationsDbAsync()
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = new SqlCommand("""
            SELECT
                L.LOCATION_NO AS LocationID,
                L.LOCATION_NM AS LocationName,
                L.ZONECD AS ZoneCode,
                L.WHCD AS WarehouseCode,
                W.WHNM AS WarehouseName,
                L.AREACD AS AreaCode,
                A.AREANM AS AreaName,
                Z.ZONENM AS ZoneName,
                L.RACK_X AS Aisle,
                L.RACK_Y AS Bay,
                L.RACK_Z AS Slot,
                CAST(NULL AS nvarchar(20)) AS PlantCode,
                CAST(N'SIS' AS nvarchar(20)) AS LocationType,
                CAST(NULL AS decimal(18,3)) AS Capacity,
                COUNT(S.LOTNO) AS LineCount,
                COALESCE(SUM(S.QTY), 0) AS TotalQty
            FROM SIS_TEST.WMS1040 L
            LEFT JOIN SIS_TEST.WMS1010 W
                ON W.CORCD = L.CORCD
               AND W.BIZCD = L.BIZCD
               AND W.WHCD = L.WHCD
            LEFT JOIN SIS_TEST.WMS1020 A
                ON A.CORCD = L.CORCD
               AND A.BIZCD = L.BIZCD
               AND A.AREACD = L.AREACD
            LEFT JOIN SIS_TEST.WMS1030 Z
                ON Z.CORCD = L.CORCD
               AND Z.BIZCD = L.BIZCD
               AND Z.ZONECD = L.ZONECD
            LEFT JOIN SIS_TEST.WMS2020 S
                ON S.LOCATION_NO = L.LOCATION_NO
            WHERE L.CORCD = @LocationCorcd
              AND L.BIZCD = @LocationBizcd
              AND COALESCE(L.USE_YN, N'Y') = N'Y'
            GROUP BY L.LOCATION_NO, L.LOCATION_NM, L.ZONECD, L.WHCD, W.WHNM,
                L.AREACD, A.AREANM, Z.ZONENM, L.RACK_X, L.RACK_Y, L.RACK_Z
            ORDER BY L.LOCATION_NO;
            """, conn);
        cmd.Parameters.Add("@LocationCorcd", SqlDbType.NVarChar, 10).Value = WhLocationCorcd;
        cmd.Parameters.Add("@LocationBizcd", SqlDbType.NVarChar, 10).Value = WhLocationBizcd;

        await using var rdr = await cmd.ExecuteReaderAsync();
        var rows = new List<LocationRow>();
        while (await rdr.ReadAsync())
        {
            rows.Add(ReadLocationRow(rdr));
        }

        return rows;
    }

    private async Task<List<LocationRow>> QueryWhLocationMapDbAsync()
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = new SqlCommand("""
            SELECT
                L.LOCATION_NO AS LocationID,
                L.LOCATION_NM AS LocationName,
                L.ZONECD AS ZoneCode,
                L.WHCD AS WarehouseCode,
                W.WHNM AS WarehouseName,
                L.AREACD AS AreaCode,
                A.AREANM AS AreaName,
                Z.ZONENM AS ZoneName,
                L.RACK_X AS Aisle,
                L.RACK_Y AS Bay,
                L.RACK_Z AS Slot,
                CAST(NULL AS nvarchar(20)) AS PlantCode,
                CAST(N'SIS' AS nvarchar(20)) AS LocationType,
                CAST(NULL AS decimal(18,3)) AS Capacity,
                COUNT(S.LOTNO) AS LineCount,
                COALESCE(SUM(S.QTY), 0) AS TotalQty
            FROM SIS_TEST.WMS1040 L
            LEFT JOIN SIS_TEST.WMS1010 W
                ON W.CORCD = L.CORCD
               AND W.BIZCD = L.BIZCD
               AND W.WHCD = L.WHCD
            LEFT JOIN SIS_TEST.WMS1020 A
                ON A.CORCD = L.CORCD
               AND A.BIZCD = L.BIZCD
               AND A.AREACD = L.AREACD
            LEFT JOIN SIS_TEST.WMS1030 Z
                ON Z.CORCD = L.CORCD
               AND Z.BIZCD = L.BIZCD
               AND Z.ZONECD = L.ZONECD
            LEFT JOIN SIS_TEST.WMS2000 S
                ON S.CORCD = @LocationCorcd
               AND S.BIZCD = @LocationBizcd
               AND S.LOCATION_NO = L.LOCATION_NO
            WHERE L.CORCD = @LocationCorcd
              AND L.BIZCD = @LocationBizcd
              AND COALESCE(L.USE_YN, N'Y') = N'Y'
            GROUP BY L.LOCATION_NO, L.LOCATION_NM, L.ZONECD, L.WHCD, W.WHNM,
                L.AREACD, A.AREANM, Z.ZONENM, L.RACK_X, L.RACK_Y, L.RACK_Z
            ORDER BY L.AREACD, L.ZONECD, L.RACK_Z, L.RACK_Y, L.RACK_X, L.LOCATION_NO;
            """, conn);
        cmd.Parameters.Add("@LocationCorcd", SqlDbType.NVarChar, 10).Value = WhLocationCorcd;
        cmd.Parameters.Add("@LocationBizcd", SqlDbType.NVarChar, 10).Value = WhLocationBizcd;

        await using var rdr = await cmd.ExecuteReaderAsync();
        var rows = new List<LocationRow>();
        while (await rdr.ReadAsync())
        {
            rows.Add(ReadLocationRow(rdr));
        }

        return rows;
    }

    private async Task<List<LocationMapItemRow>> QueryWhLocationMapItemsDbAsync(string locationId, DateTime? dateFrom, DateTime? dateTo)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();

        if (await ProcedureExistsAsync(conn, "dbo", "WH_PDA_INVENTORY_LOCATION_CONTENTS"))
        {
            await using var proc = new SqlCommand("[dbo].[WH_PDA_INVENTORY_LOCATION_CONTENTS]", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            proc.Parameters.Add("@LocationId", SqlDbType.NVarChar, 40).Value = locationId.Trim();
            proc.Parameters.Add("@StockDateFrom", SqlDbType.Date).Value =
                dateFrom.HasValue ? dateFrom.Value.Date : (object)DBNull.Value;
            proc.Parameters.Add("@StockDateTo", SqlDbType.Date).Value =
                dateTo.HasValue ? dateTo.Value.Date : (object)DBNull.Value;

            await using var procRdr = await proc.ExecuteReaderAsync();
            var procRows = new List<LocationMapItemRow>();
            while (await procRdr.ReadAsync())
                procRows.Add(ReadLocationMapItemRow(procRdr));

            return procRows;
        }

        if (await TableExistsAsync(conn, "dbo", "WH_Inventory"))
        {
            await using var dboCmd = new SqlCommand("""
                SELECT
                    COALESCE(LOT.LotCode, CONCAT(N'LOT-', W.LotID), N'-') AS LOTNO,
                    W.ItemNo AS PARTNO,
                    I.ItemName AS PARTNM,
                    SUM(COALESCE(W.OnHandQty, 0)) AS QTY,
                    I.DefaultUOM AS UNIT,
                    COALESCE(W.Status, N'Received') AS INV_STATUS,
                    CONVERT(nvarchar(10), MAX(W.LastReceivedAt), 23) AS WORK_DATE,
                    CONVERT(nvarchar(8), MAX(W.LastReceivedAt), 108) AS WORK_TIME
                FROM dbo.WH_Inventory W
                LEFT JOIN dbo.tbl_Lot LOT
                       ON LOT.LotID = W.LotID
                LEFT JOIN dbo.MD_Item I
                       ON I.ItemNo = W.ItemNo
                WHERE UPPER(W.LocationID) = UPPER(@LocationID)
                  AND COALESCE(W.OnHandQty, 0) > 0
                  AND UPPER(COALESCE(W.Status, N'Received')) NOT IN (N'CANCELED', N'RELEASED', N'PICKED')
                  AND (@StockDateFrom IS NULL OR CONVERT(date, W.LastReceivedAt) >= @StockDateFrom)
                  AND (@StockDateTo IS NULL OR CONVERT(date, W.LastReceivedAt) <= @StockDateTo)
                GROUP BY
                    COALESCE(LOT.LotCode, CONCAT(N'LOT-', W.LotID), N'-'),
                    W.ItemNo,
                    I.ItemName,
                    I.DefaultUOM,
                    COALESCE(W.Status, N'Received')
                ORDER BY W.ItemNo, LOTNO;
                """, conn);
            dboCmd.Parameters.Add("@LocationID", SqlDbType.NVarChar, 40).Value = locationId.Trim();
            dboCmd.Parameters.Add("@StockDateFrom", SqlDbType.Date).Value =
                dateFrom.HasValue ? dateFrom.Value.Date : (object)DBNull.Value;
            dboCmd.Parameters.Add("@StockDateTo", SqlDbType.Date).Value =
                dateTo.HasValue ? dateTo.Value.Date : (object)DBNull.Value;

            await using var dboRdr = await dboCmd.ExecuteReaderAsync();
            var dboRows = new List<LocationMapItemRow>();
            while (await dboRdr.ReadAsync())
                dboRows.Add(ReadLocationMapItemRow(dboRdr));

            return dboRows;
        }

        if (!await TableExistsAsync(conn, "SIS_TEST", "WMS2000"))
            return new List<LocationMapItemRow>();

        await using var cmd = new SqlCommand("""
            SELECT
                S.LOTNO,
                S.PARTNO,
                PL.PARTNM,
                S.QTY,
                COALESCE(P.UNIT, P.MEINS) AS UNIT,
                S.INV_STATUS,
                S.WORK_DATE,
                S.WORK_TIME
            FROM SIS_TEST.WMS2000 S
            LEFT JOIN SIS_TEST.ACD0020 P
                ON P.PARTNO = S.PARTNO
            LEFT JOIN SIS_TEST.ACD0020L PL
                ON PL.PARTNO = S.PARTNO
               AND PL.LANG_SET = N'EN'
            WHERE S.CORCD = @LocationCorcd
              AND S.BIZCD = @LocationBizcd
              AND UPPER(S.LOCATION_NO) = UPPER(@LocationID)
              AND COALESCE(S.QTY, 0) > 0
            ORDER BY S.PARTNO, S.LOTNO;
            """, conn);
        cmd.Parameters.Add("@LocationCorcd", SqlDbType.NVarChar, 10).Value = WhLocationCorcd;
        cmd.Parameters.Add("@LocationBizcd", SqlDbType.NVarChar, 10).Value = WhLocationBizcd;
        cmd.Parameters.Add("@LocationID", SqlDbType.NVarChar, 30).Value = locationId.Trim();

        await using var rdr = await cmd.ExecuteReaderAsync();
        var rows = new List<LocationMapItemRow>();
        while (await rdr.ReadAsync())
        {
            rows.Add(ReadLocationMapItemRow(rdr));
        }

        return rows;
    }

    private async Task<List<InventoryRow>> QueryWhInventoryDbAsync(string? q, DateTime? dateFrom, DateTime? dateTo)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        var usePdaProcedure = await ProcedureExistsAsync(conn, "dbo", "WH_PDA_INVENTORY_STATUS_LIST");
        await using var cmd = new SqlCommand(
            usePdaProcedure ? "[dbo].[WH_PDA_INVENTORY_STATUS_LIST]" : "[SIS_TEST].[PDA_WH03_INVENTORY_STATUS]",
            conn)
        {
            CommandType = CommandType.StoredProcedure
        };
        if (usePdaProcedure)
        {
            cmd.Parameters.Add("@SearchText", SqlDbType.NVarChar, 80).Value =
                string.IsNullOrWhiteSpace(q) ? DBNull.Value : q.Trim();
            cmd.Parameters.Add("@StockDateFrom", SqlDbType.Date).Value =
                dateFrom.HasValue ? dateFrom.Value.Date : (object)DBNull.Value;
            cmd.Parameters.Add("@StockDateTo", SqlDbType.Date).Value =
                dateTo.HasValue ? dateTo.Value.Date : (object)DBNull.Value;
        }
        else
        {
            cmd.Parameters.Add("@IN_CORCD", SqlDbType.NVarChar, 10).Value = WhLocationCorcd;
            cmd.Parameters.Add("@IN_BIZCD", SqlDbType.NVarChar, 10).Value = WhLocationBizcd;
            cmd.Parameters.Add("@IN_Q", SqlDbType.NVarChar, 80).Value =
                string.IsNullOrWhiteSpace(q) ? DBNull.Value : q.Trim();
            cmd.Parameters.Add("@IN_DATE_FROM", SqlDbType.Date).Value =
                dateFrom.HasValue ? dateFrom.Value.Date : (object)DBNull.Value;
            cmd.Parameters.Add("@IN_DATE_TO", SqlDbType.Date).Value =
                dateTo.HasValue ? dateTo.Value.Date : (object)DBNull.Value;
            cmd.Parameters.Add("@IN_LANG_SET", SqlDbType.NVarChar, 10).Value = "EN";
        }

        await using var rdr = await cmd.ExecuteReaderAsync();
        var rows = new List<InventoryRow>();
        while (await rdr.ReadAsync())
        {
            rows.Add(ReadInventoryRow(rdr));
        }

        return rows;
    }

    private async Task<InventoryScanLookupRow> QueryWhInventoryScanDbAsync(string scanText)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();

        if (await ProcedureExistsAsync(conn, "dbo", "WH_PDA_INVENTORY_SCAN_LOOKUP"))
        {
            await using var cmd = new SqlCommand("[dbo].[WH_PDA_INVENTORY_SCAN_LOOKUP]", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.Add("@ScanText", SqlDbType.NVarChar, 80).Value = scanText.Trim();

            await using var rdr = await cmd.ExecuteReaderAsync();
            if (await rdr.ReadAsync())
                return ReadInventoryScanLookupRow(rdr, scanText);
        }

        if (await TableExistsAsync(conn, "dbo", "MD_Location"))
        {
            var locationId = await FirstStringAsync(conn, """
                SELECT TOP (1) L.LocationID
                FROM dbo.MD_Location L
                WHERE UPPER(L.LocationID) = UPPER(@ScanText)
                  AND COALESCE(L.ActiveFlag, 1) = 1;
                """, scanText);
            if (!string.IsNullOrWhiteSpace(locationId))
                return new InventoryScanLookupRow("LOCATION", locationId, null);
        }

        if (await TableExistsAsync(conn, "dbo", "tbl_Lot"))
        {
            var partNo = await FirstStringAsync(conn, """
                SELECT TOP (1) L.ItemNo
                FROM dbo.tbl_Lot L
                WHERE UPPER(L.LotCode) = UPPER(@ScanText)
                   OR UPPER(L.ItemNo) = UPPER(@ScanText)
                ORDER BY CASE WHEN UPPER(L.LotCode) = UPPER(@ScanText) THEN 0 ELSE 1 END, L.LotID DESC;
                """, scanText);
            if (!string.IsNullOrWhiteSpace(partNo))
                return new InventoryScanLookupRow("PART", partNo, null);
        }

        if (await TableExistsAsync(conn, "dbo", "MD_Item"))
        {
            var partNo = await FirstStringAsync(conn, """
                SELECT TOP (1) I.ItemNo
                FROM dbo.MD_Item I
                WHERE UPPER(I.ItemNo) = UPPER(@ScanText)
                  AND COALESCE(I.ActiveFlag, 1) = 1;
                """, scanText);
            if (!string.IsNullOrWhiteSpace(partNo))
                return new InventoryScanLookupRow("PART", partNo, null);
        }

        if (await TableExistsAsync(conn, "SIS_TEST", "WMS1040"))
        {
            var locationId = await FirstStringAsync(conn, """
                SELECT TOP (1) L.LOCATION_NO
                FROM SIS_TEST.WMS1040 L
                WHERE UPPER(L.LOCATION_NO) = UPPER(@ScanText)
                  AND COALESCE(L.USE_YN, N'Y') = N'Y';
                """, scanText);
            if (!string.IsNullOrWhiteSpace(locationId))
                return new InventoryScanLookupRow("LOCATION", locationId, null);
        }

        if (await TableExistsAsync(conn, "SIS_TEST", "WMS2000"))
        {
            var locationId = await FirstStringAsync(conn, """
                SELECT TOP (1) S.LOCATION_NO
                FROM SIS_TEST.WMS2000 S
                WHERE UPPER(S.LOCATION_NO) = UPPER(@ScanText)
                  AND COALESCE(S.QTY, 0) > 0;
                """, scanText);
            if (!string.IsNullOrWhiteSpace(locationId))
                return new InventoryScanLookupRow("LOCATION", locationId, null);
        }

        if (await TableExistsAsync(conn, "SIS_TEST", "WMS2020"))
        {
            var partNo = await FirstStringAsync(conn, """
                SELECT TOP (1) S.PARTNO
                FROM SIS_TEST.WMS2020 S
                WHERE UPPER(S.LOTNO) = UPPER(@ScanText)
                   OR UPPER(S.PARTNO) = UPPER(@ScanText)
                ORDER BY CASE WHEN UPPER(S.LOTNO) = UPPER(@ScanText) THEN 0 ELSE 1 END;
                """, scanText);
            if (!string.IsNullOrWhiteSpace(partNo))
                return new InventoryScanLookupRow("PART", partNo, null);
        }

        if (await TableExistsAsync(conn, "SIS_TEST", "WMS2010"))
        {
            var partNo = await FirstStringAsync(conn, """
                SELECT TOP (1) H.PARTNO
                FROM SIS_TEST.WMS2010 H
                WHERE UPPER(H.LOTNO) = UPPER(@ScanText)
                   OR UPPER(H.PARTNO) = UPPER(@ScanText)
                ORDER BY CASE WHEN UPPER(H.LOTNO) = UPPER(@ScanText) THEN 0 ELSE 1 END;
                """, scanText);
            if (!string.IsNullOrWhiteSpace(partNo))
                return new InventoryScanLookupRow("PART", partNo, null);
        }

        if (await TableExistsAsync(conn, "SIS_TEST", "ACD0020"))
        {
            var partNo = await FirstStringAsync(conn, """
                SELECT TOP (1) P.PARTNO
                FROM SIS_TEST.ACD0020 P
                WHERE UPPER(P.PARTNO) = UPPER(@ScanText);
                """, scanText);
            if (!string.IsNullOrWhiteSpace(partNo))
                return new InventoryScanLookupRow("PART", partNo, null);
        }

        return new InventoryScanLookupRow("TEXT", scanText, null);
    }

    private async Task<List<InventoryLocationRow>> QueryWhInventoryLocationsDbAsync(string itemNo, DateTime? dateFrom, DateTime? dateTo)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        var usePdaProcedure = await ProcedureExistsAsync(conn, "dbo", "WH_PDA_INVENTORY_LOCATION_LIST");
        await using var cmd = new SqlCommand(
            usePdaProcedure ? "[dbo].[WH_PDA_INVENTORY_LOCATION_LIST]" : "[SIS_TEST].[PDA_WH03_INVENTORY_LOCATIONS]",
            conn)
        {
            CommandType = CommandType.StoredProcedure
        };
        if (usePdaProcedure)
        {
            cmd.Parameters.Add("@ItemNo", SqlDbType.NVarChar, 40).Value = itemNo.Trim();
            cmd.Parameters.Add("@StockDateFrom", SqlDbType.Date).Value =
                dateFrom.HasValue ? dateFrom.Value.Date : (object)DBNull.Value;
            cmd.Parameters.Add("@StockDateTo", SqlDbType.Date).Value =
                dateTo.HasValue ? dateTo.Value.Date : (object)DBNull.Value;
        }
        else
        {
            cmd.Parameters.Add("@IN_CORCD", SqlDbType.NVarChar, 10).Value = WhLocationCorcd;
            cmd.Parameters.Add("@IN_BIZCD", SqlDbType.NVarChar, 10).Value = WhLocationBizcd;
            cmd.Parameters.Add("@IN_PARTNO", SqlDbType.NVarChar, 40).Value = itemNo.Trim();
            cmd.Parameters.Add("@IN_DATE_FROM", SqlDbType.Date).Value =
                dateFrom.HasValue ? dateFrom.Value.Date : (object)DBNull.Value;
            cmd.Parameters.Add("@IN_DATE_TO", SqlDbType.Date).Value =
                dateTo.HasValue ? dateTo.Value.Date : (object)DBNull.Value;
            cmd.Parameters.Add("@IN_LANG_SET", SqlDbType.NVarChar, 10).Value = "EN";
        }

        await using var rdr = await cmd.ExecuteReaderAsync();
        var rows = new List<InventoryLocationRow>();
        while (await rdr.ReadAsync())
        {
            rows.Add(ReadInventoryLocationRow(rdr));
        }

        return rows;
    }

    private async Task<LocationRow?> QueryWhLocationDbAsync(string locationId)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = new SqlCommand("""
            SELECT TOP (1)
                L.LOCATION_NO AS LocationID,
                L.LOCATION_NM AS LocationName,
                L.ZONECD AS ZoneCode,
                L.WHCD AS WarehouseCode,
                W.WHNM AS WarehouseName,
                L.AREACD AS AreaCode,
                A.AREANM AS AreaName,
                Z.ZONENM AS ZoneName,
                L.RACK_X AS Aisle,
                L.RACK_Y AS Bay,
                L.RACK_Z AS Slot,
                CAST(NULL AS nvarchar(20)) AS PlantCode,
                CAST(N'SIS' AS nvarchar(20)) AS LocationType,
                CAST(NULL AS decimal(18,3)) AS Capacity,
                COUNT(S.LOTNO) AS LineCount,
                COALESCE(SUM(S.QTY), 0) AS TotalQty
            FROM SIS_TEST.WMS1040 L
            LEFT JOIN SIS_TEST.WMS1010 W
                ON W.CORCD = L.CORCD
               AND W.BIZCD = L.BIZCD
               AND W.WHCD = L.WHCD
            LEFT JOIN SIS_TEST.WMS1020 A
                ON A.CORCD = L.CORCD
               AND A.BIZCD = L.BIZCD
               AND A.AREACD = L.AREACD
            LEFT JOIN SIS_TEST.WMS1030 Z
                ON Z.CORCD = L.CORCD
               AND Z.BIZCD = L.BIZCD
               AND Z.ZONECD = L.ZONECD
            LEFT JOIN SIS_TEST.WMS2020 S
                ON S.LOCATION_NO = L.LOCATION_NO
            WHERE L.CORCD = @LocationCorcd
              AND L.BIZCD = @LocationBizcd
              AND COALESCE(L.USE_YN, N'Y') = N'Y'
              AND UPPER(L.LOCATION_NO) = UPPER(@LocationID)
            GROUP BY L.LOCATION_NO, L.LOCATION_NM, L.ZONECD, L.WHCD, W.WHNM,
                L.AREACD, A.AREANM, Z.ZONENM, L.RACK_X, L.RACK_Y, L.RACK_Z
            ORDER BY L.LOCATION_NO;
            """, conn);
        cmd.Parameters.Add("@LocationCorcd", SqlDbType.NVarChar, 10).Value = WhLocationCorcd;
        cmd.Parameters.Add("@LocationBizcd", SqlDbType.NVarChar, 10).Value = WhLocationBizcd;
        cmd.Parameters.Add("@LocationID", SqlDbType.NVarChar, 30).Value = locationId.Trim();

        await using var rdr = await cmd.ExecuteReaderAsync();
        return await rdr.ReadAsync() ? ReadLocationRow(rdr) : null;
    }

    private async Task<string?> QueryWhLocationTestBarcodeDbAsync()
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = new SqlCommand("""
            SELECT TOP (1) L.LOCATION_NO
            FROM SIS_TEST.WMS1040 L
            WHERE L.CORCD = @LocationCorcd
              AND L.BIZCD = @LocationBizcd
              AND COALESCE(L.USE_YN, N'Y') = N'Y'
            ORDER BY L.LOCATION_NO;
            """, conn);
        cmd.Parameters.Add("@LocationCorcd", SqlDbType.NVarChar, 10).Value = WhLocationCorcd;
        cmd.Parameters.Add("@LocationBizcd", SqlDbType.NVarChar, 10).Value = WhLocationBizcd;

        var value = await cmd.ExecuteScalarAsync();
        return value == DBNull.Value ? null : Convert.ToString(value);
    }

    private static async Task<string?> FirstStringAsync(SqlConnection conn, string sql, string scanText)
    {
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@ScanText", SqlDbType.NVarChar, 80).Value = scanText.Trim();
        var value = await cmd.ExecuteScalarAsync();
        return value == DBNull.Value ? null : Convert.ToString(value);
    }

    private static async Task<bool> TableExistsAsync(SqlConnection conn, string schema, string table)
    {
        await using var cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID(@ObjectName, N'U') IS NULL THEN 0 ELSE 1 END;", conn);
        cmd.Parameters.Add("@ObjectName", SqlDbType.NVarChar, 256).Value = $"{schema}.{table}";
        var value = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(value) == 1;
    }

    private static async Task<bool> ProcedureExistsAsync(SqlConnection conn, string schema, string procedure)
    {
        await using var cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID(@ObjectName, N'P') IS NULL THEN 0 ELSE 1 END;", conn);
        cmd.Parameters.Add("@ObjectName", SqlDbType.NVarChar, 256).Value = $"{schema}.{procedure}";
        var value = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(value) == 1;
    }

    private static InboundScanRow ReadInboundScanRow(SqlDataReader rdr)
    {
        return new InboundScanRow(
            GetString(rdr, "RECEIVE_TYPE") ?? "",
            GetString(rdr, "YN"),
            GetString(rdr, "LOTNO") ?? "",
            GetString(rdr, "BARCODE") ?? "",
            GetString(rdr, "SOURCE_TABLE"),
            GetString(rdr, "NOTENO"),
            GetString(rdr, "CASE_BARCODE"),
            GetString(rdr, "CASE_NO"),
            GetString(rdr, "INVOICE_NO"),
            GetString(rdr, "CONTAINER_NO"),
            GetString(rdr, "PARTNO"),
            GetString(rdr, "PARTNM"),
            GetDecimal(rdr, "QTY"),
            GetString(rdr, "UNIT"),
            GetString(rdr, "PONO"),
            GetInt(rdr, "PONO_SEQ"),
            GetString(rdr, "VENDCD"),
            GetString(rdr, "VENDNM"),
            GetDate(rdr, "PROD_DATE"),
            GetDate(rdr, "DELI_DATE"),
            GetDate(rdr, "ARRIV_DATE"),
            GetDate(rdr, "SHIP_DATE"),
            GetDate(rdr, "PACK_DATE"),
            GetString(rdr, "RECEIVED_LOCATION"),
            GetString(rdr, "RECEIVED_STATUS"));
    }

    private static WarehouseTransactionRow ReadWarehouseTransactionRow(SqlDataReader rdr)
    {
        return new WarehouseTransactionRow(
            GetLong(rdr, "ROW_NO") ?? 0,
            GetString(rdr, "LOTNO"),
            GetString(rdr, "PARTNO"),
            GetString(rdr, "WDATE"),
            GetString(rdr, "WTIME"),
            GetString(rdr, "LOCATION_NO"),
            GetDecimal(rdr, "QTY"),
            GetString(rdr, "STATUS") ?? "-",
            GetString(rdr, "DIRECTION") ?? "OTHER",
            GetString(rdr, "WORKER_ID"),
            GetString(rdr, "REASON_CODE"),
            GetString(rdr, "REASON_NOTE"),
            GetString(rdr, "SUPERVISOR"),
            GetNullableDecimal(rdr, "BEFORE_QTY"),
            GetNullableDecimal(rdr, "DELTA_QTY"),
            GetNullableDecimal(rdr, "AFTER_QTY"),
            GetString(rdr, "BEFORE_STATUS"),
            GetString(rdr, "AFTER_STATUS"),
            GetString(rdr, "BEFORE_LOCATION"),
            GetString(rdr, "AFTER_LOCATION"),
            GetString(rdr, "SOURCE"),
            GetString(rdr, "NOTE"));
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

    private static LocationRow ReadLocationRow(SqlDataReader rdr)
    {
        return new LocationRow(
            GetString(rdr, "LocationID") ?? "",
            GetString(rdr, "LocationName"),
            GetString(rdr, "ZoneCode"),
            GetInt(rdr, "LineCount") ?? 0,
            GetDecimal(rdr, "TotalQty"),
            GetString(rdr, "WarehouseCode"),
            GetString(rdr, "WarehouseName"),
            GetString(rdr, "AreaCode"),
            GetString(rdr, "AreaName"),
            GetString(rdr, "ZoneName"),
            GetString(rdr, "Aisle"),
            GetString(rdr, "Bay"),
            GetString(rdr, "Slot"),
            GetString(rdr, "PlantCode"),
            GetString(rdr, "LocationType"),
            GetNullableDecimal(rdr, "Capacity"));
    }

    private static LocationMapItemRow ReadLocationMapItemRow(SqlDataReader rdr)
    {
        return new LocationMapItemRow(
            GetString(rdr, "LOTNO") ?? "",
            GetString(rdr, "PARTNO"),
            GetString(rdr, "PARTNM"),
            GetDecimal(rdr, "QTY"),
            GetString(rdr, "UNIT"),
            GetString(rdr, "INV_STATUS"),
            GetString(rdr, "WORK_DATE"),
            GetString(rdr, "WORK_TIME"));
    }

    private static InventoryRow ReadInventoryRow(SqlDataReader rdr)
    {
        return new InventoryRow(
            GetInt(rdr, "INVENTORY_ID") ?? 0,
            GetString(rdr, "PARTNO") ?? "",
            GetString(rdr, "PARTNM"),
            GetString(rdr, "PRIMARY_LOCATION") ?? "-",
            null,
            GetDecimal(rdr, "SUM_QTY"),
            GetDecimal(rdr, "RESERVED_QTY"),
            GetDate(rdr, "LAST_RECEIVED_DATE"),
            GetString(rdr, "VINCD"),
            GetString(rdr, "UNIT"),
            GetNullableDecimal(rdr, "MIN_INV_DAY"),
            GetNullableDecimal(rdr, "MIN_INV_QTY"),
            GetNullableDecimal(rdr, "MAX_INV_DAY"),
            GetNullableDecimal(rdr, "MAX_INV_QTY"),
            GetInt(rdr, "LOT_COUNT") ?? 0,
            GetInt(rdr, "LOCATION_COUNT") ?? 0,
            GetString(rdr, "STATUS"),
            GetString(rdr, "STATUSNM"),
            GetDate(rdr, "LAST_RECEIVED_DATE"),
            GetString(rdr, "LOTNO"));
    }

    private static InventoryLocationRow ReadInventoryLocationRow(SqlDataReader rdr)
    {
        return new InventoryLocationRow(
            GetInt(rdr, "ROW_NO") ?? 0,
            GetString(rdr, "PARTNO") ?? "",
            GetString(rdr, "LOCATION_NO") ?? "-",
            GetString(rdr, "LOCATION_NM"),
            GetString(rdr, "WHCD"),
            GetString(rdr, "WHNM"),
            GetString(rdr, "AREACD"),
            GetString(rdr, "AREANM"),
            GetString(rdr, "ZONECD"),
            GetString(rdr, "ZONENM"),
            GetString(rdr, "RACK_X"),
            GetString(rdr, "RACK_Y"),
            GetString(rdr, "RACK_Z"),
            GetDecimal(rdr, "SUM_QTY"));
    }

    private static InventoryScanLookupRow ReadInventoryScanLookupRow(SqlDataReader rdr, string fallback)
    {
        var kind = GetString(rdr, "SEARCH_KIND");
        var text = GetString(rdr, "SEARCH_TEXT");
        return new InventoryScanLookupRow(
            string.IsNullOrWhiteSpace(kind) ? "TEXT" : kind.Trim().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(text) ? fallback : text.Trim(),
            GetString(rdr, "DISPLAY_TEXT"));
    }

    private static bool HasColumn(SqlDataReader rdr, string name)
    {
        for (var i = 0; i < rdr.FieldCount; i++)
        {
            if (string.Equals(rdr.GetName(i), name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string? GetString(SqlDataReader rdr, string name)
    {
        if (!HasColumn(rdr, name)) return null;
        var value = rdr[name];
        return value == DBNull.Value ? null : Convert.ToString(value);
    }

    private static int? GetInt(SqlDataReader rdr, string name)
    {
        if (!HasColumn(rdr, name)) return null;
        var value = rdr[name];
        return value == DBNull.Value ? null : Convert.ToInt32(value);
    }

    private static long? GetLong(SqlDataReader rdr, string name)
    {
        if (!HasColumn(rdr, name)) return null;
        var value = rdr[name];
        return value == DBNull.Value ? null : Convert.ToInt64(value);
    }

    private static decimal GetDecimal(SqlDataReader rdr, string name)
    {
        if (!HasColumn(rdr, name)) return 0;
        var value = rdr[name];
        return value == DBNull.Value ? 0 : Convert.ToDecimal(value);
    }

    private static decimal? GetNullableDecimal(SqlDataReader rdr, string name)
    {
        if (!HasColumn(rdr, name)) return null;
        var value = rdr[name];
        return value == DBNull.Value ? null : Convert.ToDecimal(value);
    }

    private static DateTime? GetDate(SqlDataReader rdr, string name)
    {
        if (!HasColumn(rdr, name)) return null;
        var value = rdr[name];
        if (value == DBNull.Value) return null;
        if (value is DateTime dt) return dt;
        return DateTime.TryParse(Convert.ToString(value), out var parsed) ? parsed : null;
    }

    private void Authorize()
    {
        _http.DefaultRequestHeaders.Authorization =
            _auth.Token is null ? null : new AuthenticationHeaderValue("Bearer", _auth.Token);
    }
}
