using AMES.Data.Aps;
using AMES.Data.Aps.Contracts;
using AMES.Data.Aps.Domain;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace AMES.Data.Tests.Aps;

public class PlanCalcGoldenTests
{
    static readonly Settings S = Fixtures.LoadSettings();
    static readonly Dictionary<string, LineInfo> L = Fixtures.LoadLines();

    [Theory]
    [InlineData("plan_LQ10.json")]
    [InlineData("plan_CV01.json")]
    [InlineData("plan_H1A100.json")]
    public void Bundle_reproduces_result_and_loads(string name)
    {
        var fx = JsonNode.Parse(File.ReadAllText(Fixtures.Path(name)))!;
        var bundle = JsonSerializer.Deserialize<PlanBundle>(fx["bundle"]!.ToJsonString(), ApsJson.Options)!;
        var (result, loads) = PlanCalc.Compute(bundle, new ShiftRules(S, L), new StageRules(S, L));
        GoldenJson.ShouldMatch(result, fx["result"]!);
        GoldenJson.ShouldMatch(loads, fx["loads"]!);
        // traces 는 스케줄러의 판단 근거(앞당기기 · 주간 잔여 · 동시취출)라 SchedulerGoldenTests 에서 검증한다.
    }

    /// calc_LQ10 = autofill 결과 bundle 을 reschedule 한 것; result · loads 는 PlanCalc 만으로 같아야 한다.
    [Fact]
    public void Calc_after_autofill_reproduces_calc_fixture_result()
    {
        var fx = JsonNode.Parse(File.ReadAllText(Fixtures.Path("calc_LQ10.json")))!;
        var bundle = JsonSerializer.Deserialize<PlanBundle>(fx["bundle"]!.ToJsonString(), ApsJson.Options)!;
        var (result, loads) = PlanCalc.Compute(bundle, new ShiftRules(S, L), new StageRules(S, L));
        GoldenJson.ShouldMatch(result, fx["result"]!);
        GoldenJson.ShouldMatch(loads, fx["loads"]!);
    }

    [Fact]
    public void Zero_uph_part_is_excluded_from_loads()
    {
        var fx = JsonNode.Parse(File.ReadAllText(Fixtures.Path("calc_LQ10.json")))!;
        var bundle = JsonSerializer.Deserialize<PlanBundle>(fx["bundle"]!.ToJsonString(), ApsJson.Options)!;
        var r = bundle.Injection.First(x => x.LineCd == "M2A100" && x.Days.Any(d => d.PlanDay > 0));
        var before = PlanCalc.Compute(bundle, new ShiftRules(S, L), new StageRules(S, L)).loads.First(l => l.LineCd == "M2A100" && l.Hours > 0);
        foreach (var s in bundle.Injection.Where(x => x.MoldCode == r.MoldCode)) s.Uph = 0;   // 같은 금형 형제까지 UPH 없음
        var after = PlanCalc.Compute(bundle, new ShiftRules(S, L), new StageRules(S, L)).loads.First(l => l.LineCd == "M2A100" && l.Date == before.Date);
        Assert.True(after.Hours < before.Hours, $"after {after.Hours} < before {before.Hours}");
        Assert.True(after.Hours >= 0);
    }
}
