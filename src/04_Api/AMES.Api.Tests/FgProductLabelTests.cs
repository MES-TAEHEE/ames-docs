using AMES.Api.Endpoints;
using Xunit;

namespace AMES.Api.Tests;

public sealed class FgProductLabelTests
{
    public static IEnumerable<object[]> Labels()
    {
        const string lot="4A7B00001";
        yield return ["[)>\u001e06\u001dPTEST-PART\u001dC:CUSTOMER\u001dT2610231AMA"+lot+"\u001e\u0004",lot];
        yield return ["[)>06PTEST-PARTC:CUSTOMERT2610231AMA"+lot,lot];
        yield return [lot,lot];
        yield return ["LOT:"+lot,lot];
    }

    [Theory]
    [MemberData(nameof(Labels))]
    public void Same_complete_lot_is_used_for_putaway_outbound_and_return(string label,string expected)
    {
        Assert.Equal(expected,FgEndpoints.ParseFgBarcode(label).Value);
        Assert.True(FgOutboundBarcode.TryNormalize(label,out var outbound,out var outboundError),outboundError);
        Assert.Equal(expected,outbound);
        Assert.True(FgEndpoints.TryNormalizeReturnBarcode(label,out var returned,out var returnError),returnError);
        Assert.Equal(expected,returned);
    }

    [Fact]
    public void An_unrecognized_product_label_is_not_accepted_as_a_raw_lot()
    {
        const string invalid="[)>06PTEST-PARTC:CUSTOMER";
        Assert.False(FgOutboundBarcode.TryNormalize(invalid,out _,out var error));
        Assert.Contains("valid LOT",error);
        Assert.False(FgEndpoints.TryNormalizeReturnBarcode(invalid,out _,out _));
    }
}
