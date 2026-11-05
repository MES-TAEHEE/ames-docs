using System.Net;
using System.Net.Http.Json;
using AMES.Contracts.Dto;
using AMES.Contracts.Exceptions;
using AMES.Pda.Services;
using Xunit;

namespace AMES.Api.Tests;

public sealed class PickingRequestConfirmationTests
{
    static ExistingPickingRequestDto Order()=>new("PK1","LINE-LOC","PART",200,50,150,"G","Partial");

    [Fact]
    public async Task Preflight_only_reads_matching_orders_and_does_not_create_anything()
    {
        var handler=new Handler(request=>JsonContent.Create(new[]{Order()}));
        using var http=new HttpClient(handler){BaseAddress=new("https://example.test")};
        Assert.Equal(150,Assert.Single(await Api(http).ExistingPickingRequestsAsync("LINE-LOC","PART")).RemainingQty);
        Assert.Equal(HttpMethod.Get,handler.Method);
        Assert.Contains("destination=LINE-LOC&partNo=PART",handler.Path);
        Assert.Equal(1,handler.Calls);
    }

    [Fact]
    public async Task Server_race_conflict_exposes_existing_order_details_without_automatic_retry()
    {
        var handler=new Handler(request=>JsonContent.Create(new PickingRequestConflictDto("Existing orders",[Order()])),HttpStatusCode.Conflict);
        using var http=new HttpClient(handler){BaseAddress=new("https://example.test")};
        var error=await Assert.ThrowsAsync<PickingRequestExistsException>(()=>Api(http).CreatePickingRequestAsync(new("LINE-LOC","PART",100)));
        Assert.Equal("PK1",Assert.Single(error.ExistingOrders).PickSlipNo);Assert.Equal(1,handler.Calls);
    }

    [Fact]
    public async Task Explicit_confirmation_is_sent_on_the_create_request()
    {
        var handler=new Handler(request=>JsonContent.Create(new PickingRequestCreatedDto("PK2")));
        using var http=new HttpClient(handler){BaseAddress=new("https://example.test")};
        await Api(http).CreatePickingRequestAsync(new("LINE-LOC","PART",100,ConfirmAdditional:true));
        Assert.Contains("\"confirmAdditional\":true",handler.Body);
    }

    [Fact]
    public async Task Ordinary_validation_conflicts_keep_the_server_message_instead_of_opening_duplicate_confirmation()
    {
        var handler=new Handler(request=>JsonContent.Create(new{message="Scan an active production-line detail location."}),HttpStatusCode.Conflict);
        using var http=new HttpClient(handler){BaseAddress=new("https://example.test")};
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>Api(http).CreatePickingRequestAsync(new("BAD","PART",1)));
        Assert.Equal("Scan an active production-line detail location.",error.Message);
    }

    [Fact]
    public void Existing_orders_must_be_confirmed_and_scanning_cannot_change_the_pending_request()
    {
        var source=Read("src/05_Pda/AMES.Pda/Components/Pages/Wh/WhPickingRequest.razor");
        Assert.Contains("await Api.ExistingPickingRequestsAsync",source);
        Assert.Contains("if(_existingOrders.Count==0)await CreatePendingRequest(false);",source);
        Assert.Contains("await CreatePendingRequest(true)",source);
        Assert.Contains("Click=\"CancelAdditionalRequest\"",source);
        Assert.Contains("_pendingRequest with{ConfirmAdditional=confirmed}",source);
        Assert.Contains("_created.Length>0 || _existingOrders.Count>0)return;",source);
        Assert.Contains("order.RequestedQty",source);Assert.Contains("order.PickedQty",source);Assert.Contains("order.RemainingQty",source);
        var repository=Read("src/02_Data/AMES.Data/Repositories/WarehouseRepository.PickingRequests.cs");
        Assert.Contains("S.ReqLocation=@Destination AND S.ItemNo=@Part",repository);
        Assert.Contains("S.Status IN('Open','Partial')",repository);
        Assert.Contains("if(existing.Count>0 && !input.ConfirmAdditional)",repository);
        var method=repository[repository.IndexOf("internal static string CreatePickingRequest",StringComparison.Ordinal)..];
        Assert.True(method.IndexOf("LockLinePicking",StringComparison.Ordinal)<method.IndexOf("ListExistingPickingRequests",StringComparison.Ordinal));
        Assert.True(method.IndexOf("ListExistingPickingRequests",StringComparison.Ordinal)<method.IndexOf("INSERT dbo.WH_PickSlip",StringComparison.Ordinal));
    }
    static WarehouseApi Api(HttpClient http)
    {var auth=new AuthState();return new(http,auth,new SparePartsApi(http,auth),new FinishedGoodsApi(http,auth));}
    sealed class Handler(Func<HttpRequestMessage,HttpContent> content,HttpStatusCode status=HttpStatusCode.OK):HttpMessageHandler
    {
        public HttpMethod? Method;public string Path="",Body="";public int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Calls++;Method=request.Method;Path=request.RequestUri!.PathAndQuery;
            if(request.Content is not null)Body=await request.Content.ReadAsStringAsync(token);
            return new(status){Content=content(request)};
        }
    }
    static string Read(string path,[System.Runtime.CompilerServices.CallerFilePath]string source="")
        =>File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!,"../../..",path)));
}
