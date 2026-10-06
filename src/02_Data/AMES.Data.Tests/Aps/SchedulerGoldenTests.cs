using AMES.Data.Aps;
using AMES.Data.Aps.Contracts;
using AMES.Data.Aps.Domain;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace AMES.Data.Tests.Aps;

public class SchedulerGoldenTests
{
    static readonly Settings S = Fixtures.LoadSettings();
    static readonly Dictionary<string, LineInfo> L = Fixtures.LoadLines();

    static PlanBundle Cleared(string fixture)
    {
        var bundle = JsonSerializer.Deserialize<PlanBundle>(Fixtures.Json(fixture)["bundle"]!.ToJsonString(), ApsJson.Options)!;
        foreach (var r in bundle.Injection) foreach (var d in r.Days) { d.PlanDay = 0; d.PlanNight = 0; d.Locked = false; }   // 스케줄러가 처음부터 채운다
        return bundle;
    }

    /// autofill 뒤 reschedule 한 원본(calc_LQ10): 셀 180 + 경고 2 + 근거 트레이스 180 (앞당기기 · 주간 몫 비율 · 동시취출 포함).
    [Fact]
    public void Reschedule_reproduces_calc_fixture_plans_warnings_and_traces()
    {
        var bundle = Cleared("autofill_LQ10.json");
        var (warnings, traces) = InjectionScheduler.RescheduleWithTraces(bundle, new ShiftRules(S, L), new StageRules(S, L));
        var calc = Fixtures.Json("calc_LQ10.json");
        GoldenJson.ShouldMatch(bundle.Injection, calc["bundle"]!["injection"]!);
        Assert.Equal(calc["scheduleWarnings"]!.AsArray().Select(w => w!.GetValue<string>()).ToList(), warnings);
        GoldenJson.ShouldMatch(traces, calc["traces"]!);
    }

    /// 첫 화면(등록 계획 기준, plan_*.json): GET /api/plan 도 스케줄러를 돌린다 — LQ10 16칸, CV01 · H1A100 0칸.
    [Theory]
    [InlineData("plan_LQ10.json")]
    [InlineData("plan_CV01.json")]
    [InlineData("plan_H1A100.json")]
    public void Reschedule_reproduces_plan_fixture_plans_and_traces(string name)
    {
        var bundle = Cleared(name);
        var (_, traces) = InjectionScheduler.RescheduleWithTraces(bundle, new ShiftRules(S, L), new StageRules(S, L));
        var fx = Fixtures.Json(name);
        GoldenJson.ShouldMatch(bundle.Injection, fx["bundle"]!["injection"]!);
        GoldenJson.ShouldMatch(traces, fx["traces"]!);
    }

    /// 2026-09-24 원본 재조회 두 건: plan_LQ10_b(2박스 한 건 앞당기기 · 여유 없음 경고 2) · plan_all(전체 라인, pack 0 품번, 11박스 앞당기기, 이동 6건 요약).
    /// GET /api/plan 의 bundle.warnings 뒤에 스케줄러 경고가 붙는다. plan_all 의 87753-R5000 09-28 은 주간/야간 경계 한 칸이 원본과 1 차이라 뺀다.
    [Theory]
    [InlineData("plan_LQ10_b.json", "")]
    [InlineData("plan_all.json", "87753-R5000")]
    public void Reschedule_reproduces_same_day_originals_with_warnings(string name, string skipPart)
    {
        var bundle = Cleared(name);
        var (warnings, traces) = InjectionScheduler.RescheduleWithTraces(bundle, new ShiftRules(S, L), new StageRules(S, L));
        var fx = Fixtures.Json(name);
        var expectedInj = fx["bundle"]!["injection"]!.AsArray().Where(r => r!["partNo"]!.GetValue<string>() != skipPart).Select(r => JsonNode.Parse(r!.ToJsonString())!).ToList();
        GoldenJson.ShouldMatch(bundle.Injection.Where(r => r.PartNo != skipPart).ToList(), new JsonArray(expectedInj.ToArray()));
        var expectedTraces = fx["traces"]!.AsArray().Where(t => t!["partNo"]!.GetValue<string>() != skipPart).Select(t => JsonNode.Parse(t!.ToJsonString())!).ToList();
        GoldenJson.ShouldMatch(traces.Where(t => t.PartNo != skipPart).ToList(), new JsonArray(expectedTraces.ToArray()));
        var expectedWarnings = fx["bundle"]!["warnings"]!.AsArray().Select(w => w!.GetValue<string>()).Where(w => w.Contains("앞당") && !w.StartsWith("공정 단계")).ToList();
        Assert.Equal(expectedWarnings, warnings);
    }

