namespace AMES.Contracts.Formatting;

/// <summary>
/// 단위(MD_Uom.DecimalPrec) 기준 수량 서식 — Web·PDA·Tablet 공용 정본(테스트 <c>AMES.Data.Tests/UomQtyTests</c>).
/// 자릿수가 정해진 단위는 그 자릿수로 고정 표시한다(KG 3 → 12.500). 값에 자릿수보다 긴 소수가 있으면
/// 반올림해 숨기지 않고 최대 <see cref="MaxDigits"/> 자리까지 더 보인다 — 표시가 저장값을 바꿔 보이면 안 된다.
/// 자릿수를 모르는 단위(미등록·빈 값·DecimalPrec NULL)는 정수면 소수 없이, 아니면 필요한 만큼만 보인다.
/// 문화권 구분자는 호출 스레드의 CurrentCulture 를 따른다(기존 N0·N3 서식과 같다).
/// </summary>
public static class UomQty
{
    public const int MaxDigits = 6;

    static readonly string LooseFormat = "#,##0." + new string('#', MaxDigits);

    public static string Format(decimal value, int? digits, IFormatProvider? provider = null)
    {
        if (digits is not int p || p < 0) return value.ToString(LooseFormat, provider);
        p = Math.Min(p, MaxDigits);
        return decimal.Round(value, p) == value
            ? value.ToString("N" + p, provider)
            : value.ToString("#,##0." + new string('0', p) + new string('#', MaxDigits - p), provider);
    }
}

/// <summary>단위 코드 → 소수 자릿수 사전. 대소문자·앞뒤 공백 무시.</summary>
public sealed class UomPrecisionMap
{
    public static readonly UomPrecisionMap Empty = new([]);

    readonly Dictionary<string, int> _digits;

    public UomPrecisionMap(IEnumerable<KeyValuePair<string, int>> entries)
    {
        _digits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, digits) in entries)
            if (!string.IsNullOrWhiteSpace(code) && digits >= 0) _digits[code.Trim()] = digits;
    }

    public int Count => _digits.Count;

    public int? Digits(string? unit)
        => !string.IsNullOrWhiteSpace(unit) && _digits.TryGetValue(unit.Trim(), out var d) ? d : null;

    /// <summary>수량만.</summary>
    public string Qty(decimal value, string? unit, IFormatProvider? provider = null)
        => UomQty.Format(value, Digits(unit), provider);

    /// <summary>"수량 단위" — 단위가 비면 수량만.</summary>
    public string QtyUnit(decimal value, string? unit, IFormatProvider? provider = null)
        => string.IsNullOrWhiteSpace(unit) ? Qty(value, unit, provider) : $"{Qty(value, unit, provider)} {unit.Trim()}";
}
