using AMES.Contracts.Formatting;
using AMES.Data.Repositories;

namespace AMES.Web.Services;

/// <summary>
/// 화면 수량 표시를 단위 마스터(MD_Uom.DecimalPrec) 자릿수로 맞추고, 단위는 기호(MD_Uom.Symbol)로 보인다. 자릿수 규칙은 <see cref="UomQty"/>.
/// 사전은 5분 캐시이며 MD 단위 화면 저장 직후 <see cref="Invalidate"/> 로 바로 갱신한다.
/// 비활성 단위도 사전에 넣는다 — 과거 데이터가 그 단위로 남아 있다.
/// </summary>
public sealed class UomFormat(MasterDataRepository md, ILogger<UomFormat> logger)
{
    static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    readonly object _gate = new();
    UomPrecisionMap? _map;
    Dictionary<string, string> _symbols = new(StringComparer.OrdinalIgnoreCase);
    DateTime _loadedAt;

    UomPrecisionMap Map()
    {
        var m = _map;
        if (m is not null && DateTime.UtcNow - _loadedAt < Ttl) return m;
        lock (_gate)
        {
            if (_map is not null && DateTime.UtcNow - _loadedAt < Ttl) return _map;
            try
            {
                var rows = md.ListUoms();
                _symbols = rows.Where(u => !string.IsNullOrWhiteSpace(u.Symbol))
                               .GroupBy(u => u.UOMCode.Trim(), StringComparer.OrdinalIgnoreCase)
                               .ToDictionary(g => g.Key, g => g.First().Symbol!.Trim(), StringComparer.OrdinalIgnoreCase);
                _map = new UomPrecisionMap(rows
                    .Where(u => u.DecimalPrec is not null)
                    .Select(u => KeyValuePair.Create(u.UOMCode, u.DecimalPrec!.Value)));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "UOM precision load failed");
                _map ??= UomPrecisionMap.Empty;
            }
            _loadedAt = DateTime.UtcNow;
            return _map;
        }
    }

    public void Invalidate() { lock (_gate) { _map = null; } }

    public int? Digits(string? unit) => Map().Digits(unit);

    public string Qty(decimal value, string? unit) => Map().Qty(value, unit);

    public string Qty(decimal? value, string? unit, string empty = "—") => value is { } v ? Qty(v, unit) : empty;

    /// <summary>"수량 기호" — 자릿수는 단위 코드로, 뒤에 붙는 단위는 기호(<see cref="Sym"/>)로 보인다.</summary>
    public string QtyUnit(decimal value, string? unit)
    {
        var qty = Qty(value, unit);
        return Sym(unit) is { Length: > 0 } s ? $"{qty} {s}" : qty;
    }

    /// <summary>
    /// 화면에 보일 단위 — MD_Uom.Symbol 을 저장된 값 그대로(대소문자 포함, 10-07 사용자 결정). 기호가 비었거나 모르는 단위면 코드 그대로,
    /// 코드가 비면 그대로(null·빈 값) 돌려준다 — 호출 쪽의 "—" 대체 규칙을 바꾸지 않는다.
    /// 웹 화면·인쇄물만 기호로 보이고, 저장값·검색·정렬·CSV/엑셀 내보내기는 단위 코드 그대로다.
    /// </summary>
    public string? Sym(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return code;
        Map();
        return _symbols.TryGetValue(code.Trim(), out var s) ? s : code;
    }
}
