using AMES.Api.Auth;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;

namespace AMES.Api.Endpoints;

public static class PalletReturnEndpoints
{
    public static void MapPalletReturns(this RouteGroupBuilder group, AmesConnectionFactory factory)
    {
        group.MapGet("/pallet-return/scan", (HttpContext ctx, string barcode) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();
            return Run(ctx, () => new FinishedGoodsRepository(factory).ScanEmptyPalletReturn(barcode));
        });
        group.MapPost("/pallet-return", (HttpContext ctx, PalletReturnRequest body) =>
        {
            if (ctx.GetSession() is not { } session) return Results.Unauthorized();
            return Run(ctx, () => new FinishedGoodsRepository(factory).ReceiveEmptyPalletReturn(body, session.OperatorId));
        });
    }

    private static IResult Run(HttpContext ctx, Func<PalletReturnDto> action)
    {
        try { return Results.Ok(action()); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
            || ex is SqlException sql && sql.Number is >= 52000 and <= 52021)
        { return Results.BadRequest(new { Message = ex.Message }); }
        catch (Exception ex)
        {
            ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("PalletReturn")
                .LogError(ex, "Empty pallet return failed");
            return Results.Json(new { Message = "Pallet return service is unavailable. Check the API connection and retry." }, statusCode: 503);
        }
    }
}
