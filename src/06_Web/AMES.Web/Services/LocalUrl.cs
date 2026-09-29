namespace AMES.Web.Services;

/// <summary>
/// 로그인 뒤 돌아갈 주소(ReturnUrl) 검사 — 같은 사이트 주소만 통과시킨다(Open Redirect 방지, ASP.NET IsLocalUrl 과 같은 규칙).
/// "/x" 는 통과, "//host"·"/\host"(브라우저가 외부 주소로 해석)·"https://…"·제어문자가 든 값은 거부한다.
/// Uri.IsWellFormedUriString(…, Relative) 는 "//host" 를 상대 주소로 보아 통과시키므로 쓰면 안 된다.
/// </summary>
public static class LocalUrl
{
    /// <summary>"/" 로 시작하는 같은 사이트 경로인가.</summary>
    public static bool IsLocal(string? url)
    {
        if (string.IsNullOrEmpty(url) || url.Any(char.IsControl)) return false;
        return url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));
    }

    /// <summary>"Account/Manage" 처럼 앱 기준 상대 경로인가(앞이 "/"·"\"·공백이 아니고 스킴이 없다).</summary>
    public static bool IsAppRelative(string? url)
    {
        if (string.IsNullOrEmpty(url) || url.Any(char.IsControl)) return false;
        // 앞 공백은 절대 주소로 바꿀 때 잘려 " //host" 가 외부 주소가 된다
        if (url[0] is '/' or '\\' || char.IsWhiteSpace(url[0])) return false;
        var path = url.Split('?', '#')[0];
        return !path.Contains(':') && !path.Contains('\\');
    }

    /// <summary>
    /// 기준 주소로 풀었을 때 같은 사이트(스킴·호스트·포트)인가 — 문자열 규칙이 놓친 변형을 막는 마지막 확인.
    /// </summary>
    public static bool ResolvesToSameSite(string url, string baseUri)
    {
        try
        {
            var b = new Uri(baseUri, UriKind.Absolute);
            var abs = new Uri(b, url);
            return Uri.Compare(abs, b, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;
        }
        catch (UriFormatException) { return false; }
    }

    /// <summary>같은 사이트 경로면 그대로, 아니면 대체 경로.</summary>
    public static string OrDefault(string? url, string fallback) => IsLocal(url) ? url! : fallback;
}
