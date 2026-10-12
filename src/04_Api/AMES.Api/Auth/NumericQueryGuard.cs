namespace AMES.Api.Auth;

public static class NumericQueryGuard
{
    private static readonly Dictionary<string, (int Min, int Max)> Limits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["topN"] = (1, 1000), ["daysBack"] = (0, 3650), ["daysAhead"] = (0, 365),
        ["monthsBack"] = (0, 120), ["monthsAhead"] = (0, 120), ["hoursBack"] = (0, 720),
        ["days"] = (1, 3650), ["year"] = (2000, 2100), ["quarter"] = (1, 4)
    };

    public static string? InvalidParameter(IQueryCollection query)
    {
        foreach (var (name, values) in query)
            if (Limits.TryGetValue(name, out var range)
                && (values.Count != 1 || !int.TryParse(values[0], out var value)
                    || value < range.Min || value > range.Max))
                return name;
        return null;
    }
}
