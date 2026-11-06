using AMES.Data.Repositories;
using Xunit;

namespace AMES.Data.Tests;

public sealed class ShippingDeliveryHeaderTests
{
    [Fact]
    public void Same_customer_can_combine_orders_on_different_days_using_the_earliest_date()
    {
        var early = new DateTime(2026, 11, 6);
        var orders = new[] { ("C001", early.AddDays(3)), ("C001", early), ("C001", early.AddDays(1)) };
        var result = FinishedGoodsRepository.ShippingDeliveryHeader(orders);
        Assert.Equal(("C001", early), result);
        Assert.Equal(result, FinishedGoodsRepository.ShippingDeliveryHeader(orders.Reverse().ToArray()));
    }

    [Fact]
    public void Customer_comparison_ignores_case_and_padding_like_the_customer_key()
    {
        var date = new DateTime(2026, 11, 6);
        Assert.Equal(("C001", date), FinishedGoodsRepository.ShippingDeliveryHeader(
            new[] { ("C001 ", date), ("c001", date.AddDays(1)) }));
    }

    [Fact]
    public void Different_customers_are_rejected_even_when_dates_match()
    {
        var date = new DateTime(2026, 11, 6);
        var error = Assert.Throws<InvalidOperationException>(() => FinishedGoodsRepository.ShippingDeliveryHeader(
            new[] { ("C001", date), ("C002", date) }));
        Assert.Equal("Orders for different customers cannot be combined in one DCR.", error.Message);
    }

    [Fact]
    public void Empty_selection_is_rejected()
        => Assert.Throws<ArgumentException>(() => FinishedGoodsRepository.ShippingDeliveryHeader([]));
}
