namespace AMES.Pda.Services;

public static class FgTransactionDisplay
{
    public static string Direction(string? value)
    {
        var direction = value?.Trim().ToUpperInvariant() ?? "";
        // Older history can still contain LOAD; actual outbound transactions use OUT.
        return direction == "LOAD" ? "OUT" : direction;
    }

    public static string Label(string? direction) => Direction(direction) switch
    {
        "" => "ALL",
        "OUT" => "OUTBOUND",
        var value => value
    };

    public static string Status(string? value)
        => value?.Trim().ToUpperInvariant() is "LOAD" or "LOADING" ? "Outbound" : value ?? "-";
}
