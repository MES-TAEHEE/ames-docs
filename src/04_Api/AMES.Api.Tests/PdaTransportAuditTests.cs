using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using AMES.Pda.Services;
using Xunit;

namespace AMES.Api.Tests;

public sealed class PdaTransportAuditTests
{
    public static IEnumerable<object[]> InvalidReplies()
        => from operation in new[] { "putaway", "return", "outbound" }
           from body in new[] { "", "not-json", "null", "{}" }
           select new object[] { operation, body };

    [Theory]
    [MemberData(nameof(InvalidReplies))]
    public async Task Invalid_success_response_must_not_report_a_completed_stock_operation(string operation, string body)
    {
        using var client = Client(_ => new(HttpStatusCode.OK) { Content = new StringContent(body) });
        var result = await Run(new(client, new AuthState()), operation);
        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.Contains("response", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("putaway")]
    [InlineData("return")]
    [InlineData("outbound")]
    public async Task Error_HTTP_status_cannot_be_overridden_by_a_success_body(string operation)
    {
        using var client = Client(_ => new(HttpStatusCode.InternalServerError)
            { Content = JsonContent.Create(new { success = true, message = "Saved" }) });
        Assert.False((await Run(new(client, new AuthState()), operation)).Success);
    }

    [Theory]
    [InlineData("putaway")]
    [InlineData("return")]
    [InlineData("outbound")]
    public async Task Valid_success_responses_are_preserved(string operation)
    {
        using var client = Client(_ => new(HttpStatusCode.OK)
            { Content = JsonContent.Create(new { success = true, message = "Saved" }) });
        var result = await Run(new(client, new AuthState()), operation);
        Assert.True(result.Success);
        Assert.Equal("Saved", result.Message);
    }

    [Theory]
    [InlineData("putaway")]
    [InlineData("return")]
    [InlineData("outbound")]
    public async Task Specific_business_rejections_are_preserved(string operation)
    {
        using var client = Client(_ => new(HttpStatusCode.BadRequest)
            { Content = JsonContent.Create(new { detail = "Stock changed. Scan it again." }) });
        var result = await Run(new(client, new AuthState()), operation);
        Assert.False(result.Success);
        Assert.Equal("Stock changed. Scan it again.", result.Message);
    }

    [Theory]
    [InlineData("putaway")]
    [InlineData("return")]
    [InlineData("outbound")]
    public async Task Lost_write_responses_warn_against_blind_retry(string operation)
    {
        using var client = Client(_ => throw new HttpRequestException("Connection lost"));
        var result = await Run(new(client, new AuthState()), operation);
        Assert.False(result.Success);
        Assert.Contains("before retrying", result.Message);
        Assert.Contains("may already", result.Message);
    }

    [Theory]
    [InlineData("putaway")]
    [InlineData("return")]
    public async Task Finished_stock_write_responses_release_their_content(string operation)
    {
        using var content = new TrackedContent("{\"success\":true,\"message\":\"Saved\"}");
        using var client = Client(_ => new(HttpStatusCode.OK) { Content = content });
        await Run(new(client, new AuthState()), operation);
        Assert.True(content.WasDisposed);
    }

    [Fact]
    public async Task Capacity_quantity_uses_invariant_decimal_not_the_device_locale()
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        try
        {
            string? query = null;
            using var client = Client(request =>
            {
                query = request.RequestUri!.Query;
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { locationId = "FG-1", isValid = true }) };
            });
            var result = await new FinishedGoodsApi(client, new AuthState())
                .FgValidatePutAwayLocationAsync("FG-1", "PART-1", null, 1.5m, "LOCATION");
            Assert.Contains("qty=1.5&", query);
            Assert.True(result!.IsValid);
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Fact]
    public async Task Database_time_uses_the_current_session_on_a_new_domain_client()
    {
        var auth = new AuthState();
        auth.SignIn("audit-token", null!);
        using var client = Client(request =>
        {
            Assert.Equal("audit-token", request.Headers.Authorization?.Parameter);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new DateTime(2026, 10, 29)) };
        });
        var api = new WarehouseApi(client, auth, new(client, auth), new(client, auth));
        Assert.Equal(new DateTime(2026, 10, 29), await api.DatabaseTimeAsync());
    }

    private static async Task<(bool Success, string Message)> Run(FinishedGoodsApi api, string operation)
    {
        if (operation == "return") { var r = await api.FgReturnAsync(new("LOT-1", "CHECK", null)); return (r.Success, r.Message); }
        if (operation == "outbound") { var r = await api.FgOutboundAsync(new("LOT-1")); return (r.Success, r.Message); }
        var result = await api.FgConfirmPutAwayAsync(new("LOT-1", "FG-1"));
        return (result.Success, result.Message);
    }

    [Theory]
    [InlineData("status")]
    [InlineData("lot")]
    public async Task Picking_lookup_preserves_server_failure_instead_of_masquerading_as_not_found(string operation)
    {
        using var client = Client(_ => new(HttpStatusCode.ServiceUnavailable)
            { Content = JsonContent.Create(new { detail = "Picking database is unavailable." }) });
        var auth = new AuthState();
        var wh = new WarehouseApi(client, auth, new(client, auth), new(client, auth));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            if (operation == "status") await wh.WhReleaseSlipStatusAsync("PK-1");
            else await wh.WhReleaseLotAsync("PK-1", "LOT-1");
        });
        Assert.Equal("Picking database is unavailable.", error.Message);
    }

    [Theory]
    [InlineData("location")]
    [InlineData("status")]
    [InlineData("lot")]
    [InlineData("outgoing")]
    [InlineData("document")]
    [InlineData("adjust-scan")]
    [InlineData("receive")]
    [InlineData("move")]
    [InlineData("putaway")]
    [InlineData("cancel")]
    [InlineData("adjust")]
    public async Task Warehouse_helpers_release_owned_responses(string operation)
    {
        using var content = new TrackedContent("{\"success\":true,\"message\":\"Saved\"}");
        using var client = Client(_ => new(HttpStatusCode.OK) { Content = content });
        var auth = new AuthState();
        var wh = new WarehouseApi(client, auth, new(client, auth), new(client, auth));
        switch (operation)
        {
            case "location": await wh.WhScanLocationAsync("LOC-1"); break;
            case "status": await wh.WhReleaseSlipStatusAsync("PK-1"); break;
            case "lot": await wh.WhReleaseLotAsync("PK-1", "LOT-1"); break;
            case "outgoing": await wh.WhDirectOutgoingLotAsync("LOT-1"); break;
            case "document": await wh.WhInboundDocumentAsync("LOCAL", "NOTE-1"); break;
            case "adjust-scan": await wh.WhScanAdjustAsync("LOT-1"); break;
            case "receive": await wh.WhReceiveInboundAsync(new("LOCAL", "LOT-1", "")); break;
            case "move": await wh.WhMoveInboundLocationAsync(new("LOCAL", "LOT-1", "LOC-1")); break;
            case "putaway": await wh.WhConfirmPutAwayAsync(new(["LOT-1"], "LOC-1")); break;
            case "cancel": await wh.WhCancelInboundAsync(new("LOCAL", "LOT-1")); break;
            case "adjust": await wh.WhSaveAdjustQtyAsync(new("LOCAL", "LOT-1", 1, "COUNT_DIFF", null)); break;
        }
        Assert.True(content.WasDisposed);
    }

    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> response)
        => new(new Handler(response)) { BaseAddress = new Uri("https://example.test") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response(request));
    }

    private sealed class TrackedContent(string content) : StringContent(content)
    {
        public bool WasDisposed { get; private set; }
        protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
    }
}
