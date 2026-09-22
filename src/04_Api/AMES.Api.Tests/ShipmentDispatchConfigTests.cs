using AMES.Api.Services;
using Xunit;

namespace AMES.Api.Tests;

public class ShipmentDispatchConfigTests
{
    [Fact]
    public void Parses_savannah_common_code_configuration()
    {
        var config = ShipmentDispatchService.ParseConfig(
            "http://192.168.1.68:5220", "Header:X-API-KEY", "key",
            "CORCD=7700;BIZCD=7710;VENDCD=310471;PURC_ORG=1A7700;PURC_PO_TYPE=1KMA;ARRIVAL_LEAD_DAYS=1;ARRIVAL_TIME=0930");

        Assert.Equal("http://192.168.1.68:5220", config.BaseUrl);
        Assert.Equal("7700", config.CompanyCode);
        Assert.Equal("0930", config.ArrivalTime);
    }

    [Fact]
    public void Rejects_missing_required_parameter()
        => Assert.Throws<InvalidOperationException>(() => ShipmentDispatchService.ParseConfig(
            "http://192.168.1.68:5220", "Header:X-API-KEY", "key", "CORCD=7700"));
}
