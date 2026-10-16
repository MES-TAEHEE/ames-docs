using System.Net;
using System.Net.Http.Json;
using AMES.Pda.Services;
using Xunit;

namespace AMES.Api.Tests;

public class PdaOutboundApiTests
{
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Session expired")]
    [InlineData(HttpStatusCode.Forbidden, "Access denied")]
    public async Task Permission_errors_are_not_reported_as_missing_barcodes(HttpStatusCode status, string message)
    {
        using var client = Client(() => new HttpResponseMessage(status));
        var result = await new FinishedGoodsApi(client, new AuthState()).FgOutboundScanAsync("LOT-001");
        Assert.False(result.Success);
        Assert.Contains(message, result.Message);
    }

    [Fact]
    public async Task Preserves_specific_API_rejection_reason()
    {
        using var client = Client(() => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(new PdaApi.FgOutboundUnitResult(false, "LOT-001: complete FG Put-Away first.", null))
        });
        var result = await new FinishedGoodsApi(client, new AuthState()).FgOutboundScanAsync("LOT-001");
        Assert.Equal("LOT-001: complete FG Put-Away first.", result.Message);
    }

    [Fact]
    public async Task Invalid_API_response_is_explained()
    {
        using var client = Client(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not json") });
        var result = await new FinishedGoodsApi(client, new AuthState()).FgOutboundScanAsync("LOT-001");
        Assert.False(result.Success);
        Assert.Contains("invalid or empty response", result.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Interrupted_outbound_warns_about_unknown_commit_status(bool timeout)
    {
        using var client = Client(() => timeout ? throw new TaskCanceledException() : throw new HttpRequestException());
        var result = await new FinishedGoodsApi(client, new AuthState()).FgOutboundAsync(new PdaApi.FgOutboundUnitReq("LOT-001"));
        Assert.False(result.Success);
        Assert.Contains("Check transaction history before retrying", result.Message);
        Assert.Contains("may already have been processed", result.Message);
    }

    private static HttpClient Client(Func<HttpResponseMessage> response)
        => new(new ResponseHandler(response)) { BaseAddress = new Uri("https://example.test") };

    private sealed class ResponseHandler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response());
    }
}
