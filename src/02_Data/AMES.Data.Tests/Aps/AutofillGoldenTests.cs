using AMES.Data.Aps;
using AMES.Data.Aps.Contracts;
using AMES.Data.Aps.Domain;
using System.Text.Json;
using Xunit;

namespace AMES.Data.Tests.Aps;

public class AutofillGoldenTests
{
    static readonly Settings S = Fixtures.LoadSettings();
    static readonly Dictionary<string, LineInfo> L = Fixtures.LoadLines();

    static PlanBundle PlanBundle() => JsonSerializer.Deserialize<PlanBundle>(Fixtures.Json("plan_LQ10.json")["bundle"]!.ToJsonString(), ApsJson.Options)!;

    [Fact]
    public void Autofill_on_plan_bundle_reproduces_autofill_fixture_supplies()
    {
        var bundle = PlanBundle();
        var expected = Fixtures.Json("autofill_LQ10.json");
        Autofill.Run(bundle, new ShiftRules(S, L), new StageRules(S, L));
        var got = bundle.Assembly.SelectMany(r => r.Days.Select(d => (r.PartNo, d.Date, d.Supply))).ToList();
        var exp = expected["bundle"]!["assembly"]!.AsArray().SelectMany(r => r!["days"]!.AsArray()
            .Select(d => (r["partNo"]!.GetValue<string>(), d!["date"]!.GetValue<string>(), d["supply"]!.GetValue<double>()))).ToList();
        Assert.Equal(exp, got);
    }

    /// traces 는 완제품 × 날짜(날짜 우선) 300건 — prevClosing · target · rawNeed · closing 까지 픽스처와 같다.
    [Fact]
    public void Autofill_traces_match_fixture()
    {
        var bundle = PlanBundle();
        var (_, traces) = Autofill.RunWithTraces(bundle, new ShiftRules(S, L), new StageRules(S, L));
        GoldenJson.ShouldMatch(traces, Fixtures.Json("autofill_LQ10.json")["traces"]!);
    }

    [Fact]
    public void Last_day_fills_demand_only_and_locked_cells_are_kept()
    {
        var bundle = PlanBundle();
        var r = bundle.Assembly.First(x => x.PartNo == "82301-P8140LBF");
        r.Days[0].Supply = 999; r.Days[0].Locked = true;
        Autofill.Run(bundle, new ShiftRules(S, L), new StageRules(S, L));
        Assert.Equal(999, r.Days[0].Supply);
        Assert.Equal(0, r.Days[^1].Supply);   // 마지막 날은 수요 − 전일 마감만 채우고, 재고가 넉넉하면 0
    }

    /// 능력 초과일에도 원본은 경고를 내지 않는다(픽스처 4일 초과 · scheduleWarnings 에 없음). 최소치(수요)조차 못 담을 때만 알린다.
    [Fact]
    public void Warns_only_when_even_minimum_demand_exceeds_line_capacity()
    {
        var bundle = PlanBundle();
        var none = Autofill.Run(bundle, new ShiftRules(S, L), new StageRules(S, L));
        Assert.Empty(none);

        var big = PlanBundle();
        foreach (var r in big.Assembly) r.Days[0].Demand += 100;   // 첫날 최소치 합 > 2902
        var warn = Autofill.Run(big, new ShiftRules(S, L), new StageRules(S, L));
        Assert.Single(warn, w => w.Contains("라인 능력") && w.Contains("2026-09-24"));
    }
}
