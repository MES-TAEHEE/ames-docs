using AMES.Contracts.Dto;

namespace AMES.Pop.Services;

/// <summary>
/// Process-wide login state. Held as a singleton scoped to the BlazorWebView's
/// service provider; pages cascade <see cref="Session"/> down from
/// <c>AppRoot</c> so they don't each have to <c>@inject</c> this.
///
/// Login.razor calls <see cref="SignIn"/> after PopAuthService succeeds;
/// every logout call routes through <see cref="SignOut"/> which clears the
/// session and notifies anyone listening to re-render.
/// </summary>
public class AppState
{
    public PopSessionDto? Session { get; private set; }

    /// <summary>Raised after <see cref="SignIn"/> / <see cref="SignOut"/>.</summary>
    public event Action? OnChange;

    public void SignIn(PopSessionDto session)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        OnChange?.Invoke();
    }

    public void SignOut()
    {
        Session = null;
        OnChange?.Invoke();
    }
}
