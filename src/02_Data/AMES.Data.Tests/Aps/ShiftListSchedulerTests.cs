using AMES.Data.Aps;
using AMES.Data.Aps.Contracts;
using AMES.Data.Aps.Domain;
using Xunit;

namespace AMES.Data.Tests.Aps;

/// <summary>교대 목록 모드(LineShift.Shifts 있음)의 스케줄러 — 비율 분할·파생 호환값·잠긴 칸 정렬·부하 교대별 시간. 목록이 없으면 종전 경로(골든 테스트가 정본).</summary>
public class ShiftListSchedulerTests
{
    static (PlanBundle b, Settings s, Dictionary<string, LineInfo> lines) Fixture(List<ShiftHours> shifts, double openingStock = 0)
    {
        var dates = new List<string> { "2026-10-07", "2026-10-08" };
        var b = new PlanBundle
        {
            Line = new LineInfo { LineCd = "FG", Type = "assembly", Active = true },
            BaseDate = dates[0], PrevDate = "2026-10-06",
            Dates = dates,
            Assembly = { new AssemblyRow { PartNo = "P1", LineCd = "FG", Days = dates.Select(d => new AssemblyDay { Date = d, Demand = 0, Supply = d == dates[0] ? 704 : 0 }).ToList() } },
            Injection = { new InjectionRow { PartNo = "K1", LineCd = "INJ", Uph = 60, PackSize = 8, MoldCode = "M1", OpeningStock = openingStock,
                                             Days = dates.Select(d => new InjectionDay { Date = d }).ToList() } },
            Bom = { new BomEdge("P1", "K1", 1) },
        };
        var s = Settings.Default();
        s.LineStages.Add(new LineStage { LineCd = "INJ", OffsetDays = 0 });
        s.LineShifts.Add(new LineShift { LineCd = "INJ", Day = shifts[0].Hours, Night = shifts.Skip(1).Sum(x => x.Hours), Shifts = shifts });
        var lines = new Dictionary<string, LineInfo>
        {
            ["FG"] = b.Line, ["INJ"] = new LineInfo { LineCd = "INJ", Type = "injection", Active = true },
        };
        return (b, s, lines);
    }

    [Fact]
    public void Shift_list_mode_splits_by_hours_ratio_and_derives_day_night()
    {
        var (b, s, lines) = Fixture(new() { new("A", 8), new("B", 8), new("C", 6) });
        var rules = new ShiftRules(s, lines); var stages = new StageRules(s, lines);

        var (warnings, traces) = InjectionScheduler.RescheduleWithTraces(b, rules, stages);

        var d = b.Injection[0].Days[0];
        Assert.NotNull(d.PlanShifts);
        Assert.Equal(new[] { "A", "B", "C" }, d.PlanShifts!.Select(x => x.Code).ToArray());
        Assert.Equal(704d, d.PlanShifts.Sum(x => x.Qty), 6);
        Assert.Equal(ShiftSplit.Proportional(704, s.LineShifts[0].Shifts!, 8).Select(x => x.Qty), d.PlanShifts.Select(x => x.Qty));
        Assert.Equal((d.PlanShifts[0].Qty, d.PlanShifts[1].Qty + d.PlanShifts[2].Qty), (d.PlanDay, d.PlanNight));   // 파생 호환값
        var t = traces.Single(x => x.PartNo == "K1" && x.Date == "2026-10-07");
        Assert.Equal(d.PlanShifts.Select(x => (x.Code, x.Qty)), t.PlanShifts!.Select(x => (x.Code, x.Qty)));
        Assert.Contains(t.Steps, st => st.Label.Contains("교대 몫") && st.Calc.Contains("비율"));
        Assert.DoesNotContain(warnings, w => w.Contains("능력"));
    }

    [Fact]
    public void Locked_cell_keeps_its_stored_shifts_aligned_to_the_current_pattern()
    {
        var (b, s, lines) = Fixture(new() { new("A", 8), new("B", 8), new("C", 6) });
        var d = b.Injection[0].Days[0];
        d.Locked = true; d.PlanDay = 200; d.PlanNight = 100; d.PlanShifts = new() { new("A", 200), new("N", 100) };   // N = 지금 패턴에 없는 교대
        var rules = new ShiftRules(s, lines); var stages = new StageRules(s, lines);

        InjectionScheduler.RescheduleWithTraces(b, rules, stages);

        Assert.Equal(new[] { ("A", 300d), ("B", 0d), ("C", 0d) }, d.PlanShifts!.Select(x => (x.Code, x.Qty)).ToArray());
        Assert.Equal((300d, 0d), (d.PlanDay, d.PlanNight));
    }

    [Fact]
    public void Legacy_mode_leaves_plan_shifts_null()
    {
        var (b, s, lines) = Fixture(new() { new("A", 8), new("B", 8) });
        s.LineShifts[0].Shifts = null;   // 골든 경로
        InjectionScheduler.RescheduleWithTraces(b, new ShiftRules(s, lines), new StageRules(s, lines));
        Assert.Null(b.Injection[0].Days[0].PlanShifts);
        Assert.Equal(704d, b.Injection[0].Days[0].PlanDay + b.Injection[0].Days[0].PlanNight, 6);
    }

