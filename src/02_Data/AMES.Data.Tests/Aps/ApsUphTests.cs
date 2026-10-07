using AMES.Data.Aps;
using Xunit;

namespace AMES.Data.Tests.Aps;

/// <summary>계획과 WO 생성이 같이 쓰는 유효 UPH(스펙 §4) — MD_MoldLine.UPH × 품번 캐비티 ÷ 금형 캐비티. LQ2RLGDU(캐비티 2, LH/RH 2품번, 120) → 60.</summary>
public class ApsUphTests
{
    [Theory]
    [InlineData(2, 2, 1)] [InlineData(2, 1, 2)] [InlineData(4, 2, 2)] [InlineData(3, 2, 1)] [InlineData(null, 2, 1)] [InlineData(2, 0, 2)]
    public void PartCavity_is_mold_cavity_divided_by_active_items_floored_at_one(int? cavity, int items, int expected)
        => Assert.Equal(expected, ApsUph.PartCavity(cavity, items));

    [Fact]
    public void Effective_scales_mold_line_uph_by_cavity_share()
    {
        Assert.Equal(60m, ApsUph.Effective(120m, 2, 2));     // 패밀리 금형 LH/RH
        Assert.Equal(120m, ApsUph.Effective(120m, 2, 1));    // 품번 하나가 캐비티 2개를 다 쓴다
        Assert.Equal(40m, ApsUph.Effective(120m, 3, 2));     // 3 ÷ 2 → 1 캐비티
        Assert.Equal(120m, ApsUph.Effective(120m, null, 2)); // 캐비티 없음 → 원값
        Assert.Equal(0m, ApsUph.Effective(0m, 2, 2));
    }
}
