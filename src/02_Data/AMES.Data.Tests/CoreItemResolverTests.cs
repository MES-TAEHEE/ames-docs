using AMES.Data.Services;
using Xunit;
using static AMES.Data.Services.CoreItemResolver;

namespace AMES.Data.Tests;

/// <summary>코어 판정 규칙 정본 — 인메모리 BOM. DB 판은 CoreItemDbTests.</summary>
public class CoreItemResolverTests
{
    static BomEdge E(string p, string c, string type = "SUB", string? name = "SUB something") => new(p, c, type, name);

    [Fact]
    public void Resolve_single_core_child_is_core()
    {
        var edges = new[] { E("FG", "C1", name: "CORE-FR DR UPR TRIM"), E("FG", "SKIN", "MATERIAL", "SKIN x") };
        var r = Resolve("FG", edges);
        Assert.Equal((Outcome.Core, "C1"), (r.Outcome, r.CoreItemNo));
        Assert.False(r.NoCore);
        Assert.Equal("C1", r.StepItemNo("FG"));
    }

    [Fact]
    public void Resolve_core_three_levels_down_is_found()
    {
        var edges = new[] { E("FG", "MOD", name: "MODULE x"), E("MOD", "PNL", name: "PNL ASSY x"),
                            E("PNL", "C1", name: "CORE-PNL x"), E("PNL", "RAIL", name: "RAIL x") };
        Assert.Equal("C1", Resolve("FG", edges).CoreItemNo);
    }

    [Fact]
    public void Resolve_without_sub_children_is_self()
    {
        var r = Resolve("FG", new[] { E("FG", "RESIN", "MATERIAL", "RESIN x") });
        Assert.Equal(Outcome.Self, r.Outcome);
        Assert.Null(r.CoreItemNo);
        Assert.False(r.NoCore);
        Assert.Equal("FG", r.StepItemNo("FG"));
        Assert.Equal(Outcome.Self, Resolve("FG", Array.Empty<BomEdge>()).Outcome);
    }

    [Fact]
    public void Resolve_sub_children_but_no_core_name_is_missing()
    {
        var r = Resolve("FG", new[] { E("FG", "RAIL", name: "RAIL x") });
        Assert.Equal(Outcome.Missing, r.Outcome);
        Assert.True(r.NoCore);
        Assert.Equal("FG", r.StepItemNo("FG"));
    }

    [Fact]
    public void Resolve_two_distinct_cores_is_ambiguous()
    {
        var r = Resolve("FG", new[] { E("FG", "C1", name: "CORE a"), E("FG", "C2", name: "CORE b") });
        Assert.Equal(Outcome.Ambiguous, r.Outcome);
        Assert.True(r.NoCore);
        Assert.Null(r.CoreItemNo);
    }

    [Fact]
    public void Resolve_same_core_reached_by_two_paths_counts_once()
    {
        var edges = new[] { E("FG", "A", name: "SUB a"), E("FG", "B", name: "SUB b"),
                            E("A", "C1", name: "CORE x"), E("B", "C1", name: "CORE x") };
        Assert.Equal((Outcome.Core, "C1"), (Resolve("FG", edges).Outcome, Resolve("FG", edges).CoreItemNo));
    }

    [Fact]
    public void Resolve_does_not_descend_into_material_children()
    {
        var edges = new[] { E("FG", "PART", "MATERIAL", "PART x"), E("PART", "C1", name: "CORE hidden") };
        Assert.Equal(Outcome.Self, Resolve("FG", edges).Outcome);
    }

    [Fact]
    public void Resolve_never_returns_the_item_itself()
    {
        // 코어 단독 WO(라우팅 C): 자기 이름이 CORE 여도 후보가 아니다 → Self
        var edges = new[] { E("C1", "RESIN", "MATERIAL", "RESIN x") };
        Assert.Equal(Outcome.Self, Resolve("C1", edges).Outcome);
    }

    [Fact]
    public void Resolve_accepts_lowercase_and_leading_spaces()
    {
        Assert.True(IsCoreName("  core-x"));
        Assert.True(IsCoreName("CORE(IMG)-GARNISH"));
        Assert.False(IsCoreName("SUB-FR DR TRIM UPR CORE"));
        Assert.False(IsCoreName(null));
        Assert.False(IsCoreName(""));
    }

    [Fact]
    public void Resolve_cycle_throws()
    {
        var edges = new[] { E("FG", "A", name: "SUB a"), E("A", "B", name: "SUB b"), E("B", "A", name: "SUB a") };
        var ex = Assert.Throws<CoreItemCycleException>(() => Resolve("FG", edges));
        Assert.Contains("A", ex.Message);
    }

    // ── 웹 WO 발행 — PP-APS 와 같은 InjFlag 기준(10-14) ──
    static InjEdge J(string p, string c, bool inj, string type = "SUB", string? name = null) => new(p, c, type, name ?? c, inj);

