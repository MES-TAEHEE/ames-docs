using AMES.Contracts.Dto;
using AMES.Data.Repositories;
using Xunit;

namespace AMES.Data.Tests;

public sealed class PdaFgDefectTests
{
    private static PdaFgDefectLot Stock => new("LOT-TEST", "ASSY-1", "Assembly", 1, "EA", "FG-1", "PALLET-1", 1, "IMG", "CONFIRMED");

    [Fact]
    public void Confirmed_serialized_assy_is_eligible_even_without_a_location()
    {
        FinishedGoodsRepository.ValidatePdaDefectStock(Stock, "ASSY", false, false);
        FinishedGoodsRepository.ValidatePdaDefectStock(Stock with { LocationNo = null, PalletNo = null }, "assy", false, false);
    }

    [Theory]
    [InlineData("SUB", false, false, "Only ASSY")]
    [InlineData("MATERIAL", false, false, "Only ASSY")]
    [InlineData("ASSY", true, false, "Customer Returns")]
    [InlineData("ASSY", false, true, "dispatch/trailer loading")]
    public void Rejects_ineligible_stock_with_specific_reason(string type, bool inspection, bool loaded, string message)
        => Assert.Contains(message, Assert.Throws<InvalidOperationException>(() =>
            FinishedGoodsRepository.ValidatePdaDefectStock(Stock, type, inspection, loaded)).Message);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(2)]
    public void Must_be_one_available_serialized_product(decimal qty)
        => Assert.Throws<InvalidOperationException>(() => FinishedGoodsRepository.ValidatePdaDefectStock(Stock with { Qty = qty }, "ASSY", false, false));

    [Theory]
    [InlineData("RAW", "IMG")]
    [InlineData("DEFECT", "IMG")]
    [InlineData("SCRAPPED", "IMG")]
    [InlineData("CONFIRMED", "")]
    public void Rejects_unconfirmed_or_unsupported_process(string status, string process)
        => Assert.Throws<InvalidOperationException>(() => FinishedGoodsRepository.ValidatePdaDefectStock(
            Stock with { ConfirmStatus = status, ProcessCode = process }, "ASSY", false, false));

    [Fact]
    public void Existing_pending_defect_can_be_viewed_after_stock_was_removed()
        => FinishedGoodsRepository.ValidatePdaDefectStock(Stock with { Qty = 0, ConfirmStatus = "DEFECT", DefectId = 10 }, "ASSY", false, false);

    [Theory]
    [InlineData("")]
    [InlineData("LOT\nOTHER")]
    public void Invalid_lot_never_reaches_SQL(string lot)
        => Assert.Throws<ArgumentException>(() => FinishedGoodsRepository.ValidatePdaDefectLotNo(lot));

    [Fact]
    public void Lot_and_reason_lengths_match_database_limits()
    {
        FinishedGoodsRepository.ValidatePdaDefectLotNo(new string('A', 40));
        Assert.Throws<ArgumentException>(() => FinishedGoodsRepository.ValidatePdaDefectLotNo(new string('A', 41)));
        Assert.Throws<ArgumentException>(() => FinishedGoodsRepository.ValidateDefectReason(" "));
        Assert.Throws<ArgumentException>(() => FinishedGoodsRepository.ValidateDefectReason(new string('A', 501)));
        Assert.Equal("Scratch", FinishedGoodsRepository.ValidateDefectReason(" Scratch "));
    }
}
