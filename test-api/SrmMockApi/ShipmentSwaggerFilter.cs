using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

public sealed class ShipmentSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type != typeof(ShipmentRequest) && context.Type != typeof(ShipmentItem)) return;
        schema.Properties = schema.Properties.ToDictionary(p => p.Key.ToUpperInvariant(), p => p.Value);
        schema.AdditionalPropertiesAllowed = false;
        schema.Required = (context.Type == typeof(ShipmentRequest)
            ? new[] { "REQUEST_ID", "CORCD", "BIZCD", "VENDCD", "PURC_ORG", "PURC_PO_TYPE", "DELI_DATE", "ARRIV_DATE", "ARRIV_TIME", "USER_ID", "ITEMS" }
            : new[] { "PONO", "PONO_SEQ", "UNIT_PACK_QTY", "DELI_QTY", "PRDT_DATE" }).ToHashSet();
    }
}

public sealed class ShipmentSwaggerFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.ApiDescription.RelativePath?.StartsWith("api/shipments") != true) return;
        operation.Description = "SRM_MM22001.Save()를 참고한 신규 테스트 규격입니다. 실제 SAP 호출, 발주 잔량 검증, 전표 출력은 수행하지 않습니다. 기본 저장소는 TEST_SRM_Shipment/TEST_SRM_ShipmentItem DB 테이블입니다.";
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "X-API-KEY", In = ParameterLocation.Header, Required = true,
            Description = "기존 발주 조회와 동일한 SRM_TEST_API_KEY", Schema = new() { Type = "string" }
        });
        operation.Responses["400"] = new() { Description = "잘못된 JSON 또는 입력값" };
        operation.Responses["401"] = new() { Description = "API 키 누락 또는 불일치" };
        operation.Responses["503"] = new() { Description = "수신 기록 저장소 사용 불가" };
        if (context.ApiDescription.HttpMethod == "POST")
        {
            operation.Responses["200"] = new() { Description = "모의 처리 완료. success, duplicate, data(REQUEST_ID, DELI_NOTE, STATUS, SAP_STATUS, SAP_SIMULATED, RECEIVED_AT_UTC, ITEM_COUNT, TOTAL_DELI_QTY) 반환" };
            operation.Responses["409"] = new() { Description = "동일 REQUEST_ID로 다른 내용 전송" };
            operation.Responses["413"] = new() { Description = "요청 본문 1 MiB 초과" };
            operation.Responses["415"] = new() { Description = "application/json 필요" };
            operation.RequestBody.Content["application/json"].Example = OpenApiAnyFactory.CreateFromJson("""
                {"REQUEST_ID":"SHIP-TEST-001","CORCD":"7700","BIZCD":"7710","VENDCD":"310471",
                 "PURC_ORG":"1A7700","PURC_PO_TYPE":"1KMA","DELI_DATE":"2026-09-17","ARRIV_DATE":"2026-09-18",
                 "ARRIV_TIME":"0930","TRUCK_NO":"TEST-TRUCK","USER_ID":"AMES_TEST",
                 "ITEMS":[{"PONO":"TEST-PO-001","PONO_SEQ":"00010","UNIT_PACK_QTY":10,"DELI_QTY":100,
                 "VEND_LOTNO":"LOT-TEST-001","PRDT_DATE":"2026-09-16","CHANGE_4M":""}]}
                """);
        }
        else operation.Responses["404"] = new() { Description = "REQUEST_ID에 해당하는 수신 기록 없음" };
    }
}
