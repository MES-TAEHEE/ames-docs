using AMES.Data.Connection;
using AMES.Tablet.Services;
using Microsoft.Extensions.Logging;
using Radzen;

namespace AMES.Tablet;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts => fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular"));

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddRadzenComponents();
        builder.Services.AddSingleton<TabletAuthState>();
        builder.Services.AddSingleton(new HttpClient { BaseAddress = new Uri(ApiBaseUrl) });
        builder.Services.AddSingleton<TabletAuthService>();
        builder.Services.AddSingleton(new AmesConnectionFactory(ConnectionString));
        builder.Services.AddSingleton<TabletInventoryService>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    private static string ApiBaseUrl =>
#if ANDROID
        "http://192.168.1.100:5210";
#else
        "http://localhost:5210";
#endif

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("AMES_CONNECTION_STRING")
#if ANDROID
        ?? "Server=tcp:192.168.1.100,1433;Database=AMES_DEV;User Id=ames_app;Password=!Dev2026;TrustServerCertificate=True;Encrypt=True;Connect Timeout=5;";
#else
        ?? "Server=localhost,1433;Database=AMES_DEV;User Id=sa;Password=AmesDev!2026Sa;TrustServerCertificate=True;Encrypt=True;Connect Timeout=5;";
#endif
}