    [Fact]
    public void Locked_cells_survive_reschedule_and_shortage_moves_to_next_day()
    {
        var bundle = Cleared("autofill_LQ10.json");
        var r = bundle.Injection.First(x => x.PartNo == "82310-P8000RBQ");
        r.Days[1].PlanDay = 36; r.Days[1].PlanNight = 0; r.Days[1].Locked = true;
        InjectionScheduler.Reschedule(bundle, new ShiftRules(S, L), new StageRules(S, L));
        Assert.Equal((36.0, 0.0), (r.Days[1].PlanDay, r.Days[1].PlanNight));
        Assert.True(r.Days[2].PlanDay + r.Days[2].PlanNight > 612, "잠긴 날 부족분이 다음 날로 밀린다");
    }

    /// reschedule 없이 계산만 할 때(수동 편집 뒤)도 근거는 나온다 — 입력한 계획을 그대로 설명한다.
    [Fact]
    public void Explain_keeps_plans_and_describes_them()
    {
        var bundle = JsonSerializer.Deserialize<PlanBundle>(Fixtures.Json("calc_LQ10.json")["bundle"]!.ToJsonString(), ApsJson.Options)!;
        var before = JsonSerializer.Serialize(bundle.Injection, ApsJson.Options);
        var traces = InjectionScheduler.Explain(bundle, new ShiftRules(S, L), new StageRules(S, L));
        Assert.Equal(before, JsonSerializer.Serialize(bundle.Injection, ApsJson.Options));
        Assert.Equal(180, traces.Count);
        var t = traces.Single(x => x.PartNo == "82310-P8000RBQ" && x.Date == "2026-09-25");
        Assert.Equal(612, t.Planned);
        Assert.Contains(t.Steps, s => s.Label.Contains("생산량") && s.Calc.Contains("입력한 계획"));
    }

    /// 리뷰 Important 1: 앞당길 곳(09-25)의 금형 칸이 잠겨 있으면 그 날로 옮기지 않는다 — 옮긴 박스가 사라지면 안 된다.
    [Fact]
    public void Pull_forward_skips_days_where_the_mold_is_locked_and_never_loses_boxes()
    {
        var bundle = Cleared("autofill_LQ10.json");
        foreach (var p in new[] { "83313-P8000RBQ", "83323-P8000RBQ" })
        { var d = bundle.Injection.First(x => x.PartNo == p).Days[1]; d.PlanDay = 184; d.PlanNight = 0; d.Locked = true; }   // 09-25 잠금
        var (warnings, traces) = InjectionScheduler.RescheduleWithTraces(bundle, new ShiftRules(S, L), new StageRules(S, L));
        Assert.All(bundle.Injection.SelectMany(r => r.Days), d => Assert.True(d.PlanDay >= 0 && d.PlanNight >= 0));
        foreach (var p in new[] { "83313-P8000RBQ", "83323-P8000RBQ" })
        {
            var d = bundle.Injection.First(x => x.PartNo == p).Days[1];
            Assert.Equal((184.0, 0.0), (d.PlanDay, d.PlanNight));
        }
        foreach (var t in traces)
            if (t.Steps.Any(s => s.Label.Contains("앞당김") && s.Calc.Contains("당겨 왔습니다")))
                Assert.True(t.Planned > 0, $"{t.PartNo} {t.Date} 로 당겨 온 박스는 계획에 실려야 한다");
        Assert.DoesNotContain(warnings, w => w.Contains("2026-09-26 → 2026-09-25"));   // 잠긴 날로는 옮기지 않는다
    }

    /// 리뷰 Important 1: 잠긴 형제가 블록 시간을 붙들고 있으면 자유 형제 박스를 옮겨도 초과가 줄지 않으니 옮기지 않는다 (음수 금지).
    [Fact]
    public void Locked_sibling_pinning_the_block_does_not_trigger_endless_moves()
    {
        var bundle = Cleared("autofill_LQ10.json");
        var d = bundle.Injection.First(x => x.PartNo == "83323-P8000RBQ").Days[2]; d.PlanDay = 552; d.PlanNight = 184; d.Locked = true;   // 09-26 잠금 736
        var (warnings, _) = InjectionScheduler.RescheduleWithTraces(bundle, new ShiftRules(S, L), new StageRules(S, L));
        Assert.All(bundle.Injection.SelectMany(r => r.Days), x => Assert.True(x.PlanDay >= 0 && x.PlanNight >= 0));
        var moves = warnings.FirstOrDefault(w => w.StartsWith("사출기 능력을 넘겨")) ?? "";
        Assert.DoesNotContain("83313/23-P8000|RBQ 2026-09-26", moves);   // 잠긴 형제 736 이 블록을 붙드는 동안 83313 박스를 옮겨도 초과가 줄지 않는다
    }
}
