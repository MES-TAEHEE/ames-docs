using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

// These are test contracts adapted from SRM_MM22001.Save, not existing SEMS endpoints.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ShipmentRequest
{
    public string REQUEST_ID { get; init; } = "";
    public string CORCD { get; init; } = "";
    public string BIZCD { get; init; } = "";
    public string VENDCD { get; init; } = "";
    public string PURC_ORG { get; init; } = "";
    public string PURC_PO_TYPE { get; init; } = "";
    public string DELI_DATE { get; init; } = "";
    public string ARRIV_DATE { get; init; } = "";
    public string ARRIV_TIME { get; init; } = "";
    public string TRUCK_NO { get; init; } = "";
    public string USER_ID { get; init; } = "";
    public ShipmentItem[] ITEMS { get; init; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ShipmentItem
{
    public string PONO { get; init; } = "";
    public string PONO_SEQ { get; init; } = "";
    public decimal UNIT_PACK_QTY { get; init; }
    public decimal DELI_QTY { get; init; }
    public string VEND_LOTNO { get; init; } = "";
    public string PRDT_DATE { get; init; } = "";
    public string CHANGE_4M { get; init; } = "";
}

public sealed record ShipmentReceipt(
    string REQUEST_ID, string DELI_NOTE, string STATUS, string SAP_STATUS,
    bool SAP_SIMULATED, DateTimeOffset RECEIVED_AT_UTC, int ITEM_COUNT, decimal TOTAL_DELI_QTY);
public sealed record StoredShipment(ShipmentRequest Request, ShipmentReceipt Receipt);

public static class ShipmentApi
{
    // Preserve SRM's upper-case field names on this API only; existing PO responses are unchanged.
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.Strict,
        WriteIndented = true
    };

    public static void MapShipmentEndpoints(this WebApplication app, IShipmentStore store,
        byte[] expectedKeyHash, string company, string business)
    {
        var group = app.MapGroup("/api/shipments").WithTags("출하 수신 (테스트)");
        group.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            http.Response.Headers.CacheControl = "no-store";
            var key = http.Request.Headers["X-API-KEY"];
            if (key.Count != 1 || string.IsNullOrEmpty(key[0]) ||
                !CryptographicOperations.FixedTimeEquals(expectedKeyHash,
                    SHA256.HashData(Encoding.UTF8.GetBytes(key[0]!))))
                return Results.Json(new { error = "유효한 X-API-KEY 헤더가 필요합니다." }, statusCode: 401);
            try { return await next(context); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or Microsoft.Data.SqlClient.SqlException)
            {
                app.Logger.LogError("Shipment storage unavailable ({ErrorType}, SQL {SqlNumber})", ex.GetType().Name,
                    ex is Microsoft.Data.SqlClient.SqlException sql ? sql.Number : 0);
                if (ex is Microsoft.Data.SqlClient.SqlException dbError)
                    app.Logger.LogError("SQL storage error: {Message}", dbError.Message);
                return Results.Json(new { error = "출하 수신 기록을 읽거나 저장할 수 없습니다." }, statusCode: 503);
            }
        });

        group.MapPost("", async (HttpRequest http, CancellationToken cancellationToken) =>
        {
            if (http.ContentLength > 1_048_576)
                return Results.Json(new { error = "요청 본문은 1 MiB 이하여야 합니다." }, statusCode: 413);
            var sizeFeature = http.HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
            if (sizeFeature is { IsReadOnly: false }) sizeFeature.MaxRequestBodySize = 1_048_576;
            if (!http.HasJsonContentType())
                return Results.Json(new { error = "Content-Type: application/json이 필요합니다." }, statusCode: 415);
            ShipmentRequest? request;
            try { request = await JsonSerializer.DeserializeAsync<ShipmentRequest>(http.Body, Json, cancellationToken); }
            catch (JsonException) { return Results.BadRequest(new { error = "JSON 필드명·자료형을 확인하세요." }); }
            catch (BadHttpRequestException ex) when (ex.StatusCode == 413)
            { return Results.Json(new { error = "요청 본문은 1 MiB 이하여야 합니다." }, statusCode: 413); }
            var error = Validate(request, company, business);
            if (error is not null) return Results.BadRequest(new { error });
            var (stored, duplicate, conflict) = await store.Save(request!, cancellationToken);
            if (conflict)
                return Results.Conflict(new { error = "같은 REQUEST_ID에 다른 출하 내용이 이미 저장되어 있습니다." });
            return Results.Json(new { success = true, duplicate, data = stored!.Receipt }, Json);
        }).WithSummary("납품서 Save() 기반 출하 수신 — SAP 성공 모의")
          .Accepts<ShipmentRequest>("application/json");

        group.MapGet("/{requestId}", async (string requestId, CancellationToken cancellationToken) =>
        {
            if (!ValidId(requestId)) return Results.BadRequest(new { error = "REQUEST_ID는 영문·숫자·하이픈·밑줄 1~100자입니다." });
            var stored = await store.Get(requestId, cancellationToken);
            return stored is null ? Results.NotFound(new { error = "출하 수신 기록이 없습니다." })
                : Results.Json(new { success = true, data = stored.Receipt, request = stored.Request }, Json);
        }).WithSummary("REQUEST_ID로 출하 수신 결과 및 요청 조회");
    }

    private static bool ValidId(string? value) => value is { Length: > 0 and <= 100 }
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static bool ValidDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static string? Validate(ShipmentRequest? r, string company, string business)
    {
        if (r is null) return "요청 본문이 필요합니다.";
        if (!ValidId(r.REQUEST_ID)) return "REQUEST_ID는 영문·숫자·하이픈·밑줄 1~100자입니다.";
        if (r.CORCD != company || r.BIZCD != business) return "테스트 회사/사업장 범위가 일치하지 않습니다.";
        foreach (var (name, value) in new[] { ("VENDCD", r.VENDCD), ("PURC_ORG", r.PURC_ORG),
                     ("PURC_PO_TYPE", r.PURC_PO_TYPE), ("USER_ID", r.USER_ID) })
            if (string.IsNullOrWhiteSpace(value) || value.Length > 100) return $"{name}은 1~100자 필수값입니다.";
        if (r.TRUCK_NO is null || r.TRUCK_NO.Length > 100) return "TRUCK_NO는 100자 이하여야 합니다.";
        if (!ValidDate(r.DELI_DATE, out var delivery) || !ValidDate(r.ARRIV_DATE, out var arrival) || arrival < delivery)
            return "DELI_DATE/ARRIV_DATE는 yyyy-MM-dd이며 도착일은 납품일보다 빠를 수 없습니다.";
        if (r.ARRIV_TIME is not { Length: 4 } || !r.ARRIV_TIME.All(char.IsAsciiDigit) ||
            !TimeOnly.TryParseExact(r.ARRIV_TIME, "HHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return "ARRIV_TIME은 HHmm 형식(0000~2359)입니다.";
        if (r.ITEMS is null || r.ITEMS.Length is < 1 or > 1000) return "ITEMS는 1~1000건이어야 합니다.";
        foreach (var item in r.ITEMS)
        {
            if (item is null) return "ITEMS에 null 항목을 넣을 수 없습니다.";
            if (string.IsNullOrWhiteSpace(item.PONO) || item.PONO.Length > 100 ||
                string.IsNullOrWhiteSpace(item.PONO_SEQ) || item.PONO_SEQ.Length > 100)
                return "각 항목의 PONO/PONO_SEQ는 1~100자 필수값입니다.";
            if (item.UNIT_PACK_QTY <= 0 || item.DELI_QTY <= 0 ||
                item.UNIT_PACK_QTY > 1_000_000_000m || item.DELI_QTY > 1_000_000_000m)
                return "UNIT_PACK_QTY/DELI_QTY는 0 초과 1,000,000,000 이하여야 합니다.";
            if (decimal.Round(item.UNIT_PACK_QTY, 6) != item.UNIT_PACK_QTY || decimal.Round(item.DELI_QTY, 6) != item.DELI_QTY)
                return "수량은 소수점 이하 6자리까지 지원합니다.";
            if (!(r.CORCD == "1000" && r.PURC_ORG == "1A1100") && item.DELI_QTY > item.UNIT_PACK_QTY * 1000)
                return "납품수량/포장수량은 1000 이하여야 합니다.";
            if (!ValidDate(item.PRDT_DATE, out _)) return "각 항목의 PRDT_DATE는 yyyy-MM-dd 필수값입니다.";
            if (item.VEND_LOTNO is null || item.VEND_LOTNO.Length > 100 || item.CHANGE_4M is null || item.CHANGE_4M.Length > 100)
                return "VEND_LOTNO/CHANGE_4M은 100자 이하 문자열이어야 합니다.";
        }
        return null;
    }
}

// One immutable file per request. Exclusive directory ownership prevents two API processes
// from acknowledging different deliveries for the same key. Atomic rename precedes success.
public interface IShipmentStore : IDisposable
{
    Task<StoredShipment?> Get(string requestId, CancellationToken ct);
    Task<(StoredShipment? Stored, bool Duplicate, bool Conflict)> Save(ShipmentRequest request, CancellationToken ct);
}

public sealed class ShipmentStore : IShipmentStore
{
    private readonly string directory;
    private readonly FileStream ownership;
    private readonly SemaphoreSlim gate = new(1, 1);

    public ShipmentStore(string directory)
    {
        this.directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(this.directory);
        ownership = new FileStream(Path.Combine(this.directory, ".lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
    }

    private string FileName(string requestId) => Path.Combine(directory,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requestId))) + ".json");

    public async Task<StoredShipment?> Get(string requestId, CancellationToken ct)
    {
        var path = FileName(requestId);
        if (!File.Exists(path)) return null;
        await using var file = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<StoredShipment>(file, ShipmentApi.Json, ct)
            ?? throw new InvalidDataException("Invalid shipment record.");
    }

    public async Task<(StoredShipment? Stored, bool Duplicate, bool Conflict)> Save(ShipmentRequest request, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var existing = await Get(request.REQUEST_ID, ct);
            if (existing is not null)
            {
                var same = JsonSerializer.Serialize(existing.Request, ShipmentApi.Json)
                    == JsonSerializer.Serialize(request, ShipmentApi.Json);
                return (existing, same, !same);
            }
            var receipt = new ShipmentReceipt(request.REQUEST_ID, "TEST-" + Guid.NewGuid().ToString("N"),
                "COMPLETED", request.PURC_PO_TYPE == "1K10" ? "NOT_REQUIRED" : "SIMULATED_SUCCESS",
                true, DateTimeOffset.UtcNow, request.ITEMS.Length, request.ITEMS.Sum(i => i.DELI_QTY));
            var stored = new StoredShipment(request, receipt);
            var destination = FileName(request.REQUEST_ID);
            var temporary = destination + ".tmp";
            try
            {
                await using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await JsonSerializer.SerializeAsync(file, stored, ShipmentApi.Json, ct);
                    file.Flush(flushToDisk: true);
                }
                File.Move(temporary, destination);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return (stored, false, false);
        }
        finally { gate.Release(); }
    }

    public void Dispose() { ownership.Dispose(); gate.Dispose(); }
}
