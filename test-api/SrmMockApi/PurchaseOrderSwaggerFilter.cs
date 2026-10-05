using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

public sealed class PurchaseOrderSwaggerFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var path = context.ApiDescription.RelativePath;
        if (path != "Service/WEBSRV_INQUERY_PO.ashx" && path != "api/purchase-orders") return;
        operation.Summary = "발주 정보 조회";
        operation.Description = "발주일 기준 조회. 회사/사업장 7700/7710 샘플이며, 2026-08-01~2026-09-30은 426건을 반환합니다. APIKEY에 전달받은 키를 입력하세요.";
        var parameters = new (string Name, string Description, string? Default)[]
        {
            ("APIKEY", "발급받은 API 키 (필수)", null),
            ("CORCD", "회사 코드", "7700"),
            ("BIZCD", "사업장 코드", "7710"),
            ("PURC_ORG", "구매조직", "1A7700"),
            ("VENDCD", "거래처 코드", "310471"),
            ("PO_DATE_BEG", "발주 시작일 (포함, yyyy-MM-dd)", "2026-08-01"),
            ("PO_DATE_TO", "발주 종료일 (포함, yyyy-MM-dd)", "2026-09-30")
        };
        operation.Parameters = parameters.Select(p => new OpenApiParameter
        {
            Name = p.Name,
            In = ParameterLocation.Query,
            Required = true,
            Description = p.Description,
            Schema = new OpenApiSchema
            {
                Type = "string",
                Default = p.Default is null ? null : new OpenApiString(p.Default)
            }
        }).ToList();
        operation.Responses["200"] = new OpenApiResponse
        {
            Description = "발주 JSON 배열 (건별 45개 항목), 결과가 없으면 []",
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new() { Schema = new() { Type = "array", Items = new() { Type = "object", AdditionalPropertiesAllowed = true } } }
            }
        };
        operation.Responses["400"] = new() { Description = "필수 조건 누락 또는 잘못된 검색 조건" };
        operation.Responses["401"] = new() { Description = "API 키 누락 또는 불일치" };
        operation.Responses["503"] = new() { Description = "테스트 DB 조회 불가" };
    }
}
