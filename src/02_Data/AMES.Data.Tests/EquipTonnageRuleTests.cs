using AMES.Data.Repositories;
using Xunit;

namespace AMES.Data.Tests;

/// <summary>MD-014 설비 톤수 규칙: 공통코드 EQUIP_TYPE 가 INJ(사출)인 설비만 톤수를 가진다.</summary>
public class EquipTonnageRuleTests
{
    [Theory]
    [InlineData("INJ")]
    [InlineData("inj")]
    [InlineData(" INJ ")]
    public void Injection_keeps_tonnage(string type)
        => Assert.Equal(650, MasterDataRepository.NormalizeEquipTonnage(type, 650));

    [Theory]
    [InlineData("WRAP")]
    [InlineData("PNT")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("INJ_MACHINE")]
    public void Other_types_clear_tonnage(string? type)
        => Assert.Null(MasterDataRepository.NormalizeEquipTonnage(type, 650));

    [Fact]
    public void Injection_without_tonnage_stays_null()
        => Assert.Null(MasterDataRepository.NormalizeEquipTonnage("INJ", null));
}
