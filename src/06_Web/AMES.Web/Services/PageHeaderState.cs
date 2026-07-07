namespace AMES.Web.Services;

/// <summary>
/// 현재 화면의 경로 코드(예: PP-004)와 이름을 담아 TopBar 브레드크럼에 전달한다.
/// 각 페이지의 &lt;AmesPageHeader&gt;가 값을 설정하고 TopBar가 구독한다.
/// Scoped — Blazor Server 회로(circuit)당 1개.
/// </summary>
public sealed class PageHeaderState
{
    public string? Code  { get; private set; }
    public string? Title { get; private set; }

    public event Action? OnChanged;

    public void Set(string? code, string? title)
    {
        if (code == Code && title == Title) return;
        Code  = code;
        Title = title;
        OnChanged?.Invoke();
    }
}
