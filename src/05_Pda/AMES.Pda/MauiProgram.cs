using AMES.Data.Connection;
using AMES.Pda.Services;
using Microsoft.Extensions.Logging;

namespace AMES.Pda;

public static class MauiProgram
{
    /// <summary>
    /// API base URL used by the PDA client.
    /// Windows / macCatalyst → localhost. Override at runtime later via
    /// a settings screen.
    /// </summary>
    public static string ApiBaseUrl =>
#if ANDROID
        "http://192.168.1.100:5210";
#else
        "http://localhost:5210";
#endif

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        // ── Auth + API ──────────────────────────────────────────────────
        builder.Services.AddSingleton<AuthState>();
        builder.Services.AddSingleton(new AmesConnectionFactory(
            "Server=tcp:192.168.1.100,1433;Database=AMES_DEV;User Id=ames_app;Password=!Dev2026;TrustServerCertificate=True;Encrypt=True;Connect Timeout=5;"));
        builder.Services.AddHttpClient<PdaApi>(c => c.BaseAddress = new Uri(ApiBaseUrl));

        return builder.Build();
    }
}
