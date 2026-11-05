using AMES.Web.Components;
using Xunit;

namespace AMES.Api.Tests;

public sealed class LocationMapClickTests
{
    [Theory]
    [InlineData("/wh/location-map")]
    [InlineData("/fg/location-map")]
    [InlineData("/WH/LOCATION-MAP")]
    [InlineData("/portal/deliveries")]
    [InlineData("/scm/purchase-orders")]
    public void Navigation_clicks_are_not_discarded_after_a_slow_request(string path)
        => Assert.False(AmesComponentBase.ShouldDiscardQueuedClick(path, TimeSpan.FromMilliseconds(10)));

    [Theory]
    [InlineData("/pp/work-order")]
    [InlineData("/md/fd/location")]
    [InlineData("/wh/picking-orders")]
    public void Other_screens_keep_their_queued_save_click_protection(string path)
    {
        Assert.True(AmesComponentBase.ShouldDiscardQueuedClick(path, TimeSpan.FromMilliseconds(10)));
        Assert.False(AmesComponentBase.ShouldDiscardQueuedClick(path, TimeSpan.FromMilliseconds(400)));
    }
}
