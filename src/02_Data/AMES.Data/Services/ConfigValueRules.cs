using System.Globalization;

namespace AMES.Data.Services;

/// <summary>
/// SYS-009 설정값(SYS_Config) 입력 규칙 — 유형(ConfigType)별 형식과 키별 허용 범위·소수 자리(10-28). 화면 입력칸의 Min/Max 와 저장 전 검사가 같은 표를 본다.
/// 표에 없는 INT 는 0 이상 정수, DECIMAL 은 0 이상·소수 4자리까지. bool 은 소문자 true/false 로 저장한다.
/// 범위는 그 값을 읽는 코드의 유효 범위와 같게 둔다(예 MNT_PM_LEAD_DAYS 0~365 = PmRules, PP_LINE_RUN_GAP_* = SP_PP_LineStateLog_Fill).
/// </summary>
public static class ConfigValueRules
{
    public sealed record Range(decimal Min, decimal Max, int Decimals);

    public enum Problem { None, NotNumber, OutOfRange, TooManyDecimals, NotBool }

    static readonly Dictionary<string, Range> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MAX_RETRY_COUNT"]         = new(0, 10, 0),
        ["PLC_HEARTBEAT_SEC"]       = new(1, 3600, 0),
        ["SAP_SYNC_INTERVAL_MIN"]   = new(1, 1440, 0),
        ["OEE_TARGET_PCT"]          = new(0, 100, 1),
        ["OTD_TARGET_PCT"]          = new(0, 100, 1),
        ["YIELD_TARGET_PCT"]        = new(0, 100, 1),
        ["MNT_PM_LEAD_DAYS"]        = new(0, 365, 0),
        ["PM_LOOKAHEAD_DAYS"]       = new(0, 365, 0),
        ["ALERT_REPEAT_MINUTES"]    = new(0, 1440, 0),
        ["DEFAULT_SHIFT_HOURS"]     = new(0, 24, 2),
        ["PP_LINE_RUN_GAP_CYCLES"]  = new(0, 10, 0),
        ["PP_LINE_RUN_GAP_MIN"]     = new(1, 60, 0),
        ["PP_PROD_BUFFER_WORKDAYS"] = new(1, 60, 0),
        ["PASSWORD_MIN_LEN"]        = new(6, 128, 0),
        ["SESSION_TIMEOUT_MIN"]     = new(5, 1440, 0),
        ["LOG_RETENTION_DAYS"]      = new(1, 3650, 0),
        ["DASH_REFRESH_SEC"]        = new(5, 3600, 0),
    };

    static readonly Range IntDefault     = new(0, int.MaxValue, 0);
    static readonly Range DecimalDefault = new(0, 999_999_999m, 4);

    /// <summary>
    /// 화면에 두지 않는 키 — 읽는 코드가 없어 바꿔도 아무 효과가 없다. PLANT_TIMEZONE: 시각의 기준은 DB 서버 시각(DbClock)이라 표시 시간대를 따로 두지 않는다(10-28 사용자 결정).
    /// </summary>
    public static readonly IReadOnlySet<string> HiddenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PLANT_TIMEZONE" };

    public static bool IsNumeric(string? type) => IsInt(type) || IsDecimal(type);
    public static bool IsInt(string? type)     => string.Equals(type, "INT", StringComparison.OrdinalIgnoreCase);
    public static bool IsDecimal(string? type) => string.Equals(type, "DECIMAL", StringComparison.OrdinalIgnoreCase);
    public static bool IsBool(string? type)    => string.Equals(type, "bool", StringComparison.OrdinalIgnoreCase);

    /// <summary>숫자 유형의 허용 범위 — 숫자가 아니면 null.</summary>
    public static Range? RangeOf(string? key, string? type)
    {
        if (!IsNumeric(type)) return null;
        if (key is not null && Known.TryGetValue(key.Trim(), out var r)) return IsInt(type) ? r with { Decimals = 0 } : r;
        return IsInt(type) ? IntDefault : DecimalDefault;
    }

    /// <summary>bool 저장값 해석 — true·1·Y·yes 는 켜짐(대소문자 무시), 그 밖·빈 값은 꺼짐.</summary>
    public static bool BoolValue(string? value)
        => value?.Trim().ToLowerInvariant() is "true" or "1" or "y" or "yes";

    /// <summary>
    /// 저장 전 검사 — 통과하면 normalized 에 저장할 문자열(숫자 = invariant, bool = true/false). 빈 숫자 값은 통과(빈 값 저장)한다.
    /// 숫자·bool 이 아닌 유형은 검사하지 않고 그대로 돌려준다.
    /// </summary>
    public static Problem Validate(string? key, string? type, string? value, out string? normalized)
    {
        normalized = value;
        if (IsBool(type))
        {
            var v = value?.Trim().ToLowerInvariant();
            if (v is not ("true" or "false")) return Problem.NotBool;
            normalized = v;
            return Problem.None;
        }
        var range = RangeOf(key, type);
        if (range is null) return Problem.None;
        if (string.IsNullOrWhiteSpace(value)) { normalized = null; return Problem.None; }
        if (!decimal.TryParse(value.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d))
            return Problem.NotNumber;
        if (DecimalPlaces(d) > range.Decimals) return range.Decimals == 0 ? Problem.NotNumber : Problem.TooManyDecimals;
        if (d < range.Min || d > range.Max) return Problem.OutOfRange;
        normalized = (range.Decimals == 0 ? decimal.Truncate(d) : d).ToString(CultureInfo.InvariantCulture);
        return Problem.None;
    }

    static int DecimalPlaces(decimal d)
    {
        d = d / 1.0000000000000000000000000000m;   // 끝의 0 을 떼어 실제 소수 자리만 센다(7.0 → 0자리)
        return (decimal.GetBits(d)[3] >> 16) & 0xFF;
    }
}
