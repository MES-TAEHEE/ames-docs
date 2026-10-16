using AMES.Data.Aps.Contracts;
using AMES.Data.Aps.Domain;
using Xunit;

namespace AMES.Data.Tests.Aps;

/// 형제(동시 취출)의 단위는 금형이 아니라 금형 × 색상 — 같은 금형이라도 색상이 다르면 따로 찍으므로 시간을 더하고, 같은 색상이면 한 shot 으로 본다.
public class MoldColorGroupingTests
{
    static readonly Settings S = Settings.Default();
    static readonly Dictionary<string, LineInfo> L = new(StringComparer.OrdinalIgnoreCase)
    {
        ["L1"] = new LineInfo { LineCd = "L1", Type = "assembly" },
        ["M1"] = new LineInfo { LineCd = "M1", Type = "injection" },
    };
    static readonly string[] Dates = { "2026-09-28", "2026-09-29" };

    static PlanBundle Bundle(string? colorA, string? colorB)
    {
        var b = new PlanBundle { Line = L["L1"], BaseDate = Dates[0], PrevDate = ApsCalendar.PrevDate(Dates[0]), Dates = Dates.ToList() };
        foreach (var (part, color) in new[] { ("XA", colorA), ("XB", colorB) })
        {
            b.Assembly.Add(new AssemblyRow
            {
                PartNo = part, LineCd = "L1",
                Days = Dates.Select(d => new AssemblyDay { Date = d, Demand = 60, Supply = 60, Locked = true }).ToList(),
            });
            b.Injection.Add(new InjectionRow
            {
                PartNo = part, LineCd = "M1", Uph = 60, PackSize = 1, MoldCode = "MOLD-1", MoldColor = color,
                Days = Dates.Select(d => new InjectionDay { Date = d }).ToList(),
            });
            b.Bom.Add(new BomEdge(part, part, 1));
        }
        return b;
    }

    static double HoursOn(PlanBundle b, string date)
    {
        var rules = new ShiftRules(S, L); var stages = new StageRules(S, L);
        InjectionScheduler.Reschedule(b, rules, stages);
        return PlanCalc.BuildLoads(b, rules).Single(l => l.LineCd == "M1" && l.Date == date).Hours;
    }

    [Theory]
    [InlineData("NNB", "NNB")]
    [InlineData(null, null)]
    public void Same_mold_and_same_color_share_one_shot(string? a, string? c)
        => Assert.Equal(1d, HoursOn(Bundle(a, c), Dates[0]));   // 60 EA ÷ 60 UPH, 형제 최대 1개분만

    [Fact]
    public void Same_mold_but_different_colors_are_separate_runs()
        => Assert.Equal(2d, HoursOn(Bundle("NNB", "YGU"), Dates[0]));   // 색상마다 따로 찍는다 — 1h + 1h

    [Fact]
    public void Group_key_is_mold_plus_color_and_falls_back_to_part()
    {
        Assert.Equal("MOLD-1/NNB", new InjectionRow { PartNo = "X", MoldCode = "MOLD-1", MoldColor = "NNB" }.MoldGroupKey());
        Assert.Equal("MOLD-1",     new InjectionRow { PartNo = "X", MoldCode = "MOLD-1" }.MoldGroupKey());
        Assert.Equal("X",          new InjectionRow { PartNo = "X", MoldColor = "NNB" }.MoldGroupKey());
    }
}
