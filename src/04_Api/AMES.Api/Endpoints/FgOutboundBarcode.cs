namespace AMES.Api.Endpoints;

internal static class FgOutboundBarcode
{
    public static bool TryNormalize(string? barcode, out string value, out string error)
    {
        value = (barcode ?? "").Replace("\u0002", "").Replace("\u0003", "").Trim();
        error = "";
        if(value.Contains("[)>",StringComparison.Ordinal))
        {
            var lot=AMES.Devices.ImgScanParser.ExtractLotCode(value);
            if(string.IsNullOrWhiteSpace(lot)) {error="The product label does not contain a valid LOT barcode.";return false;}
            value=lot;
        }
        var separator = value.IndexOf(':');
        if (separator >= 0)
        {
            if (!new[] { "FGLOT", "LOT", "STOCK" }.Contains(value[..separator].Trim(), StringComparer.OrdinalIgnoreCase))
            {
                error = "Unsupported barcode type. Scan a finished-goods pallet, box, or LOT barcode.";
                return false;
            }
            value = value[(separator + 1)..].Trim();
        }
        if (value.Length == 0) error = "Scan Barcode first.";
        else if (value.Length > 50) error = "Barcode exceeds 50 characters. Scan the inventory barcode, not the full label text.";
        else if (value.Any(char.IsControl)) error = "Barcode contains invalid control characters. Scan again.";
        return error.Length == 0;
    }

    public static string Failure(string barcode, bool inventoryExists, string? unitType,
        bool productionLotExists, string? productionStatus, bool locationBarcode, bool previouslyOutbound,
        bool missingPart)
    {
        if (inventoryExists && string.IsNullOrWhiteSpace(unitType))
            return $"{barcode}: inventory exists, but its unit type is missing. Correct the inventory record before outbound.";
        if (missingPart)
            return $"{barcode}: positive inventory has no linked part number. Correct the inventory record before outbound.";
        if (previouslyOutbound)
            return $"{barcode}: outbound was already recorded and no available inventory remains. Check transaction history.";
        if (inventoryExists)
            return $"{barcode}: no positive-quantity part inventory is available in this unit. Check its remaining stock and contents.";
        if (locationBarcode)
            return $"{barcode} is a location barcode. Scan a finished-goods pallet, box, or LOT barcode instead.";
        if (productionLotExists)
            return $"{barcode}: production LOT exists (status: {productionStatus ?? "Not set"}), but no warehouse inventory record exists. Check production confirmation and complete FG Put-Away first.";
        return $"{barcode}: no inventory or production LOT is registered for this barcode. Check the barcode and its production/inbound record.";
    }

    public static string? LocationFailure(string lot, string? location, string? masterLocation, string? locationType)
    {
        if (string.IsNullOrWhiteSpace(location))
            return $"{lot}: no storage location is assigned. Complete FG Put-Away before outbound.";
        if (masterLocation is null)
            return $"{lot}: location {location} is not registered in Location Master. Correct its location before outbound.";
        if (!string.Equals(locationType, "FG", StringComparison.OrdinalIgnoreCase))
            return $"{lot}: location {location} is not an FG location (type: {locationType ?? "Not set"}). Finished-goods outbound cannot process this stock.";
        return null;
    }
}
