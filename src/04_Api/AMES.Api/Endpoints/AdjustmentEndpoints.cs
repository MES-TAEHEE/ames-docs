using AMES.Api.Auth;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;
using System.Data;

namespace AMES.Api.Endpoints;

internal static class AdjustmentEndpoints
{
    internal sealed record Stock(string Barcode, string LotNo, string PartNo, string? PartName, decimal Qty, string? Unit);
    internal sealed record LocationStock(string LocationId, List<Stock> Items);

    internal static void MapAdjustmentLocation(this RouteGroupBuilder group, AmesConnectionFactory factory, bool finishedGoods)
    {
        group.AddEndpointFilter(async (context, next) =>
        {
            var path = context.HttpContext.Request.Path.Value ?? "";
            if (path.Contains("/adjust/", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/inventory/adjust", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/inbound/adjust-qty", StringComparison.OrdinalIgnoreCase))
            {
                var session = context.HttpContext.GetSession();
                if (session is null) return Results.Unauthorized();
                if (!session.IsAdmin) return Results.Problem("Administrator access is required for quantity adjustment.", statusCode: 403);
            }
            return await next(context);
        });

        group.MapGet("/adjust/location", (string barcode) =>
        {
            using var conn = factory.OpenConnection();
            using var location = new SqlCommand("SELECT LocationID FROM dbo.MD_Location WHERE LocationID = @Barcode AND COALESCE(ActiveFlag, 1) = 1", conn);
            location.Parameters.Add("@Barcode", SqlDbType.NVarChar, 80).Value = barcode.Trim();
            if (location.ExecuteScalar() is not string locationId) return Results.Ok((LocationStock?)null);

            using var cmd = new SqlCommand(finishedGoods ? """
                SELECT COALESCE(NULLIF(S.StockNumber,''), L.LotCode) AS Barcode,
                       L.LotCode, S.ItemNo, I.ItemName, S.Qty, I.DefaultUOM
                FROM dbo.FG_Inventory S
                JOIN dbo.tbl_Lot L ON L.LotID = S.LotID
                LEFT JOIN dbo.MD_Item I ON I.ItemNo = S.ItemNo
                WHERE S.Location = @Location AND S.Qty >= 0
                  AND UPPER(ISNULL(S.Status,'')) NOT IN ('SHIPPED','DELIVERED','CLOSED','CANCELED','CANCELLED')
                ORDER BY S.ItemNo, L.LotCode, S.StockID;
                """ : """
                SELECT L.LotCode AS Barcode, L.LotCode, W.ItemNo, I.ItemName, W.OnHandQty, I.DefaultUOM
                FROM dbo.WH_Inventory W
                JOIN dbo.tbl_Lot L ON L.LotID = W.LotID
                LEFT JOIN dbo.MD_Item I ON I.ItemNo = W.ItemNo
                WHERE W.LocationID = @Location AND W.OnHandQty >= 0
                  AND UPPER(ISNULL(W.Status,'')) NOT IN ('CANCELED','CANCELLED','RELEASED','PICKED')
                ORDER BY W.ItemNo, L.LotCode;
                """, conn);
            cmd.Parameters.Add("@Location", SqlDbType.NVarChar, 80).Value = locationId;
            using var reader = cmd.ExecuteReader();
            var items = new List<Stock>();
            while (reader.Read())
                items.Add(new Stock(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader[3] as string, reader.GetDecimal(4), reader[5] as string));
            return Results.Ok(new LocationStock(locationId, items));
        });
    }
}
