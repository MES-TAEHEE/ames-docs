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
