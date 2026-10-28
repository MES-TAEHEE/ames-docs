using AMES.Api.Auth;
using AMES.Data.Connection;
using AMES.Data.Repositories;

namespace AMES.Api.Endpoints;

public static class AssyReadyEndpoints
{
    public static void MapAssyReady(this RouteGroupBuilder group,AmesConnectionFactory factory)
    {
        group.MapGet("/putaway/ready-pallets",(HttpContext ctx)=>
            ctx.GetSession() is null ? Results.Unauthorized() : Results.Ok(new AssyReadyRepository(factory).Read()));
    }
}
