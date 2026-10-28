using System.Net;
using System.Net.Http.Json;
using AMES.Api.Auth;
using AMES.Api.Endpoints;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace AMES.Api.Tests;

// SELECT-only route harness. No application workers, login writes, loading or shipment calls.
public sealed class ShippingDeliveryReadOnlyTests(ITestOutputHelper output)
{
    [ShippingDatabaseFact]
    public async Task Open_DCRs_are_unique_and_counts_match_their_order_and_part_details()
    {
        var factory = new AmesConnectionFactory(Environment.GetEnvironmentVariable("AMES_SHIPPING_READ_TEST_DB")!);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        await using var app = builder.Build();
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Method == "GET" && ctx.Request.Headers.ContainsKey("Test-Authenticated"))
                ctx.Items[BearerAuth.SessionKey] = new PopSessionDto
                {
                    SessionId = 0, OperatorId = "READ-TEST", EmployeeNo = "READ-TEST", EmployeeName = "Read-only test",
                    TerminalId = "READ-TEST", LineId = "", ShiftCode = "", AuthMethod = default,
                    StartedAt = DateTime.MinValue, ExpiresAt = DateTime.MaxValue
                };
            await next(ctx);
        });
        app.MapShipping(factory);
        await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var anonymous = await http.GetAsync("/api/fg/shipping/deliveries");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        http.DefaultRequestHeaders.Add("Test-Authenticated", "1");
        using var response = await http.GetAsync("/api/fg/shipping/deliveries");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rows = (await response.Content.ReadFromJsonAsync<List<ShippingDeliveryListDto>>())!;
        Assert.Equal(rows.Count, rows.Select(row => row.DeliveryId).Distinct().Count());
        Assert.Equal(rows.OrderBy(row => row.ShipDate).ThenBy(row => row.DeliveryId).Select(row => row.DeliveryId),
            rows.Select(row => row.DeliveryId));
        var repository = new FinishedGoodsRepository(factory);
        foreach (var row in rows)
        {
            Assert.Contains(row.Status, new[] { "Open", "Loading" });
            Assert.True(row.OrderCount > 0);
            Assert.True(row.PartCount > 0);
            var detail = repository.GetShippingDelivery(row.DeliveryNo);
            Assert.Equal(row.OrderCount, detail.OrderNos.Count);
            Assert.Equal(row.PartCount, detail.Parts.Count);
            output.WriteLine($"{row.DeliveryNo}: one row, {row.OrderCount} orders, {row.PartCount} parts.");
        }
        output.WriteLine($"{rows.Count} open DCRs checked without stock or document changes.");
        await app.StopAsync();
    }

    private sealed class ShippingDatabaseFactAttribute : FactAttribute
    {
        public ShippingDatabaseFactAttribute()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AMES_SHIPPING_READ_TEST_DB")))
                Skip = "Set AMES_SHIPPING_READ_TEST_DB for SELECT-only DCR list verification.";
        }
    }
}
