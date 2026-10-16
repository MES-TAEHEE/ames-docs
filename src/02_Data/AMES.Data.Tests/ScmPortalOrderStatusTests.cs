using AMES.Data.Repositories;
using Xunit;

namespace AMES.Data.Tests;

public sealed class ScmPortalOrderStatusTests
{
    [Fact]
    public void Portal_status_follows_shipment_and_receipt_progress()
    {
        static string Status(bool cancelled, params (decimal Ordered, decimal Shipped, decimal Received)[] lines)
            => ScmRepository.PortalOrderStatus(cancelled, lines);

        Assert.Equal("Published", Status(false, (10, 0, 0)));
        Assert.Equal("Shipped", Status(false, (10, 4, 0)));
        Assert.Equal("Shipped", Status(false, (10, 10, 0)));
        Assert.Equal("PartiallyReceived", Status(false, (10, 10, 4)));
        Assert.Equal("PartiallyReceived", Status(false, (10, 10, 10), (5, 5, 0)));
        Assert.Equal("Received", Status(false, (10, 10, 10), (5, 5, 5)));
        Assert.Equal("Cancelled", Status(true, (10, 10, 10)));
    }
}
