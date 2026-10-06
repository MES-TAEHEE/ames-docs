using AMES.Data.Aps.Contracts;
using AMES.Data.Aps.Domain;
using Xunit;

namespace AMES.Data.Tests.Aps;

public class ShiftRulesTests
{
    static readonly Dictionary<string, LineInfo> L = Fixtures.LoadLines();
    readonly ShiftRules _r = new(Fixtures.LoadSettings(), L);

    [Fact]
    public void Injection_line_without_line_shift_uses_factory_default()
    {
        var (d, n, src, _) = _r.ShiftFor("M2A100", "2026-09-24", injection: true);
        Assert.Equal((10.5, 11.5, "사출 기본"), (d, n, src));
    }

    [Fact]
    public void LQ10_capacity_is_uph_times_stations_times_hours()
    {
        var cap = _r.AsmCapFor("LQ10", "2026-09-24")!.Value;
        Assert.Equal(2902, cap.cap);   // floor(68 × 4 × 10.67)
        Assert.Equal(4, cap.stations);
        Assert.Null(_r.AsmCapFor("CV01", "2026-09-24"));   // 라인별 근무가 없고 dailyCapacity 도 0
    }

    [Fact]
    public void Date_exception_beats_line_shift()
    {
        var s = Fixtures.LoadSettings();
        s.ShiftExceptions.Add(new ShiftException { Date = "2026-09-26", LineCd = null, Day = 0, Night = 0, Note = "휴무" });
        var r = new ShiftRules(s, L);
        Assert.Equal((0.0, 0.0, "예외", (string?)"휴무"), r.ShiftFor("LQ10", "2026-09-26", false));
        Assert.Equal("라인", r.ShiftFor("LQ10", "2026-09-25", false).src);
    }

    [Fact]
    public void Pack_cover_uph_rules_match_frontend()
    {
        Assert.Equal(5, _r.PackFor("82301-P8140LBF"));   // packRules 비어 있으면 roundTo
        Assert.Equal(15, ShiftRules.RoundUp(14.8, 5));
        Assert.Equal(0, ShiftRules.RoundUp(0, 5));
        Assert.Equal(324, ShiftRules.RoundUp(292, 36));
        Assert.Equal(0.4, _r.CoverFor(139.4));
        Assert.Equal(0.7, _r.CoverFor(33.8));
        Assert.Equal(1.5, _r.CoverFor(11.2));
        Assert.Equal(58.88, _r.UphRuleFor("82310-P8000D4P")!.Uph);
        Assert.Equal("82310/20-P8000", _r.UphRuleFor("82310-P8000D4P")!.MoldGroup);
        Assert.Null(_r.UphRuleFor("99999-ZZZZZ"));
        Assert.True(_r.TrimLastDay);
    }

    /// 스펙 §2.4: LineShift.DailyCap 이 있으면 그 값(uph 0 · 줄 1 · 시간 0), 없으면 종전 floor(uph × st × h). 픽스처에는 dailyCap 키가 없다.
    [Fact]
    public void DailyCap_overrides_uph_formula_only_when_present()
    {
        var s = Fixtures.LoadSettings();
        Assert.Null(s.LineShifts.First(x => x.LineCd == "LQ10").DailyCap);
        Assert.Equal(2902, new ShiftRules(s, L).AsmCapFor("LQ10", "2026-09-24")!.Value.cap);

        s.LineShifts.First(x => x.LineCd == "LQ10").DailyCap = 1500;
        Assert.Equal((1500.0, 0.0, 1, 0.0), new ShiftRules(s, L).AsmCapFor("LQ10", "2026-09-24")!.Value);
    }
}
