using AMES.Pda.Services;
using Microsoft.Extensions.Logging;

namespace AMES.Pda;

public static class MauiProgram
{
    /// <summary>
    /// API base URL. Android emulator → 10.0.2.2 maps to host loopback.
    /// Windows / macCatalyst → localhost. Override at runtime later via
    /// a settings screen.
    /// </summary>
    public static string ApiBaseUrl =>
#if ANDROID
        "http://10.0.2.2:5210";
#else
        "http://localhost:5210";
        //"http://192.168.1.100:5210";
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
        builder.Services.AddHttpClient<PdaApi>(c => c.BaseAddress = new Uri(ApiBaseUrl));

        return builder.Build();
    }
}
