namespace AMES.Web.Components.Shared;

public static class LocationMapDemoData
{
    private sealed record StockSeed(string LocationNo, string LotNo, string PartNo, string PartName, decimal Qty);

    private static readonly StockSeed[] StockSeeds =
    [
        new("1F-A-02", "LOT-2608-101", "96230-PI000", "FEEDER CABLE", 30),
        new("1F-D-05", "LOT-2608-102", "86631-PI000", "REAR BUMPER BEAM", 24),
        new("1F-H-12", "LOT-2608-103", "85740-PI000", "LUGGAGE SIDE TRIM", 36),
        new("1F-L-18", "LOT-2608-104", "85710-PI000", "TAIL GATE UPPER TRIM", 18),
        new("2F-A-03", "LOT-2608-202", "85820-PI000", "QUARTER INNER TRIM", 28),
        new("2F-C-06", "LOT-2608-201", "81710-PI000NNB", "TRIM ASSY-TAIL GATE, LWR", 42),
        new("2F-F-10", "LOT-2608-203", "85830-PI000", "BACK PANEL TRIM", 55),
        new("2F-I-14", "LOT-2608-204", "85750-PI000", "LUGGAGE FLOOR TRIM", 20),
        new("2F-M-19", "LOT-2608-205", "85760-PI000", "PACKAGE TRAY TRIM", 16),
        new("3F-B-04", "LOT-2608-302", "82302-PI000NNB", "FRONT DOOR TRIM, RH", 52),
        new("3F-G-11", "LOT-2608-301", "82301-PI000NNB", "PNL ASSY-FR DR TRIM COMPL, LH", 64),
        new("3F-J-15", "LOT-2608-303", "83301-PI000", "REAR DOOR TRIM, LH", 44),
        new("3F-M-20", "LOT-2608-304", "83302-PI000", "REAR DOOR TRIM, RH", 40),
        new("4F-A-02", "LOT-2608-022", "85770-PI000", "CARGO SCREEN COVER", 22),
        new("4F-B-04", "LOT-2608-001", "96230-PI000", "FEEDER CABLE", 48),
        new("4F-D-07", "LOT-2607-014", "81710-PI000NNB", "TRIM ASSY-TAIL GATE, LWR", 75),
        new("4F-D-07", "LOT-2608-002", "81710-PI000NNB", "TRIM ASSY-TAIL GATE, LWR", 25),
        new("4F-F-10", "LOT-2608-023", "85890-PI000", "TRUNK SIDE FINISHER", 26),
        new("4F-H-13", "LOT-2606-118", "82301-PI000NNB", "PNL ASSY-FR DR TRIM COMPL, LH", 90),
        new("4F-L-16", "LOT-2608-021", "82710-DW000WK", "CAP-SIDE MT'G", 32),
        new("4F-M-20", "LOT-2608-024", "85780-PI000", "LUGGAGE BOARD ASSY", 14),
    ];

    public static (List<LocationMapCell> Locations, List<LocationMapStock> Inventory) Create(string areaCode, string areaName, string locationPrefix = "")
    {
        var quantities = StockSeeds
            .GroupBy(x => x.LocationNo, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Sum(row => row.Qty), StringComparer.OrdinalIgnoreCase);
        var locations = new List<LocationMapCell>(4 * 13 * 20);

        foreach (var floor in Enumerable.Range(1, 4))
        {
            foreach (var row in "ABCDEFGHIJKLM")
            {
                for (var column = 1; column <= 20; column++)
                {
                    var sourceLocationNo = $"{floor}F-{row}-{column:00}";
                    var locationNo = $"{locationPrefix}{sourceLocationNo}";
                    var qty = quantities.GetValueOrDefault(sourceLocationNo);
                    locations.Add(new LocationMapCell(
                        locationNo,
                        $"{floor}F {areaName} Row {row} Location {column:00}",
                        "EOS",
                        "EOS",
                        areaCode,
                        areaName,
                        "RACK",
                        "Rack",
                        column.ToString("00"),
                        row.ToString(),
                        $"{floor}F",
                        qty,
                        qty > 0 ? "STOCKED" : "EMPTY"));
                }
            }
        }

        var inventory = StockSeeds
            .Select(x => new LocationMapStock(
                $"{locationPrefix}{x.LocationNo}", x.PartNo, x.PartName, x.LotNo, x.Qty, "EA",
                "EOS", "EOS", areaCode, areaName))
            .ToList();
        return (locations, inventory);
    }
}
