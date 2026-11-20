using System.Text.Json;
using AMES.Api.Auth;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Data.SqlClient;

namespace AMES.Api.Endpoints;

public static class FgDefectEndpoints
{
    public static void MapFgDefects(this RouteGroupBuilder group, AmesConnectionFactory factory,
        ILogger logger)
    {
        var repo = new FinishedGoodsRepository(factory);

        IResult Run(HttpContext ctx, Func<IResult> action)
        {
            if (ctx.GetSession() is not { } session) return Results.Unauthorized();
            try
            {
                if (!repo.CanRegisterPdaDefect(session.EmployeeNo))
                    return Results.Json(new { message = "You do not have the Inventory Defect permission." }, statusCode: 403);
                return action();
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { message = ex.Message }); }
            catch (SqlException ex)
            {
                logger.LogError(ex, "FG defect query failed");
                return Results.Json(new { message = "Defect registration service is unavailable. Retry after checking the connection." }, statusCode: 503);
            }
        }

        group.MapGet("/defects/access", (HttpContext ctx) => Run(ctx, () =>
        {
            // Check photo storage before the operator spends time capturing evidence.
            repo.PdaDefectPhoto(0);
            return Results.Ok(new { CanRegister = true });
        }));
        group.MapGet("/defects/scan", (HttpContext ctx, string barcode) => Run(ctx, () =>
        {
            if (!FgOutboundBarcode.TryNormalize(barcode, out var lot, out var error))
                return Results.BadRequest(new { message = error });
            return Results.Ok(repo.ScanPdaDefect(lot));
        }));
        group.MapGet("/defects/{id:int}/photo", (HttpContext ctx, int id) => Run(ctx, () =>
        {
            var photo = repo.PdaDefectPhoto(id);
            if (photo is null) return Results.NotFound();
            ctx.Response.Headers.CacheControl = "no-store";
            ctx.Response.Headers.XContentTypeOptions = "nosniff";
            return Results.File(photo.Value.Bytes, photo.Value.ContentType);
        }));
        group.MapPost("/defects", async (HttpContext ctx) =>
        {
            if (ctx.GetSession() is not { } session) return Results.Unauthorized();
            try
            {
                if (!repo.CanRegisterPdaDefect(session.EmployeeNo))
                    return Results.Json(new { message = "You do not have the Inventory Defect permission." }, statusCode: 403);
                const long maxBody = 5 * 1024 * 1024;
                if (ctx.Request.ContentLength > maxBody) return Results.StatusCode(413);
                var limit = ctx.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = maxBody;
                var body = await ctx.Request.ReadFromJsonAsync<PdaFgDefectRequest>(ctx.RequestAborted)
                    ?? throw new ArgumentException("Scan the LOT and enter its defect information.");
                if (body.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(body.LotNo) || body.LotNo.Length > 40)
                    throw new ArgumentException("Scan a finished-goods LOT again.");
                FinishedGoodsRepository.ValidateDefectReason(body.Reason);
                FgDefectPhotos.Validate(body.Photo, body.PhotoType);
                return Results.Ok(repo.RegisterPdaDefect(body, session.EmployeeNo, session.SessionId));
            }
            catch (UnauthorizedAccessException ex) { return Results.Json(new { message = ex.Message }, statusCode: 403); }
            catch (ArgumentException ex) { return Results.BadRequest(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { message = ex.Message }); }
            catch (JsonException) { return Results.BadRequest(new { message = "Invalid defect registration. Scan the LOT and select the photo again." }); }
            catch (BadHttpRequestException ex) { return Results.StatusCode(ex.StatusCode); }
            catch (Exception ex)
            {
                logger.LogError(ex, "FG defect registration failed for {Employee}", session.EmployeeNo);
                return Results.Json(new { message = "Defect registration could not be confirmed. Scan the LOT to check its status before retrying." }, statusCode: 503);
            }
        });
    }
}

internal static class FgDefectPhotos
{
    public const int MaxBytes = 3 * 1024 * 1024;
    internal static string Validate(byte[]? photo, string? type)
    {
        if (photo is null || photo.Length is < 20 or > MaxBytes)
            throw new ArgumentException("Add a JPEG or PNG defect photo up to 3 MB.");
        if (type == "image/jpeg" && photo[0] == 0xff && photo[1] == 0xd8 && photo[2] == 0xff
            && photo[^2] == 0xff && photo[^1] == 0xd9) return "jpg";
        if (type == "image/png" && photo.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "png";
        throw new ArgumentException("Select a JPEG or PNG image. This file is not a supported photo.");
    }
}
