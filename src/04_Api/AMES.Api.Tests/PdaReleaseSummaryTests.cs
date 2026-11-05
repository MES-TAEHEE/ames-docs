using System.Net;
using AMES.Pda.Services;
using Xunit;

namespace AMES.Api.Tests;

public sealed class PdaReleaseSummaryTests
{
    [Fact]
    public void Picking_success_lists_only_released_lots_parts_quantities_and_reason()
    {
        var lots = new[] { Lot("UIT-1", "RESIN", 100, "G"), Lot("UIT-2", "SKIN", .75m, "SH") };
        var summary = PdaReleaseSummary.Picking(lots, "Production Line", unit => unit!.ToLowerInvariant());
        Assert.Equal("LOT: UIT-1\nPart: RESIN\nQty: 100 g\n\nLOT: UIT-2\nPart: SKIN\nQty: 0.75 sh\nReason: Production Line", summary);
    }

    [Fact]
    public void Direct_success_shows_the_actual_released_quantity_not_the_remaining_inventory()
        => Assert.Equal("LOT: UIT-1\nPart: RESIN\nQty: 12.125 g\nReason: Defect",
            PdaReleaseSummary.Direct("UIT-1", "RESIN", 12.125m, "g", "Defect"));

    [Theory]
    [InlineData("{\"success\":false,\"message\":\"LOT UIT-1 has no available inventory.\"}", "LOT UIT-1 has no available inventory.")]
    [InlineData("{\"title\":\"Bad Request\",\"detail\":\"Older LOT UIT-2 must be released first.\"}", "Older LOT UIT-2 must be released first.")]
    public async Task Failed_release_keeps_the_specific_server_error(string body, string expected)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(body) };
        Assert.Equal(expected, await WarehouseApi.ReadReleaseErrorAsync(response));
    }

    private static PdaApi.ReleaseLotRow Lot(string lot, string part, decimal qty, string unit)
        => new("PK1", lot, part, null, qty, unit, null, null, null, null, null, null, true, true, null);
}
