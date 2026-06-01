using AMES.Pop.Common;
using AMES.Pop.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.DependencyInjection;

namespace AMES.Pop.Forms;

/// <summary>
/// WinForms shell that hosts the entire Blazor POP UI for the life of the
/// application. Session state lives in <see cref="AppState"/> (a singleton
/// inside the WebView's DI container), so login / logout never needs to
/// destroy and recreate this form — Login.razor signs in, AppState changes,
/// the router moves to /inj02 or /img02, logout signs out and goes back to
/// /login.
/// </summary>
public class PopBlazorForm : PopForm
{
    private readonly BlazorWebView _webView;

    public PopBlazorForm()
    {
        Text = "A-MES POP";

        var services = new ServiceCollection();
        services.AddWindowsFormsBlazorWebView();
#if DEBUG
        services.AddBlazorWebViewDeveloperTools();
#endif
        services.AddSingleton<AppState>();

        _webView = new BlazorWebView
        {
            Dock      = DockStyle.Fill,
            HostPage  = "wwwroot/index.html",
            StartPath = "/login",
            Services  = services.BuildServiceProvider(),
        };
        _webView.RootComponents.Add(new RootComponent(
            selector:      "#app",
            componentType: typeof(AppRoot),
            parameters:    new Dictionary<string, object?>()));

        Controls.Add(_webView);

        BlazorHost.ActionRequested += OnAction;
        FormClosing += (_, _) => BlazorHost.ActionRequested -= OnAction;
    }

    /// <summary>
    /// Action channel from Razor pages. Most navigation stays inside the
    /// WebView via NavigationManager — only 'shell-exit' bubbles up here
    /// to close the form (used by an explicit Exit button if we add one).
    /// </summary>
    protected virtual Task OnAction(string action)
    {
        if (action == "shell-exit")
            BeginInvoke(() => Close());
        return Task.CompletedTask;
    }
}
