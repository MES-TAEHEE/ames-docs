using AMES.Data.Aps;
using AMES.Data.Aps.Contracts;
using AMES.Data.Aps.Domain;
using Xunit;

namespace AMES.Data.Tests.Aps;

/// 완제품 공급 칸을 올리면 그 소요가 떨어지는 사출 칸(선행일 반영)의 등록 계획 잠금을 자동으로 푼다 — 잠긴 채면 엔진이 늘어난 소요를
/// 첫 번째 안 잠긴 뒤 날짜로 밀어 WO 가 조립일 뒤에 나온다(실행 #507, 10-15 사용자 결정 "잠금을 풀어서 자동으로 늘리게").
public class ApsSupplyEditTests
{
    static readonly Dictionary<string, LineInfo> L = new(StringComparer.OrdinalIgnoreCase)
    {
        ["L1"] = new LineInfo { LineCd = "L1", Type = "assembly" },
        ["M1"] = new LineInfo { LineCd = "M1", Type = "injection" },
    };
    static readonly string[] Dates = { "2026-10-15", "2026-10-16", "2026-10-19", "2026-10-20" };

    static StageRules Stages(int offset) { var s = Settings.Default(); s.StageDefaults.Injection.OffsetDays = offset; return new StageRules(s, L, foldPreHorizon: true); }

    static PlanBundle Bundle()
    {
        AssemblyRow Asm(string p) => new() { PartNo = p, LineCd = "L1", Days = Dates.Select(d => new AssemblyDay { Date = d, Supply = 64, Locked = true }).ToList() };
        InjectionRow Inj(string p) => new() { PartNo = p, LineCd = "M1", Uph = 60, PackSize = 32, Days = Dates.Select(d => new InjectionDay { Date = d, PlanDay = 64, Locked = true }).ToList() };
        return new PlanBundle
        {
            Line = L["L1"], BaseDate = Dates[0], PrevDate = ApsCalendar.PrevDate(Dates[0]), Dates = Dates.ToList(),
            Assembly = { Asm("LH"), Asm("RH") },
            Injection = { Inj("CORE-LH"), Inj("CORE-RH") },
            Bom = { new BomEdge("LH", "CORE-LH", 1), new BomEdge("RH", "CORE-RH", 1) },
        };
    }

    [Fact]
    public void Unlocks_the_injection_cell_offset_days_before_the_supply_day_for_that_items_cores_only()
    {
        var b = Bundle();
        var unlocked = ApsSupplyEdit.UnlockInjectionFor(b, Stages(1), "LH", dayIndex: 1);   // 10-16 공급 → 10-15 사출

        Assert.Equal(new[] { new ApsSupplyEdit.Unlocked("CORE-LH", "2026-10-15") }, unlocked);
        Assert.False(b.Injection[0].Days[0].Locked);
        Assert.True(b.Injection[0].Days[1].Locked);                          // 같은 품번의 다른 날은 그대로
        Assert.All(b.Injection[1].Days, d => Assert.True(d.Locked));          // RH 코어는 건드리지 않는다
    }

    [Fact]
    public void Supply_before_the_horizon_unlocks_the_first_injection_day()
    {
        var b = Bundle();
        var unlocked = ApsSupplyEdit.UnlockInjectionFor(b, Stages(2), "LH", dayIndex: 1);   // 10-16 − 2일 = 계획판 앞 → 기준일에 몬다

        Assert.Equal("2026-10-15", Assert.Single(unlocked).Date);
        Assert.False(b.Injection[0].Days[0].Locked);
    }

    [Fact]
    public void Already_open_cells_and_unrelated_items_are_not_reported()
    {
        var b = Bundle();
        b.Injection[0].Days[0].Locked = false;
        Assert.Empty(ApsSupplyEdit.UnlockInjectionFor(b, Stages(1), "LH", dayIndex: 1));
        Assert.Empty(ApsSupplyEdit.UnlockInjectionFor(b, Stages(1), "NOPE", dayIndex: 1));
        Assert.Empty(ApsSupplyEdit.UnlockInjectionFor(b, Stages(1), "LH", dayIndex: 99));
    }

    [Fact]
    public void Same_item_rule_unlocks_the_items_own_injection_row()
    {
        var b = Bundle();
        b.Injection.Add(new InjectionRow { PartNo = "LH", LineCd = "M1", Uph = 60, PackSize = 1, Days = Dates.Select(d => new InjectionDay { Date = d, Locked = true }).ToList() });
        b.Bom.Add(new BomEdge("LH", "LH", 1));

        var unlocked = ApsSupplyEdit.UnlockInjectionFor(b, Stages(0), "LH", dayIndex: 2);
        Assert.Equal(2, unlocked.Count);
        Assert.Contains(new ApsSupplyEdit.Unlocked("LH", "2026-10-19"), unlocked);
        Assert.Contains(new ApsSupplyEdit.Unlocked("CORE-LH", "2026-10-19"), unlocked);
    }
}
