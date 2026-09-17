using AMES.Pda.Services;
using Microsoft.Extensions.Logging;
using Radzen;

namespace AMES.Pda;

public static class MauiProgram
{
    public static bool IsDeveloperMode
    {
        get
        {
            var value = Environment.GetEnvironmentVariable("AMES_PDA_DEV_MODE");
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static MauiApp CreateMauiApp()
    {
        var settings = PdaSettings.Load();
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddRadzenComponents();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        // ── Auth + API ──────────────────────────────────────────────────
        builder.Services.AddSingleton<AuthState>();
        builder.Services.AddSingleton(settings);
        builder.Services.AddHttpClient<AuthApi>(c => c.BaseAddress = new Uri(settings.ApiBaseUrl));
        builder.Services.AddHttpClient<WarehouseApi>(c => c.BaseAddress = new Uri(settings.ApiBaseUrl));
        builder.Services.AddHttpClient<FinishedGoodsApi>(c => c.BaseAddress = new Uri(settings.ApiBaseUrl));
        builder.Services.AddHttpClient<SparePartsApi>(c => c.BaseAddress = new Uri(settings.ApiBaseUrl));

        return builder.Build();
    }
}
