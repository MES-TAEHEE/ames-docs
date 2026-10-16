using System.Globalization;
using System.Text.Json;
using AMES.Data.Repositories;

namespace AMES.Data.Services.PoSync;

public sealed record SrmPoMapResult(IReadOnlyList<PpRepository.CustomerOrderImportRow> Rows, int Skipped);

/// <summary>
/// INQUERY 커서 행 → <see cref="PpRepository.UpsertCustomerOrders"/> 입력. Excel 경로(Web PoListParser)와 같은 규칙:
/// PONO 마지막 '-' 뒤가 정수면 SoLineNo, 음수 발주수량 제외. 자동화라 삭제표시(LOEKZ='L')·길이 초과도 조용히 제외하고 센다.
/// 납품완료(ELIKZ='X')는 납품수량 갱신을 위해 포함한다.
/// </summary>
public static class SrmPoMapper
{
    private const int MaxCodeLen = 20;   // PP_CustomerOrder.SoNumber / ItemNo VARCHAR(20)

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
    };

    public static SrmPoMapResult Map(IReadOnlyList<SrmPoRow> rows)
    {
        var byKey = new Dictionary<(string, int?), PpRepository.CustomerOrderImportRow>();
        var order = new List<(string, int?)>();
        int skipped = 0;

        foreach (var r in rows)
        {
            var (so, line) = SplitPoNo(r.PONO ?? "");
            var item = (r.PARTNO ?? "").Trim();
            var qty  = r.PO_QTY ?? 0m;

            if (so.Length == 0 || item.Length == 0 || qty < 0
                || string.Equals(r.LOEKZ?.Trim(), "L", StringComparison.OrdinalIgnoreCase)
                || so.Length > MaxCodeLen || item.Length > MaxCodeLen)
            {
                skipped++;
                continue;
            }

            var key = (so, line);
            if (!byKey.ContainsKey(key)) order.Add(key);
            byKey[key] = new PpRepository.CustomerOrderImportRow(
                so, line, item, qty, r.DELI_QTY ?? 0m,
                ParseDate(r.PO_DATE), ParseDate(r.PO_DELI_DATE));
        }

        return new SrmPoMapResult(order.Select(k => byKey[k]).ToList(), skipped);
    }

    public static (string SoNumber, int? SoLineNo) SplitPoNo(string s)
    {
        s = s.Trim();
        if (s.Length == 0) return ("", null);
        int dash = s.LastIndexOf('-');
        if (dash <= 0 || dash == s.Length - 1) return (s, null);
        var head = s[..dash];
        var tail = s[(dash + 1)..];
        return int.TryParse(tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? (head, n) : (s, null);
    }

    /// <summary>응답 루트가 배열이면 그대로, 객체면 첫 배열형 프로퍼티(OUT_CURSOR/data/rows …)를 행 목록으로 본다.</summary>
    public static IReadOnlyList<SrmPoRow> ParseRows(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new FormatException("응답이 JSON 이 아닙니다: " + ex.Message, ex); }

        using (doc)
        {
            var root = doc.RootElement;
            JsonElement arr;
            if (root.ValueKind == JsonValueKind.Array) arr = root;
            else if (root.ValueKind == JsonValueKind.Object
                     && root.EnumerateObject().FirstOrDefault(p => p.Value.ValueKind == JsonValueKind.Array) is { Value.ValueKind: JsonValueKind.Array } p)
                arr = p.Value;
            else throw new FormatException("응답에서 행 배열을 찾지 못했습니다.");

            return arr.Deserialize<List<SrmPoRow>>(JsonOpts) ?? [];
        }
    }

    private static DateTime? ParseDate(string? s)
        => DateTime.TryParseExact(s?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;
}
