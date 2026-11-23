using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace AMES.Data.Services.SapIf;

/// <summary>
/// MES → B1 송신 한 종류의 EOS_IF 테이블 정의(정의서 IF-T01~T09, 11-13 EOS_IF 실제 구조 기준).
/// 헤더 업무 컬럼 <paramref name="HdrCols"/> 와 라인 업무 컬럼 <paramref name="LineCols"/> 만 쓴다 —
/// 제어 컬럼(IF_ACTION·IF_STATUS·MES_TS)과 웅진이 되쓰는 컬럼(B1_DOCENTRY 등)은 Sender 가 채우거나 건드리지 않는다.
/// </summary>
/// <param name="KeyWithAction">EOS_IF 의 키 중복 판정이 (MES 키, IF_ACTION) 인가(생산오더만 — 나머지는 MES 키 단독 UNIQUE).</param>
public sealed record SapOutSpec(
    string IfId, string Name, string HdrTable, string KeyCol, bool KeyWithAction, string[] HdrCols,
    string? LineTable = null, string[]? LineCols = null);

public static class SapOutSpecs
{
    public static readonly IReadOnlyList<SapOutSpec> All =
    [
        new("IF-T01", "생산오더", "IF_TX_PRODORDER", "MES_WO_NO", true,
            ["MES_WO_NO", "ItemNo", "PlannedQty", "WhsCode", "StartDate", "DueDate", "PostDate", "LineID", "MES_SO_NO", "Remarks", "CloseDate"]),
        new("IF-T02", "자재 투입", "IF_TX_PRODISSUE", "MES_ISSUE_NO", false,
            ["MES_ISSUE_NO", "MES_WO_NO", "PostDate", "Remarks"],
            "IF_TX_PRODISSUE_LINE", ["CompItemNo", "Qty", "WhsCode", "ExtraFlag", "LineRemarks"]),
        new("IF-T03", "생산 실적", "IF_TX_PRODRECEIPT", "MES_RESULT_NO", false,
            ["MES_RESULT_NO", "MES_WO_NO", "ItemNo", "CompleteQty", "RejectQty", "WhsCode", "RejectWhsCode", "PostDate", "WorkDate", "Shift", "LineID", "DefectCode", "Remarks"]),
        new("IF-T05", "PO 입고", "IF_TX_GRPO_HDR", "MES_GR_NO", false,
            ["MES_GR_NO", "VendorID", "PostDate", "VendorRefNo", "Remarks"],
            "IF_TX_GRPO_LINE", ["B1_DOCENTRY", "LINENUM", "ItemNo", "Qty", "WhsCode", "LineRemarks"]),
        new("IF-T06", "재고 이동", "IF_TX_TRANSFER_HDR", "MES_TR_NO", false,
            ["MES_TR_NO", "PostDate", "MoveType", "FromWhsCode", "ToWhsCode", "MES_WO_NO", "Remarks"],
            "IF_TX_TRANSFER_LINE", ["ItemNo", "Qty", "FromWhsCode", "ToWhsCode", "DefectCode"]),
        new("IF-T07", "출하", "IF_TX_DELIVERY_HDR", "MES_SHIP_NO", false,
            ["MES_SHIP_NO", "CustomerID", "ShipToCode", "PostDate", "CustPONo", "Carrier", "Remarks"],
            "IF_TX_DELIVERY_LINE", ["ItemNo", "Qty", "WhsCode", "CustItemNo", "PalletCount"]),
        new("IF-T09", "재고 조정", "IF_TX_ADJUST", "MES_ADJ_NO", false,
            ["MES_ADJ_NO", "ItemNo", "WhsCode", "AdjQty", "ReasonCode", "PostDate", "ApprovedBy", "MES_LOCATION", "Remarks"]),
    ];

    public static SapOutSpec? Find(string ifId)
        => All.FirstOrDefault(s => string.Equals(s.IfId, ifId, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// 아웃박스 <c>PayloadJson</c> = <c>{"hdr":{컬럼:값…},"lines":[{컬럼:값…}…]}</c>. 컬럼 이름은 EOS_IF 컬럼 그대로이며
/// 명세에 없는 컬럼은 거부한다(제어·결과 컬럼을 JSON 으로 넣을 수 없게). 날짜 컬럼(이름이 Date 로 끝남)은 <c>yyyy-MM-dd</c> 문자열.
/// </summary>
public sealed record SapOutPayload(
    IReadOnlyDictionary<string, object?> Hdr, IReadOnlyList<IReadOnlyDictionary<string, object?>> Lines)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static SapOutPayload Parse(SapOutSpec spec, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("hdr", out var h) || h.ValueKind != JsonValueKind.Object)
            throw new FormatException("PayloadJson 에 hdr 객체가 없습니다.");

        var hdr = ReadObject(h, spec.HdrCols, spec.HdrTable);
        if (!hdr.TryGetValue(spec.KeyCol, out var key) || key is null || string.IsNullOrWhiteSpace(key.ToString()))
            throw new FormatException($"hdr.{spec.KeyCol} 값이 없습니다.");

        var lines = new List<IReadOnlyDictionary<string, object?>>();
        if (root.TryGetProperty("lines", out var ls) && ls.ValueKind == JsonValueKind.Array)
        {
            if (ls.GetArrayLength() > 0 && spec.LineTable is null)
                throw new FormatException($"{spec.IfId} 는 라인이 없는 인터페이스입니다.");
            foreach (var l in ls.EnumerateArray())
                lines.Add(ReadObject(l, spec.LineCols ?? [], spec.LineTable ?? ""));
        }
        return new SapOutPayload(hdr, lines);
    }

    private static Dictionary<string, object?> ReadObject(JsonElement obj, string[] allowed, string table)
    {
        var d = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var p in obj.EnumerateObject())
        {
            if (!allowed.Contains(p.Name, StringComparer.Ordinal))
                throw new FormatException($"{table} 에 쓸 수 없는 컬럼입니다: {p.Name}");
            d[p.Name] = Convert(p.Name, p.Value);
        }
        return d;
    }

    private static object? Convert(string col, JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => e.TryGetDecimal(out var m) ? m : throw new FormatException($"{col} 숫자 범위 초과"),
        JsonValueKind.String => col.EndsWith("Date", StringComparison.Ordinal)
            ? DateTime.TryParseExact(e.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                ? dt : throw new FormatException($"{col} 날짜 형식은 yyyy-MM-dd 여야 합니다: {e.GetString()}")
            : e.GetString(),
        _ => throw new FormatException($"{col} 지원하지 않는 JSON 형식({e.ValueKind})"),
    };

    /// <summary>헤더·라인 값을 JSON 으로(날짜는 yyyy-MM-dd). 아웃박스에 넣을 때 쓴다.</summary>
    public static string ToJson(IDictionary<string, object?> hdr, IEnumerable<IDictionary<string, object?>>? lines = null)
    {
        static object? N(object? v) => v switch
        {
            DateTime d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => v,
        };
        var h = hdr.ToDictionary(kv => kv.Key, kv => N(kv.Value));
        var l = (lines ?? []).Select(x => x.ToDictionary(kv => kv.Key, kv => N(kv.Value))).ToList();
        return JsonSerializer.Serialize(new { hdr = h, lines = l }, JsonOptions);
    }
}
