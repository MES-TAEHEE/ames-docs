using AMES.Data.Aps;
using AMES.Data.Aps.Contracts;
using AMES.Data.Aps.Domain;
using AMES.Data.Scheduling;
using Xunit;
using static AMES.Data.Aps.ApsSettingsLoader;

namespace AMES.Data.Tests.Aps;

/// <summary>
/// 공통코드 APS_SETTING·APS_COVER_TIER + MD_ApsLineStage 행 → Settings + ApsOptions (스펙 §3.2·§3.3·§4.1).
/// 파싱 실패·누락·UseFlag=0 은 Settings.Default() 값 + 키 이름이 든 경고 1줄 — 조용히 0 금지. 순수 함수, DB 없음.
/// </summary>
public class ApsSettingsLoaderTests
{
    static CodeRow S(string key, string? val, bool use = true) => new(GroupSetting, key, val, null, use);
    static CodeRow T(string min, string? cover, int sort, bool use = true) => new(GroupCoverTier, min, cover, sort, use);

    static readonly CodeRow[] Full =
    {
        S(KeyDefaultCover, "0.8"), S(KeyRoundTo, "10"), S(KeyTrimLastDay, "0"), S(KeyInjOffsetDays, "2"),
        S(KeyShiftDayH, "8"), S(KeyShiftNightH, "16"), S(KeyPullForward, "1"), S(KeySafetyTerm, "true"), S(KeyWarnStatus, "Y"),
        T("20", "0.7", 20), T("0", "1.5", 30), T("100", "0.4", 10),
    };
    static readonly LineStageRow[] NoStages = Array.Empty<LineStageRow>();

    [Fact]
    public void Full_set_parses_without_warnings()
    {
        var p = Parse(Full, NoStages);

        Assert.Empty(p.Warnings);
        Assert.Equal(0.8, p.Settings.DefaultDaysOfCover);
        Assert.Equal(10, p.Settings.RoundTo);
        Assert.False(p.Settings.TrimLastDay);
        Assert.Equal(2, p.Settings.StageDefaults.Injection.OffsetDays);
        Assert.Equal(0, p.Settings.StageDefaults.Assembly.OffsetDays);
        // 교대 시간 폴백 행은 폐기(2026-10-06) — 행이 남아 있어도 읽지 않고 엔진 기본값(골든 픽스처용)만 둔다
        Assert.Equal(Settings.Default().Shift.Day,   p.Settings.Shift.Day);
        Assert.Equal(Settings.Default().Shift.Night, p.Settings.Shift.Night);
        Assert.Equal(0, p.Settings.DailyCapacity);
        Assert.Empty(p.Settings.PackRules);
        Assert.Empty(p.Settings.UphRules);
        Assert.Empty(p.Settings.LineShifts);
        Assert.Empty(p.Settings.ShiftExceptions);
        Assert.Null(p.Settings.SavedAt);
        Assert.Equal(new ApsOptions(true, true, true), p.Options);
    }

    [Fact]
    public void Cover_tiers_are_sorted_by_min_demand_descending()
    {
        var p = Parse(Full, NoStages);

        Assert.Equal(new[] { new CoverTier(100, 0.4), new CoverTier(20, 0.7), new CoverTier(0, 1.5) }, p.Settings.CoverTiers);
    }

    [Fact]
    public void Missing_keys_use_defaults_with_one_warning_each_naming_the_key()
    {
        var p = Parse(Array.Empty<CodeRow>(), NoStages);

        var d = Settings.Default();
        Assert.Equal(d.DefaultDaysOfCover, p.Settings.DefaultDaysOfCover);
        Assert.Equal(d.RoundTo, p.Settings.RoundTo);
        Assert.True(p.Settings.TrimLastDay);
        Assert.Equal(1, p.Settings.StageDefaults.Injection.OffsetDays);
        Assert.Equal(d.Shift.Day, p.Settings.Shift.Day);
        Assert.Equal(d.Shift.Night, p.Settings.Shift.Night);
        Assert.Equal(d.CoverTiers, p.Settings.CoverTiers);
        Assert.Equal(ApsOptions.Default, p.Options);
        foreach (var key in new[] { KeyDefaultCover, KeyRoundTo, KeyTrimLastDay, KeyInjOffsetDays, KeyPullForward, KeySafetyTerm, KeyWarnStatus })
            Assert.Single(p.Warnings, w => w.Contains(key));
        Assert.Single(p.Warnings, w => w.Contains(GroupCoverTier));
        // 교대 시간 폴백(SHIFT_DAY_H/NIGHT_H)·기본 패턴(DEFAULT_PATTERN)은 빠져도 경고하지 않는다 — 패턴은 ApsRepository 가 미설정이면 조회 자체를 막는다(2026-10-06)
        Assert.DoesNotContain(p.Warnings, w => w.Contains(KeyShiftDayH) || w.Contains(KeyShiftNightH) || w.Contains(KeyDefaultPattern));
        Assert.Equal(8, p.Warnings.Count);
    }

