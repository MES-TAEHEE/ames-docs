using System.Globalization;
using AMES.Data.Aps.Contracts;
using AMES.Data.Aps.Domain;

namespace AMES.Data.Aps;

/// <summary>
/// 공통코드 APS_SETTING(CodeValue=키, Attribute1=값)·APS_COVER_TIER(CodeValue=일수요 하한, Attribute1=커버일수) + MD_ApsLineStage 행 →
/// Settings + ApsOptions (스펙 §3.2·§3.3·§4.1). DB 읽기는 ApsRepository.ReadSettings — 이 파일은 SqlClient 를 참조하지 않는다.
/// 파싱 실패·누락·UseFlag=0 은 Settings.Default() 값 + 키 이름이 든 경고 1줄. PackRules·UphRules·LineShifts·ShiftExceptions 는 비워 두고
/// BuildBundle 이 채운다. 정본 테스트 ApsSettingsLoaderTests.
/// </summary>
public static class ApsSettingsLoader
{
    public const string GroupSetting = "APS_SETTING", GroupCoverTier = "APS_COVER_TIER";
    public const string KeyDefaultCover = "DEFAULT_COVER", KeyRoundTo = "ROUND_TO", KeyTrimLastDay = "TRIM_LAST_DAY", KeyInjOffsetDays = "INJ_OFFSET_DAYS",
                        KeyShiftDayH = "SHIFT_DAY_H", KeyShiftNightH = "SHIFT_NIGHT_H", KeyPullForward = "PULL_FORWARD", KeySafetyTerm = "SAFETY_TERM", KeyWarnStatus = "WARN_STATUS",
                        KeyDefaultPattern = "DEFAULT_PATTERN";

    public sealed record CodeRow(string GroupCode, string CodeValue, string? Attribute1, int? SortOrder, bool UseFlag);

    /// <summary>PP-APS 설정 다이얼로그가 고르는 입력 컨트롤 — Flag = 체크박스(1/0), Pattern = 전역 가동 시간 패턴 콤보, Int·Number·Text = 입력란.</summary>
    public enum SettingKind { Text, Number, Int, Flag, Pattern }
    public sealed record KnownSetting(string Key, SettingKind Kind, string Default);

    /// <summary>APS_SETTING 의 여덟 키(마이그레이션 §5 순서)와 기본값 — 행이 없을 때 다이얼로그가 이 값으로 만든다. 기본값은 Parse 의 폴백과 같다.
    /// SHIFT_DAY_H/NIGHT_H 는 2026-10-06 부터 다이얼로그에서 뺐다 — APS 는 가동 시간 패턴이 없으면 조회를 막아 폴백에 닿지 않는다(행은 남겨도 Text 로 보인다).</summary>
    public static readonly IReadOnlyList<KnownSetting> KnownSettings = new[]
    {
        new KnownSetting(KeyDefaultCover,  SettingKind.Number,  "0.7"),
        new KnownSetting(KeyRoundTo,       SettingKind.Int,     "5"),
        new KnownSetting(KeyTrimLastDay,   SettingKind.Flag,    "1"),
        new KnownSetting(KeyInjOffsetDays, SettingKind.Int,     "1"),
        new KnownSetting(KeyDefaultPattern, SettingKind.Pattern, ""),
        new KnownSetting(KeyPullForward,   SettingKind.Flag,    "0"),
        new KnownSetting(KeySafetyTerm,    SettingKind.Flag,   "0"),
        new KnownSetting(KeyWarnStatus,    SettingKind.Flag,   "0"),
    };

    /// <summary>폐기된 키 — 교대 시간 폴백(10-06, 패턴이 대체). migrate_aps.sql 이 행을 지우고, 다이얼로그는 남은 행을 숨기며, Parse 는 읽지 않는다.</summary>
    public static readonly IReadOnlyList<string> RetiredKeys = new[] { KeyShiftDayH, KeyShiftNightH };
    public static bool IsRetired(string key) => RetiredKeys.Any(k => string.Equals(k, key.Trim(), StringComparison.OrdinalIgnoreCase));

    public static SettingKind KindOf(string key) =>
        KnownSettings.FirstOrDefault(k => string.Equals(k.Key, key.Trim(), StringComparison.OrdinalIgnoreCase))?.Kind ?? SettingKind.Text;

