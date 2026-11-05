using System.Net;
using System.Net.Http.Json;
using AMES.Api.Auth;
using AMES.Api.Endpoints;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using AMES.Pda.Services;
using Xunit;

namespace AMES.Api.Tests;

public sealed class PdaPickingRequestTests
{
    [Fact]
    public async Task Both_request_routes_require_a_session_before_accessing_database()
    {
        await using var app=App("Server=127.0.0.1;Database=Unused;Integrated Security=true");
        await app.StartAsync();
        using var http=new HttpClient{BaseAddress=new(app.Urls.Single())};
        Assert.Equal(HttpStatusCode.Unauthorized,(await http.GetAsync("/api/wh/picking-requests/options")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await http.GetAsync("/api/wh/picking-requests/existing?destination=LINE&partNo=PART")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await http.PostAsJsonAsync("/api/wh/picking-requests",new CreatePickingRequestDto("L","P",1,new(2026,11,5)))).StatusCode);
        await app.StopAsync();
    }

    [Fact]
    public async Task Client_sends_decimal_quantity_and_date_without_a_caller_supplied_actor()
    {
        var handler=new Handler();
        using var http=new HttpClient(handler){BaseAddress=new("https://example.test")};
        var auth=new AuthState();
        var api=new WarehouseApi(http,auth,new SparePartsApi(http,auth),new FinishedGoodsApi(http,auth));
        Assert.Equal("PK-TEST",(await api.CreatePickingRequestAsync(new("LOC","RESIN",.125m,new(2026,11,5)))).PickSlipNo);
        Assert.Equal("/api/wh/picking-requests",handler.Path);
        Assert.Contains("\"qty\":0.125",handler.Body);
        Assert.DoesNotContain("actor",handler.Body,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Client_preserves_specific_validation_error()
    {
        using var http=new HttpClient(new Handler(true)){BaseAddress=new("https://example.test")};
        var auth=new AuthState();
        var api=new WarehouseApi(http,auth,new SparePartsApi(http,auth),new FinishedGoodsApi(http,auth));
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>api.CreatePickingRequestAsync(new("LOC","RESIN",0,new(2026,11,5))));
        Assert.Equal("Enter a positive quantity.",error.Message);
    }

    [Fact]
    public void Pda_menu_form_and_server_share_the_existing_request_workflow()
    {
        Assert.Contains("/wh/picking-request",Read("src/05_Pda/AMES.Pda/Components/Pages/Wh/WhHome.razor"));
        var page=Read("src/05_Pda/AMES.Pda/Components/Pages/Wh/WhPickingRequest.razor");
        Assert.Contains("if(_saving || _loading || _options?.CanCreate!=true || _created.Length>0)return;",page);
        Assert.Contains("_created=result.PickSlipNo;",page);
        var endpoint=Read("src/04_Api/AMES.Api/Endpoints/PdaPickingRequestEndpoints.cs");
        Assert.Contains("HasPickingRequestAccess(session.EmployeeNo,'E')",endpoint);
        Assert.Contains("DbClock.ReadNow(factory).Date,LineLocationOnly:true,ConfirmAdditional:body.ConfirmAdditional)",endpoint);
        Assert.Contains("ListPickingRequestDestinations(lineLocationsOnly:true)",endpoint);
        Assert.DoesNotContain("body.RequiredAt",endpoint);
        Assert.Contains("g.MapPickingRequests(factory);",Read("src/04_Api/AMES.Api/Endpoints/WhEndpoints.cs"));
    }

    [LocalCloneFact]
    public async Task Local_clone_options_check_actual_roles_and_bad_quantity_never_writes()
    {
        var cs=Environment.GetEnvironmentVariable("AMES_LINE_PICKING_READONLY_CONNECTION");
        Assert.False(string.IsNullOrWhiteSpace(cs),"Use the explicit local clone for this read-only check.");
        var target=new SqlConnectionStringBuilder(cs);
        Assert.Equal("AMES_TEST_PDA_20261029",target.InitialCatalog);
        Assert.Equal("localhost\\SQLEXPRESS",target.DataSource,ignoreCase:true);
        using var connection=new SqlConnection(cs);await connection.OpenAsync();
        using var command=new SqlCommand("""
            SELECT TOP(1) P.EmployeeNo FROM dbo.SYS_UserProfile P
            JOIN dbo.AspNetUserRoles U ON U.UserId=P.UserID
            JOIN dbo.SYS_RolePermission R ON R.RoleID=U.RoleId
            WHERE P.AccountStatus='ACTIVE' AND R.ScreenCode='WH-05' AND R.ModuleCode='WEB'
              AND R.PermissionLevel LIKE '%R%' AND R.PermissionLevel LIKE '%E%';
            """,connection);
        var employee=Convert.ToString(await command.ExecuteScalarAsync());Assert.False(string.IsNullOrEmpty(employee));
        await using var app=App(cs!,employee);
        await app.StartAsync();using var http=new HttpClient{BaseAddress=new(app.Urls.Single())};
        var options=await http.GetFromJsonAsync<PickingRequestOptionsDto>("/api/wh/picking-requests/options");
        Assert.True(options!.CanCreate);Assert.NotEmpty(options.Parts);Assert.NotEmpty(options.Destinations);
        using var locations=new SqlCommand("SELECT LocationID FROM dbo.MD_Location L JOIN dbo.MD_Line M ON M.LineID=L.LineID WHERE L.LocationLevel=2 AND COALESCE(L.ActiveFlag,1)=1 AND COALESCE(M.Status,'ACTIVE')='ACTIVE'",connection);
        var lineLocations=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using(var reader=await locations.ExecuteReaderAsync())while(await reader.ReadAsync())lineLocations.Add(reader.GetString(0));
        Assert.All(options.Destinations,p=>Assert.Contains(p.Code,lineLocations));
        Assert.All(options.Parts,p=>Assert.False(string.IsNullOrWhiteSpace(p.Unit)));
        Assert.Equal(HttpStatusCode.BadRequest,(await http.PostAsJsonAsync("/api/wh/picking-requests",new CreatePickingRequestDto(options.Destinations[0].Code,options.Parts[0].Code,0,options.Today))).StatusCode);
        http.DefaultRequestHeaders.Add("Test-Denied","1");
        Assert.Equal(HttpStatusCode.Forbidden,(await http.GetAsync("/api/wh/picking-requests/options")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await http.GetAsync("/api/wh/picking-requests/existing?destination=LINE&partNo=PART")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await http.PostAsJsonAsync("/api/wh/picking-requests",new CreatePickingRequestDto("L","P",1,options.Today))).StatusCode);
        await app.StopAsync();
    }

    static WebApplication App(string connection,string? employee=null)
    {
        var builder=WebApplication.CreateBuilder();builder.WebHost.UseUrls("http://127.0.0.1:0");builder.Logging.ClearProviders();
        var app=builder.Build();
        app.Use(async(context,next)=>
        {
            if(employee is not null)
            {
                var actor=context.Request.Headers.ContainsKey("Test-Denied")?"NO-SUCH-REQUEST-USER":employee;
                context.Items[BearerAuth.SessionKey]=new PopSessionDto{SessionId=0,OperatorId=actor,EmployeeNo=actor,EmployeeName="Test",TerminalId="TEST",LineId="",ShiftCode="",AuthMethod=default,StartedAt=default,ExpiresAt=DateTime.MaxValue};
            }
            await next(context);
        });
        app.MapGroup("/api/wh").MapPickingRequests(new AmesConnectionFactory(connection));return app;
    }
    sealed class Handler(bool fail=false):HttpMessageHandler
    {
        public string Path="",Body="";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Path=request.RequestUri!.AbsolutePath;Body=await request.Content!.ReadAsStringAsync(token);
            return new(fail?HttpStatusCode.BadRequest:HttpStatusCode.OK){Content=fail?JsonContent.Create(new{message="Enter a positive quantity."}):JsonContent.Create(new PickingRequestCreatedDto("PK-TEST"))};
        }
    }
    static string Read(string path,[System.Runtime.CompilerServices.CallerFilePath]string source="")
        =>File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!,"../../..",path)));

    private sealed class LocalCloneFactAttribute:FactAttribute
    {
        public LocalCloneFactAttribute()
        {
            if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AMES_LINE_PICKING_READONLY_CONNECTION")))
                Skip="Set the explicit local clone connection for read-only request permission verification.";
        }
    }
}
