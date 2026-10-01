using AMES.Data.Services;
using Xunit;
using static AMES.Data.Services.BomHierarchy;
using V = AMES.Data.Services.BomVersionRules.VersionInfo;

namespace AMES.Data.Tests;

/// <summary>BOM 상하위 관계: 품번별 대표 버전만 관계로 보고, 선택 품목 기준 하위(+)·상위(−) 품목을 가장 가까운 경로로 찾는다.</summary>
public class BomHierarchyTests
{
    static readonly DateOnly Today = new(2026, 10, 2);
    static V Ver(string id, string root, string status = "APPROVED", DateOnly? from = null, DateOnly? to = null, DateTime? created = null)
        => new(id, root, status, from ?? new DateOnly(2026, 8, 28), to, created ?? new DateTime(2026, 8, 28));

    // ASSY-CRN, ASSY-DFS ─(LV1)→ SUB ─(LV1)→ CORE, ASSY-CRN 는 SKIN 도 직접 사용
    static Graph Sample() => Build(
        [Ver("V-CRN", "ASSY-CRN"), Ver("V-DFS", "ASSY-DFS"), Ver("V-SUB", "SUB")],
        [new("V-CRN", "SUB"), new("V-CRN", "SKIN"), new("V-DFS", "SUB"), new("V-SUB", "CORE")],
        Today);

    [Fact]
    public void Core_finds_both_assemblies_two_levels_up_via_the_sub()
    {
        var r = Find(Sample(), "CORE");
        Assert.Contains(r, x => x.ItemNo == "SUB" && x.Depth == -1 && x.Via.Count == 0);
        Assert.Contains(r, x => x.ItemNo == "ASSY-CRN" && x.Depth == -2 && x.Via.SequenceEqual(["SUB"]));
        Assert.Contains(r, x => x.ItemNo == "ASSY-DFS" && x.Depth == -2 && x.Via.SequenceEqual(["SUB"]));
        Assert.DoesNotContain(r, x => x.Depth > 0);
    }

    [Fact]
    public void Assembly_finds_children_down_and_no_parents()
    {
        var r = Find(Sample(), "ASSY-CRN");
        Assert.Equal(["CORE:2", "SKIN:1", "SUB:1"], r.Select(x => $"{x.ItemNo}:{x.Depth}").OrderBy(s => s));
        Assert.Equal(["SUB"], r.Single(x => x.ItemNo == "CORE").Via);
    }

    [Fact]
    public void Middle_item_finds_both_directions()
    {
        var r = Find(Sample(), "sub");   // 대소문자 무시
        Assert.Equal(["ASSY-CRN:-1", "ASSY-DFS:-1", "CORE:1"], r.Select(x => $"{x.ItemNo}:{x.Depth}").OrderBy(s => s));
    }

    [Fact]
    public void Only_the_representative_version_counts_as_a_relation()
    {
        // SUB 의 승인 버전 V2(유효)는 CORE2, 지난 초안 V1 은 CORE 를 쓴다 → CORE 는 SUB 의 하위가 아니다
        var g = Build(
            [Ver("V-SUB-1", "SUB", "DRAFT", created: new DateTime(2026, 9, 1)), Ver("V-SUB-2", "SUB", "APPROVED", new DateOnly(2026, 9, 1))],
            [new("V-SUB-1", "CORE"), new("V-SUB-2", "CORE2")],
            Today);
        Assert.Equal(["CORE2"], Find(g, "SUB").Select(x => x.ItemNo));
        Assert.Empty(Find(g, "CORE"));
    }

    [Fact]
    public void Cycles_do_not_loop_and_selected_item_is_never_listed()
    {
        var g = Build([Ver("VA", "A"), Ver("VB", "B")], [new("VA", "B"), new("VB", "A")], Today);
        var r = Find(g, "A");
        Assert.Equal(["B:1"], r.Select(x => $"{x.ItemNo}:{x.Depth}"));
    }

    [Fact]
    public void Depth_is_limited()
    {
        var vers  = Enumerable.Range(0, 12).Select(i => Ver($"V{i}", $"I{i}")).ToList();
        var edges = Enumerable.Range(0, 11).Select(i => new Edge($"V{i}", $"I{i + 1}")).ToList();
        Assert.Equal(3, Find(Build(vers, edges, Today), "I0", maxDepth: 3).Count);
    }

    [Fact]
    public void Representative_prefers_valid_approved_then_latest_approved_then_latest_created()
    {
        V[] vs =
        [
            Ver("OLD", "X", "APPROVED", new DateOnly(2026, 1, 1), new DateOnly(2026, 9, 30)),
            Ver("NOW", "X", "APPROVED", new DateOnly(2026, 10, 1)),
            Ver("NEXT", "X", "APPROVED", new DateOnly(2026, 12, 1)),
            Ver("DRAFT", "X", "DRAFT", created: new DateTime(2026, 10, 2)),
        ];
        Assert.Equal("NOW", BomVersionRules.Representative(vs, "x", Today)!.VersionId);
        Assert.Equal("NEXT", BomVersionRules.Representative(vs.Where(v => v.VersionId != "NOW"), "X", Today)!.VersionId);
        Assert.Equal("DRAFT", BomVersionRules.Representative(vs.Where(v => v.Status != "APPROVED"), "X", Today)!.VersionId);
        Assert.Null(BomVersionRules.Representative(vs, "Y", Today));
    }
}
