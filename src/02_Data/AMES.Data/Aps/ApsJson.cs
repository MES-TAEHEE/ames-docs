using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AMES.Data.Aps;

/// 골든 비교기·PP_ApsRun JSON 컬럼 3개가 같이 쓰는 직렬화 옵션 (REBUILD JsonOptions.Default 와 동일).
public static class ApsJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // 원본 응답 픽스처 어디에도 null 값이 없다 — null 필드는 생략한다.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // 한글을 \uXXXX 로 바꾸지 않는다
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        WriteIndented = false,
    };
}
