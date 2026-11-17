using AMES.Web.Services;
using Xunit;
namespace AMES.Data.Tests;
public sealed class PackingUnitQuantityTests
{
    [Theory]
    [InlineData(3,100,1000,300)]
    [InlineData(2,100,200,200)]
    [InlineData(0,100,1000,0)]
    public void Editing_unit_count_sets_the_full_packed_quantity(decimal count,decimal packing,decimal remaining,decimal expected)
        =>Assert.Equal(expected,PackingUnitQuantity.FromCount(count,packing,remaining));
    [Theory]
    [InlineData(3,100,170)]
    [InlineData(-1,100,1000)]
    [InlineData(1.5,100,1000)]
    [InlineData(1,0,1000)]
    [InlineData(1001,1,2000)]
    public void Invalid_count_or_excess_quantity_is_rejected(decimal count,decimal packing,decimal remaining)
        =>Assert.Throws<ArgumentException>(()=>PackingUnitQuantity.FromCount(count,packing,remaining));
}
