using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AMES.Api.Endpoints;
using AMES.Contracts.Dto;
using AMES.Pda.Services;
using Xunit;

namespace AMES.Api.Tests;

public sealed class FgFifoContractTests
{
    static readonly FifoWarningDto Older = new("PART", "OLDER-LOT", "PALLET-1", "FG-1", new(2026, 10, 1));

    [Fact]
    public void Old_requests_default_to_no_FIFO_acknowledgement()
    {
        Assert.False(JsonSerializer.Deserialize<FgEndpoints.OutboundUnitReq>("""{"Barcode":"LOT"}""")!.AllowNonFifo);
        Assert.False(JsonSerializer.Deserialize<DeliveryLoadRequest>("""{"DeliveryBarcode":"DCR","PalletBarcode":"PALLET"}""")!.AllowNonFifo);
        Assert.False(JsonSerializer.Deserialize<DeliveryCompleteRequest>("""{"DeliveryBarcode":"DCR"}""")!.AllowNonFifo);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Direct_outbound_preserves_warning_details_and_only_sends_explicit_override(bool acknowledge)
    {
        using var http = new HttpClient(new Handler(async request =>
        {
            var body = await request.Content!.ReadFromJsonAsync<FgEndpoints.OutboundUnitReq>();
            Assert.Equal(acknowledge, body!.AllowNonFifo);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new FgEndpoints.OutboundUnitResult(false,
                "Older available stock exists.", null, FifoWarnings: [Older])) };
        })) { BaseAddress = new("https://example.test") };
        var result = await new FinishedGoodsApi(http, new AuthState()).FgOutboundAsync(new("LOT", "C1", acknowledge));
        Assert.False(result.Success);
        Assert.Equal(0, result.ProcessedCount);
        Assert.Equal(Older, Assert.Single(result.FifoWarnings!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DCR_load_and_complete_preserve_warning_and_explicit_override(bool complete)
    {
        using var http = new HttpClient(new Handler(async request =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.True(body.RootElement.GetProperty("allowNonFifo").GetBoolean());
            Assert.Equal(complete ? "/api/fg/shipping/complete" : "/api/fg/shipping/load", request.RequestUri!.AbsolutePath);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new ShippingActionResult(false,
                "Older stock exists.", FifoWarnings: [Older])) };
        })) { BaseAddress = new("https://example.test") };
        var api = new FinishedGoodsApi(http, new AuthState());
        var result = complete ? await api.CompleteShippingDeliveryAsync("DCR", true) : await api.LoadShippingDeliveryAsync("DCR", "PALLET", true);
        Assert.False(result.Success); Assert.Null(result.Delivery);
        Assert.Equal(Older, Assert.Single(result.FifoWarnings!));
    }

    sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => response(request);
    }
}
