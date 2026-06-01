using AMES.Pop.Common;
using AMES.Pop.Forms;

namespace AMES.Pop;

/// <summary>
/// A-MES POP (shop-floor terminal) entry point.
/// Boots a single full-screen WinForms shell (PopBlazorForm) that hosts the
/// Blazor UI. The Blazor router decides whether to show /login or the
/// post-login dashboards based on AppState.Session.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        PopServices.Initialize();        // SQL + repos + auth — used by Blazor pages
        Application.Run(new PopBlazorForm());
    }
}