    // ── 가동 시간 패턴 지정 (2026-10-06 사용자 결정: APS 설정의 패턴이 PP-LSB 저장 패턴·자동 해석보다 우선, 미설정이면 조회 차단) ──

    [Fact]
    public void Parse_exposes_default_pattern_and_line_patterns()
    {
        var rows = Full.Append(S(KeyDefaultPattern, " LP-CMN-001 ")).ToList();
        var stages = new[] { new LineStageRow("LINE-INJ-01", 1, true, null, "LP-APS-INJ01"), new LineStageRow("LINE-INJ-02", 1, true, null, null) };

        var p = Parse(rows, stages);

        Assert.Empty(p.Warnings);
        Assert.Equal("LP-CMN-001", p.DefaultPatternId);
        Assert.Equal("LP-APS-INJ01", p.LinePatterns["LINE-INJ-01"]);
        Assert.Null(p.LinePatterns["LINE-INJ-02"]);
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("   ")]
    public void Blank_default_pattern_is_null(string? raw)
    {
        var p = Parse(Full.Append(S(KeyDefaultPattern, raw)).ToList(), NoStages);

        Assert.Null(p.DefaultPatternId);
        Assert.Empty(p.Warnings);
    }

    [Fact]
    public void EffectivePattern_prefers_line_then_default_then_null()
    {
        var rows = Full.Append(S(KeyDefaultPattern, "LP-CMN-001")).ToList();
        var p = Parse(rows, new[] { new LineStageRow("LINE-INJ-01", 1, true, null, "LP-APS-INJ01"), new LineStageRow("LINE-INJ-02", 1, true, null, " ") });

        Assert.Equal("LP-APS-INJ01", p.EffectivePattern("line-inj-01"));   // 라인 키는 대소문자 무관
        Assert.Equal("LP-CMN-001",   p.EffectivePattern("LINE-INJ-02"));   // 빈 라인 지정 = 기본 패턴
        Assert.Equal("LP-CMN-001",   p.EffectivePattern("LINE-INJ-09"));   // 행 없음 = 기본 패턴

        var none = Parse(Full, NoStages);
        Assert.Null(none.EffectivePattern("LINE-INJ-01"));
    }

    [Fact]
    public void Pattern_kind_accepts_blank_and_any_id_and_is_text_in_the_editor()
    {
        Assert.Equal(SettingKind.Pattern, KindOf(KeyDefaultPattern));
        Assert.Null(ValidateSetting(KeyDefaultPattern, ""));
        Assert.Null(ValidateSetting(KeyDefaultPattern, null));
        Assert.Null(ValidateSetting(KeyDefaultPattern, "LP-CMN-001"));
    }

    [Fact]
    public void Invalid_values_warn_and_fall_back()
    {
        var rows = Full.Where(c => c.CodeValue is not (KeyRoundTo or KeyPullForward))
                       .Append(S(KeyRoundTo, "five")).Append(S(KeyPullForward, "maybe")).ToList();

        var p = Parse(rows, NoStages);

        Assert.Equal(5, p.Settings.RoundTo);
        Assert.False(p.Options.PullForwardSupply);
        Assert.Single(p.Warnings, w => w.Contains(KeyRoundTo) && w.Contains("five"));
        Assert.Single(p.Warnings, w => w.Contains(KeyPullForward) && w.Contains("maybe"));
        Assert.Equal(2, p.Warnings.Count);
    }

    [Fact]
    public void Negative_inj_offset_days_warns_and_falls_back_to_default()
    {
        var rows = Full.Where(c => c.CodeValue != KeyInjOffsetDays).Append(S(KeyInjOffsetDays, "-2")).ToList();

        var p = Parse(rows, NoStages);

        Assert.Equal(1, p.Settings.StageDefaults.Injection.OffsetDays);
        var w = Assert.Single(p.Warnings);
        Assert.Contains(KeyInjOffsetDays, w);
        Assert.Contains("'-2'", w);
    }

