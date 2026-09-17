using AMES.Data.Services;
using Xunit;
using static AMES.Data.Services.MoldResolver;

namespace AMES.Data.Tests;

/// <summary>품번→금형 선택 규칙: ① 직전 금형과 같은 후보 → ② 라인 배정 후보 중 교체 시간 최소 → ③ MoldId 순.</summary>
public class MoldResolverTests
{
    static MoldCandidate C(string id, bool assigned, int min) => new(id, assigned, min);

    [Fact]
    public void Empty_candidates_returns_null()
        => Assert.Null(Choose(Array.Empty<MoldCandidate>(), "M1"));

    [Fact]
    public void Prefers_candidate_equal_to_previous_mold()
    {
        var pick = Choose(new[] { C("M1", true, 10), C("M2", true, 5) }, prevMoldId: "M2");
        Assert.Equal("M2", pick!.MoldId);
    }

    [Fact]
    public void Without_previous_match_prefers_line_assigned_with_smallest_change_min()
    {
        var pick = Choose(new[] { C("M1", false, 5), C("M2", true, 40), C("M3", true, 20) }, prevMoldId: "MX");
        Assert.Equal("M3", pick!.MoldId);
    }

    [Fact]
    public void Ties_on_change_min_break_by_mold_id()
    {
        var pick = Choose(new[] { C("M9", true, 20), C("M2", true, 20) }, prevMoldId: null);
        Assert.Equal("M2", pick!.MoldId);
    }

    [Fact]
    public void Without_line_assignment_falls_back_to_first_mold_id()
    {
        var pick = Choose(new[] { C("M9", false, 5), C("M2", false, 50) }, prevMoldId: null);
        Assert.Equal("M2", pick!.MoldId);
    }

    [Fact]
    public void Needs_mold_only_for_injection()
    {
        Assert.True(NeedsMold("INJ"));
        Assert.False(NeedsMold("IMG"));
        Assert.False(NeedsMold(null));
    }
}
