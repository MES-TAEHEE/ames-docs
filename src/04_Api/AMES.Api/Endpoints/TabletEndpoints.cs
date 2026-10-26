using AMES.Api.Auth;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;
using AMES.Contracts.Dto;
using AMES.Data.Repositories;

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

        group.MapGet("/location-map", (HttpContext context, ILoggerFactory logs) =>
        {
            if (context.GetSession() is null) return Results.Unauthorized();
            try
            {
                var master = new MasterDataRepository(factory);
                var warehouse = new WarehouseRepository(factory);
                var locations = master.ListLocations().Where(x => x.ActiveFlag && x.LocationLevel == MasterDataRepository.LocationLevelDetail
                    && x.AreaCode != "SPARE_PARTS_AREA")
                    .Select(x => new TabletMapLocationDto(x.LocationID, x.LocationName, x.WhCode, x.AreaCode, x.Aisle, x.Bay, x.Slot, x.Capacity)).ToList();
                var areas = warehouse.ListWarehouseAreas().Where(x => x.AreaCode != "SPARE_PARTS_AREA" && !string.IsNullOrWhiteSpace(x.WhCode))
                    .Select(x => new TabletMapAreaDto(x.WhCode!, x.WhName ?? x.WhCode!, x.AreaCode, x.AreaName ?? x.AreaCode, x.StorageMode)).ToList();
                foreach (var wh in warehouse.ListWarehouses())
                    if (!areas.Any(x => x.WarehouseCode == wh.WhCode))
                        areas.Add(new(wh.WhCode, wh.WhName ?? wh.WhCode, "", "", "GRID"));
                foreach (var location in locations)
                    if (!areas.Any(x => x.WarehouseCode == location.WarehouseCode && x.AreaCode == location.AreaCode))
                        areas.Add(new(location.WarehouseCode, location.WarehouseCode, location.AreaCode, location.AreaCode, "GRID"));
                var ids = locations.Select(x => x.LocationNo).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var inventory = warehouse.ListUnifiedInventory(false, includeFinishedGoods: true, includeDefects: true)
                    .Where(x => ids.Contains(x.LocationNo))
                    .Select(x => new TabletMapStockDto(x.LocationNo, x.LotNo, x.PartNo, x.PartName, x.Qty, x.Unit, x.DefectQty)).ToList();
                return Results.Ok(new TabletLocationMapDto(areas, locations, inventory));
            }
            catch (SqlException ex)
            {
                logs.CreateLogger("TabletLocationMap").LogError(ex, "Tablet location map query failed");
                return Results.Json(new { message = "Location map is unavailable. Please retry." }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).WithName("GetTabletLocationMap").WithSummary("Read warehouse, area, grid/open storage and inventory for the tablet");

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
                    W.LotNo AS LOT_NO,
                    W.PartNo AS PART_NO,
                    COALESCE(NULLIF(W.PartName,N''),I.ItemName) AS PART_NAME,
                    COALESCE(W.Qty, 0) AS QTY,
                    COALESCE(NULLIF(I.DefaultUOM, N''), N'EA') AS UNIT
                FROM dbo.MD_Location L
                LEFT JOIN dbo.WH_Inventory W ON W.LocationNo=L.LocationID AND W.Qty>0
                LEFT JOIN dbo.MD_Item I ON I.ItemNo=W.PartNo
                WHERE COALESCE(L.ActiveFlag, 1) = 1
                ORDER BY LINE_CODE,
                         TRY_CONVERT(int, L.Bay), L.Bay,
                         TRY_CONVERT(int, L.Aisle), L.Aisle,
                         TRY_CONVERT(int, L.Slot), L.Slot,
                         L.LocationID,
                         W.LotNo;
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
