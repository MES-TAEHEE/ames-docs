namespace AMES.Data.Services;

/// <summary>
/// 금형 소진율·등급(MNT-004 금형 관리, MNT-009 금형 타일). 정본 테스트 MoldUsageRulesTests.
///   · 소진율 = 현재 Shot ÷ 정격 Shot × 100 (정격이 없으면 없음)
///   · 등급 = 소진율 ≥ 90 위험 / ≥ 70 경고 / 그 밖 정상, 소진율이 없으면 저장된 등급(ThresholdLevel)
///   · 표시값은 소수 2자리에서 버린다 — 반올림하면 69.996% 가 「70.00%」인데 등급은 정상으로 보여 어긋난다.
/// </summary>
public static class MoldUsageRules
{
    public const string Ok = "OK", Warning = "WARNING", Critical = "CRITICAL";
    public static readonly string[] Grades = { Ok, Warning, Critical };

    public static decimal? Usage(long? currentShots, long? ratedShots) =>
        ratedShots is > 0 && currentShots is { } cur ? Math.Max(0m, 100m * cur / ratedShots.Value) : null;

    public static string GradeOf(decimal usage) => usage >= 90m ? Critical : usage >= 70m ? Warning : Ok;

    public static string? Grade(decimal? usage, string? storedLevel) =>
        usage is { } u ? GradeOf(u) : storedLevel is { Length: > 0 } t && Grades.Contains(t) ? t : null;

    public static decimal Shown(decimal usage) => Math.Floor(usage * 100m) / 100m;
}
