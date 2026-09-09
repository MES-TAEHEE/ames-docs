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

    /// <summary>로그인 시 선택된 라인의 WC ProcessCode (INJ/IMG/PNT/QC).</summary>
    public string? ModuleCode { get; private set; }

    /// <summary>
    /// PIN 설정 오버레이가 붙잡고 있는 세션. 아직 <see cref="SignIn"/> 전이지만
    /// PR_PopSession 행은 이미 만들어져 있다 — 그래서 이 구간도 유휴 감시 대상이다.
    /// </summary>
    public PopSessionDto? PendingSession { get; private set; }

    /// <summary>Raised after <see cref="SignIn"/> / <see cref="SignOut"/>.</summary>
    public event Action? OnChange;

    /// <summary>
    /// 로그아웃으로 닫히는 세션과 그 사유. PR_PopSession 행을 닫는 구독자는 AppRoot 하나뿐이다 —
    /// 로그아웃 버튼이 화면마다 흩어져 있어서, 각 호출부가 아니라 여기서 한 번에 잡는다.
    /// AppState 자체는 DB 를 모른다.
    /// </summary>
    public event Action<PopSessionDto, string>? SigningOut;

    public void SignIn(PopSessionDto session, string moduleCode)
    {
        Session        = session ?? throw new ArgumentNullException(nameof(session));
        ModuleCode     = moduleCode;
        PendingSession = null;
        OnChange?.Invoke();
    }

    /// <param name="reason">PR_PopSession.LogoutReason 에 남는다 — 수동 로그아웃은 USER, 유휴 만료는 IDLE.</param>
    public void SignOut(string reason = "USER")
    {
        // PIN 설정 중에 나가도 행은 이미 있다 — 그쪽도 닫아야 한다.
        var closing = Session ?? PendingSession;

        Session        = null;
        ModuleCode     = null;
        PendingSession = null;

        if (closing is not null) SigningOut?.Invoke(closing, reason);
        OnChange?.Invoke();
    }

    public void BeginPinSetup(PopSessionDto session)
    {
        PendingSession = session ?? throw new ArgumentNullException(nameof(session));
        OnChange?.Invoke();
    }

    /// <summary>PIN 저장에 성공해 곧 <see cref="SignIn"/> 으로 이어지는 경로 — 세션을 닫지 않는다.</summary>
    public void EndPinSetup()
    {
        if (PendingSession is null) return;
        PendingSession = null;
        OnChange?.Invoke();
    }
}
