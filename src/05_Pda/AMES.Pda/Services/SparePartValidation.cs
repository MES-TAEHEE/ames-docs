namespace AMES.Pda.Services;

public static class SparePartValidation
{
    public static bool FallsBelowSafetyStock(decimal currentQty, decimal releaseQty, int? safetyStock)
        => safetyStock is > 0 && currentQty - releaseQty < safetyStock.Value;

    public static string[] MissingFields(params (string Name, string? Value)[] fields) => fields
        .Where(f => string.IsNullOrWhiteSpace(f.Value) || f.Value.Trim() == "-")
        .Select(f => f.Name).ToArray();
}
