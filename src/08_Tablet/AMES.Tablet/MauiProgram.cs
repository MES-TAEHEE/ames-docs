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
        builder.Services.AddSingleton(new HttpClient { BaseAddress = new Uri(ApiBaseUrl), Timeout = TimeSpan.FromSeconds(30) });
        builder.Services.AddSingleton<TabletAuthService>();
        builder.Services.AddSingleton<TabletInventoryApi>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    private const string ApiBaseUrl = "http://desktop-poedibs.tail526f68.ts.net:5210";
}
