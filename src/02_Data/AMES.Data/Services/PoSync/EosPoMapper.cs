using System.Globalization;
using System.Text.Json;

namespace AMES.Data.Services.PoSync;

/// <summary>EOS 조회 API(SIS.EOS_GET_PO — Savannah·Georgia) 응답 한 행. 프로퍼티명은 응답 컬럼명 그대로. 항번은 Georgia 가 <c>10.0</c> 처럼 소수로 보낸다.</summary>
public sealed record EosPoRow(
    string?  PO_NO,
    decimal? PO_SEQ,
    string?  VENDOR_CODE,
    string?  PART_NO,
    decimal? PO_QTY,
    string?  DELIVERY_DATE,
    string?  PO_DATE);

/// <summary>
/// EOS 조회 API 요청 본문·응답 해석(소스 TYPE=EOS). 응답은 Savannah <c>{"value":[…],"count":n}</c>, Georgia <c>{"OUT_CURSOR":[…]}</c> 이고 모든 협력사의 발주가 섞여 오므로
/// 우리 협력사 코드(VENDCD)의 행만 남겨 SRM 행 모양(<see cref="SrmPoRow"/>)으로 바꾼다 — 그 뒤 매핑·업서트는 SRM 과 같다.
/// 날짜 창은 납기일(DELIVERY_DATE) 기준이다(SRM 은 발주일). 원격은 한 번에 31일·1,000행까지만 받아 <see cref="ChunkDays"/> 일씩 나눠 부르고, 1,000행을 넘은 구간은 반으로 나눠 다시 부른다. 납품수량이 없어 DELI_QTY 는 null(기존 납품수량 유지).
/// </summary>
public static class EosPoMapper
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>한 번에 부르는 납기일 일수 — 원격 상한 31일, 큰 결과는 원격 DB 오류(502)라 넉넉히 작게 둔다.</summary>
    public const int ChunkDays = 7;

    /// <summary>자동 확장 때 원격 결과(모든 협력사)가 이만큼 연속으로 비면 더 앞으로 가지 않는다 — 공휴일 주 하나로 멈추지 않게 2.</summary>
    public const int EmptyChunksToStop = 2;

    public enum RemoteError { None, RangeTooLong, TooManyRows }

    /// <summary>원격 오류 문구 판정 — ORA-20001 = 31일 초과, ORA-20002 = 1,000행 초과(SIS.EOS_GET_PO, 11-25 실측).</summary>
    public static RemoteError ClassifyError(string? message)
        => message is null ? RemoteError.None
         : message.Contains("ORA-20002", StringComparison.Ordinal) ? RemoteError.TooManyRows
         : message.Contains("ORA-20001", StringComparison.Ordinal) ? RemoteError.RangeTooLong
         : RemoteError.None;

    /// <summary>[from, to] 를 양 끝 포함 n 일씩 자른다(from &gt; to 면 비어 있음).</summary>
    public static IEnumerable<(DateOnly From, DateOnly To)> Chunks(DateOnly from, DateOnly to, int days)
    {
        for (var f = from; f <= to; f = f.AddDays(days))
        {
            var t = f.AddDays(days - 1);
            yield return (f, t < to ? t : to);
        }
    }

    /// <summary>요청 본문 — 조회 이름과 납기일 창(yyyy-MM-dd), 결과 커서. 원격 규격 그대로(DbType 16 = 문자열, 121 = 커서).</summary>
    public static string BuildBody(string sql, DateOnly from, DateOnly to)
    {
        var body = new Dictionary<string, object?>
        {
            ["SQL"] = sql,
            ["Parameters"] = new Dictionary<string, object?>
            {
                ["IN_DATE_FROM"] = Param("IN_DATE_FROM", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ["IN_DATE_TO"]   = Param("IN_DATE_TO",   to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ["OUT_CURSOR"]   = new Dictionary<string, object?>
                {
                    ["Name"] = "OUT_CURSOR", ["Value"] = null, ["DbType"] = 121, ["Direction"] = 2, ["Size"] = 512,
                },
            },
            ["IsPureSQL"] = false,
            ["IsEncryptedSQL"] = false,
        };
        return JsonSerializer.Serialize(body);

        static Dictionary<string, object?> Param(string name, string value)
            => new() { ["Name"] = name, ["Value"] = value, ["DbType"] = 16, ["Direction"] = 1 };
    }

    /// <summary>응답 루트가 배열이면 그대로, 객체면 첫 배열형 프로퍼티(value)를 행 목록으로 본다.</summary>
    public static IReadOnlyList<EosPoRow> ParseRows(string json)
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
            return arr.Deserialize<List<EosPoRow>>(JsonOpts) ?? [];
        }
    }

    /// <summary>
    /// 우리 협력사 코드 행만 SRM 행 모양으로. vendorCodes 는 설정 VENDCD 값 그대로 — 쉼표로 여러 개(「309224,312598」).
    /// PONO = 발주번호-항번(SrmPoMapper 가 SoNumber·SoLineNo 로 나눈다).
    /// </summary>
    public static IReadOnlyList<SrmPoRow> ToSrmRows(IEnumerable<EosPoRow> rows, string vendorCodes)
    {
        var vend = new HashSet<string>(AMES.Data.Services.Srm.SrmVendorCheck.Split(vendorCodes), StringComparer.OrdinalIgnoreCase);
        return rows
            .Where(r => r.VENDOR_CODE is { } v && vend.Contains(v.Trim()))
            .Select(r => new SrmPoRow(
                PONO: r.PO_SEQ is { } seq ? $"{r.PO_NO?.Trim()}-{SeqText(seq)}" : r.PO_NO?.Trim(),
                PO_DATE: r.PO_DATE,
                PO_DELI_DATE: r.DELIVERY_DATE,
                PARTNO: r.PART_NO,
                LOEKZ: null,
                ELIKZ: null,
                PO_QTY: r.PO_QTY,
                DELI_QTY: null))
            .ToList();
    }

    // 정수 항번(10.0)은 "10" 으로 — SrmPoMapper 가 '-' 뒤 정수만 SoLineNo 로 읽는다. 소수 항번은 그대로 둔다.
    static string SeqText(decimal seq)
        => seq == decimal.Truncate(seq) ? decimal.Truncate(seq).ToString(CultureInfo.InvariantCulture) : seq.ToString(CultureInfo.InvariantCulture);
}
