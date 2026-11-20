using AMES.Api.Endpoints;
using AMES.Data.Connection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AMES.Api.Tests;

public sealed class FgDefectAuthorizationTests
{
    [Fact]
    public async Task All_routes_require_a_session_before_reading_database_or_body()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        await using var app = builder.Build();
        app.MapGroup("/api/fg").MapFgDefects(new AmesConnectionFactory("Server=invalid;Database=unused;Integrated Security=true"), app.Logger);
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        foreach (var path in new[] { "access", "scan?barcode=LOT", "1/photo" })
        {
            using var response = await client.GetAsync("/api/fg/defects/" + path);
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using var post = await client.PostAsync("/api/fg/defects", new StringContent("not JSON"));
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, post.StatusCode);
        await app.StopAsync();
    }
}
