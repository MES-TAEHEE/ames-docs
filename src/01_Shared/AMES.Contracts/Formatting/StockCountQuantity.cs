using System.Globalization;

namespace AMES.Contracts.Formatting;

public static class StockCountQuantity
{
    public const decimal Maximum = 999999999m;

    public static bool AllowsFraction(string? unit) => !string.IsNullOrWhiteSpace(unit)
        && !string.Equals(unit.Trim(), "EA", StringComparison.OrdinalIgnoreCase);

    public static bool HasStoragePrecision(decimal quantity) => quantity == decimal.Round(quantity, 3);

    public static bool IsValid(decimal quantity, string? unit) => quantity >= 0 && quantity <= Maximum
        && HasStoragePrecision(quantity) && (AllowsFraction(unit) || quantity == decimal.Truncate(quantity));

    public static bool TryParse(string? text, string? unit, out decimal quantity) => decimal.TryParse(text,
        NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out quantity) && IsValid(quantity, unit);
}
