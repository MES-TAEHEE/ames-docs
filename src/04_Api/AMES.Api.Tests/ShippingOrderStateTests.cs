using AMES.Contracts.Dto;
using AMES.Web.Services;
using Xunit;

namespace AMES.Api.Tests;

public sealed class ShippingOrderStateTests
{
    [Theory]
    [InlineData("Open", false, true)]
    [InlineData("Open", true, true)]
    [InlineData("Loading", true, true)]
    [InlineData("Shipped", true, false)]
    [InlineData("Cancelled", false, false)]
    public void Unshipped_counts_Open_and_Loading_regardless_of_DCR_assignment(string status, bool assigned, bool expected)
    {
        var order = Order(1, status, assigned);
        Assert.Equal(expected, ShipmentPlanRows.IsUnshipped(order));
        Assert.Equal(assigned && expected ? 1 : 0, ShipmentPlanRows.FromOrders([order]).Count);
    }

    [Fact]
    public void KPI_and_plan_share_states_but_KPI_also_includes_unassigned_orders()
    {
        ShippingOrderDto[] orders = [Order(1, "Open", false), Order(2, "Open", true),
            Order(3, "Loading", true), Order(4, "Shipped", true)];
        Assert.Equal(3, orders.Count(ShipmentPlanRows.IsUnshipped));
        var delivery = Assert.Single(ShipmentPlanRows.FromOrders(orders));
        Assert.Equal(2, delivery.Orders.Count);
        Assert.Equal("Loading", delivery.Status);
    }

    private static ShippingOrderDto Order(int id, string status, bool assigned) =>
        new(id, $"ORD-{id}", "CUSTOMER", new(2026, 10, 29), status, assigned ? 1 : null, assigned ? "DCR-1" : null, []);
}
