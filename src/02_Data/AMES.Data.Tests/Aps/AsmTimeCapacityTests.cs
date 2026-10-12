using AMES.Data.Aps.Contracts;
using AMES.Data.Aps.Domain;
using Xunit;

namespace AMES.Data.Tests.Aps;

/// <summary>
/// 완제품 라인 능력을 품번별 BOP 사이클로 재는 시간 모드(2026-10-12 사용자 결정 "품번마다 사이클이 다르니 그대로 계산").
/// 라인 LineShift 에 교대 시간이 있고 그 라인 행 전부에 AssemblyRow.CycleSec 가 있으면 Autofill 은 능력을 분(가동 시간 × 60)으로,
/// 행의 무게를 CycleSec ÷ 60(분/EA)으로 센다. 하나라도 CycleSec 가 없으면 종전 수량 모드(AsmCapFor) — 골든 픽스처는 CycleSec 가 없다.
/// </summary>
public class AsmTimeCapacityTests
{
    static readonly string[] Dates = { "2026-10-13", "2026-10-14" };

    static readonly Dictionary<string, LineInfo> Lines = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A100"] = new LineInfo { LineCd = "A100", Type = "assembly" },
    };

    static AssemblyRow Asm(string partNo, double? cycleSec, double[] demand, double[]? supply = null, bool[]? locked = null) => new()
    {
        PartNo = partNo, LineCd = "A100", CycleSec = cycleSec,
        Days = Dates.Select((d, i) => new AssemblyDay { Date = d, Demand = demand[i], Supply = supply?[i] ?? 0, Locked = locked?[i] ?? false }).ToList(),
    };

    static PlanBundle Bundle(params AssemblyRow[] asm) => new()
    {
        Line = Lines["A100"], BaseDate = Dates[0], Dates = Dates.ToList(), Assembly = asm.ToList(),
    };

    /// 커버 1일 · 포장 1 · A100 가동 hours 시간(DailyCap 없음)
    static Settings Cover1(double hours)
    {
        var s = Settings.Default();
        s.CoverTiers = new(); s.DefaultDaysOfCover = 1; s.RoundTo = 1;
        s.LineShifts.Add(new LineShift { LineCd = "A100", Day = hours, Night = 0 });
        return s;
    }

    [Fact]
    public void Trims_by_minutes_so_a_slow_item_costs_more_capacity()
    {
        // 0일: P1(60초) 수요 100·다음날 100 → need 200·min 100, P2(120초) 수요 100·다음날 150 → need 250·min 100.
        // 분 = 200×1 + 250×2 = 700 > 600(10h) → 초과 100분. extra 는 P2(150) > P1(100) 이라 P2 부터 ceil(100 ÷ 2) = 50 EA 깎는다 → P2 200, P1 200
        var s = Cover1(10);
        var b = Bundle(Asm("P1", 60, new double[] { 100, 100 }), Asm("P2", 120, new double[] { 100, 150 }));

        var warnings = Autofill.Run(b, new ShiftRules(s, Lines), new StageRules(s, Lines));

        Assert.Equal(200, b.Assembly[0].Days[0].Supply);
        Assert.Equal(200, b.Assembly[1].Days[0].Supply);
        Assert.DoesNotContain(warnings, w => w.StartsWith("A100 " + Dates[0]));
    }

    [Fact]
    public void Tie_on_extra_cuts_in_row_order_and_the_cut_is_in_whole_units()
    {
        // 둘 다 need 200·min 100(extra 100) → 행 순서 P1 부터. 분 = 200 + 400 = 600 > 540(9h) → 초과 60분 → P1 60 EA → 140, P2 200. 140 + 400 = 540
        var s = Cover1(9);
        var b = Bundle(Asm("P1", 60, new double[] { 100, 100 }), Asm("P2", 120, new double[] { 100, 100 }));

        Autofill.Run(b, new ShiftRules(s, Lines), new StageRules(s, Lines));

        Assert.Equal(140, b.Assembly[0].Days[0].Supply);
        Assert.Equal(200, b.Assembly[1].Days[0].Supply);
    }

    [Fact]
    public void Locked_cells_consume_capacity_by_their_own_cycle()
    {
        // P2 잠김 250 EA × 2분 = 500분, P1 need 200(200분) → 700 > 600 → P1 에서 100 EA(extra 100) 깎여 100. 잠긴 값은 그대로
        var s = Cover1(10);
        var b = Bundle(Asm("P1", 60, new double[] { 100, 100 }),
                       Asm("P2", 120, new double[] { 100, 100 }, supply: new double[] { 250, 0 }, locked: new[] { true, false }));

        Autofill.Run(b, new ShiftRules(s, Lines), new StageRules(s, Lines));

        Assert.Equal(100, b.Assembly[0].Days[0].Supply);
        Assert.Equal(250, b.Assembly[1].Days[0].Supply);
    }

    [Fact]
    public void Shortfall_below_minimum_warns_in_minutes_and_hours()
    {
        // 커버 0 → need = min. P1 300 EA × 1분 + P2 200 EA × 2분 = 700 > 600 → 깎을 extra 가 없어 100분 부족 경고
        var s = Cover1(10); s.DefaultDaysOfCover = 0;
        var b = Bundle(Asm("P1", 60, new double[] { 300, 0 }), Asm("P2", 120, new double[] { 200, 0 }));

        var warnings = Autofill.Run(b, new ShiftRules(s, Lines), new StageRules(s, Lines));

        Assert.Equal(300, b.Assembly[0].Days[0].Supply);
        Assert.Equal(200, b.Assembly[1].Days[0].Supply);
        var w = Assert.Single(warnings, x => x.StartsWith("A100 " + Dates[0]));
        Assert.Contains("10h", w); Assert.Contains("100분", w);
    }

    [Fact]
    public void Pull_forward_measures_the_earlier_day_room_in_minutes()
    {
        // 커버 0, P1 120초. 1일 수요 400 = 800분 > 600 → 200분 초과를 0일(여유 600분)로 당긴다 — 100 EA
        var s = Cover1(10); s.DefaultDaysOfCover = 0;
        var b = Bundle(Asm("P1", 120, new double[] { 0, 400 }));

        var warnings = Autofill.Run(b, new ShiftRules(s, Lines), new StageRules(s, Lines), new ApsOptions(PullForwardSupply: true));

        Assert.Empty(warnings);
        Assert.Equal(new double[] { 100, 300 }, b.Assembly[0].Days.Select(d => d.Supply).ToArray());
    }

    [Fact]
    public void A_row_without_cycle_keeps_the_whole_line_in_unit_mode()
    {
        // P2 에 CycleSec 가 없으면 수량 모드 — 이 라인은 DailyCap 도 UPH 도 없어 제한 없음(AsmCapFor null). 시간 모드였다면 700분 > 600 이라 깎였을 값
        var s = Cover1(10);
        var b = Bundle(Asm("P1", 60, new double[] { 100, 100 }), Asm("P2", null, new double[] { 100, 150 }));

        var warnings = Autofill.Run(b, new ShiftRules(s, Lines), new StageRules(s, Lines));

        Assert.Empty(warnings);
        Assert.Equal(200, b.Assembly[0].Days[0].Supply);
        Assert.Equal(250, b.Assembly[1].Days[0].Supply);
    }

    [Fact]
    public void Daily_cap_line_ignores_cycles_and_stays_in_unit_mode()
    {
        // LineShift.DailyCap 이 있으면 종전 수량 능력 — 사이클이 있어도 분으로 재지 않는다(폴백 경로). P1 need 300(extra 200)·P2 250(extra 150), 550 > 500 → P1 부터 50 깎인다
        var s = Cover1(10); s.LineShifts.Clear();
        s.LineShifts.Add(new LineShift { LineCd = "A100", DailyCap = 500 });
        var b = Bundle(Asm("P1", 60, new double[] { 100, 200 }), Asm("P2", 120, new double[] { 100, 150 }));

        Autofill.Run(b, new ShiftRules(s, Lines), new StageRules(s, Lines));

        Assert.Equal(250, b.Assembly[0].Days[0].Supply);
        Assert.Equal(250, b.Assembly[1].Days[0].Supply);
    }
}