    /// <summary>저장 전 검증 — Parse 가 경고 + 기본값으로 떨어뜨릴 값을 미리 막는다. 통과면 null, 아니면 키 이름이 든 ko 문구.
    /// 모르는 키는 Parse 가 읽지 않으므로 어떤 값이든 통과. 교대 시간은 0~24h.</summary>
    public static string? ValidateSetting(string key, string? raw)
    {
        var kind = KindOf(key);
        var v = (raw ?? "").Trim();
        switch (kind)
        {
            case SettingKind.Text: return null;
            case SettingKind.Pattern: return null;   // 빈 값 = 미지정(조회 때 차단), 존재·ACTIVE 여부는 DB 를 아는 ApsRepository 가 본다
            case SettingKind.Flag:
                return v.ToUpperInvariant() is "1" or "0" or "TRUE" or "FALSE" or "Y" or "N" or "YES" or "NO" ? null
                     : $"{key}: 값 '{raw}' 은 1/0 이어야 합니다.";
            case SettingKind.Int:
                if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return $"{key}: 값 '{raw}' 은 정수여야 합니다.";
                if (string.Equals(key.Trim(), KeyRoundTo, StringComparison.OrdinalIgnoreCase) && i < 1) return $"{key}: 1 이상이어야 합니다.";
                if (i < 0) return $"{key}: 0 이상이어야 합니다.";
                return null;
            default:
                if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) || double.IsNaN(d) || double.IsInfinity(d))
                    return $"{key}: 값 '{raw}' 은 숫자여야 합니다.";
                if (d < 0) return $"{key}: 0 이상이어야 합니다.";
                if (d > 24 && key.Trim().StartsWith("SHIFT_", StringComparison.OrdinalIgnoreCase)) return $"{key}: 교대 시간은 24 이하여야 합니다.";
                return null;
        }
    }

    /// <summary>APS_COVER_TIER 한 행(일수요 하한, 커버일수) 검증 — 둘 다 0 이상 숫자. 통과면 null.</summary>
    public static string? ValidateTier(string? minDailyDemand, string? daysOfCover)
    {
        if (!double.TryParse((minDailyDemand ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var min) || min < 0)
            return $"{GroupCoverTier}: 일수요 하한 '{minDailyDemand}' 은 0 이상 숫자여야 합니다.";
        if (!double.TryParse((daysOfCover ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var cover) || cover < 0)
            return $"{GroupCoverTier}: 커버일수 '{daysOfCover}' 은 0 이상 숫자여야 합니다.";
        return null;
    }
    /// <summary>MD_ApsLineStage 한 행. PatternId = 그 라인의 APS 가동 시간 패턴(MD_LineTimePattern), 비면 DEFAULT_PATTERN 을 따른다.</summary>
    public sealed record LineStageRow(string LineId, int OffsetDays, bool UseStock, string? Note, string? PatternId);

    /// <summary>
    /// Parse 결과. DefaultPatternId / LinePatterns 는 엔진 Settings 밖의 AMES 어댑터 값 — 사출 라인·날짜 능력을 읽을 패턴을 EffectivePattern 이 정한다
    /// (2026-10-06 사용자 결정: 라인 지정 → 기본 패턴 순이고 PP_LineSchedule 저장 패턴·라인 전용/전역 자동 해석보다 우선, 둘 다 없으면 ApsRepository 가 조회를 막는다).
    /// </summary>
    public sealed record Parsed(Settings Settings, ApsOptions Options, List<string> Warnings,
                                string? DefaultPatternId, IReadOnlyDictionary<string, string?> LinePatterns)
    {
        public string? EffectivePattern(string lineId) =>
            LinePatterns.TryGetValue(lineId, out var p) && !string.IsNullOrWhiteSpace(p) ? p.Trim() : DefaultPatternId;
    }

    const int DefaultInjOffsetDays = 1;   // 스펙 §3.3 INJ_OFFSET_DAYS 기본(도너 Settings.Default() 는 0)

    public static Parsed Parse(IReadOnlyList<CodeRow> codes, IReadOnlyList<LineStageRow> lineStages)
    {
        var warnings = new List<string>();
        var s = Settings.Default();
        var d = Settings.Default();   // 기본값 참조용(경고 문구)

        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in codes)
            if (c.UseFlag && string.Equals(c.GroupCode, GroupSetting, StringComparison.OrdinalIgnoreCase))
                map[c.CodeValue.Trim()] = c.Attribute1;   // 같은 키 행이 둘이면 마지막 행이 이긴다(PK 가 CodeID 라 MD-26 에서 가능)

        s.DefaultDaysOfCover = Num(map, KeyDefaultCover, d.DefaultDaysOfCover, warnings);
        s.RoundTo            = Int(map, KeyRoundTo, d.RoundTo, warnings);
        s.TrimLastDay        = Flag(map, KeyTrimLastDay, d.TrimLastDay, warnings);
        s.StageDefaults.Injection.OffsetDays = Int(map, KeyInjOffsetDays, DefaultInjOffsetDays, warnings, min: 0);
        s.StageDefaults.Assembly.OffsetDays  = 0;
        // 교대 시간은 가동 시간 패턴(세그먼트 × WORK_SHIFT.SortOrder)에서만 나온다(2026-10-06) — SHIFT_DAY_H/NIGHT_H 는 폐기(RetiredKeys), 행이 남아 있어도 읽지 않는다.
        // Settings.Shift 는 엔진 기본값 그대로(패턴 개념이 없는 SEG 골든 픽스처 재현용)
        s.DailyCapacity = 0;
        var defaultPattern = map.TryGetValue(KeyDefaultPattern, out var dp) && !string.IsNullOrWhiteSpace(dp) ? dp.Trim() : null;
        var linePatterns = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in lineStages) linePatterns[r.LineId] = string.IsNullOrWhiteSpace(r.PatternId) ? null : r.PatternId.Trim();
        s.SavedAt = null;

        var tiers = new List<CoverTier>();
        foreach (var c in codes.Where(c => c.UseFlag && string.Equals(c.GroupCode, GroupCoverTier, StringComparison.OrdinalIgnoreCase)))
        {
            if (double.TryParse(c.CodeValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var min)
                && double.TryParse(c.Attribute1, NumberStyles.Float, CultureInfo.InvariantCulture, out var cover))
                tiers.Add(new CoverTier(min, cover));
            else
                warnings.Add($"{GroupCoverTier} 행 '{c.CodeValue}'/'{c.Attribute1}' 을 숫자로 읽을 수 없어 건너뜁니다.");
        }
        if (tiers.Count > 0)
            s.CoverTiers = tiers.OrderByDescending(t => t.MinDailyDemand).ToList();
        else
            warnings.Add($"{GroupCoverTier} 가 없어 기본 구간표(100→0.4 · 20→0.7 · 0→1.5)를 씁니다.");

        s.PackRules = new();
        s.UphRules = new();
        s.LineShifts = new();
        s.ShiftExceptions = new();
        s.LineStages = lineStages.Select(r => new LineStage
        {
            RootLine = null, LineCd = r.LineId, OffsetDays = r.OffsetDays, UseStock = r.UseStock, BatchDays = 1, Note = r.Note,
        }).ToList();

        var options = new ApsOptions(
            Flag(map, KeyPullForward, false, warnings),
            Flag(map, KeySafetyTerm,  false, warnings),
            Flag(map, KeyWarnStatus,  false, warnings));

        return new Parsed(s, options, warnings, defaultPattern, linePatterns);
    }

    static double Num(Dictionary<string, string?> map, string key, double def, List<string>? w)
    {
        // w = null 이면 조용히 기본값(경고 대상이 아닌 키)
        if (!map.TryGetValue(key, out var raw)) { w?.Add(Missing(key, def)); return def; }
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return v;
        w?.Add(Invalid(key, raw, def)); return def;
    }

    /// min 이 있으면 그보다 작은 값도 읽을 수 없는 값과 같이 경고 + 기본값.
    static int Int(Dictionary<string, string?> map, string key, int def, List<string> w, int? min = null)
    {
        if (!map.TryGetValue(key, out var raw)) { w.Add(Missing(key, def)); return def; }
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && (min is null || v >= min)) return v;
        w.Add(Invalid(key, raw, def)); return def;
    }

    static bool Flag(Dictionary<string, string?> map, string key, bool def, List<string> w)
    {
        if (!map.TryGetValue(key, out var raw)) { w.Add(Missing(key, def ? 1 : 0)); return def; }
        switch ((raw ?? "").Trim().ToUpperInvariant())
        {
            case "1": case "TRUE": case "Y": case "YES": return true;
            case "0": case "FALSE": case "N": case "NO": return false;
        }
        w.Add(Invalid(key, raw, def ? 1 : 0)); return def;
    }

    static string Missing(string key, object def) =>
        $"{GroupSetting} {key} 가 없어 기본값 {Convert.ToString(def, CultureInfo.InvariantCulture)} 을 씁니다.";
    static string Invalid(string key, string? raw, object def) =>
        $"{GroupSetting} {key} 값 '{raw}' 을 읽을 수 없어 기본값 {Convert.ToString(def, CultureInfo.InvariantCulture)} 을 씁니다.";
}
