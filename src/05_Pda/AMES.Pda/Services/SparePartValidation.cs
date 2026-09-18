namespace AMES.Pda.Services;

public static class SparePartValidation
{
    public static string[] MissingFields(params (string Name, string? Value)[] fields) => fields
        .Where(f => string.IsNullOrWhiteSpace(f.Value) || f.Value.Trim() == "-")
        .Select(f => f.Name).ToArray();
}
