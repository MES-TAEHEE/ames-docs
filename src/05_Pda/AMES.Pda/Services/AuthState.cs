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
    public bool SessionCheckRequired { get; private set; }
    public string? SessionCheckError { get; private set; }
    public int SessionCheckVersion { get; private set; }
    private bool _expiryNotice;
    private readonly object _gate = new();
    public event Action?   OnChange;

    public void SignIn(string token, PopSessionDto session)
    {
        lock (_gate)
        {
            Token = token; Session = session;
            SessionCheckRequired = false; SessionCheckError = null; _expiryNotice = false;
        }
        OnChange?.Invoke();
    }

    public void SignOut()
    {
        lock (_gate)
        {
            Token = null; Session = null;
            SessionCheckRequired = false; SessionCheckError = null; _expiryNotice = false;
        }
        OnChange?.Invoke();
    }

    public void RequireSessionCheck()
    {
        lock (_gate)
        {
            if (Token is null) return;
            SessionCheckVersion++;
            SessionCheckRequired = true;
            SessionCheckError = null;
        }
        OnChange?.Invoke();
    }

    public void CompleteSessionCheck(string token, int version, PopSessionDto? session, string? error)
    {
        lock (_gate)
        {
            if (Token != token || SessionCheckVersion != version) return;
            if (session is not null) Session = session;
            SessionCheckRequired = session is null;
            SessionCheckError = error;
        }
        OnChange?.Invoke();
    }

    public void ExpireSession(string token)
    {
        lock (_gate)
        {
            if (Token != token) return;
            Token = null; Session = null;
            SessionCheckRequired = false; SessionCheckError = null;
            _expiryNotice = true;
        }
        OnChange?.Invoke();
    }

    public bool TakeExpiryNotice()
    {
        lock (_gate)
        {
            var pending = _expiryNotice;
            _expiryNotice = false;
            return pending;
        }
    }
}
