using AMES.Contracts.Dto;

namespace AMES.Pda.Services;

/// <summary>
/// Session held in-memory for the life of the running PDA. Login sets the
/// token + session DTO; Logout clears them. Components subscribe to
/// OnChange to re-render when auth state flips.
/// </summary>
public sealed class AuthState
{
    public string?         Token   { get; private set; }
    public PopSessionDto?  Session { get; private set; }
    public event Action?   OnChange;

    public void SignIn(string token, PopSessionDto session)
    {
        Token = token; Session = session;
        OnChange?.Invoke();
    }

    public void SignOut()
    {
        Token = null; Session = null;
        OnChange?.Invoke();
    }
}
