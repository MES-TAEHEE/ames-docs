using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AMES.Api.Auth;
using AMES.Api.Endpoints;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using AMES.Pda.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AMES.Api.Tests;

public sealed class DispatchPreparationContractTests
{
    [Fact]
    public async Task Legacy_outbound_without_DCR_is_rejected_before_any_database_access()
    {
        var builder=WebApplication.CreateBuilder();builder.WebHost.UseUrls("http://127.0.0.1:0");builder.Logging.ClearProviders();
        await using var app=builder.Build();
        app.Use(async(context,next)=>{
            if(context.Request.Headers.ContainsKey("Test-Session"))context.Items[BearerAuth.SessionKey]=new PopSessionDto{SessionId=0,EmployeeNo="TEST",OperatorId="TEST",EmployeeName="Test",TerminalId="TEST",LineId="",ShiftCode="",AuthMethod=default,StartedAt=default,ExpiresAt=DateTime.MaxValue};
            await next(context);
        });
        app.MapFg(new AmesConnectionFactory("Server=127.0.0.1;Database=Unused;Integrated Security=true;Connect Timeout=1"));
        await app.StartAsync();using var http=new HttpClient{BaseAddress=new Uri(app.Urls.Single())};
        Assert.Equal(HttpStatusCode.Unauthorized,(await http.PostAsJsonAsync("/api/fg/outbound",new {Barcode="LOT-1"})).StatusCode);
        http.DefaultRequestHeaders.Add("Test-Session","1");
        using var response=await http.PostAsJsonAsync("/api/fg/outbound",new {Barcode="LOT-1"});
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        Assert.Contains("DCR",(await response.Content.ReadFromJsonAsync<FgEndpoints.OutboundUnitResult>())!.Message);
        await app.StopAsync();
    }

    [Fact]
    public async Task Preparation_contract_keeps_DCR_individual_barcode_and_prepared_counts_without_a_shipment()
    {
        using var handler=new Handler(async request=>{
            var body=await request.Content!.ReadFromJsonAsync<FgEndpoints.OutboundUnitReq>();
            Assert.Equal("DCR-1",body!.DeliveryBarcode);Assert.Equal("LOT-1",body.Barcode);
            var delivery=new ShippingDeliveryDto(1,"DCR-1","C1",DateTime.Today,null,"Open",["ORD-1"],[new("A","Part A",2,0,1)],[],[new("LOT-1","LOT-1","A",1)],true);
            return new(HttpStatusCode.OK){Content=JsonContent.Create(new FgEndpoints.OutboundUnitResult(true,"Awaiting trailer loading.",null,Delivery:delivery))};
        });
        using var http=new HttpClient(handler){BaseAddress=new Uri("http://test")};
        var result=await new FinishedGoodsApi(http,new AuthState()).FgOutboundAsync(new("LOT-1","C1",false,"DCR-1"));
        Assert.True(result.Success);Assert.Null(result.ShipmentId);Assert.True(result.Delivery!.SupportsPreparation);
        Assert.Equal(1,Assert.Single(result.Delivery.Parts).PreparedQty);Assert.Equal(0,Assert.Single(result.Delivery.Parts).LoadedQty);
        Assert.Empty(result.Delivery.Loads);Assert.Single(result.Delivery.Preparations!);
    }

    [Fact]
    public void Old_delivery_responses_do_not_enable_the_new_preparation_flow()
    {
        var response=JsonSerializer.Deserialize<ShippingDeliveryDto>("""{"DeliveryId":1,"DeliveryNo":"DCR-1","CustomerId":"C1","Status":"Open","OrderNos":[],"Parts":[],"Loads":[]}""")!;
        Assert.False(response.SupportsPreparation);
    }

    [Fact]
    public void DCR_contract_preserves_staging_overage_and_whole_unit_pick_recommendations()
    {
        var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var delivery=new ShippingDeliveryDto(1,"DCR-1","C1",DateTime.Today,null,"Open",["ORD-1"],
            [new("A","Part A",20,0,16)],[],[],true,[new("A","PAL-20","PALLET","FG01","FG storage",20,new(2026,11,1))]);
        var received=JsonSerializer.Deserialize<ShippingDeliveryDto>(JsonSerializer.Serialize(delivery,options),options)!;
        Assert.Equal(16m,Assert.Single(received.Parts).StagingQty);
        Assert.Equal("FG01",Assert.Single(received.Recommendations!).LocationNo);
        Assert.Equal(20m,Assert.Single(received.Recommendations!).Qty);
        var over=received with{Parts=[new("A","Part A",20,0,30)],Recommendations=[]};
        Assert.Equal(30m,Assert.Single(over.Parts).StagingQty);
        Assert.Equal(20m,Assert.Single(over.Parts).RequiredQty);
        var shortage=received with{Parts=[new("A","Part A",20,0,16,2)]};
        var shortageResponse=JsonSerializer.Deserialize<ShippingDeliveryDto>(JsonSerializer.Serialize(shortage,options),options)!;
        Assert.Equal(2m,Assert.Single(shortageResponse.Parts).StockShortageQty);
    }

    sealed class Handler(Func<HttpRequestMessage,Task<HttpResponseMessage>> send):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>send(request);
    }
}
