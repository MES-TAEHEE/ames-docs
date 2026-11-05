using AMES.Api.Auth;
using AMES.Contracts.Dto;
using AMES.Contracts.Exceptions;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Data.Services;
using Microsoft.Data.SqlClient;

namespace AMES.Api.Endpoints;

public static class PdaPickingRequestEndpoints
{
    public static void MapPickingRequests(this RouteGroupBuilder group, AmesConnectionFactory factory)
    {
        var repository=new WarehouseRepository(factory);
        group.MapGet("/picking-requests/options", (HttpContext context) =>
        {
            if(context.GetSession() is not {} session)return Results.Unauthorized();
            if(!repository.HasPickingRequestAccess(session.EmployeeNo,'R'))return Results.StatusCode(403);
            return Results.Ok(new PickingRequestOptionsDto(
                repository.ListPickingRequestDestinations(lineLocationsOnly:true).Select(Option).ToList(),
                repository.ListPickingRequestParts().Select(Option).ToList(),DbClock.ReadNow(factory).Date,
                repository.HasPickingRequestAccess(session.EmployeeNo,'E')));
        });
        group.MapGet("/picking-requests/existing", (HttpContext context,string destination,string partNo) =>
        {
            if(context.GetSession() is not {} session)return Results.Unauthorized();
            if(!repository.HasPickingRequestAccess(session.EmployeeNo,'R'))return Results.StatusCode(403);
            try{return Results.Ok(repository.ListExistingPickingRequests(destination,partNo));}
            catch(ArgumentException ex){return Results.BadRequest(new{message=ex.Message});}
        });
        group.MapPost("/picking-requests", (HttpContext context, CreatePickingRequestDto body) =>
        {
            if(context.GetSession() is not {} session)return Results.Unauthorized();
            if(!repository.HasPickingRequestAccess(session.EmployeeNo,'R') ||
                !repository.HasPickingRequestAccess(session.EmployeeNo,'E'))return Results.StatusCode(403);
            try
            {
                var number=repository.CreatePickingRequest(new(body.Destination,body.PartNo,body.Qty,
                    DbClock.ReadNow(factory).Date,LineLocationOnly:true,ConfirmAdditional:body.ConfirmAdditional),session.EmployeeNo);
                return Results.Ok(new PickingRequestCreatedDto(number));
            }
            catch(PickingRequestExistsException ex){return Results.Conflict(new PickingRequestConflictDto(ex.Message,ex.ExistingOrders.ToList()));}
            catch(ArgumentException ex){return Results.BadRequest(new {message=ex.Message});}
            catch(InvalidOperationException ex){return Results.Conflict(new {message=ex.Message});}
            catch(SqlException ex) when(ex.Number is 51831 or 51820)
            {return Results.BadRequest(new {message=ex.Message});}
        });
    }

    private static PickingRequestOptionDto Option(WarehouseRepository.PickingRequestOption option)
        =>new(option.Code,option.Display,option.Unit);
}
