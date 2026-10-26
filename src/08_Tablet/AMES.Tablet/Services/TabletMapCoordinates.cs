namespace AMES.Tablet.Services;

public static class TabletMapCoordinates
{
    public static string Floor(TabletInventoryApi.InventoryRow row)
    {
        var value = row.RackZ?.Trim().ToUpperInvariant().Trim('F');
        return int.TryParse(value, out var floor) && floor > 0 ? floor.ToString() : "-";
    }

    public static string FloorLabel(string floor) => floor == "-" ? "Storage" : $"{floor}F";

    // Match the Web map: MD_Location.Aisle is horizontal, Bay is vertical. Never swap stored axes.
    public static string Column(TabletInventoryApi.InventoryRow row) => Axis(row.RackX);
    public static string Row(TabletInventoryApi.InventoryRow row) => Axis(row.RackY);
    public static bool HasGrid(TabletInventoryApi.InventoryRow row) => Column(row) != "-" && Row(row) != "-";
    public static bool IsFull(decimal qty, decimal? capacity) => capacity is > 0 && qty >= capacity.Value;
    private static string Axis(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

    public static int AxisOrder(string value)
    {
        var text = value.Trim().ToUpperInvariant();
        if (int.TryParse(text, out var number)) return number;
        return text.Length > 1 && text[0] is >= 'A' and <= 'Z' && int.TryParse(text[1..], out var suffix) && suffix > 0
            ? (suffix - 1) * 26 + text[0] - 'A' + 1 : int.MaxValue;
    }
}
