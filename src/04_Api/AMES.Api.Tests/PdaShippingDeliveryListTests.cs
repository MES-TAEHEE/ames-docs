using System.Net;
using System.Net.Http.Json;
using AMES.Contracts.Dto;
using AMES.Pda.Services;
using Xunit;

namespace AMES.Api.Tests;

public sealed class PdaShippingDeliveryListTests
{
    [Fact]
    public async Task List_requests_DCRs_and_keeps_multi_order_deliveries_as_one_row()
    {
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/fg/shipping/deliveries", request.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new[]
                {
                    new ShippingDeliveryListDto(1, "DCR-1", "C-1", new DateTime(2026, 10, 28), null, "Open", 2, 3),
                    new ShippingDeliveryListDto(2, "DCR-2", "C-2", new DateTime(2026, 10, 29), "TR-2", "Loading", 1, 1)
                })
            };
        })) { BaseAddress = new Uri("https://example.test") };

        var rows = await new FinishedGoodsApi(http, new AuthState()).OpenShippingDeliveriesAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal("DCR-1", rows[0].DeliveryNo);
        Assert.Equal(2, rows[0].OrderCount);
        Assert.Equal(3, rows[0].PartCount);
        Assert.Null(rows[0].TrailerNo);
        Assert.Equal("Loading", rows[1].Status);
    }

    [Fact]
    public async Task List_failure_is_not_reported_as_an_empty_DCR_list()
    {
        using var http = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = JsonContent.Create(new { Message = "DCR list service is unavailable." })
        })) { BaseAddress = new Uri("https://example.test") };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new FinishedGoodsApi(http, new AuthState()).OpenShippingDeliveriesAsync());
        Assert.Contains("DCR list service is unavailable", error.Message);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response(request));
    }
}
