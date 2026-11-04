namespace AMES.Pda.Services;

public static class PdaScannerIntegration
{
    // Other manufacturers use the shared keyboard-wedge input, not Honeywell broadcasts.
    public static bool UsesHoneywellIntents(string? manufacturer, string? brand)
        => (manufacturer?.Contains("Honeywell", StringComparison.OrdinalIgnoreCase) ?? false)
            || (brand?.Contains("Honeywell", StringComparison.OrdinalIgnoreCase) ?? false);
}
