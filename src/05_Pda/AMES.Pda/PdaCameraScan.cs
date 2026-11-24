namespace AMES.Pda;

// Phones without a Honeywell imager scan through the camera; the result joins the same hub as hardware scans.
public static class PdaCameraScan
{
    public static Func<Task<string?>>? Scanner { get; set; }

    public static bool IsAvailable => Scanner is not null;

    public static async Task<bool> ScanAsync()
    {
        if (Scanner is null)
            return false;

        var value = await Scanner();
        if (string.IsNullOrWhiteSpace(value))
            return false;

        // Returning from the camera screen re-activates the app and starts a session check; the hub drops
        // scans until it finishes, so wait for it instead of losing the value.
        var auth = IPlatformApplication.Current?.Services.GetService<Services.AuthState>();
        var deadline = Environment.TickCount64 + 12_000;
        await Task.Delay(300);
        while (auth?.SessionCheckRequired == true && auth.SessionCheckError is null && Environment.TickCount64 < deadline)
            await Task.Delay(100);
        if (auth?.SessionCheckRequired == true)
            return false;

        PdaBarcodeHub.Publish(value);
        return true;
    }
}
