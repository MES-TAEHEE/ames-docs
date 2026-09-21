using AMES.Data.Repositories;
using Xunit;

namespace AMES.Data.Tests;

/// <summary>MD-026 예비품 위치 규칙: 구역 SP_EXTRA ↔ 칸 EX 고정 ↔ ExtraLocation 은 그 구역에서만.</summary>
public class SpareLocationRuleTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("03")]
    public void Extra_zone_forces_slot_EX_and_keeps_trimmed_extra_location(string? slotIn)
    {
        var (slot, extra) = MasterDataRepository.NormalizeSpareLocation("SP_EXTRA", slotIn, "  Office cabinet #2 ");
        Assert.Equal("EX", slot);
        Assert.Equal("Office cabinet #2", extra);
    }

    [Fact]
    public void Extra_zone_with_blank_extra_location_stores_null()
    {
        var (slot, extra) = MasterDataRepository.NormalizeSpareLocation("sp_extra", "EX", "   ");
        Assert.Equal("EX", slot);
        Assert.Null(extra);
    }

    [Theory]
    [InlineData("SP_A1", "03", "03")]
    [InlineData("SP_A1", "EX", null)]   // EX 는 SP_EXTRA 밖에서 쓸 수 없다
    [InlineData("SP_FL1", null, null)]
    [InlineData(null, "02", "02")]
    public void Other_zones_drop_extra_location_and_reject_slot_EX(string? zone, string? slotIn, string? slotOut)
    {
        var (slot, extra) = MasterDataRepository.NormalizeSpareLocation(zone, slotIn, "leftover text");
        Assert.Equal(slotOut, slot);
        Assert.Null(extra);
    }
}
