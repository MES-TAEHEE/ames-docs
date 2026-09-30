using AMES.Contracts.Enums;
using AMES.Data.Services;
using Xunit;

namespace AMES.Pop.Tests;

public class CoreLotRulesTests
{
    [Theory]
    [InlineData("CONFIRMED",    ImgCoreOutcome.Created)]
    [InlineData("RAW",          ImgCoreOutcome.CoreUnconfirmed)]
    [InlineData("NG_BLOCKED",   ImgCoreOutcome.CoreDefect)]
    [InlineData("DEFECT",       ImgCoreOutcome.CoreDefect)]
    [InlineData("SCRAPPED",     ImgCoreOutcome.CoreScrapped)]
    [InlineData("NG_CONFIRMED", ImgCoreOutcome.CoreScrapped)]
    [InlineData("BOGUS",        ImgCoreOutcome.CoreUnconfirmed)]   // 모르는 상태는 안전 쪽(거부)
    public void Check_maps_core_status(string status, ImgCoreOutcome expected)
        => Assert.Equal(expected, CoreLotRules.Check(status));
}
