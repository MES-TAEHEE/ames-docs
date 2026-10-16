using System.Text.Json;

namespace AMES.Pda.Services;

public sealed record PdaSettings(string ApiBaseUrl, string TerminalId, string LineId, string ShiftCode)
{
    public static PdaSettings Load()
    {
        using var stream = FileSystem.OpenAppPackageFileAsync("pda-settings.json").GetAwaiter().GetResult();
        var settings = JsonSerializer.Deserialize<PdaSettings>(stream,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("PDA settings could not be loaded.");

        if (!Uri.TryCreate(settings.ApiBaseUrl, UriKind.Absolute, out _)
            || string.IsNullOrWhiteSpace(settings.TerminalId)
            || string.IsNullOrWhiteSpace(settings.LineId)
            || string.IsNullOrWhiteSpace(settings.ShiftCode))
            throw new InvalidOperationException("PDA settings are incomplete.");

        return settings;
    }
}
