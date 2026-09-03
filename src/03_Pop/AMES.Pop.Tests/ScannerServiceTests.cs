using AMES.Pop.Services;
using Xunit;

namespace AMES.Pop.Tests;

public class ScannerServiceTests
{
    [Fact]
    public void Publish_delivers_to_every_subscriber_even_if_one_throws()
    {
        var log = new List<string>();
        var svc = new ScannerService(log.Add);
        var got = new List<string>();
        svc.OnScan += _ => throw new InvalidOperationException("boom");
        svc.OnScan += got.Add;

        svc.Publish("A91I10001");

        Assert.Equal(new[] { "A91I10001" }, got);
        Assert.Contains(log, m => m.Contains("boom"));
    }

    [Fact]
    public void Publish_with_no_subscribers_is_a_noop()
    {
        var svc = new ScannerService();
        svc.Publish("X");
    }

    [Fact]
    public void ConnectionChanged_fires_only_on_transition()
    {
        var svc = new ScannerService();
        var events = new List<bool>();
        svc.ConnectionChanged += events.Add;

        svc.SetConnected(true);
        svc.SetConnected(true);
        svc.SetConnected(false);
        svc.SetConnected(false);

        Assert.Equal(new[] { true, false }, events);
        Assert.False(svc.IsConnected);
    }

    [Fact]
    public void IsEnabled_defaults_to_false()
    {
        Assert.False(new ScannerService().IsEnabled);
    }
}
