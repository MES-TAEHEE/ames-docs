using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AMES.Api.Endpoints;
using AMES.Contracts.Dto;
using AMES.Pda.Services;
using Xunit;

namespace AMES.Api.Tests;

public sealed class SparePartWorkOrderTests
{
    [Fact]
    public void Old_clients_can_still_release_without_a_work_order()
    {
        const string json = """{"EosSpNo":"SPI-001","Qty":1}""";
        Assert.Null(JsonSerializer.Deserialize<WhEndpoints.SparePartMoveReq>(json)!.WorkOrderNo);
        Assert.Null(JsonSerializer.Deserialize<PdaApi.SparePartMoveReq>(json)!.WorkOrderNo);
    }

    [Fact]
    public async Task Release_transmits_the_selected_work_order()
    {
        using var client = new HttpClient(new Handler(async request =>
        {
            Assert.Equal("/api/wh/sp/release", request.RequestUri!.AbsolutePath);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("MWO-2610-001", json.RootElement.GetProperty("workOrderNo").GetString());
            Assert.Equal(1, json.RootElement.GetProperty("qty").GetInt32());
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new PdaApi.SparePartMoveResult(true, "Released")) };
        })) { BaseAddress = new Uri("https://example.test") };
        var result = await new SparePartsApi(client, new AuthState()).SpReleaseAsync(new("SPI-001", WorkOrderNo: "MWO-2610-001"));
        Assert.True(result.Success);
    }

    [Fact]
    public async Task Work_order_choices_use_the_authenticated_spare_parts_endpoint()
    {
        using var client = new HttpClient(new Handler(request =>
        {
            Assert.Equal("/api/wh/sp/work-orders", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new[] { new PdaSparePartWorkOrder("MWO-1", "EQ-1", "Equipment", "Inspect", "ASSIGNED") })
            });
        })) { BaseAddress = new Uri("https://example.test") };
        Assert.Equal("MWO-1", Assert.Single(await new SparePartsApi(client, new AuthState()).OpenWorkOrdersAsync()).WorkOrderNo);
    }

    [Theory]
    [InlineData("EQ-2", "MWO-2")]
    [InlineData("Equipment two", "MWO-2")]
    [InlineData(null, "MWO-1")]
    [InlineData("", "MWO-1")]
    public void Scanned_equipment_matches_are_first_but_all_open_choices_remain(string? equipment, string first)
    {
        var choices = new[]
        {
            new PdaSparePartWorkOrder("MWO-1", "EQ-1", "Equipment one", null, "ISSUED"),
            new PdaSparePartWorkOrder("MWO-2", "EQ-2", "Equipment two", null, "IN_PROGRESS"),
            new PdaSparePartWorkOrder("MWO-3", null, null, null, "ASSIGNED")
        };
        var ordered = SparePartValidation.OrderWorkOrders(choices, equipment);
        Assert.Equal(3, ordered.Count);
        Assert.Equal(first, ordered[0].WorkOrderNo);
    }

    [Fact]
    public async Task Closed_work_order_rejection_keeps_the_specific_server_message()
    {
        const string message = "Release cannot be cancelled after its maintenance work order is completed or closed.";
        using var client = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(new { detail = message })
        }))) { BaseAddress = new Uri("https://example.test") };
        var result = await new SparePartsApi(client, new AuthState()).CancelMoveAsync("SPI-001", "OUT");
        Assert.False(result.Success);
        Assert.Equal(message, result.Message);
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}
