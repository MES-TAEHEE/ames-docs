using System.Net.Http.Json;

namespace AMES.Tablet.Services;

public sealed class TabletInventoryApi(HttpClient http)
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
            var rows = await http.GetFromJsonAsync<List<InventoryRow>>("/api/tablet/inventory") ?? [];

            return rows.Count > 0
                ? new InventorySnapshot(rows, false, null)
                : new InventorySnapshot(DemoRows(), true, "No warehouse locations were returned. Showing demo data.");
        }
        catch (Exception ex)
        {
            return new InventorySnapshot(DemoRows(), true, $"Inventory API unavailable. Showing demo data. {ex.Message}");
        }
    }

    private static List<InventoryRow> DemoRows()
    {
        var rows = new List<InventoryRow>();
        foreach (var floor in Enumerable.Range(1, 4))
        {
            foreach (var row in "ABCDEFGHIJKLM")
            {
                for (var column = 1; column <= 20; column++)
                {
                    rows.Add(new InventoryRow(
                        $"{floor}F-{row}-{column:00}", $"{floor}F Material Row {row} Location {column:00}", $"{floor}F MATERIAL",
                        $"{floor}F", "MATERIAL", "RACK", row.ToString(), column.ToString("00"), "01",
                        null, null, null, 0, "EA"));
                }
            }
        }

        AddStock(rows, "1F-A-02", "LOT-2608-101", "96230-PI000", "FEEDER CABLE", 30, "EA");
        AddStock(rows, "1F-D-05", "LOT-2608-102", "86631-PI000", "REAR BUMPER BEAM", 24, "EA");
        AddStock(rows, "1F-H-12", "LOT-2608-103", "85740-PI000", "LUGGAGE SIDE TRIM", 36, "EA");
        AddStock(rows, "1F-L-18", "LOT-2608-104", "85710-PI000", "TAIL GATE UPPER TRIM", 18, "EA");
        AddStock(rows, "2F-A-03", "LOT-2608-202", "85820-PI000", "QUARTER INNER TRIM", 28, "EA");
        AddStock(rows, "2F-C-06", "LOT-2608-201", "81710-PI000NNB", "TRIM ASSY-TAIL GATE, LWR", 42, "EA");
        AddStock(rows, "2F-F-10", "LOT-2608-203", "85830-PI000", "BACK PANEL TRIM", 55, "EA");
        AddStock(rows, "2F-I-14", "LOT-2608-204", "85750-PI000", "LUGGAGE FLOOR TRIM", 20, "EA");
        AddStock(rows, "2F-M-19", "LOT-2608-205", "85760-PI000", "PACKAGE TRAY TRIM", 16, "EA");
        AddStock(rows, "3F-B-04", "LOT-2608-302", "82302-PI000NNB", "FRONT DOOR TRIM, RH", 52, "EA");
        AddStock(rows, "3F-G-11", "LOT-2608-301", "82301-PI000NNB", "PNL ASSY-FR DR TRIM COMPL, LH", 64, "EA");
        AddStock(rows, "3F-J-15", "LOT-2608-303", "83301-PI000", "REAR DOOR TRIM, LH", 44, "EA");
        AddStock(rows, "3F-M-20", "LOT-2608-304", "83302-PI000", "REAR DOOR TRIM, RH", 40, "EA");
        AddStock(rows, "4F-A-02", "LOT-2608-022", "85770-PI000", "CARGO SCREEN COVER", 22, "EA");
        AddStock(rows, "4F-B-04", "LOT-2608-001", "96230-PI000", "FEEDER CABLE", 48, "EA");
        AddStock(rows, "4F-D-07", "LOT-2607-014", "81710-PI000NNB", "TRIM ASSY-TAIL GATE, LWR", 75, "EA");
        AddStock(rows, "4F-D-07", "LOT-2608-002", "81710-PI000NNB", "TRIM ASSY-TAIL GATE, LWR", 25, "EA");
        AddStock(rows, "4F-F-10", "LOT-2608-023", "85890-PI000", "TRUNK SIDE FINISHER", 26, "EA");
        AddStock(rows, "4F-H-13", "LOT-2606-118", "82301-PI000NNB", "PNL ASSY-FR DR TRIM COMPL, LH", 90, "EA");
        AddStock(rows, "4F-L-16", "LOT-2608-021", "82710-DW000WK", "CAP-SIDE MT'G", 32, "EA");
        AddStock(rows, "4F-M-20", "LOT-2608-024", "85780-PI000", "LUGGAGE BOARD ASSY", 14, "EA");
        return rows;
    }

    private static void AddStock(List<InventoryRow> rows, string location, string lot, string part, string name, decimal qty, string unit)
    {
        var rack = rows.First(x => x.LocationNo == location);
        rows.Add(rack with { LotNo = lot, PartNo = part, PartName = name, Qty = qty, Unit = unit });
    }
}
