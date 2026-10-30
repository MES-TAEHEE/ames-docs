using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AMES.Api.Endpoints;
using AMES.Pda.Services;
using Xunit;

namespace AMES.Api.Tests;

public sealed class SparePartScenarioReviewTests
{
    [Fact]
    public void Existing_cancel_clients_can_omit_the_transaction_id()
    {
        const string json = """{"SerialNo":"SPI-001","MoveType":"IN"}""";
        Assert.Null(JsonSerializer.Deserialize<WhEndpoints.SparePartCancelReq>(json)!.TransactionId);
        Assert.Null(JsonSerializer.Deserialize<PdaApi.SparePartCancelReq>(json)!.TransactionId);
    }

    [Fact]
    public async Task History_cancel_sends_the_selected_transaction_not_just_the_serial()
    {
        using var client = new HttpClient(new Handler(async request =>
        {
            Assert.Equal("/api/wh/sp/cancel", request.RequestUri!.AbsolutePath);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal(42, json.RootElement.GetProperty("transactionId").GetInt64());
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new PdaApi.SparePartMoveResult(true, "Cancelled")) };
        })) { BaseAddress = new Uri("https://example.test") };
        Assert.True((await new SparePartsApi(client, new AuthState()).CancelMoveAsync("SPI-001", "IN", transactionId: 42)).Success);
        Assert.Contains("\"Cancelled from Transactions.\", row.RowNo)", ReadPda("Wh/Wh08TransactionHistory.razor"));
    }

    [Theory]
    [InlineData("private async Task Receive()", "private async Task CancelInbound()", "Inbound Complete")]
    [InlineData("private async Task CancelInbound()", "private (string Label", "Inbound Cancelled")]
    public void Inbound_success_replaces_the_persistent_scan_message(string start, string end, string title)
    {
        var source = ReadPda("Sp/SpInbound.razor");
        var methodStart = source.IndexOf(start, StringComparison.Ordinal);
        var method = source[methodStart..source.IndexOf(end, methodStart, StringComparison.Ordinal)];
        var message = method.IndexOf("_message = $\"", StringComparison.Ordinal);
        var modal = method.IndexOf($"ShowModal(\"{title}\", _message, \"success\")", StringComparison.Ordinal);
        Assert.True(message > method.IndexOf("if (!result.Success)", StringComparison.Ordinal) && modal > message);
        Assert.Contains("_messageClass = \"ok\";", method[message..modal]);
    }

    [Fact]
    public void Released_serial_does_not_report_a_missing_master_location()
    {
        Assert.Contains("_lot.ItemStatus == \"RELEASED\" ? \"This serial was released. Cancel the release before receiving it again.\"",
            ReadPda("Sp/SpInbound.razor"));
    }

    [Theory]
    [InlineData("Sp/SpInbound.razor", "CancelInbound")]
    [InlineData("Sp/SpRelease.razor", "CancelRelease")]
    public void Cancel_handlers_reject_queued_clicks_while_busy_or_showing_a_modal(string file, string method)
    {
        var source = ReadPda(file);
        var start = source.IndexOf($"private async Task {method}()", StringComparison.Ordinal);
        var body = source[start..source.IndexOf("_busy = true;", start, StringComparison.Ordinal)];
        Assert.Contains("if (_busy || _modalOpen) return;", body);
    }

    [Fact]
    public void Safety_override_requires_a_pending_confirmation_and_a_single_request()
    {
        var source = ReadPda("Sp/SpRelease.razor");
        Assert.Contains("if (_busy || !_safetyReleasePending) return;", source);
        var start = source.IndexOf("private async Task ReleaseConfirmed()", StringComparison.Ordinal);
        Assert.Contains("if (_busy) return;", source[start..source.IndexOf("_busy = true;", start, StringComparison.Ordinal)]);
    }

    [Fact]
    public void Serialized_history_does_not_label_a_serial_as_the_master_spare_part_number()
    {
        var page = ReadPda("Wh/Wh08TransactionHistory.razor");
        Assert.Contains("IsSpareParts ? \"Barcode\" : \"Lot No\"", page);
        Assert.DoesNotContain("IsSpareParts ? \"EOS SP No\" : \"Lot No\"", page);
    }

    private static string ReadPda(string relative, [System.Runtime.CompilerServices.CallerFilePath] string file = "")
        => File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../05_Pda/AMES.Pda/Components/Pages", relative)));

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}
