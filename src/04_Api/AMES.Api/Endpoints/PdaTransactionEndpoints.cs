using AMES.Api.Auth;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Data.Services;

namespace AMES.Api.Endpoints;

public static class PdaTransactionEndpoints
{
    public static void MapTransactionFeed(this RouteGroupBuilder group,AmesConnectionFactory factory)
    {
        group.MapGet("/transaction-feed",(HttpContext ctx,string? search,DateTime? from,DateTime? to,
            string? worker,string? reason,string? direction,DateTime? beforeAt,long? beforeId,
            int? pageSize,int? deliveryId,string? partNo)=>
        {
            if(ctx.GetSession() is null)return Results.Unauthorized();
            var start=from??DbClock.Today.AddMonths(-1);var end=to??DbClock.Today.AddMonths(1);
            if(start>end || end.Date==DateTime.MaxValue.Date || (end-start).TotalDays>366)
                return Results.BadRequest(new{Message="Select a valid date range (up to 366 days)."});
            try{return Results.Ok(new PdaTransactionRepository(factory).ReadPage(search,start,end,worker,reason,
                direction,beforeAt,beforeId,pageSize??100,deliveryId,partNo));}
            catch(ArgumentException ex){return Results.BadRequest(new{Message=ex.Message});}
        });
        group.MapGet("/transaction-feed/dcr/{deliveryId:int}/parts",(HttpContext ctx,int deliveryId)=>
        {
            if(ctx.GetSession() is null)return Results.Unauthorized();
            try{return Results.Ok(new PdaTransactionRepository(factory).ReadParts(deliveryId));}
            catch(ArgumentException ex){return Results.BadRequest(new{Message=ex.Message});}
        });
    }
}
