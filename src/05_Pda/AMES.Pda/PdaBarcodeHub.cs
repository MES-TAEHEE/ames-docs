namespace AMES.Pda;

public static class PdaBarcodeHub
{
    public static event Action<string>? Scanned;
    private static string _lastValue = "";
    private static DateTime _lastAt = DateTime.MinValue;

    public static void Publish(string? barcode)
    {
        var value = Normalize(barcode);
        if (string.IsNullOrWhiteSpace(value))
            return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var now = DateTime.UtcNow;
            if (string.Equals(value, _lastValue, StringComparison.Ordinal) && now - _lastAt < TimeSpan.FromMilliseconds(900))
                return;

            _lastValue = value;
            _lastAt = now;
            Scanned?.Invoke(value);
        });
    }

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? ""
            : value.Replace("\u0002", "").Replace("\u0003", "").Replace("\r", "").Replace("\n", "").Trim();
}
