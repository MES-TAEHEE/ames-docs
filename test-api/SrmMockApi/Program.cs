using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;

var builder = WebApplication.CreateBuilder(args);
// This standalone test service must not depend on Windows Event Log write privileges.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "SRM 발주·출하 테스트 API", Version = "v1" });
    options.OperationFilter<PurchaseOrderSwaggerFilter>();
    options.OperationFilter<ShipmentSwaggerFilter>();
    options.SchemaFilter<ShipmentSchemaFilter>();
});
// ASP.NET request logs can contain APIKEY in the URL; do not log request URLs.
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]))
    builder.WebHost.UseUrls("http://localhost:5220");
var apiKey = builder.Configuration["SRM_TEST_API_KEY"];
var connectionString = builder.Configuration.GetConnectionString("SrmTestDatabase");
if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("Set SRM_TEST_API_KEY and ConnectionStrings__SrmTestDatabase before starting.");
var expectedKeyHash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
var company = builder.Configuration["SRM_TEST_CORCD"] ?? "7700";
var business = builder.Configuration["SRM_TEST_BIZCD"] ?? "7710";
var fields = "VEND,VENDNM,VINCD,VINNM,PO_DATE,PONO,PO_DELI_DATE,PARTNO,PARTNM,STR_LOC,STR_LOCNM,PO_UNIT,UNIT_PACK_QTY,PO_QTY,ELIKZ,DELI_CMP_CHK,PURC_ORG,PURC_ORGNM,PURC_PO_TYPE,PURC_PO_TYPENM,PURC_GRP,PURC_GRPNM,MAT_GRP,CUSTCD,CUSTNM,CUST_PONO,PMI,SD_PONO,SD_DELI_DATE,FTA_CERTI,UMSON,MAT_GRPNM,NATIONCD,RETPO,PSTYP,LOEKZ,AAC,UPDATE_DATE,DELI_QTY,DEF_QTY,ARRIV_QTY,GRN_QTY,REMAINQTY,CHK,BSTZD_NM".Split(',');
var selectColumns = string.Join(",", fields.Select(field => $"[{field}]"));
var required = new[] { "APIKEY", "CORCD", "BIZCD", "PURC_ORG", "VENDCD", "PO_DATE_BEG", "PO_DATE_TO" };
var app = builder.Build();
using IShipmentStore shipmentStore = builder.Configuration["SRM_TEST_SHIPMENT_STORAGE"] == "File"
    ? new ShipmentStore(builder.Configuration["SRM_TEST_SHIPMENT_PATH"]
        ?? Path.Combine(app.Environment.ContentRootPath, "App_Data", "shipments"))
    : new SqlShipmentStore(connectionString);
app.UseSwagger();
app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "SRM 발주 테스트 API v1"));

app.MapGet("/", () => Results.Content("""
<!doctype html><html lang="ko"><meta charset="utf-8"><title>SRM 테스트 API</title>
<body><h1>SRM 발주·출하 테스트 API</h1><p><a href="/swagger">Swagger 열기</a></p>
<p>POST /api/shipments — 납품서 Save() 기반 출하 수신 모의 API (X-API-KEY 헤더)</p>
<p>GET /api/shipments/{requestId} — 저장된 출하 결과 조회. 실제 SAP 전송은 하지 않습니다.</p>
<p>GET /Service/WEBSRV_INQUERY_PO.ashx</p>
<p>필수 조건: APIKEY, CORCD, BIZCD, PURC_ORG, VENDCD, PO_DATE_BEG, PO_DATE_TO</p>
<p>테스트 DB에서 발주일 기준으로 조회합니다. 결과는 원본 형식의 JSON 배열입니다.</p>
<p>현재 샘플의 회사/사업장: 7700 / 7710. API 키는 호출 프로그램에서 지정하세요.</p>
<p><a href="/api/health">서버 및 DB 상태 확인</a></p></body></html>
""", "text/html; charset=utf-8"));

