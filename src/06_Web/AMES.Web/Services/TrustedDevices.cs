using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace AMES.Web.Services;

/// <summary>
/// 내부 로그인 신뢰 기기 쿠키(10-07, OWASP "device cookie" 방식) — 남이 일부러 틀려 계정을 잠가도 평소 쓰던 PC 는 계속 로그인되게 한다.
///   · 로그인에 성공한 브라우저에 그 계정 전용 쿠키(ames.td.{계정 해시}, Data Protection 서명, 1년, 경로 /Account)를 준다. 기기 수 제한 없음.
///   · 계정이 LOCKED 여도 신뢰 기기에서 맞는 비밀번호면 로그인된다(계정 잠금은 그대로 — 새 기기는 Admin 해제 전까지 막힌다).
///   · 신뢰 기기에서 틀린 횟수는 계정 카운터가 아니라 그 기기에 센다. 5회면 그 기기만 신뢰를 해제한다(사용자 결정 (a)) — 이후 새 기기처럼 취급.
/// 기기별 실패 수·해제 목록은 메모리라 앱 재시작이면 사라진다(해제된 쿠키는 브라우저에서 지워지므로 영향은 쿠키를 따로 보관해 둔 경우뿐).
/// </summary>
public sealed class TrustedDevices(IDataProtectionProvider dataProtection)
{
    public const int MaxDeviceFailures = 5;
    static readonly TimeSpan Lifetime = TimeSpan.FromDays(365);
    const string CookiePath = "/Account";

    readonly IDataProtector _protector = dataProtection.CreateProtector("AMES.TrustedDevice.v1");
    readonly ConcurrentDictionary<string, int> _failures = new();
    readonly ConcurrentDictionary<string, byte> _revoked = new();

    static string CookieName(string userId)
        => "ames.td." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userId)))[..16].ToLowerInvariant();

    /// <summary>이 요청이 그 계정의 신뢰 기기에서 왔으면 기기 식별값(nonce), 아니면 null.</summary>
    public string? Read(HttpContext ctx, string userId)
    {
        if (!ctx.Request.Cookies.TryGetValue(CookieName(userId), out var raw) || string.IsNullOrEmpty(raw)) return null;
        try
        {
            var parts = _protector.Unprotect(raw).Split('|');
            if (parts.Length != 3 || parts[0] != userId || !long.TryParse(parts[2], out var ticks)) return null;
            if (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc) > Lifetime || _revoked.ContainsKey(parts[1])) return null;
            return parts[1];
        }
        catch (CryptographicException) { return null; }   // 위조·다른 서버 키·손상
    }

    /// <summary>로그인 성공 — 신뢰 기기로 등록하거나(새 기기) 기간을 연장하고 그 기기의 실패 수를 지운다.</summary>
    public void Trust(HttpContext ctx, string userId, string? existingNonce)
    {
        var nonce = existingNonce ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        _failures.TryRemove(nonce, out _);
        ctx.Response.Cookies.Append(CookieName(userId), _protector.Protect($"{userId}|{nonce}|{DateTime.UtcNow.Ticks}"), new CookieOptions
        {
            Path = CookiePath, HttpOnly = true, IsEssential = true, SameSite = SameSiteMode.Lax,
            Secure = ctx.Request.IsHttps, Expires = DateTimeOffset.UtcNow.Add(Lifetime),
        });
    }

    /// <summary>신뢰 기기에서 비밀번호를 틀림 — 그 기기에 센다. 5회째면 그 기기의 신뢰를 해제하고 true.</summary>
    public bool RecordFailure(HttpContext ctx, string userId, string nonce)
    {
        if (_failures.AddOrUpdate(nonce, 1, (_, n) => n + 1) < MaxDeviceFailures) return false;
        _failures.TryRemove(nonce, out _);
        _revoked[nonce] = 0;
        ctx.Response.Cookies.Delete(CookieName(userId), new CookieOptions { Path = CookiePath });
        return true;
    }
}
