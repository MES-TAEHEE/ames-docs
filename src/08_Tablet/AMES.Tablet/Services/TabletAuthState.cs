using AMES.Contracts.Dto;

namespace AMES.Tablet.Services;

public sealed class TabletAuthState
{
    public string? Token { get; private set; }
    public PopSessionDto? Session { get; private set; }

    public void SignIn(string token, PopSessionDto session)
    {
        Token = token;
        Session = session;
    }

    public void SignOut()
    {
        Token = null;
        Session = null;
    }
}
