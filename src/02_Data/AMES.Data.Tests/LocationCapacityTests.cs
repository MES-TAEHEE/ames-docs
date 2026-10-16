using AMES.Data.Services;
using Xunit;

namespace AMES.Data.Tests;

public class LocationCapacityTests
{
    [Theory]
    [InlineData(100, null, false)]
    [InlineData(100, 0, false)]
    [InlineData(99, 100, false)]
    [InlineData(100, 100, true)]
    [InlineData(101, 100, true)]
    public void Full_requires_a_positive_capacity(int qty, int? capacity, bool expected)
        => Assert.Equal(expected, LocationCapacity.IsFull(qty, capacity));

    [Theory]
    [InlineData(120, 200, 100, true)]
    [InlineData(100, 200, 100, false)]
    [InlineData(90, 200, 100, false)]
    [InlineData(120, 100, 100, false)]
    [InlineData(120, 50, 100, false)]
    [InlineData(120, null, 100, true)]
    [InlineData(120, 0, 100, true)]
    [InlineData(120, 200, 0, false)]
    [InlineData(120, 200, null, false)]
    public void Reduction_warns_only_when_new_limit_is_below_stock(int qty, int? previous, int? proposed, bool expected)
        => Assert.Equal(expected, LocationCapacity.ShouldWarnOnReduction(qty, previous, proposed));
}
