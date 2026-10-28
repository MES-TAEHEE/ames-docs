using System.Net;
using System.Net.Http.Json;
using AMES.Api.Auth;
using AMES.Api.Endpoints;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace AMES.Api.Tests;

// Starts only the route harness; no worker, login/session write, or stock mutation.
public sealed class FgPutAwayPalletReadOnlyTests(ITestOutputHelper output)
{
    [PalletDatabaseFact]
    public async Task Existing_unlocated_assy_pallet_scans_as_one_unit_with_its_lots()
    {
        var builder=WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        await using var app=builder.Build();
        app.Use(async(ctx,next)=>
        {
            if(ctx.Request.Method=="GET" && (ctx.Request.Path=="/api/fg/putaway/scan" || ctx.Request.Path=="/api/wh/putaway/scan"))
                ctx.Items[BearerAuth.SessionKey]=new PopSessionDto {
                    SessionId=0,OperatorId="READ-TEST",EmployeeNo="READ-TEST",EmployeeName="Read-only test",
                    TerminalId="READ-TEST",LineId="",ShiftCode="",AuthMethod=default,
                    StartedAt=DateTime.MinValue,ExpiresAt=DateTime.MaxValue};
            await next(ctx);
        });
        app.MapFg(new AmesConnectionFactory(Environment.GetEnvironmentVariable("AMES_FG_PALLET_READ_TEST_DB")!));
        app.MapWh(new AmesConnectionFactory(Environment.GetEnvironmentVariable("AMES_FG_PALLET_READ_TEST_DB")!));
        await app.StartAsync();
        using var http=new HttpClient {BaseAddress=new Uri(app.Urls.Single())};
        var pallet=Environment.GetEnvironmentVariable("AMES_FG_PALLET_READ_TEST_BARCODE")!;
        using var response=await http.GetAsync("/api/fg/putaway/scan?barcode="+Uri.EscapeDataString(pallet));
        var body=await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var result=await response.Content.ReadFromJsonAsync<FgEndpoints.PutAwayResult>();
        Assert.True(result!.Success,body);
        Assert.Equal(pallet,result.Row!.LotNo);
        Assert.Equal("PALLET",result.Row.BarcodeType);
        Assert.NotEmpty(result.Row.Items!);
        Assert.All(result.Row.Items!,item=>{
            Assert.True(item.IsProductionCompleted); Assert.False(item.AlreadyStocked); Assert.True(item.Qty>0);
        });
        Assert.Equal(result.Row.Items!.Sum(item=>item.Qty),result.Row.Qty);
        using var unifiedResponse=await http.GetAsync("/api/wh/putaway/scan?barcode="+Uri.EscapeDataString(pallet));
        Assert.Equal(HttpStatusCode.OK,unifiedResponse.StatusCode);
        var unified=await unifiedResponse.Content.ReadFromJsonAsync<AMES.Pda.Services.PdaApi.PutAwaySelectionResult>();
        Assert.Equal("FG",unified!.SelectionType);
        Assert.Equal(pallet,unified.FinishedGoods!.LotNo);
        Assert.Equal(result.Row.Qty,unified.FinishedGoods.Qty);
        Assert.Equal(result.Row.Items!.Select(item=>item.LotNo),unified.FinishedGoods.Items!.Select(item=>item.LotNo));
        Assert.Empty(unified.Boxes);
        Assert.Empty(unified.SelectedBarcodes);
        using var wrongTypeResponse=await http.GetAsync("/api/wh/putaway/scan?barcode="+Uri.EscapeDataString("WO:"+pallet));
        Assert.Equal(HttpStatusCode.BadRequest,wrongTypeResponse.StatusCode);
        var generalBarcode=Environment.GetEnvironmentVariable("AMES_FG_PALLET_READ_TEST_GENERAL_BARCODE");
        if (!string.IsNullOrWhiteSpace(generalBarcode))
        {
            using var generalResponse=await http.GetAsync("/api/wh/putaway/scan?barcode="+Uri.EscapeDataString(generalBarcode));
            Assert.Equal(HttpStatusCode.OK,generalResponse.StatusCode);
            var general=await generalResponse.Content.ReadFromJsonAsync<AMES.Pda.Services.PdaApi.PutAwaySelectionResult>();
            Assert.Null(general!.FinishedGoods);
            Assert.NotEmpty(general.Boxes);
            output.WriteLine($"{generalBarcode}: general warehouse selection retained.");
        }
        output.WriteLine($"{pallet}: {result.Row.Items!.Count} ASSY LOTs, Qty {result.Row.Qty} — scan succeeded without writes.");
        await app.StopAsync();
    }

    private sealed class PalletDatabaseFactAttribute:FactAttribute
    {
        public PalletDatabaseFactAttribute()
        {
            if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AMES_FG_PALLET_READ_TEST_DB"))
                || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AMES_FG_PALLET_READ_TEST_BARCODE")))
                Skip="Set AMES_FG_PALLET_READ_TEST_DB and BARCODE for SELECT-only pallet scan verification.";
        }
    }
}
