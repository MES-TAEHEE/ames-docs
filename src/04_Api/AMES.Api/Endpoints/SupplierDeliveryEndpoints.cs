using AMES.Api.Auth;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Data.Services;

namespace AMES.Api.Endpoints;

public static class SupplierDeliveryEndpoints
{
    public static void MapSupplierDeliveries(this RouteGroupBuilder group, AmesConnectionFactory factory)
    {
        group.MapGet("/inbound/deliveries", (HttpContext context, DateTime? from, DateTime? to, string? search, string? vendor) =>
        {
            if (context.GetSession() is null) return Results.Unauthorized();
            from ??= DbClock.Today.AddDays(-6);
            to ??= DbClock.Today;
            if (from > to || to.Value.Date == DateTime.MaxValue.Date || (to.Value - from.Value).TotalDays > 366 || search?.Length > 120 || vendor?.Length > 50)
                return Results.BadRequest(new { Message = "Select a valid delivery date range (up to 366 days) and search text." });
            try
            {
                return Results.Ok(new ScmRepository(factory).ListSupplierDeliveryProgress(from, to, search, vendor, shippedOnly: true));
            }
            catch (Exception ex)
            {
                context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("SupplierDeliveries")
                    .LogError(ex, "Supplier inbound schedule query failed");
                return Results.Problem("Delivery schedule service is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });
    }
}
