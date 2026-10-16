using System.Net;
using System.Net.Http.Json;
using AMES.Api.Endpoints;
using AMES.Api.Auth;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using AMES.Pda.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AMES.Api.Tests;

// Opt-in SELECT-only verification against an existing database; never starts a worker or writes a work order.
public class PdaMaintenanceReadOnlyTests
{
    [MaintenanceDatabaseFact]
    public async Task Board_and_work_details_match_existing_schema_and_unknown_employee_is_denied()
    {
        var builder=WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        await using var app=builder.Build();
        app.Use(async(ctx,next)=>
        {
            if(ctx.Request.Method=="GET" && ctx.Request.Headers.TryGetValue("Test-Employee",out var employee))
                ctx.Items[BearerAuth.SessionKey]=new PopSessionDto {SessionId=0,OperatorId=employee.ToString(),EmployeeNo=employee.ToString(),
                    EmployeeName="Read-only test",TerminalId="READ-TEST",LineId="",ShiftCode="",AuthMethod=default,
                    StartedAt=DateTime.MinValue,ExpiresAt=DateTime.MaxValue};
            await next(ctx);
        });
        app.MapPdaMaintenance(new AmesConnectionFactory(Environment.GetEnvironmentVariable("AMES_MNT_READ_TEST_DB")!));
        await app.StartAsync();
        using var http=new HttpClient {BaseAddress=new Uri(app.Urls.Single())};
        using var unauth=await http.GetAsync("/api/mnt/pda/board");
        Assert.Equal(HttpStatusCode.Unauthorized,unauth.StatusCode);
        http.DefaultRequestHeaders.Add("Test-Employee","__NO_MNT_USER__");
        using var denied=await http.GetAsync("/api/mnt/pda/board");
        Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        http.DefaultRequestHeaders.Remove("Test-Employee");
        http.DefaultRequestHeaders.Add("Test-Employee",Environment.GetEnvironmentVariable("AMES_MNT_READ_TEST_EMPLOYEE")??"PTEST");
        var client=new MaintenanceApi(http,new AuthState());
        var board=await client.Board(null,null,false,false);
        Assert.NotNull(board);Assert.True(board.Access.CanRead);
        Assert.NotEqual(default,board.Today);
        foreach(var work in board.WorkOrders)
        {
            using var response=await http.GetAsync($"/api/mnt/pda/work-orders/{work.Id}");
            Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
            var detail=await response.Content.ReadFromJsonAsync<PdaMntDetail>();
            Assert.Equal(work.Id,detail!.Work.Id);
            Assert.Equal(work.TaskCount,detail.Tasks.Count);
        }
        using var range=await http.GetAsync("/api/mnt/pda/board?from=2026-01-01&to=2028-01-01");
        Assert.Equal(HttpStatusCode.BadRequest,range.StatusCode);
        // The test middleware never supplies a session to writes.
        using var mutation=await http.PostAsJsonAsync("/api/mnt/pda/work-orders/1/start",new PdaMntStart(false));
        Assert.Equal(HttpStatusCode.Unauthorized,mutation.StatusCode);
        await app.StopAsync();
    }

    private sealed class MaintenanceDatabaseFactAttribute:FactAttribute
    {
        public MaintenanceDatabaseFactAttribute()
        {
            if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AMES_MNT_READ_TEST_DB")))
                Skip="Set AMES_MNT_READ_TEST_DB to enable SELECT-only maintenance verification.";
        }
    }
}
