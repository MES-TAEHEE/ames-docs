using System.Net;
using System.Net.Http.Json;
using AMES.Pda.Services;
using AMES.Api.Endpoints;
using Xunit;

namespace AMES.Api.Tests;

public sealed class WarehouseInboundLookupTests
{
    [Fact]
    public void Same_part_at_different_line_positions_is_not_combined()
    {
        var first = WhEndpoints.ReleaseDestinationKey("part-a", "li01a1f1");
        Assert.Equal(first, WhEndpoints.ReleaseDestinationKey(" PART-A ", " LI01A1F1 "));
        Assert.NotEqual(first, WhEndpoints.ReleaseDestinationKey("PART-A", "LI02A1F1"));
        Assert.Equal(WhEndpoints.ReleaseDestinationKey("PART-A", null), WhEndpoints.ReleaseDestinationKey("part-a", ""));
    }
    [Theory]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.OK)]
    public async Task Missing_barcode_returns_null_instead_of_a_service_failure(HttpStatusCode status)
    {
        using var client = Client(status);
        Assert.Null(await Api(client).WhScanInboundAsync("LOCAL", "MISSING"));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "Only boxes from a shipped delivery note can be received.")]
    [InlineData(HttpStatusCode.BadRequest, "Primary locations cannot hold stock. Scan a detail location.")]
    [InlineData(HttpStatusCode.Forbidden, "Access denied.")]
    public async Task Validation_and_permission_failures_are_not_misreported_as_missing(HttpStatusCode status, string message)
    {
        using var client = Client(status, message);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Api(client).WhScanInboundAsync("LOCAL", "BOX-1"));
        Assert.Contains(message, error.Message);
    }

    static WarehouseApi Api(HttpClient client)
    {
        var auth = new AuthState();
        return new(client, auth, new SparePartsApi(client, auth), new FinishedGoodsApi(client, auth));
    }

    static HttpClient Client(HttpStatusCode status, string? message = null)
        => new(new Handler(status, message)) { BaseAddress = new("https://example.test") };

    sealed class Handler(HttpStatusCode status, string? message) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status) { Content = message is not null ? JsonContent.Create(new { detail = message })
                : status == HttpStatusCode.OK ? new StringContent("null", System.Text.Encoding.UTF8, "application/json") : null });
    }
}