    [Fact]
    public void ByInjFlag_single_injection_item_below_a_subassembly_is_core()
    {
        var edges = new[] { J("FG", "PNL", false, name: "PNL ASSY x"), J("PNL", "C1", true, name: "CORE-x"), J("PNL", "SKIN", false, "MATERIAL") };
        Assert.Equal((Outcome.Core, "C1"), (ResolveByInjFlag("FG", edges).Outcome, ResolveByInjFlag("FG", edges).CoreItemNo));
    }

    [Fact]
    public void ByInjFlag_rail_only_assembly_resolves_to_the_rail()
    {
        // 품명 규칙으로는 CORE 가 없어 Missing(→ APS WO 생성 전체 실패)이던 경우
        var edges = new[] { J("FG", "RAIL", true, name: "RAIL-FR DR x") };
        Assert.Equal(Outcome.Missing, Resolve("FG", new[] { new BomEdge("FG", "RAIL", "SUB", "RAIL-FR DR x") }).Outcome);
        var r = ResolveByInjFlag("FG", edges);
        Assert.Equal((Outcome.Core, "RAIL"), (r.Outcome, r.CoreItemNo));
    }

    [Fact]
    public void ByInjFlag_core_and_rail_picks_the_core_named_item()
    {
        var edges = new[] { J("FG", "82314-XA000", true, name: "RAIL-FR DR TRIM UPR, LH (for IMG)"),
                            J("FG", "D0111-XA000", true, name: "CORE-FR DR TRIM UPR, LH (IMG)") };
        Assert.Equal("D0111-XA000", ResolveByInjFlag("FG", edges).CoreItemNo);
    }

    [Fact]
    public void ByInjFlag_several_injection_items_without_one_core_name_is_ambiguous()
    {
        var none = new[] { J("FG", "R1", true, name: "RAIL a"), J("FG", "R2", true, name: "RAIL b") };
        var two  = new[] { J("FG", "C1", true, name: "CORE a"), J("FG", "C2", true, name: "CORE b") };
        Assert.Equal(Outcome.Ambiguous, ResolveByInjFlag("FG", none).Outcome);
        Assert.Equal(Outcome.Ambiguous, ResolveByInjFlag("FG", two).Outcome);
        Assert.True(ResolveByInjFlag("FG", two).NoCore);
    }

    [Fact]
    public void ByInjFlag_stops_at_injection_items_and_ignores_names_without_injflag()
    {
        // 사출품 아래는 내려가지 않는다(InjFlag 1 의 자식 CORE 는 보지 않음), InjFlag 0 이면 품명이 CORE 여도 사출품이 아니다
        var edges = new[] { J("FG", "C1", true, name: "CORE-x"), J("C1", "C9", true, name: "CORE-deeper"),
                            J("FG", "S1", false, name: "CORE-but-not-injection") };
        Assert.Equal("C1", ResolveByInjFlag("FG", edges).CoreItemNo);
    }

    [Fact]
    public void ByInjFlag_material_is_not_descended_and_counts_as_no_child()
    {
        var edges = new[] { J("FG", "RESIN", false, "MATERIAL"), J("RESIN", "C1", true) };
        Assert.Equal(Outcome.Self, ResolveByInjFlag("FG", edges).Outcome);
        Assert.Equal(Outcome.Self, ResolveByInjFlag("FG", Array.Empty<InjEdge>()).Outcome);
    }

    [Fact]
    public void ByInjFlag_subassembly_without_injection_items_is_missing()
    {
        var r = ResolveByInjFlag("FG", new[] { J("FG", "PNL", false, name: "PNL ASSY") });
        Assert.Equal(Outcome.Missing, r.Outcome);
        Assert.True(r.NoCore);
    }

    [Fact]
    public void ByInjFlag_cycle_is_skipped_like_aps_and_depth_is_capped()
    {
        var cyc = new[] { J("FG", "A", false), J("A", "B", false), J("B", "A", false), J("B", "C1", true) };
        Assert.Equal("C1", ResolveByInjFlag("FG", cyc).CoreItemNo);

        // 깊이 상한(APS BomWalkMaxDepth = 5) 밖의 사출품은 보지 않는다
        var deep = new List<InjEdge>();
        var parent = "FG";
        for (var i = 1; i <= AMES.Data.Repositories.ApsRepository.BomWalkMaxDepth; i++) { deep.Add(J(parent, $"S{i}", false)); parent = $"S{i}"; }
        deep.Add(J(parent, "TOO-DEEP", true));
        Assert.Equal(Outcome.Missing, ResolveByInjFlag("FG", deep).Outcome);
    }

    [Fact]
    public void IsCoreProcess_is_inj_only_case_insensitive()
    {
        Assert.True(IsCoreProcess("INJ"));
        Assert.True(IsCoreProcess("inj"));
        Assert.False(IsCoreProcess("IMG"));
        Assert.False(IsCoreProcess(null));
        Assert.True(MoldResolver.NeedsMold("INJ"));
    }
}
