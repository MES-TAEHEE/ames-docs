using AMES.Data.Services;
using Xunit;

namespace AMES.Data.Tests;

// 설비 유형 ↔ 라인 공정 규칙: 사출 설비는 사출 라인, 감싸기(WRAP)는 감싸기(IMG) 라인, 도장(PNT)은 도장 라인에만 붙는다.
public class EquipmentLineRulesTests
{
    [Theory]
    [InlineData("INJ", "INJ")]
    [InlineData("WRAP", "IMG")]
    [InlineData("PNT", "PNT")]
    [InlineData(" inj ", "INJ")]
    public void Type_maps_to_its_line_process(string type, string process)
        => Assert.Equal(process, EquipmentLineRules.ProcessFor(type));

    [Theory]
    [InlineData("INJ", "INJ", true)]
    [InlineData("INJ", "IMG", false)]
    [InlineData("INJ", "PNT", false)]
    [InlineData("WRAP", "IMG", true)]
    [InlineData("WRAP", "INJ", false)]
    [InlineData("PNT", "PNT", true)]
    [InlineData("PNT", "IMG", false)]
    [InlineData("INJ", null, false)]
    public void Known_type_allows_only_its_own_process(string type, string? process, bool expected)
        => Assert.Equal(expected, EquipmentLineRules.Allows(type, process));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ETC")]
    public void Unknown_or_empty_type_has_no_rule(string? type)
    {
        Assert.Null(EquipmentLineRules.ProcessFor(type));
        Assert.True(EquipmentLineRules.Allows(type, "INJ"));
        Assert.True(EquipmentLineRules.Allows(type, null));
    }
}
