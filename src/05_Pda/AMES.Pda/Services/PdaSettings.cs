using System.Text.Json;

namespace AMES.Pda.Services;

public sealed record PdaSettings(string ApiBaseUrl, string TerminalId)
{
    public static PdaSettings Load()
    {
        using var stream = FileSystem.OpenAppPackageFileAsync("pda-settings.json").GetAwaiter().GetResult();
        var settings = JsonSerializer.Deserialize<ConnectionSettings>(stream,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("PDA settings could not be loaded.");

        if (!Uri.TryCreate(settings.ApiBaseUrl, UriKind.Absolute, out _))
            throw new InvalidOperationException("PDA settings are incomplete.");

#if ANDROID
        var context = Android.App.Application.Context;
        var androidId = Android.Provider.Settings.Secure.GetString(
            context.ContentResolver, Android.Provider.Settings.Secure.AndroidId);
        // A fallback installation ID must not be copied to another PDA by Android backup.
        var identityDirectory = context.NoBackupFilesDir?.AbsolutePath
            ?? throw new InvalidOperationException("Device identity storage is unavailable.");
#else
        string? androidId = null;
        var identityDirectory = FileSystem.AppDataDirectory;
#endif
        return new PdaSettings(settings.ApiBaseUrl, PdaDeviceIdentity.Resolve(androidId, identityDirectory));
    }

    private sealed record ConnectionSettings(string ApiBaseUrl);
}
