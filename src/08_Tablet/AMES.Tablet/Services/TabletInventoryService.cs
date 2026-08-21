using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Tablet.Services;

public sealed class TabletInventoryService(AmesConnectionFactory db)
{
    public sealed record InventoryRow(
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

    public sealed record InventorySnapshot(IReadOnlyList<InventoryRow> Rows, bool IsDemo, string? Message);

    public async Task<InventorySnapshot> LoadAsync()
    {
        try
        {
            await using var conn = db.CreateConnection();
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("""
                SELECT
                    L.LocationID AS LOCATION_NO,
                    L.LocationName AS LOCATION_NAME,
                    COALESCE(NULLIF(L.LocationType, N''), NULLIF(L.ZoneCode, N''), NULLIF(L.PlantCode, N''), N'STORAGE') AS LINE_CODE,
                    L.PlantCode AS WH_CODE,
                    L.ZoneCode AS AREA_CODE,
                    L.LocationType AS ZONE_CODE,
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
                """, conn);

            await using var reader = await cmd.ExecuteReaderAsync();
            var rows = new List<InventoryRow>();
            while (await reader.ReadAsync())
            {
                rows.Add(new InventoryRow(
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

            return rows.Count > 0
                ? new InventorySnapshot(rows, false, null)
                : new InventorySnapshot(DemoRows(), true, "No warehouse locations were returned. Showing demo data.");
        }
        catch (Exception ex)
        {
            return new InventorySnapshot(DemoRows(), true, $"Database unavailable. Showing demo data. {ex.Message}");
        }
    }

    private static string? Text(SqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : Convert.ToString(reader.GetValue(ordinal))?.Trim();
    }

    private static List<InventoryRow> DemoRows()
    {
        var rows = new List<InventoryRow>();
        foreach (var line in new[] { "D", "E", "F" })
        {
            for (var bay = 1; bay <= 18; bay++)
            {
                rows.Add(new InventoryRow(
                    $"{line}0-{bay:00}-A1", $"{line} Line Rack {bay:00}", $"{line} LINE",
                    "B", line, "STORAGE", line, bay.ToString("00"), "A1",
                    null, null, null, 0, "EA"));
            }
        }

        AddStock(rows, "D0-04-A1", "LOT-2608-001", "96230-PI000", "FEEDER CABLE", 48, "EA");
        AddStock(rows, "E0-07-A1", "LOT-2607-014", "81710-PI000NNB", "TRIM ASSY-TAIL GATE, LWR", 75, "EA");
        AddStock(rows, "E0-07-A1", "LOT-2608-002", "81710-PI000NNB", "TRIM ASSY-TAIL GATE, LWR", 25, "EA");
        AddStock(rows, "F0-13-A1", "LOT-2606-118", "82301-PI000NNB", "PNL ASSY-FR DR TRIM COMPL, LH", 90, "EA");
        AddStock(rows, "F0-16-A1", "LOT-2608-021", "82710-DW000WK", "CAP-SIDE MT'G", 32, "EA");
        return rows;
    }

    private static void AddStock(List<InventoryRow> rows, string location, string lot, string part, string name, decimal qty, string unit)
    {
        var rack = rows.First(x => x.LocationNo == location);
        rows.Add(rack with { LotNo = lot, PartNo = part, PartName = name, Qty = qty, Unit = unit });
    }
}
