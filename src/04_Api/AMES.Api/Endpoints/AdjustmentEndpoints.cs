using AMES.Api.Auth;
using AMES.Data.Connection;
using AMES.Data.Repositories;
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

        if (!finishedGoods)
            group.MapGet("/adjust/lines", () =>
            {
                using var conn = factory.OpenConnection();
                using var cmd = new SqlCommand(WarehouseRepository.ActiveLineLocationsSql + """

                    AND EXISTS (SELECT 1 FROM dbo.WH_Inventory W
                                WHERE W.LocationNo COLLATE DATABASE_DEFAULT=MD_Line.LotPrefix COLLATE DATABASE_DEFAULT)
                    ORDER BY LotPrefix;
                    """, conn);
                using var reader = cmd.ExecuteReader();
                var lines = new List<string>();
                while (reader.Read()) lines.Add(reader.GetString(0));
                return Results.Ok(lines);
            }).WithSummary("List registered MD_Line.LotPrefix locations with inventory for quantity adjustment");

        group.MapGet("/adjust/location", (string barcode) =>
        {
            using var conn = factory.OpenConnection();
            using var location = new SqlCommand("SELECT LocationID FROM dbo.MD_Location WHERE LocationID = @Barcode AND COALESCE(ActiveFlag, 1) = 1", conn);
            location.Parameters.Add("@Barcode", SqlDbType.NVarChar, 80).Value = barcode.Trim();
            var locationId = (!finishedGoods ? WarehouseRepository.ResolveLineLocation(conn, barcode) : null)
                ?? location.ExecuteScalar() as string;
            if (locationId is null) return Results.Ok((LocationStock?)null);

            using var cmd = new SqlCommand(finishedGoods ? """
                SELECT W.LotNo AS Barcode,
                       W.LotNo, W.PartNo, COALESCE(NULLIF(W.PartName,''), I.ItemName),
                       W.Qty, COALESCE(NULLIF(I.DefaultUOM,''), 'EA')
                FROM dbo.WH_Inventory W
                LEFT JOIN dbo.MD_Item I ON I.ItemNo = W.PartNo
                LEFT JOIN dbo.MD_Location L ON L.LocationID = W.LocationNo
                WHERE W.LocationNo = @Location AND W.Qty >= 0
                  AND (UPPER(COALESCE(L.AreaCode,'')) = 'FG_AREA' OR UPPER(W.LocationNo) LIKE 'FG%')
                ORDER BY W.PartNo, W.LotNo;
                """ : """
                SELECT W.LotNo AS Barcode,
                       W.LotNo, W.PartNo, COALESCE(NULLIF(W.PartName,''), I.ItemName),
                       W.Qty, COALESCE(NULLIF(I.DefaultUOM,''), 'EA')
                FROM dbo.WH_Inventory W
                LEFT JOIN dbo.MD_Item I ON I.ItemNo COLLATE DATABASE_DEFAULT = W.PartNo COLLATE DATABASE_DEFAULT
                LEFT JOIN dbo.MD_Location L ON L.LocationID COLLATE DATABASE_DEFAULT = W.LocationNo COLLATE DATABASE_DEFAULT
                WHERE W.LocationNo = @Location AND W.Qty >= 0
                  AND UPPER(COALESCE(L.AreaCode,'')) <> 'FG_AREA'
                  AND NULLIF(W.PartNo,'') IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM dbo.WH_Inventory C WHERE C.ParentLotNo=W.LotNo)
                ORDER BY W.PartNo, W.LotNo;
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
