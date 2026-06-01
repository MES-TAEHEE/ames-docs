using AMES.Contracts.Dto;
using AMES.Pop.Common;
using AMES.Pop.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.DependencyInjection;

namespace AMES.Pop.Forms;

/// <summary>
/// Base WinForms shell that hosts a <see cref="BlazorWebView"/> and serves
/// the Blazor router. Subclasses (or LoginForm) construct an instance,
/// passing the operator session + the initial route (e.g. "/inj02"); the
/// session is shared down via a <see cref="CascadingValue{T}"/> so every
/// page can `[CascadingParameter] PopSessionDto Session`.
/// </summary>
public class PopBlazorForm : PopForm
{
    private readonly BlazorWebView _webView;
    private readonly PopSessionDto _session;
    private readonly string        _route;

    public PopBlazorForm(PopSessionDto session, string route)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _route   = route;
        Text     = $"A-MES POP · {route}";

        var services = new ServiceCollection();
        services.AddWindowsFormsBlazorWebView();
#if DEBUG
        services.AddBlazorWebViewDeveloperTools();
#endif

        _webView = new BlazorWebView
        {
            Dock      = DockStyle.Fill,
            HostPage  = "wwwroot/index.html",
            StartPath = _route,    // navigate straight to the route so the
                                   // user never sees the brief "/" landing
                                   // (which has no @page and showed 404).
            Services  = services.BuildServiceProvider(),
        };
        _webView.RootComponents.Add(new RootComponent(
            selector:      "#app",
            componentType: typeof(AppRoot),
            parameters:    new Dictionary<string, object?>
            {
                ["Session"] = _session,
            }));
        Controls.Add(_webView);

        BlazorHost.ActionRequested += OnAction;
        FormClosing += (_, _) => BlazorHost.ActionRequested -= OnAction;
    }

    /// <summary>
    /// Dispatch nav: routes to a new sub-screen; logout simply closes the
    /// form which returns control to LoginForm. Subclasses can override
    /// for screen-specific actions.
    /// </summary>
    protected virtual Task OnAction(string action)
    {
        if (action == "logout")
        {
            BeginInvoke(() => Close());
            return Task.CompletedTask;
        }
        if (action.StartsWith("nav:"))
        {
            // Sub-screen navigation is a no-op for the PoC; later we'll route
            // by spinning up a new PopBlazorForm for that screen.
            return Task.CompletedTask;
        }
        return Task.CompletedTask;
    }
}
