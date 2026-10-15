using System.Text.Json;

namespace AMES.Data.Services;

/// <summary>
/// 보전 작업지시 사용 부품(MNT_WorkOrder.PartsUsedJSON) 규칙의 정본(10-15 사용자 결정 (b) — 차감은 PDA 예비품 출고, 웹은 기록만).
/// 완료·종료 때 PDA 가 그 작업지시로 출고한 이력(취소분 제외)을 합쳐 스냅샷한다.
/// 새 형식 = 줄 배열 [{SparePartNo, Qty, PartNo, PartName}] (PartNo·PartName 은 기록 시점 스냅샷).
/// 옛 형식 = 자유 입력 문자열 배열 ["VLV-12","SEAL-3"] — 표시만 한다. 테스트: MwoPartsUsedTests.
/// </summary>
public static class MwoPartsUsed
{
    public sealed record Line(string SparePartNo, int Qty, string? PartNo = null, string? PartName = null);

    static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>같은 부품 줄을 합치고(대소문자·공백 무시, 처음 본 번호·이름 유지) 부품번호 순으로. 부품번호가 비었거나 수량이 0 이하인 줄은 버린다.</summary>
    public static List<Line> Merge(IEnumerable<Line>? lines)
    {
        var merged = new Dictionary<string, Line>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in lines ?? [])
        {
            var no = l.SparePartNo?.Trim() ?? "";
            if (no.Length == 0 || l.Qty <= 0) continue;
            merged[no] = merged.TryGetValue(no, out var prev) ? prev with { Qty = prev.Qty + l.Qty } : l with { SparePartNo = no };
        }
        return merged.Values.OrderBy(l => l.SparePartNo, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string? Serialize(IReadOnlyCollection<Line> lines) =>
        lines.Count == 0 ? null : JsonSerializer.Serialize(lines, Json);

    /// <summary>새 형식 줄 목록. 옛 문자열 배열·비어 있음·JSON 아님 → 빈 목록.</summary>
    public static List<Line> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith("[{")) return [];
        try { return JsonSerializer.Deserialize<List<Line>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    /// <summary>표시 문구 — 새 형식 "PartNo 이름 × 수량", 옛 형식은 문자열 그대로, JSON 이 아니면 원문.</summary>
    public static string Describe(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "";
        var lines = Parse(json);
        if (lines.Count > 0) return Describe(lines);
        try { return string.Join(", ", JsonSerializer.Deserialize<string[]>(json) ?? []); }
        catch (JsonException) { return json; }
    }

    public static string Describe(IEnumerable<Line> lines) =>
        string.Join(", ", lines.Select(l =>
            $"{(string.IsNullOrEmpty(l.PartNo) ? l.SparePartNo : l.PartNo)}{(string.IsNullOrEmpty(l.PartName) ? "" : " " + l.PartName)} × {l.Qty}"));
}
