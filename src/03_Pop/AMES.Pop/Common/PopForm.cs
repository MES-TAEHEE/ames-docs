namespace AMES.Pop.Common;

/// <summary>
/// Base class for every POP screen.
///
/// POP terminals are dedicated industrial touch panels (typically 15" 1920×1080)
/// running A-MES exclusively — there's no window manager UX. Every screen
/// therefore launches borderless + maximized so the operator sees only the
/// app, with no taskbar / title bar / close button to fat-finger by accident.
///
/// Escape acts as the universal "back" key: on a sub-screen it closes the
/// dialog (returning to the dashboard); on the root LoginForm it exits.
/// </summary>
public class PopForm : Form
{
    public PopForm()
    {
        // chrome
        FormBorderStyle = FormBorderStyle.None;
        WindowState     = FormWindowState.Maximized;
        StartPosition   = FormStartPosition.Manual;
        ShowInTaskbar   = true;
        BackColor       = PopTheme.BgOuter;
        ForeColor       = PopTheme.TextWhite;
        Font            = PopTheme.Body;
        AutoScaleMode   = AutoScaleMode.Dpi;
        KeyPreview      = true;
        DoubleBuffered  = true;

        // Cover the primary screen's full bounds. WindowState=Maximized already
        // does this for us, but setting Bounds explicitly avoids one-pixel
        // mismatches when Visual Studio's debug host stretches the form.
        var screen = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        Bounds = screen;

        // Universal back / exit shortcut.
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) OnEscapePressed();
        };
    }

    /// <summary>
    /// Override on the root form (LoginForm) to prompt before exit;
    /// default behaviour for sub-screens is just Close().
    /// </summary>
    protected virtual void OnEscapePressed() => Close();
}