    [Fact]
    public void Disabled_rows_are_ignored_like_missing()
    {
        var rows = Full.Where(c => c.CodeValue != KeyDefaultCover).Append(S(KeyDefaultCover, "0.9", use: false)).ToList();

        var p = Parse(rows, NoStages);

        Assert.Equal(0.7, p.Settings.DefaultDaysOfCover);
        Assert.Single(p.Warnings, w => w.Contains(KeyDefaultCover));
    }

    [Fact]
    public void Bad_tier_row_is_skipped_with_warning_and_rest_kept()
    {
        var rows = Full.Where(c => c.GroupCode != GroupCoverTier)
                       .Concat(new[] { T("100", "0.4", 10), T("abc", "0.7", 20), T("0", null, 30) }).ToList();

        var p = Parse(rows, NoStages);

        Assert.Equal(new[] { new CoverTier(100, 0.4) }, p.Settings.CoverTiers);
        Assert.Equal(2, p.Warnings.Count);
        Assert.All(p.Warnings, w => Assert.Contains(GroupCoverTier, w));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("true", true)]
    [InlineData("FALSE", false)]
    [InlineData("y", true)]
    [InlineData("N", false)]
    public void Flags_accept_numeric_and_word_forms(string raw, bool expected)
    {
        var rows = Full.Where(c => c.CodeValue != KeyWarnStatus).Append(S(KeyWarnStatus, raw)).ToList();

        Assert.Equal(expected, Parse(rows, NoStages).Options.WarnStatus);
    }

    [Fact]
    public void Line_stage_rows_become_common_line_stages_with_null_root()
    {
        var stages = new[] { new LineStageRow("LINE-INJ-01", 1, true, "사출 선행 1일", null), new LineStageRow("LINE-IMG-01", 0, false, null, null) };

        var p = Parse(Full, stages);

        Assert.Collection(p.Settings.LineStages,
            s => { Assert.Null(s.RootLine); Assert.Equal("LINE-INJ-01", s.LineCd); Assert.Equal(1, s.OffsetDays); Assert.True(s.UseStock); Assert.Equal(1, s.BatchDays); Assert.Equal("사출 선행 1일", s.Note); },
            s => { Assert.Null(s.RootLine); Assert.Equal("LINE-IMG-01", s.LineCd); Assert.Equal(0, s.OffsetDays); Assert.False(s.UseStock); Assert.Equal(1, s.BatchDays); Assert.Null(s.Note); });
    }

    [Fact]
    public void Adapter_follows_factory_calendar_rows_and_weekend_fallback()
    {
        IApsCalendar cal = new WorkdayCalendarAdapter(new WorkdayCalendar(new (DateTime, string?)[]
        {
            (new DateTime(2026, 10, 7), "HOLIDAY"),    // 수 — 휴무
            (new DateTime(2026, 10, 10), "SPECIAL"),   // 토 — 특근
        }));

        Assert.True(cal.IsWorkday(new DateOnly(2026, 10, 5)));    // 월, 행 없음
        Assert.False(cal.IsWorkday(new DateOnly(2026, 10, 7)));
        Assert.True(cal.IsWorkday(new DateOnly(2026, 10, 10)));
        Assert.False(cal.IsWorkday(new DateOnly(2026, 10, 11)));  // 일, 행 없음
        Assert.False(cal.IsWorkday(new DateOnly(2026, 10, 17)));  // 토, 행 없음
    }

    // ── PP-APS 설정 다이얼로그 편집 검증 (2026-10-06 사용자 요청 "APS Settings 값 변경 가능하도록") — 저장 전에 거르고, Parse 와 같은 해석 규칙 ──

    [Theory]
    [InlineData(KeyDefaultCover, "0.7")] [InlineData(KeyDefaultCover, "0")] [InlineData(KeyDefaultCover, " 1.5 ")]
    [InlineData(KeyRoundTo, "5")] [InlineData(KeyRoundTo, "1")]
    [InlineData(KeyInjOffsetDays, "0")] [InlineData(KeyInjOffsetDays, "3")]
    [InlineData(KeyTrimLastDay, "1")] [InlineData(KeyPullForward, "0")] [InlineData(KeySafetyTerm, "Y")] [InlineData(KeyWarnStatus, "false")]
    [InlineData("SOME_FUTURE_KEY", "anything")]
    public void ValidateSetting_accepts_values_parse_would_accept(string key, string raw)
    {
        Assert.Null(ValidateSetting(key, raw));
    }