    /// <summary>리뷰 F2: 부하 탭의 교대별 시간은 계획의 교대별 수량 ÷ UPH(금형 그룹 최대) — "차례로 채우기"가 아니라 계획과 같은 분할을 보인다.</summary>
    [Fact]
    public void Loads_follow_the_plans_per_shift_quantities()
    {
        var (b, s, lines) = Fixture(new() { new("A", 4), new("B", 4), new("C", 4) });
        var rules = new ShiftRules(s, lines); var stages = new StageRules(s, lines);
        InjectionScheduler.Reschedule(b, rules, stages);                 // 704 ÷ 60 = 11.73h, 비율 분할 232 / 232 / 240

        var (_, loads) = PlanCalc.Compute(b, rules, stages);

        var l = loads.Single(x => x.LineCd == "INJ" && x.Date == "2026-10-07");
        var ps = b.Injection[0].Days[0].PlanShifts!;
        Assert.Equal(ps.Select(x => (x.Code, Math.Round(x.Qty / 60, 2))), l.ShiftHours!.Select(x => (x.Code, Math.Round(x.Hours, 2))));
        Assert.Equal((Math.Round(ps[0].Qty / 60, 2), Math.Round((ps[1].Qty + ps[2].Qty) / 60, 2)), (l.DayHours, Math.Round(l.NightHours, 2)));
        Assert.Equal(12d, l.Capacity);
        Assert.Equal(11.73, l.Hours);
    }

    /// <summary>리뷰 F1: 같은 라인·날짜의 잠긴 블록(등록 계획·사용자 잠금)이 쓴 교대 시간을 빼고 남은 시간 비율로 자유 블록을 나눈다 — 종전 "잠긴 블록은 주간을 쓴다" 규칙의 교대 목록판.</summary>
    [Fact]
    public void Free_block_is_split_by_hours_remaining_after_locked_blocks()
    {
        var (b, s, lines) = Fixture(new() { new("A", 8), new("B", 8) });
        // 두 번째 사출품 K2(다른 금형)가 잠긴 채 A 교대를 다 쓴다: 480개 ÷ 60 UPH = 8h
        b.Injection.Add(new InjectionRow { PartNo = "K2", LineCd = "INJ", Uph = 60, PackSize = 8, MoldCode = "M2",
                                            Days = b.Dates.Select(d => new InjectionDay { Date = d, Locked = d == b.Dates[0], PlanDay = d == b.Dates[0] ? 480 : 0,
                                                                                          PlanShifts = d == b.Dates[0] ? new() { new("A", 480), new("B", 0) } : null }).ToList() });
        b.Assembly[0].Days[0].Supply = 240;                              // K1 소요 240 = 4h → 남은 시간은 B 8h 뿐
        var rules = new ShiftRules(s, lines); var stages = new StageRules(s, lines);

        InjectionScheduler.RescheduleWithTraces(b, rules, stages);

        var k1 = b.Injection.Single(r => r.PartNo == "K1").Days[0];
        Assert.Equal(new[] { ("A", 0d), ("B", 240d) }, k1.PlanShifts!.Select(x => (x.Code, x.Qty)).ToArray());
        var k2 = b.Injection.Single(r => r.PartNo == "K2").Days[0];
        Assert.Equal(new[] { ("A", 480d), ("B", 0d) }, k2.PlanShifts!.Select(x => (x.Code, x.Qty)).ToArray());   // 잠긴 칸은 그대로
    }

    /// <summary>리뷰 F1 보충: 잠긴 블록이 모든 교대를 다 썼으면(남은 시간 0) 전체 시간 비율로 되돌아간다 — 수량이 사라지지 않는다.</summary>
    [Fact]
    public void Free_block_falls_back_to_full_hours_when_nothing_remains()
    {
        var (b, s, lines) = Fixture(new() { new("A", 8), new("B", 4) });
        b.Injection.Add(new InjectionRow { PartNo = "K2", LineCd = "INJ", Uph = 60, PackSize = 8, MoldCode = "M2",
                                            Days = b.Dates.Select(d => new InjectionDay { Date = d, Locked = d == b.Dates[0], PlanDay = d == b.Dates[0] ? 480 : 0, PlanNight = d == b.Dates[0] ? 240 : 0,
                                                                                          PlanShifts = d == b.Dates[0] ? new() { new("A", 480), new("B", 240) } : null }).ToList() });
        b.Assembly[0].Days[0].Supply = 120;
        InjectionScheduler.RescheduleWithTraces(b, new ShiftRules(s, lines), new StageRules(s, lines));

        var k1 = b.Injection.Single(r => r.PartNo == "K1").Days[0];
        Assert.Equal(120d, k1.PlanShifts!.Sum(x => x.Qty), 6);
        Assert.Equal(new[] { ("A", 80d), ("B", 40d) }, k1.PlanShifts.Select(x => (x.Code, x.Qty)).ToArray());   // 8:4 비율, 8 단위 내림
    }
}
