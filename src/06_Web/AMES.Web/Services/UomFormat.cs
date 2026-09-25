using AMES.Contracts.Formatting;
using AMES.Data.Repositories;

namespace AMES.Web.Services;

/// <summary>
/// 화면 수량 표시를 단위 마스터(MD_Uom.DecimalPrec) 자릿수로 맞춘다. 규칙은 <see cref="UomQty"/>.
/// 사전은 5분 캐시이며 MD 단위 화면 저장 직후 <see cref="Invalidate"/> 로 바로 갱신한다.
/// 비활성 단위도 사전에 넣는다 — 과거 데이터가 그 단위로 남아 있다.
/// </summary>
public sealed class UomFormat(MasterDataRepository md, ILogger<UomFormat> logger)
{
    static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    readonly object _gate = new();
    UomPrecisionMap? _map;
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
                _map = new UomPrecisionMap(md.ListUoms()
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

    public string QtyUnit(decimal value, string? unit) => Map().QtyUnit(value, unit);
}
