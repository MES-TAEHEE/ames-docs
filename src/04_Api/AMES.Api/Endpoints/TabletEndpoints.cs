using AMES.Api.Auth;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Api.Endpoints;

public static class TabletEndpoints
{
    public sealed record TabletInventoryRow(
        string LocationNo,
        string? LocationName,
        string LineCode,
        string? WarehouseCode,
        string? AreaCode,
        string? ZoneCode,
        string? RackX,
        string? RackY,
        string? RackZ,
        string? LotNo,
        string? PartNo,
        string? PartName,
        decimal Qty,
        string Unit);

    public static void MapTablet(this WebApplication app, AmesConnectionFactory factory)
    {
        var group = app.MapGroup("/api/tablet").WithTags("Tablet");

        group.MapGet("/inventory", async (HttpContext context) =>
        {
            if (context.GetSession() is null)
                return Results.Unauthorized();

            await using var connection = factory.CreateConnection();
            await connection.OpenAsync();
            await using var command = new SqlCommand("""
                SELECT
                    L.LocationID AS LOCATION_NO,
                    L.LocationName AS LOCATION_NAME,
                    COALESCE(NULLIF(L.ZoneCode, N''), NULLIF(L.AreaCode, N''), N'STORAGE') AS LINE_CODE,
                    L.WhCode AS WH_CODE,
                    L.AreaCode AS AREA_CODE,
                    L.ZoneCode AS ZONE_CODE,
                    L.Aisle AS RACK_X,
                    L.Bay AS RACK_Y,
                    L.Slot AS RACK_Z,
                    CASE WHEN W.InventoryID IS NULL THEN NULL ELSE COALESCE(NULLIF(LOT.LotCode, N''), CONCAT(N'LOT-', W.LotID)) END AS LOT_NO,
                    W.ItemNo AS PART_NO,
                    I.ItemName AS PART_NAME,
                    COALESCE(W.OnHandQty, 0) AS QTY,
                    COALESCE(NULLIF(I.DefaultUOM, N''), N'EA') AS UNIT
                FROM dbo.MD_Location L
                LEFT JOIN dbo.WH_Inventory W
                       ON W.LocationID = L.LocationID
                      AND COALESCE(W.OnHandQty, 0) > 0
                      AND UPPER(COALESCE(W.Status, N'RECEIVED')) <> N'CANCELED'
                LEFT JOIN dbo.tbl_Lot LOT ON LOT.LotID = W.LotID
                LEFT JOIN dbo.MD_Item I ON I.ItemNo = W.ItemNo
                WHERE COALESCE(L.ActiveFlag, 1) = 1
                ORDER BY LINE_CODE,
                         TRY_CONVERT(int, L.Bay), L.Bay,
                         TRY_CONVERT(int, L.Aisle), L.Aisle,
                         TRY_CONVERT(int, L.Slot), L.Slot,
                         L.LocationID,
                         LOT.LotCode;
                """, connection);

            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<TabletInventoryRow>();
            while (await reader.ReadAsync())
            {
                rows.Add(new TabletInventoryRow(
                    Text(reader, "LOCATION_NO") ?? "-",
                    Text(reader, "LOCATION_NAME"),
                    Text(reader, "LINE_CODE") ?? "STORAGE",
                    Text(reader, "WH_CODE"),
                    Text(reader, "AREA_CODE"),
                    Text(reader, "ZONE_CODE"),
                    Text(reader, "RACK_X"),
                    Text(reader, "RACK_Y"),
                    Text(reader, "RACK_Z"),
                    Text(reader, "LOT_NO"),
                    Text(reader, "PART_NO"),
                    Text(reader, "PART_NAME"),
                    reader.GetDecimal(reader.GetOrdinal("QTY")),
                    Text(reader, "UNIT") ?? "EA"));
            }

            return Results.Ok(rows);
        })
        .WithName("GetTabletInventory")
        .WithSummary("태블릿 위치 맵용 재고 조회")
        .WithDescription("활성 로케이션과 현재 재고를 조회하여 태블릿 위치 맵에 표시합니다.")
        .Produces<List<TabletInventoryRow>>()
        .Produces(StatusCodes.Status401Unauthorized);
    }

    private static string? Text(SqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : Convert.ToString(reader.GetValue(ordinal))?.Trim();
    }
}
