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

public sealed class PdaTransactionFeedTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Page_bounds_are_checked_before_opening_a_connection(int size)
    {
        var repo=new PdaTransactionRepository(new AmesConnectionFactory("Server=127.0.0.1;Database=Unused;Integrated Security=true"));
        Assert.Throws<ArgumentException>(()=>repo.ReadPage(null,new(2026,1,1),new(2026,1,2),pageSize:size));
        Assert.Throws<ArgumentException>(()=>repo.ReadPage(null,new(2026,1,1),new(2026,1,2),beforeId:1));
    }

    [Fact]
    public async Task Feed_and_DCR_details_require_login_and_reject_bad_page_sizes()
    {
        var builder=WebApplication.CreateBuilder();builder.WebHost.UseUrls("http://127.0.0.1:0");builder.Logging.ClearProviders();
        await using var app=builder.Build();
        app.Use(async(ctx,next)=>
        {
            if(ctx.Request.Headers.ContainsKey("Test-Session"))ctx.Items[BearerAuth.SessionKey]=new PopSessionDto
            {SessionId=0,OperatorId="READ-TEST",EmployeeNo="READ-TEST",EmployeeName="Read test",TerminalId="READ-TEST",LineId="",ShiftCode="",AuthMethod=default,StartedAt=default,ExpiresAt=DateTime.MaxValue};
            await next(ctx);
        });
        var factory=new AmesConnectionFactory("Server=127.0.0.1;Database=Unused;Integrated Security=true");
        app.MapGroup("/api/wh").MapTransactionFeed(factory);
        app.MapGroup("/api/fg").MapAssyReady(factory);
        await app.StartAsync();using var http=new HttpClient{BaseAddress=new(app.Urls.Single())};
        foreach(var path in new[]{"/api/wh/transaction-feed","/api/wh/transaction-feed/dcr/1/parts","/api/fg/putaway/ready-pallets"})
            Assert.Equal(HttpStatusCode.Unauthorized,(await http.GetAsync(path)).StatusCode);
        http.DefaultRequestHeaders.Add("Test-Session","1");
        Assert.Equal(HttpStatusCode.BadRequest,(await http.GetAsync("/api/wh/transaction-feed?pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await http.GetAsync("/api/wh/transaction-feed?beforeId=1")).StatusCode);
        await app.StopAsync();
    }

    [ReadOnlyDatabaseFact]
    public async Task Actual_new_GET_routes_return_DCR_parts_and_pallet_ready_JSON_without_workers()
    {
        var factory=new AmesConnectionFactory(Environment.GetEnvironmentVariable("AMES_SHIPPING_READ_TEST_DB")!);
        var builder=WebApplication.CreateBuilder();builder.WebHost.UseUrls("http://127.0.0.1:0");builder.Logging.ClearProviders();
        await using var app=builder.Build();
        app.Use(async(ctx,next)=>
        {
            ctx.Items[BearerAuth.SessionKey]=new PopSessionDto
            {SessionId=0,OperatorId="READ-TEST",EmployeeNo="READ-TEST",EmployeeName="Read test",TerminalId="READ-TEST",LineId="",ShiftCode="",AuthMethod=default,StartedAt=default,ExpiresAt=DateTime.MaxValue};
            await next(ctx);
        });
        app.MapGroup("/api/wh").MapTransactionFeed(factory);app.MapGroup("/api/fg").MapAssyReady(factory);
        await app.StartAsync();using var http=new HttpClient{BaseAddress=new(app.Urls.Single())};
        var feed=await http.GetFromJsonAsync<PdaTransactionPageDto>("/api/wh/transaction-feed?search=DCR-261019-000001&from=2026-01-01&to=2026-12-31&pageSize=100");
        Assert.Equal(1,Assert.Single(feed!.Items).DeliveryId);
        var parts=await http.GetFromJsonAsync<List<PdaTransactionPartDto>>("/api/wh/transaction-feed/dcr/1/parts");
        Assert.Equal(3,parts!.Count);
        var lots=await http.GetFromJsonAsync<PdaTransactionPageDto>("/api/wh/transaction-feed?deliveryId=1&partNo=M2310-PI010NNB&pageSize=100");
        Assert.Equal(60,lots!.Items.Count);
        Assert.NotNull(await http.GetFromJsonAsync<List<AssyReadyPalletDto>>("/api/fg/putaway/ready-pallets"));
        await app.StopAsync();
    }

    [ReadOnlyDatabaseFact]
    public void Actual_history_pages_DCR_parts_LOTs_and_unassigned_ASSY_are_read_only()
    {
        var factory=new AmesConnectionFactory(Environment.GetEnvironmentVariable("AMES_SHIPPING_READ_TEST_DB")!);
        var repo=new PdaTransactionRepository(factory);
        var from=new DateTime(2026,1,1);var to=new DateTime(2026,12,31);
        var first=repo.ReadPage(null,from,to);
        Assert.InRange(first.Items.Count,0,100);
        if(first.HasMore)
        {
            Assert.Equal(100,first.Items.Count);
            var next=repo.ReadPage(null,from,to,beforeAt:first.BeforeAt,beforeId:first.BeforeId);
            Assert.Empty(next.Items.Select(x=>x.Id).Intersect(first.Items.Select(x=>x.Id)));
            Assert.All(next.Items,r=>Assert.True(r.OccurredAt<first.BeforeAt || (r.OccurredAt==first.BeforeAt&&r.Id<first.BeforeId)));
            output.WriteLine($"Paging: {first.Items.Count} + {next.Items.Count}, no duplicate IDs.");
        }
        var dcr=Assert.Single(repo.ReadPage("DCR-261019-000001",from,to).Items);
        Assert.Equal(1,dcr.DeliveryId);Assert.Equal(140m,dcr.Qty);Assert.Equal(3,dcr.PartCount);
        var parts=repo.ReadParts(1);Assert.Equal(3,parts.Count);Assert.Equal(dcr.Qty,parts.Sum(x=>x.Qty));
        foreach(var part in parts)
        {
            var lots=repo.ReadPage(null,new(2000,1,1),new(2000,1,1),deliveryId:1,partNo:part.PartNo);
            Assert.All(lots.Items,row=>Assert.Equal(part.PartNo,row.PartNo));
            Assert.Equal(part.Qty,lots.Items.Sum(x=>x.Qty));
            output.WriteLine($"DCR 1 / {part.PartNo}: {lots.Items.Count} LOTs, qty {part.Qty}.");
        }
        var ready=new AssyReadyRepository(factory).Read();
        Assert.DoesNotContain(ready,p=>p.PalletNo=="EOS-PLT-2610-0001");
        Assert.All(ready,p=>{Assert.NotEmpty(p.Parts);Assert.All(p.Parts,x=>Assert.True(x.Qty>0));});
        output.WriteLine($"ASSY Ready: {ready.Count} groups; already located pallet excluded.");
    }

    private sealed class ReadOnlyDatabaseFactAttribute:FactAttribute
    {
        public ReadOnlyDatabaseFactAttribute(){if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AMES_SHIPPING_READ_TEST_DB")))Skip="Opt-in SELECT-only actual database test.";}
    }
}
