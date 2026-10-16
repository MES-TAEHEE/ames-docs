using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AMES.Data.Repositories;

namespace AMES.Data.Services.DemandPlan;

/// <summary>SRM MM30011(일별 구매계획, JIT) 응답 봉투. success/dates 는 `Parse` 가 확인해 넘긴다.</summary>
public sealed class SrmMipResponse
{
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("plan_date")] public string? PlanDate { get; set; }
    [JsonPropertyName("dates")] public Dictionary<string, string>? Dates { get; set; }
    [JsonPropertyName("count")] public int? Count { get; set; }
    [JsonPropertyName("data")] public List<SrmMipRow>? Data { get; set; }
}

/// <summary>응답 데이터 행 1개. 날짜별 예정량은 `D{k}_PR_QTY`/`D{k}_PO_QTY` 로 오므로 확장 데이터로 받는다.</summary>
public sealed class SrmMipRow
{
    public string? PARTNO { get; set; }
    public string? PARTNM { get; set; }
    public string? UNIT { get; set; }
    public decimal? PACK_QTY { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed record SrmMipMapResult(IReadOnlyList<PpRepository.DemandPlanCell> Cells, DateOnly From, DateOnly To, int Rows, int Skipped, List<string> Warnings);

/// <summary>
/// MM30011 응답 → <see cref="PpRepository.DemandPlanCell"/>. dates 봉투(`D{k}` → yyyy-MM-dd)가 날짜를 정하므로
/// PO Sync 의 PONO 파싱 같은 앵커 규칙이 없다. PR_QTY 만 수요(ScheduledQty)로 쓰고 PO_QTY 는 참고용으로 같이 싣는다.
/// </summary>
public static class SrmMipMapper
{
    private const int MaxItemLen = 20;   // PP_DemandPlan.ItemNo VARCHAR(20)

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static SrmMipResponse Parse(string json)
    {
        SrmMipResponse? r;
        try { r = JsonSerializer.Deserialize<SrmMipResponse>(json, JsonOpts); }
        catch (JsonException ex) { throw new FormatException("응답이 JSON 이 아닙니다: " + ex.Message, ex); }
        if (r is null) throw new FormatException("응답이 비어 있습니다.");
        if (!r.Success) throw new FormatException("응답 success=false: " + Head(json));
        if (r.Dates is null || r.Dates.Count == 0) throw new FormatException("응답에 dates 가 없습니다.");
        return r;
    }

    public static SrmMipMapResult Map(SrmMipResponse r)
    {
        var warnings = new List<string>();
        var days = new List<(string Key, DateOnly Date)>();
        foreach (var (k, v) in r.Dates!)
            if (DateOnly.TryParseExact(v?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) days.Add((k, d));
            else warnings.Add($"dates.{k} = '{v}' 는 날짜가 아니라 무시했습니다.");
        if (days.Count == 0) throw new FormatException("응답 dates 에 읽을 수 있는 날짜가 없습니다.");
        var from = days.Min(x => x.Date);
        var to = days.Max(x => x.Date);

        var cells = new List<PpRepository.DemandPlanCell>();
        int skipped = 0, negative = 0;
        foreach (var row in r.Data ?? [])
        {
            var part = (row.PARTNO ?? "").Trim();
            if (part.Length == 0 || part.Length > MaxItemLen) { skipped++; continue; }

            var extra = row.Extra is null
                ? null
                : new Dictionary<string, JsonElement>(row.Extra, StringComparer.OrdinalIgnoreCase);

            foreach (var (key, date) in days)
            {
                var pr = Num(extra, key + "_PR_QTY");
                var po = Num(extra, key + "_PO_QTY");
                if (pr < 0) { negative++; continue; }
                if (pr == 0) continue;
                cells.Add(new(part, row.PARTNM?.Trim(), row.UNIT?.Trim(), row.PACK_QTY is > 0 ? row.PACK_QTY : null, date, pr, Math.Max(0, po)));
            }
        }
        if (skipped > 0) warnings.Add($"PARTNO 가 비었거나 20자를 넘는 행 {skipped}건을 제외했습니다.");
        if (negative > 0) warnings.Add($"음수 예정량 칸 {negative}개를 무시했습니다.");
        return new SrmMipMapResult(cells, from, to, r.Data?.Count ?? 0, skipped, warnings);
    }

    private static decimal Num(Dictionary<string, JsonElement>? extra, string key)
    {
        if (extra is null || !extra.TryGetValue(key, out var e)) return 0m;
        return e.ValueKind switch
        {
            JsonValueKind.Number => e.TryGetDecimal(out var d) ? d : 0m,
            JsonValueKind.String => decimal.TryParse(e.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 0m,
            _ => 0m,
        };
    }

    private static string Head(string s) => s.Length <= 200 ? s : s[..200];
}
