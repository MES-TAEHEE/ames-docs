using System.Text.RegularExpressions;

namespace AMES.Data.Services;

/// <summary>
/// MD 화면 값 검사 규칙(10-28 MD 단위테스트 결과서 FAIL 조치) — 화면이 저장 전에 같은 규칙으로 막고 저장소가 마지막으로 다시 본다.
/// 반환값은 위반한 규칙 이름(resx 키 MD.Rule.{이름})이고 null 이면 통과. 비어 있는 값은 검사하지 않는다(필수 여부는 화면 몫).
/// 상한은 10-28 사용자 결정 — 내열등급 0~300 °C, 경화온도 1~300 °C, 경화시간 1~600 분, 전착전압 1~500 V.
/// </summary>
public static class MdValueRules
{
    public const int HeatRatingMin = 0,   HeatRatingMax = 300;
    public const int CureTempMin = 1,     CureTempMax = 300;
    public const int CureDurationMin = 1, CureDurationMax = 600;
    public const int ElectroVMin = 1,     ElectroVMax = 500;

    /// <summary>검사기준 규격 — 하한 ≤ 상한, 공칭은 하한~상한 사이(값이 있는 것끼리만).</summary>
    public static string? InspectionSpec(decimal? nominal, decimal? lsl, decimal? usl)
    {
        if (lsl is { } l && usl is { } u && l > u) return "LslAboveUsl";
        if (nominal is { } n && ((lsl is { } lo && n < lo) || (usl is { } hi && n > hi))) return "NominalOutsideSpec";
        return null;
    }

    public static string? RfidHeatRating(int? heatRating)
        => heatRating is { } h && (h < HeatRatingMin || h > HeatRatingMax) ? "HeatRatingRange" : null;

    public static string? RalCure(int? cureTemp, int? cureDuration, int? electroV)
    {
        if (cureTemp is { } t && (t < CureTempMin || t > CureTempMax)) return "CureTempRange";
        if (cureDuration is { } d && (d < CureDurationMin || d > CureDurationMax)) return "CureDurationRange";
        if (electroV is { } v && (v < ElectroVMin || v > ElectroVMax)) return "ElectroVRange";
        return null;
    }

    /// <summary>포장사양 — 수량 3종은 1 이상, 중량은 0 보다 크고 순중량 ≤ 총중량.</summary>
    public static string? Packaging(int? qtyPerInner, int? innerPerOuter, int? outerPerPallet, decimal? netWeightKg, decimal? grossWeightKg)
    {
        if (qtyPerInner is < 1 || innerPerOuter is < 1 || outerPerPallet is < 1) return "PackQtyMin";
        if (netWeightKg is <= 0 || grossWeightKg is <= 0) return "PackWeightPositive";
        if (netWeightKg is { } n && grossWeightKg is { } g && n > g) return "NetAboveGross";
        return null;
    }

    /// <summary>적용기간 — 시작일이 종료일보다 늦으면 안 된다.</summary>
    public static string? Period(DateOnly? from, DateOnly? to)
        => from is { } f && to is { } t && f > t ? "PeriodReversed" : null;

    static readonly Regex GroupCodeRegex = new("^[A-Z0-9_]+$", RegexOptions.CultureInvariant);

    /// <summary>공통코드 그룹코드 — 대문자로 바꾼 뒤 영문·숫자·밑줄만.</summary>
    public static string? GroupCode(string? groupCode)
        => string.IsNullOrEmpty(groupCode) || GroupCodeRegex.IsMatch(groupCode.Trim().ToUpperInvariant()) ? null : "GroupCodeChars";

    /// <summary>저장소가 규칙 위반을 받으면 던진다 — 화면이 먼저 같은 규칙으로 막으므로 정상 경로에서는 나오지 않는다.</summary>
    public static void Throw(string? rule)
    {
        if (rule is not null) throw new MdValueRuleException(rule);
    }
}

public sealed class MdValueRuleException(string rule) : ArgumentException($"Value rule violated: {rule}")
{
    public string Rule { get; } = rule;
}