app.MapGet("/api/health", async (CancellationToken cancellationToken) =>
{
    try
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT COUNT(*) FROM dbo.TEST_SRM_PurchaseOrderResponse", connection);
        var count = (int)(await command.ExecuteScalarAsync(cancellationToken))!;
        return Results.Ok(new { status = "ok", mode = "database", records = count });
    }
    catch (SqlException)
    {
        return Results.Json(new { status = "unavailable" }, statusCode: 503);
    }
});

async Task<IResult> QueryOrders(HttpContext context, CancellationToken cancellationToken)
{
    context.Response.Headers.CacheControl = "no-store";
    var query = context.Request.Query;
    var suppliedKey = query["APIKEY"];
    if (suppliedKey.Count != 1 || string.IsNullOrEmpty(suppliedKey[0]) ||
        !CryptographicOperations.FixedTimeEquals(expectedKeyHash, SHA256.HashData(Encoding.UTF8.GetBytes(suppliedKey[0]!))))
        return Results.Json(new { error = "유효한 APIKEY가 필요합니다." }, statusCode: 401);

    if (query.Keys.Any(key => !required.Contains(key, StringComparer.OrdinalIgnoreCase)))
        return Results.BadRequest(new { error = "지원하지 않는 검색 매개변수가 있습니다." });
    foreach (var field in required)
        if (query[field].Count != 1 || string.IsNullOrWhiteSpace(query[field][0]))
            return Results.BadRequest(new { error = $"{field} 값을 한 개 지정해야 합니다." });

    foreach (var field in new[] { "CORCD", "BIZCD", "PURC_ORG", "VENDCD" })
        if (query[field][0]!.Length > 256)
            return Results.BadRequest(new { error = $"{field} 값이 너무 깁니다." });

    if (!DateOnly.TryParseExact(query["PO_DATE_BEG"][0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var begin) ||
        !DateOnly.TryParseExact(query["PO_DATE_TO"][0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) || begin > end)
        return Results.BadRequest(new { error = "날짜는 yyyy-MM-dd 형식이어야 하며 시작일이 종료일보다 늦을 수 없습니다." });

    // The supplied fixture has no CORCD/BIZCD columns. It belongs to this configured scope.
    if (query["CORCD"][0] != company || query["BIZCD"][0] != business)
        return Results.Json(Array.Empty<object>());

    try
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand($"""
            SELECT {selectColumns}
            FROM dbo.TEST_SRM_PurchaseOrderResponse
            WHERE PURC_ORG = @PurchasingOrganization AND VEND = @Vendor
              AND TRY_CONVERT(date, PO_DATE, 23) BETWEEN @BeginDate AND @EndDate
            ORDER BY SourceRowNo
            """, connection);
        command.Parameters.Add("@PurchasingOrganization", SqlDbType.NVarChar, 256).Value = query["PURC_ORG"][0]!;
        command.Parameters.Add("@Vendor", SqlDbType.NVarChar, 256).Value = query["VENDCD"][0]!;
        command.Parameters.Add("@BeginDate", SqlDbType.Date).Value = begin.ToDateTime(TimeOnly.MinValue);
        command.Parameters.Add("@EndDate", SqlDbType.Date).Value = end.ToDateTime(TimeOnly.MinValue);
        var rows = new List<Dictionary<string, object?>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var index = 0; index < fields.Length; index++)
                row[fields[index]] = reader.IsDBNull(index) ? null : reader.GetValue(index);
            rows.Add(row);
        }
        return Results.Json(rows);
    }
    catch (SqlException)
    {
        return Results.Json(new { error = "테스트 DB를 조회할 수 없습니다." }, statusCode: 503);
    }
}

app.MapGet("/Service/WEBSRV_INQUERY_PO.ashx", QueryOrders);
// Keep the original test route as an alias, with the same key and query requirements.
app.MapGet("/api/purchase-orders", QueryOrders);
app.MapShipmentEndpoints(shipmentStore, expectedKeyHash, company, business);
app.Run();

