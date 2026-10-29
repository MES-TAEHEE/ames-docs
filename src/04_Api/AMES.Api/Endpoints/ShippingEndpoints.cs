using AMES.Api.Auth;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;

namespace AMES.Api.Endpoints;

public static class ShippingEndpoints
{
    public static void MapShipping(this WebApplication app,AmesConnectionFactory factory)
    {
        var group=app.MapGroup("/api/fg/shipping").WithTags("Finished Goods");
        group.MapGet("/orders",(HttpContext ctx)=>
        {
            if(ctx.GetSession() is null)return Results.Unauthorized();
            try{return Results.Ok(new FinishedGoodsRepository(factory).ListShippingOrders(onlyOpen:true));}
            catch(Exception ex)
            {
                app.Logger.LogError(ex,"FG open shipment orders load failed");
                return Results.Json(new { Message="Shipment order service is unavailable. Check the API connection and retry." },statusCode:503);
            }
        });
        group.MapGet("/deliveries",(HttpContext ctx)=>
        {
            if(ctx.GetSession() is null)return Results.Unauthorized();
            try{return Results.Ok(new FinishedGoodsRepository(factory).ListOpenShippingDeliveries());}
            catch(Exception ex)
            {
                app.Logger.LogError(ex,"FG open DCR list load failed");
                return Results.Json(new { Message="DCR list service is unavailable. Check the API connection and retry." },statusCode:503);
            }
        });
        group.MapGet("/delivery",(HttpContext ctx,string barcode)=>
        {
            if(ctx.GetSession() is null)return Results.Unauthorized();
            return Run(app,()=>new FinishedGoodsRepository(factory).GetShippingDelivery(barcode));
        });
        group.MapPost("/load",(HttpContext ctx,DeliveryLoadRequest body)=>
        {
            if(ctx.GetSession() is not {} session)return Results.Unauthorized();
            return Run(app,()=>new FinishedGoodsRepository(factory).LoadShippingDelivery(body.DeliveryBarcode,body.PalletBarcode,session.OperatorId,body.AllowNonFifo));
        });
        group.MapPost("/unload",(HttpContext ctx,DeliveryLoadRequest body)=>
        {
            if(ctx.GetSession() is not {} session)return Results.Unauthorized();
            return Run(app,()=>new FinishedGoodsRepository(factory).UnloadShippingDelivery(body.DeliveryBarcode,body.PalletBarcode,session.OperatorId));
        });
        group.MapPost("/complete",(HttpContext ctx,DeliveryCompleteRequest body)=>
        {
            if(ctx.GetSession() is not {} session)return Results.Unauthorized();
            return Run(app,()=>new FinishedGoodsRepository(factory).CompleteShippingDelivery(body.DeliveryBarcode,session.OperatorId,body.AllowNonFifo));
        });
    }
    private static IResult Run(WebApplication app,Func<ShippingDeliveryDto> action)
    {
        try{return Results.Ok(new ShippingActionResult(true,"Saved.",action()));}
        catch(FifoConfirmationRequiredException ex)
        {return Results.Ok(new ShippingActionResult(false,ex.Message,FifoWarnings:ex.Warnings));}
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException || ex is SqlException sql && sql.Number is >=51000 and <=52999)
        {return Results.BadRequest(new ShippingActionResult(false,ex.Message));}
        catch(Exception ex)
        {app.Logger.LogError(ex,"FG shipping failed");return Results.Json(new ShippingActionResult(false,"Shipping service is unavailable. Retry after checking the API connection."),statusCode:503);}
    }
}
