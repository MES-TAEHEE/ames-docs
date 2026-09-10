using AMES.Contracts.Enums;
using AMES.Data.Services;
using Xunit;

namespace AMES.Pop.Tests;

public class LotDefectRulesTests
{
    [Theory]
    [InlineData("RAW",        DefectRegisterOutcome.Registered)]
    [InlineData("CONFIRMED",  DefectRegisterOutcome.Registered)]
    [InlineData("NG_BLOCKED", DefectRegisterOutcome.Registered)]
    [InlineData("DEFECT",     DefectRegisterOutcome.AlreadyInRework)]
    [InlineData("SCRAPPED",   DefectRegisterOutcome.Scrapped)]
    [InlineData("NG_CONFIRMED", DefectRegisterOutcome.Scrapped)]   // 마이그레이션 전 잔존 행도 폐기로 본다
    public void CheckRegister_maps_lot_status(string status, DefectRegisterOutcome expected)
        => Assert.Equal(expected, LotDefectRules.CheckRegister(status));

    [Fact]
    public void CheckRegister_rejects_unknown_status()
        => Assert.Throws<InvalidOperationException>(() => LotDefectRules.CheckRegister("BOGUS"));

    [Theory]
    [InlineData("RAW",        LotConfirmBlock.None)]
    [InlineData("CONFIRMED",  LotConfirmBlock.AlreadyConfirmed)]
    [InlineData("NG_BLOCKED", LotConfirmBlock.NgBlocked)]
    [InlineData("DEFECT",     LotConfirmBlock.InRework)]
    [InlineData("SCRAPPED",   LotConfirmBlock.Scrapped)]
    [InlineData("NG_CONFIRMED", LotConfirmBlock.Scrapped)]
    public void ConfirmBlock_maps_lot_status(string status, LotConfirmBlock expected)
        => Assert.Equal(expected, LotDefectRules.ConfirmBlock(status));

    [Theory]
    [InlineData("CONFIRMED",  true)]
    [InlineData("RAW",        false)]
    [InlineData("NG_BLOCKED", false)]
    public void Only_confirmed_lots_reverse_a_result(string prior, bool expected)
        => Assert.Equal(expected, LotDefectRules.ReversesResult(prior));

    [Theory]
    [InlineData(null,       true)]
    [InlineData("REWORKED", false)]
    [InlineData("SCRAPPED", false)]
    [InlineData("LEGACY",   false)]
    public void Only_open_rows_can_be_decided(string? disposition, bool expected)
        => Assert.Equal(expected, LotDefectRules.CanDecide(disposition));
}
