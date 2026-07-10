using System.Data;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
    public sealed record InboundScheduleRow(int PoId, string PoNumber, int? PoLineNo, string? VendorId,
        string? ItemNo, string? ItemName, string? CarCode, string? Unit, decimal OrderQty, decimal ReceivedQty,
        decimal NonDeliverQty, DateTime? DueDate, DateTime? PoCreateDate, string? Status);
    public sealed record InboundScanRow(string ReceiveType, string? Yn, string LotNo, string Barcode,
        string? SourceTable, string? NoteNo, string? CaseBarcode, string? CaseNo, string? InvoiceNo,
        string? ContainerNo, string? PartNo, string? PartName, decimal Qty, string? Unit, string? PoNo,
        int? PoSeq, string? VendorId, string? VendorName, DateTime? ProductionDate, DateTime? DeliveryDate,
        DateTime? ArrivalDate, DateTime? ShipDate, DateTime? PackDate, string? ReceivedLocation,
        string? ReceivedStatus);
    public sealed record InboundTransactionLogRow(long RowNo, string? LotNo, string? PartNo,
        string? WorkDate, string? WorkTime, string? LocationId, decimal Qty, string Status,
        string Direction, string? WorkerId, string? ReasonCode, string? ReasonNote,
        string? Supervisor, decimal? BeforeQty, decimal? DeltaQty, decimal? AfterQty,
        string? BeforeStatus, string? AfterStatus, string? BeforeLocation, string? AfterLocation,
        string? Source, string? Note);
    public sealed record InventoryRow(int InventoryId, string ItemNo, string? ItemName, string LocationId,
        int? LotId, decimal OnHandQty, decimal ReservedQty, DateTime? ExpiryDate,
        string? CarCode = null, string? Unit = null, decimal? MinDays = null, decimal? MinQty = null,
        decimal? MaxDays = null, decimal? MaxQty = null, int LotCount = 0, int LocationCount = 0,
        string? Status = null, string? StatusName = null, DateTime? LastReceivedDate = null);
    public sealed record InventoryLocationRow(int RowNo, string ItemNo, string LocationId, string? LocationName,
        string? WarehouseCode, string? WarehouseName, string? AreaCode, string? AreaName,
        string? ZoneCode, string? ZoneName, string? RackX, string? RackY, string? RackZ, decimal Qty);
    public sealed record LocationRow(string LocationId, string? LocationName, string? Zone, int LineCount, decimal TotalQty,
        string? WarehouseCode = null, string? WarehouseName = null, string? AreaCode = null, string? AreaName = null,
        string? ZoneName = null, string? X = null, string? Y = null, string? Z = null,
        string? PlantCode = null, string? LocationType = null, decimal? Capacity = null);
    public sealed record LocationMapItemRow(string LotNo, string? PartNo, string? PartName, decimal Qty, string? Unit,
        string? InventoryStatus, string? WorkDate, string? WorkTime);
    public sealed record ReleaseScheduleRow(int ReleaseScheduleId, int? WoId, string? WoNumber,
        string ItemNo, string? ItemName, decimal DemandQty, decimal PickedQty, DateTime? RequiredAt, string? Status);
    public sealed record TransactionRow(long TxnId, DateTime TxnTime, string TxnType, string? ItemNo,
        string? LocationId, decimal QtyBefore, decimal Delta, decimal QtyAfter, string? ReasonCode);

    public sealed record ReceiveReq(string LotCode, decimal Qty, string LocationId);
    public sealed record InboundReceiveReq(string Mode, string Barcode, string LocationId);
    public sealed record InboundCancelReq(string Mode, string Barcode);
    public sealed record InboundAdjustReq(string Mode, string Barcode, decimal DeltaQty, string ReasonCode,
        string? ReasonNote, string SupervisorPin);
    public sealed record InboundReceiveResult(bool Success, string Message, InboundScanRow? Row);
    public sealed record AdjustReq(string ItemNo, string LocationId, decimal Delta, string ReasonCode, string? Note);
    public sealed record PickReq(int ReleaseScheduleId, string LotCode, decimal Qty);

    public Task<List<InboundRow>>         WhInboundTodayAsync()    => Get<List<InboundRow>>("/api/wh/inbound/today");
    public async Task<List<InboundScheduleRow>> WhInboundScheduleAsync(int? year = null, int? quarter = null, string? vendorId = null)
    {
        try
        {
            return await QueryWhInboundScheduleDbAsync(year, quarter, vendorId);
        }
        catch
        {
            // Keep the older API route as a fallback while WH-01 reads SIS_TEST directly.
        }

        var args = new List<string>();
        if (year.HasValue) args.Add($"year={year.Value}");
        if (quarter.HasValue) args.Add($"quarter={quarter.Value}");
        if (!string.IsNullOrWhiteSpace(vendorId)) args.Add($"vendorId={Uri.EscapeDataString(vendorId)}");

        var query = args.Count == 0 ? "" : "?" + string.Join("&", args);
        return await Get<List<InboundScheduleRow>>("/api/wh/inbound/schedule" + query);
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
            return await QueryWhLocationsDbAsync();
        }
        catch
        {
            return await Get<List<LocationRow>>("/api/wh/locations");
        }
    }
    public async Task<List<LocationRow>> WhLocationMapAsync()
    {
        try
        {
            return await QueryWhLocationMapDbAsync();
        }
        catch
        {
            return await WhLocationsAsync();
        }
    }
    public async Task<List<LocationMapItemRow>> WhLocationMapItemsAsync(string locationId)
    {
        try
        {
            return await QueryWhLocationMapItemsDbAsync(locationId);
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
            return await QueryWhLocationDbAsync(locationId);
        }
        catch
        {
            var rows = await WhLocationsAsync();
            return rows.FirstOrDefault(r => string.Equals(r.LocationId, locationId.Trim(), StringComparison.OrdinalIgnoreCase));
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
    public Task<List<ReleaseScheduleRow>> WhReleaseScheduleAsync() => Get<List<ReleaseScheduleRow>>("/api/wh/release/schedule");
    public Task<List<TransactionRow>>     WhTransactionsAsync(int days = 7) => Get<List<TransactionRow>>($"/api/wh/transactions?days={days}");

    public Task<HttpResponseMessage> WhReceiveAsync(ReceiveReq body) => Post("/api/wh/inbound/receive", body);
    public async Task<InboundScanRow?> WhScanInboundAsync(string mode, string barcode)
    {
        var proc = string.Equals(mode, "CKD", StringComparison.OrdinalIgnoreCase)
            ? "[SIS_TEST].[PDA_WH002_SCAN_CKD]"
            : "[SIS_TEST].[PDA_WH002_SCAN_LOCAL]";

        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(proc, conn)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.Add("@IN_BARCODE", SqlDbType.NVarChar, 50).Value = barcode.Trim();

        await using var rdr = await cmd.ExecuteReaderAsync();
        return await rdr.ReadAsync() ? ReadInboundScanRow(rdr) : null;
    }

    public async Task<List<InboundTransactionLogRow>> WhInboundTransactionLogsAsync(string? lotNo = null, DateTime? dateFrom = null, DateTime? dateTo = null)
    {
        try
        {
            return await QueryWhInboundTransactionLogsDbAsync(lotNo, dateFrom, dateTo);
        }
        catch
        {
            return new List<InboundTransactionLogRow>();
        }
    }

    public async Task<string?> WhInboundTestBarcodeAsync(string mode)
    {
        var sql = string.Equals(mode, "CKD", StringComparison.OrdinalIgnoreCase)
            ? """
                SELECT TOP (1) C.BOX_BARCODE
                FROM SIS_TEST.AMF1030 C
                LEFT JOIN SIS_TEST.WMS2020 S
                    ON S.CORCD = C.CORCD
                   AND S.BIZCD = C.BIZCD
                   AND S.LOTNO = C.BOX_BARCODE
                ORDER BY CASE WHEN S.LOTNO IS NULL THEN 0 ELSE 1 END, C.BOX_BARCODE;
                """
            : """
                SELECT TOP (1) B.BOX_BARCODE
                FROM SIS_TEST.AMM9011 B
                LEFT JOIN SIS_TEST.WMS2020 S
                    ON S.CORCD = B.CORCD
                   AND S.BIZCD = B.BIZCD
                   AND S.LOTNO = B.BOX_BARCODE
                ORDER BY CASE WHEN S.LOTNO IS NULL THEN 0 ELSE 1 END, B.BOX_BARCODE;
                """;

        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        var value = await cmd.ExecuteScalarAsync();
        return value == DBNull.Value ? null : Convert.ToString(value);
    }

    public async Task<InboundReceiveResult> WhReceiveInboundAsync(InboundReceiveReq body)
    {
        try
        {
            await using var conn = _db.CreateConnection();
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("[SIS_TEST].[PDA_WH002_RECEIVE]", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.Add("@IN_MODE", SqlDbType.NVarChar, 10).Value = body.Mode.Trim();
            cmd.Parameters.Add("@IN_BARCODE", SqlDbType.NVarChar, 50).Value = body.Barcode.Trim();
            cmd.Parameters.Add("@IN_LOCATION_NO", SqlDbType.NVarChar, 30).Value = body.LocationId.Trim();
            cmd.Parameters.Add("@IN_USERID", SqlDbType.NVarChar, 40).Value =
                (object?)_auth.Session?.EmployeeNo ?? "PDA";

            await using var rdr = await cmd.ExecuteReaderAsync();
            var row = await rdr.ReadAsync() ? ReadInboundScanRow(rdr) : null;
            return new InboundReceiveResult(true, "Received", row);
        }
        catch (SqlException ex)
        {
            return new InboundReceiveResult(false, ex.Message, null);
        }
        catch (Exception ex)
        {
            return new InboundReceiveResult(false, ex.Message, null);
        }
    }

    public async Task<InboundReceiveResult> WhMoveInboundLocationAsync(InboundReceiveReq body)
    {
        try
        {
            await using var conn = _db.CreateConnection();
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("[SIS_TEST].[PDA_WH002_MOVE_LOCATION]", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.Add("@IN_MODE", SqlDbType.NVarChar, 10).Value = body.Mode.Trim();
            cmd.Parameters.Add("@IN_BARCODE", SqlDbType.NVarChar, 50).Value = body.Barcode.Trim();
            cmd.Parameters.Add("@IN_LOCATION_NO", SqlDbType.NVarChar, 30).Value = body.LocationId.Trim();
            cmd.Parameters.Add("@IN_USERID", SqlDbType.NVarChar, 40).Value =
                (object?)_auth.Session?.EmployeeNo ?? "PDA";

            await using var rdr = await cmd.ExecuteReaderAsync();
            var row = await rdr.ReadAsync() ? ReadInboundScanRow(rdr) : null;
            return new InboundReceiveResult(true, "Location changed", row);
        }
        catch (SqlException ex)
        {
            return new InboundReceiveResult(false, ex.Message, null);
        }
        catch (Exception ex)
        {
            return new InboundReceiveResult(false, ex.Message, null);
        }
    }

    public async Task<InboundReceiveResult> WhCancelInboundAsync(InboundCancelReq body)
    {
        try
        {
            await using var conn = _db.CreateConnection();
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("[SIS_TEST].[PDA_WH002_CANCEL]", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.Add("@IN_MODE", SqlDbType.NVarChar, 10).Value = body.Mode.Trim();
            cmd.Parameters.Add("@IN_BARCODE", SqlDbType.NVarChar, 50).Value = body.Barcode.Trim();
            cmd.Parameters.Add("@IN_USERID", SqlDbType.NVarChar, 40).Value =
                (object?)_auth.Session?.EmployeeNo ?? "PDA";

            await using var rdr = await cmd.ExecuteReaderAsync();
            var row = await rdr.ReadAsync() ? ReadInboundScanRow(rdr) : null;
            return new InboundReceiveResult(true, "Incoming canceled", row);
        }
        catch (SqlException ex)
        {
            return new InboundReceiveResult(false, ex.Message, null);
        }
        catch (Exception ex)
        {
            return new InboundReceiveResult(false, ex.Message, null);
        }
    }

    public async Task<InboundReceiveResult> WhAdjustInboundQtyAsync(InboundAdjustReq body)
    {
        try
        {
            await using var conn = _db.CreateConnection();
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("[SIS_TEST].[PDA_WH002_ADJUST_QTY]", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.Add("@IN_MODE", SqlDbType.NVarChar, 10).Value = body.Mode.Trim();
            cmd.Parameters.Add("@IN_BARCODE", SqlDbType.NVarChar, 50).Value = body.Barcode.Trim();
            cmd.Parameters.Add("@IN_DELTA_QTY", SqlDbType.Decimal).Value = body.DeltaQty;
            cmd.Parameters["@IN_DELTA_QTY"].Precision = 18;
            cmd.Parameters["@IN_DELTA_QTY"].Scale = 3;
            cmd.Parameters.Add("@IN_REASON_CODE", SqlDbType.NVarChar, 30).Value = body.ReasonCode.Trim();
            cmd.Parameters.Add("@IN_REASON_NOTE", SqlDbType.NVarChar, 500).Value =
                string.IsNullOrWhiteSpace(body.ReasonNote) ? DBNull.Value : body.ReasonNote.Trim();
            cmd.Parameters.Add("@IN_SUPERVISOR_PIN", SqlDbType.NVarChar, 40).Value = body.SupervisorPin.Trim();
            cmd.Parameters.Add("@IN_USERID", SqlDbType.NVarChar, 40).Value =
                (object?)_auth.Session?.EmployeeNo ?? "PDA";

            await using var rdr = await cmd.ExecuteReaderAsync();
            var row = await rdr.ReadAsync() ? ReadInboundScanRow(rdr) : null;
            return new InboundReceiveResult(true, "Quantity adjusted", row);
        }
        catch (SqlException ex)
        {
            return new InboundReceiveResult(false, ex.Message, null);
        }
        catch (Exception ex)
        {
            return new InboundReceiveResult(false, ex.Message, null);
        }
    }

    public Task<HttpResponseMessage> WhAdjustAsync (AdjustReq  body) => Post("/api/wh/inventory/adjust", body);
    public Task<HttpResponseMessage> WhPickAsync   (PickReq    body) => Post("/api/wh/release/pick",      body);

    // ── FG ───────────────────────────────────────────────────────────────
    public sealed record FgStockRow(int StockId, string? StockNumber, string ItemNo, string? ItemName,
        int? LotId, string? CustomerCode, decimal Qty, string? Location, string? Status, DateTime? StockTs);
    public sealed record FgOrderRow(int ShipmentOrderId, string? ShipOrderNumber, string? CustomerCode,
        string? CustomerPo, DateTime? ShipDate, string? CarrierCode, string? DestPlant, string? Status, int LineCount);
    public sealed record FgHistoryRow(int LoadingId, string? LoadingNumber, int? ShipmentOrderId,
        string? ShipOrderNumber, string? CustomerCode, string? LicensePlate, string? DriverName,
        DateTime? DepartureTs, string? OTDStatus);
    public sealed record FgDashboard(int OpenOrders, int ReadyToShip, int InTransit, int DeliveredToday,
        int PendingReturns, decimal StockOnHand);

    public sealed record FgPutAwayReq(int WoId, string ItemNo, decimal Qty, string ActualLoc, int PalletCount);
    public sealed record FgPickReq(int ShipmentOrderId, int StockId, decimal Qty);
    public sealed record FgLoadingReq(int ShipmentOrderId, string LicensePlate, string DriverName, string DockNo, string? SealNo);
    public sealed record FgDeliveryReq(int ShipmentOrderId, int? LoadingId);
    public sealed record FgDayEndReq(string CloseMode, string? Note);
    public sealed record FgReturnReq(string CustomerCode, int? OriginalShipmentOrderId, string ReturnReason, decimal Qty, string ItemNo);

    public Task<List<FgStockRow>>   FgInventoryAsync(string? q = null)
        => Get<List<FgStockRow>>("/api/fg/inventory" + (string.IsNullOrEmpty(q) ? "" : $"?q={Uri.EscapeDataString(q)}"));
    public Task<List<FgOrderRow>>   FgOrdersAsync()  => Get<List<FgOrderRow>>("/api/fg/orders");
    public Task<List<FgHistoryRow>> FgHistoryAsync() => Get<List<FgHistoryRow>>("/api/fg/history");
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
    public Task<HttpResponseMessage> FgPickAsync    (FgPickReq    body)  => Post("/api/fg/pick",     body);
    public Task<HttpResponseMessage> FgLoadingAsync (FgLoadingReq body)  => Post("/api/fg/loading",  body);
    public Task<HttpResponseMessage> FgDeliveryAsync(FgDeliveryReq body) => Post("/api/fg/delivery", body);
    public Task<HttpResponseMessage> FgDayEndAsync  (FgDayEndReq  body)  => Post("/api/fg/dayend",   body);
    public Task<HttpResponseMessage> FgReturnAsync  (FgReturnReq  body)  => Post("/api/fg/return",   body);

    // ── private HTTP helpers ────────────────────────────────────────────
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
    private async Task<List<InboundTransactionLogRow>> QueryWhInboundTransactionLogsDbAsync(string? lotNo, DateTime? dateFrom, DateTime? dateTo)
    {
        var rows = new List<InboundTransactionLogRow>();
        var lotFilter = string.IsNullOrWhiteSpace(lotNo) ? null : lotNo.Trim();
        var fromFilter = dateFrom?.Date ?? DateTime.Today.AddDays(-7);
        var toFilter = dateTo?.Date ?? DateTime.Today;

        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();

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
                WHERE (@IN_LOTNO IS NULL OR H.LOTNO = @IN_LOTNO)
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
                    WHERE (@IN_LOTNO IS NULL OR H.LOTNO = @IN_LOTNO)

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
                    WHERE (@IN_LOTNO IS NULL OR S.LOTNO = @IN_LOTNO)
                      AND NOT EXISTS
                      (
                          SELECT 1
                          FROM SIS_TEST.WMS2010 H
                          WHERE H.LOTNO = S.LOTNO
                      )

                    UNION ALL

                    SELECT
                        3 AS SORT_BUCKET,
                        A.LOTNO,
                        A.PARTNO,
                        A.WORK_DATE AS WDATE,
                        A.WORK_TIME AS WTIME,
                        A.LOCATION_NO,
                        COALESCE(A.AFTER_QTY, 0) AS QTY,
                        N'Adjust' AS STATUS,
                        N'ADJ' AS DIRECTION,
                        A.USER_ID AS WORKER_ID,
                        A.REASON_CODE,
                        A.REASON_NOTE,
                        A.USER_ID AS SUPERVISOR,
                        A.BEFORE_QTY,
                        A.DELTA_QTY,
                        A.AFTER_QTY,
                        N'QTY BEFORE' AS BEFORE_STATUS,
                        N'QTY AFTER' AS AFTER_STATUS,
                        A.LOCATION_NO AS BEFORE_LOCATION,
                        A.LOCATION_NO AS AFTER_LOCATION,
                        N'PDA_WH002_ADJUST_AUDIT' AS SOURCE,
                        CONCAT(A.REASON_CODE, N' ', CASE WHEN A.DELTA_QTY > 0 THEN N'+' ELSE N'' END, CONVERT(nvarchar(40), A.DELTA_QTY)) AS NOTE,
                        COALESCE(A.INSERT_DATE, SYSUTCDATETIME()) AS SORT_TS
                    FROM SIS_TEST.PDA_WH002_ADJUST_AUDIT A
                    WHERE (@IN_LOTNO IS NULL OR A.LOTNO = @IN_LOTNO)
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
        cmd.Parameters.Add("@IN_LOTNO", SqlDbType.NVarChar, 100).Value =
            (object?)lotFilter ?? DBNull.Value;
        cmd.Parameters.Add("@IN_DATE_FROM", SqlDbType.Date).Value = fromFilter;
        cmd.Parameters.Add("@IN_DATE_TO", SqlDbType.Date).Value = toFilter;

        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            rows.Add(ReadInboundTransactionLogRow(rdr));
        }

        return rows;
    }

    private async Task<List<InboundScheduleRow>> QueryWhInboundScheduleDbAsync(int? year, int? quarter, string? vendorId)
    {
        var today = DateTime.Today;
        var queryYear = year ?? today.Year;
        var queryQuarter = quarter ?? ((today.Month - 1) / 3) + 1;

        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = new SqlCommand("[SIS_TEST].[APG_WM40120_INQUERY_VENDER_BACK_ORDER]", conn)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@IN_CORCD", "1000");
        cmd.Parameters.AddWithValue("@IN_BIZCD", "5011");
        cmd.Parameters.AddWithValue("@IN_YYYY", queryYear.ToString());
        cmd.Parameters.AddWithValue("@IN_QUATER", queryQuarter.ToString());
        cmd.Parameters.Add("@IN_VENDCD", SqlDbType.NVarChar, 10).Value =
            string.IsNullOrWhiteSpace(vendorId) ? DBNull.Value : vendorId;
        cmd.Parameters.AddWithValue("@IN_LANG_SET", "EN");

        await using var rdr = await cmd.ExecuteReaderAsync();
        var rows = new List<InboundScheduleRow>();
        var poId = 1;
        while (await rdr.ReadAsync())
        {
            var orderQty = GetDecimal(rdr, "PO_QTY");
            var receivedQty = GetDecimal(rdr, "GRN_QTY");
            var nonDeliverQty = GetDecimal(rdr, "NON_DELI_QTY");
            var dueDate = GetDate(rdr, "PO_DELI_DATE");
            var status = nonDeliverQty <= 0
                ? "Complete"
                : dueDate.HasValue && dueDate.Value.Date < today ? "Late"
                : "In Progress";

            rows.Add(new InboundScheduleRow(
                poId++,
                GetString(rdr, "PONO") ?? "",
                GetInt(rdr, "PONO_SEQ"),
                GetString(rdr, "VENDNM") ?? GetString(rdr, "VENDCD"),
                GetString(rdr, "PARTNO"),
                GetString(rdr, "PARTNM"),
                GetString(rdr, "VINCD"),
                GetString(rdr, "PO_UNIT"),
                orderQty,
                receivedQty,
                nonDeliverQty,
                dueDate,
                GetDate(rdr, "PO_DATE"),
                status));
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

    private async Task<List<LocationMapItemRow>> QueryWhLocationMapItemsDbAsync(string locationId)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();
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
        await using var cmd = new SqlCommand("[SIS_TEST].[PDA_WH03_INVENTORY_STATUS]", conn)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.Add("@IN_CORCD", SqlDbType.NVarChar, 10).Value = WhLocationCorcd;
        cmd.Parameters.Add("@IN_BIZCD", SqlDbType.NVarChar, 10).Value = WhLocationBizcd;
        cmd.Parameters.Add("@IN_Q", SqlDbType.NVarChar, 80).Value =
            string.IsNullOrWhiteSpace(q) ? DBNull.Value : q.Trim();
        cmd.Parameters.Add("@IN_DATE_FROM", SqlDbType.Date).Value =
            dateFrom.HasValue ? dateFrom.Value.Date : (object)DBNull.Value;
        cmd.Parameters.Add("@IN_DATE_TO", SqlDbType.Date).Value =
            dateTo.HasValue ? dateTo.Value.Date : (object)DBNull.Value;
        cmd.Parameters.Add("@IN_LANG_SET", SqlDbType.NVarChar, 10).Value = "EN";

        await using var rdr = await cmd.ExecuteReaderAsync();
        var rows = new List<InventoryRow>();
        while (await rdr.ReadAsync())
        {
            rows.Add(ReadInventoryRow(rdr));
        }

        return rows;
    }

    private async Task<List<InventoryLocationRow>> QueryWhInventoryLocationsDbAsync(string itemNo, DateTime? dateFrom, DateTime? dateTo)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = new SqlCommand("[SIS_TEST].[PDA_WH03_INVENTORY_LOCATIONS]", conn)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.Add("@IN_CORCD", SqlDbType.NVarChar, 10).Value = WhLocationCorcd;
        cmd.Parameters.Add("@IN_BIZCD", SqlDbType.NVarChar, 10).Value = WhLocationBizcd;
        cmd.Parameters.Add("@IN_PARTNO", SqlDbType.NVarChar, 40).Value = itemNo.Trim();
        cmd.Parameters.Add("@IN_DATE_FROM", SqlDbType.Date).Value =
            dateFrom.HasValue ? dateFrom.Value.Date : (object)DBNull.Value;
        cmd.Parameters.Add("@IN_DATE_TO", SqlDbType.Date).Value =
            dateTo.HasValue ? dateTo.Value.Date : (object)DBNull.Value;
        cmd.Parameters.Add("@IN_LANG_SET", SqlDbType.NVarChar, 10).Value = "EN";

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

    private static async Task<bool> TableExistsAsync(SqlConnection conn, string schema, string table)
    {
        await using var cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID(@ObjectName, N'U') IS NULL THEN 0 ELSE 1 END;", conn);
        cmd.Parameters.Add("@ObjectName", SqlDbType.NVarChar, 256).Value = $"{schema}.{table}";
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

    private static InboundTransactionLogRow ReadInboundTransactionLogRow(SqlDataReader rdr)
    {
        return new InboundTransactionLogRow(
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
            GetDate(rdr, "LAST_RECEIVED_DATE"));
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
