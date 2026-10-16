using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace AMES.Web.Services;

/// <summary>
/// 상단바에서 자기 비밀번호를 바꾼 뒤 로그인 쿠키를 다시 발급하기 위한 1회용 표(10-07).
/// 비밀번호를 바꾸면 보안 스탬프가 바뀌어 기존 쿠키가 재확인 주기(기본 5분) 안에 끊기는데, 쿠키는 회로가 아니라 HTTP 응답으로만
/// 다시 줄 수 있다. 회로가 변경 직후 표를 발급하고 /Account/RefreshSignIn 으로 이동하면 그 요청이 새 쿠키를 받는다.
/// 표는 임의 32바이트·1회용·60초 유효이고 메모리에만 있다(앱 재시작이면 사라져 재로그인하면 된다).
/// </summary>
public sealed class SignInRefreshTickets
{
    static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);
    readonly ConcurrentDictionary<string, (string UserId, DateTime ExpiresUtc)> _tickets = new();

    public string Issue(string userId)
    {
        var now = DateTime.UtcNow;
        foreach (var (k, v) in _tickets)
            if (v.ExpiresUtc <= now) _tickets.TryRemove(k, out _);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _tickets[token] = (userId, now + Lifetime);
        return token;
    }

    public string? Redeem(string? token)
    {
        if (string.IsNullOrEmpty(token) || !_tickets.TryRemove(token, out var t)) return null;
        return t.ExpiresUtc > DateTime.UtcNow ? t.UserId : null;
    }
}
