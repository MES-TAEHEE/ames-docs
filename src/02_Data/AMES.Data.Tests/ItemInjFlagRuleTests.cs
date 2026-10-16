using AMES.Data.Repositories;
using Xunit;

namespace AMES.Data.Tests;

/// <summary>MD-003 사출품 여부 규칙: 품목 유형 SUB 만 InjFlag 를 가진다.</summary>
public class ItemInjFlagRuleTests
{
    [Theory]
    [InlineData("SUB")]
    [InlineData("sub")]
    [InlineData(" SUB ")]
    public void Sub_keeps_inj_flag(string type)
        => Assert.True(MasterDataRepository.NormalizeInjFlag(type, true));

    [Theory]
    [InlineData("ASSY")]
    [InlineData("MATERIAL")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("SUBASSY")]
    public void Other_types_clear_inj_flag(string? type)
        => Assert.False(MasterDataRepository.NormalizeInjFlag(type, true));

    [Fact]
    public void Sub_without_inj_flag_stays_off()
        => Assert.False(MasterDataRepository.NormalizeInjFlag("SUB", false));
}
