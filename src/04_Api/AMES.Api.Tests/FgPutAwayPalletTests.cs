using AMES.Api.Endpoints;
using System.Text.Json;
using Xunit;

namespace AMES.Api.Tests;

public sealed class FgPutAwayPalletTests
{
    [Fact]
    public void Pallet_accepts_only_assy_parts()
    {
        FgEndpoints.EnsureAssyPutAwayContents([("LOT-A", "PART-A", "ASSY"), ("LOT-B", "PART-B", "ASSY")]);
    }

    [Theory]
    [InlineData("PART-B", "INJ")]
    [InlineData("PART-B", "RAW")]
    [InlineData("PART-B", null)]
    [InlineData(null, "ASSY")]
    public void One_non_assy_or_unknown_part_blocks_the_entire_pallet(string? partNo, string? itemType)
    {
        var error=Assert.Throws<InvalidOperationException>(() => FgEndpoints.EnsureAssyPutAwayContents(
            [("LOT-A", "PART-A", "ASSY"), ("LOT-B", partNo, itemType)]));
        Assert.Contains("LOT-B", error.Message);
        Assert.Contains("not registered as ASSY", error.Message);
        Assert.Contains("Nothing on this pallet was moved", error.Message);
    }

    [Fact]
    public void Confirmation_requires_the_reviewed_lots_and_quantities()
    {
        List<(string LotNo,decimal Qty)> current=[("LOT-A",1),("LOT-B",1)];
        Assert.True(FgEndpoints.MatchesPutAwayContents(current,new(){["LOT-B"]=1,["LOT-A"]=1}));
        Assert.False(FgEndpoints.MatchesPutAwayContents(current,null));
        Assert.False(FgEndpoints.MatchesPutAwayContents(current,new(){["LOT-A"]=1}));
        Assert.False(FgEndpoints.MatchesPutAwayContents(current,new(){["LOT-A"]=2,["LOT-B"]=1}));
        Assert.False(FgEndpoints.MatchesPutAwayContents(current,new(){["LOT-A"]=1,["LOT-C"]=1}));
    }

    [Fact]
    public void Pda_and_api_agree_on_expected_pallet_contents()
    {
        var request=new AMES.Pda.Services.PdaApi.FgPutAwayConfirmReq("PALLET-1","FA01A1F1",new(){["LOT-1"]=1});
        var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var result=JsonSerializer.Deserialize<FgEndpoints.PutAwayConfirmReq>(JsonSerializer.Serialize(request,options),options)!;
        Assert.Equal("PALLET-1",result.Barcode);
        Assert.Equal(1,result.ExpectedItems!["LOT-1"]);
    }
}
