namespace AMES.Pda.Services;

public sealed class PdaUomFormat(WarehouseApi api)
{
    private Dictionary<string, string> _symbols = new(StringComparer.OrdinalIgnoreCase);

    public async Task RefreshAsync()
    {
        var rows = await api.UomsAsync();
        _symbols = rows.Where(x => !string.IsNullOrWhiteSpace(x.Symbol))
            .GroupBy(x => x.UOMCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().Symbol!, StringComparer.OrdinalIgnoreCase);
    }

    public string Symbol(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "EA";
        return _symbols.TryGetValue(code.Trim(), out var symbol) ? symbol : code;
    }
}
