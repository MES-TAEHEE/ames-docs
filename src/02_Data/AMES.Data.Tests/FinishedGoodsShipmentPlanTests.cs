using AMES.Data.Repositories;
using Xunit;

namespace AMES.Data.Tests;

public sealed class FinishedGoodsShipmentPlanTests
{
    [Fact]
    public void Inputs_for_the_same_supply_line_are_combined()
    {
        var rows = FinishedGoodsRepository.NormalizeShipmentPlanInputs(
        [
            new(101, 4),
            new(101, 6),
            new(202, 0)
        ]);

        var row = Assert.Single(rows);
        Assert.Equal(101, row.SoId);
        Assert.Equal(10, row.Qty);
    }

    [Fact]
    public void Quantity_cannot_exceed_the_unplanned_balance()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            FinishedGoodsRepository.ValidateShipmentPlanQuantity("PART-01", 11, 10));

        Assert.Contains("remaining quantity", error.Message);
    }

    [Fact]
    public void Quantity_must_be_a_whole_number()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            FinishedGoodsRepository.ValidateShipmentPlanQuantity("PART-01", 1.5m, 10));

        Assert.Contains("whole-number", error.Message);
    }

    [Fact]
    public void Mixed_source_orders_use_one_multi_value_on_the_plan_header()
    {
        var value = FinishedGoodsRepository.GetShipmentPlanHeaderValue(["SO-001", "SO-002"]);

        Assert.Equal("MULTI", value);
    }

    [Fact]
    public void Only_unconnected_planned_pp_orders_can_be_deleted()
    {
        FinishedGoodsRepository.ValidateShipmentPlanDelete("PP", "PLANNED", false);

        Assert.Throws<InvalidOperationException>(() =>
            FinishedGoodsRepository.ValidateShipmentPlanDelete("PP", "SHIPPED", false));
        Assert.Throws<InvalidOperationException>(() =>
            FinishedGoodsRepository.ValidateShipmentPlanDelete("PP", "PLANNED", true));
    }

}