    [Theory]
    [InlineData(KeyDefaultCover, "-0.1")] [InlineData(KeyDefaultCover, "abc")] [InlineData(KeyDefaultCover, "")] [InlineData(KeyDefaultCover, null)]
    [InlineData(KeyRoundTo, "0")] [InlineData(KeyRoundTo, "2.5")] [InlineData(KeyRoundTo, "-5")]
    [InlineData(KeyInjOffsetDays, "-1")] [InlineData(KeyInjOffsetDays, "1.5")]
    [InlineData(KeyTrimLastDay, "maybe")] [InlineData(KeyWarnStatus, "2")]
    public void ValidateSetting_rejects_with_a_message_naming_the_key(string key, string? raw)
    {
        var err = ValidateSetting(key, raw);
        Assert.NotNull(err);
        Assert.Contains(key, err);
    }

    [Theory]
    [InlineData("0", "1.5")] [InlineData("20", "0.7")] [InlineData("12.5", "0")]
    public void ValidateTier_accepts_numeric_pairs(string min, string cover) => Assert.Null(ValidateTier(min, cover));

    [Theory]
    [InlineData("-1", "0.7")] [InlineData("abc", "0.7")] [InlineData("20", "-0.1")] [InlineData("20", "x")] [InlineData("", "0.7")] [InlineData("20", null)]
    public void ValidateTier_rejects_bad_pairs(string min, string? cover) => Assert.NotNull(ValidateTier(min, cover));

    [Fact]
    public void Setting_kinds_drive_the_editor_control()
    {
        Assert.Equal(SettingKind.Flag,   KindOf(KeyTrimLastDay));
        Assert.Equal(SettingKind.Flag,   KindOf(KeyPullForward));
        Assert.Equal(SettingKind.Flag,   KindOf(KeySafetyTerm));
        Assert.Equal(SettingKind.Flag,   KindOf(KeyWarnStatus));
        Assert.Equal(SettingKind.Int,    KindOf(KeyRoundTo));
        Assert.Equal(SettingKind.Int,    KindOf(KeyInjOffsetDays));
        Assert.Equal(SettingKind.Number, KindOf(KeyDefaultCover));
        Assert.Equal(SettingKind.Pattern, KindOf(KeyDefaultPattern));
        // 교대 시간 폴백 두 키는 다이얼로그에서 뺐다(APS 는 패턴 없이는 조회가 막혀 폴백에 닿지 않는다) — 남은 행은 모르는 키처럼 Text 로 보인다
        Assert.Equal(SettingKind.Text,   KindOf(KeyShiftDayH));
        Assert.Equal(SettingKind.Text,   KindOf(KeyShiftNightH));
        // 폐기 키 — 다이얼로그는 남은 행을 모르는 키처럼 보이지 않고 숨긴다, 마이그레이션은 행을 지운다
        Assert.True(IsRetired(KeyShiftDayH));
        Assert.True(IsRetired(" shift_night_h "));
        Assert.False(IsRetired(KeyDefaultPattern));
        Assert.False(IsRetired("SOME_FUTURE_KEY"));
        Assert.Equal(SettingKind.Text,   KindOf("SOME_FUTURE_KEY"));
        Assert.Equal(SettingKind.Int,    KindOf("round_to"));   // 키는 대소문자 무관(Parse 와 같다)
    }

    [Fact]
    public void Known_settings_list_the_eight_keys_with_defaults_that_parse_without_setting_warnings()
    {
        Assert.Equal(new[] { KeyDefaultCover, KeyRoundTo, KeyTrimLastDay, KeyInjOffsetDays, KeyDefaultPattern, KeyPullForward, KeySafetyTerm, KeyWarnStatus },
                     KnownSettings.Select(k => k.Key));
        Assert.All(KnownSettings, k => Assert.Null(ValidateSetting(k.Key, k.Default)));

        var rows = KnownSettings.Select(k => S(k.Key, k.Default)).ToList();
        var p = Parse(rows, NoStages);

        Assert.Single(p.Warnings);   // 구간표만 없다
        Assert.Contains(GroupCoverTier, p.Warnings[0]);
        var d = Settings.Default();
        Assert.Equal(d.DefaultDaysOfCover, p.Settings.DefaultDaysOfCover);
        Assert.Equal(d.RoundTo, p.Settings.RoundTo);
        Assert.Equal(1, p.Settings.StageDefaults.Injection.OffsetDays);
        Assert.Equal(d.Shift.Day, p.Settings.Shift.Day);
        Assert.Equal(ApsOptions.Default, p.Options);
    }
}
