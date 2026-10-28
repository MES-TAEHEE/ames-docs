using System.Security.Claims;
using System.Text;
using System.Security.Cryptography;

namespace AMES.Web.Services;

/// <summary>
/// 감사 행위자 코드(CreatedBy·ModifiedBy·ApprovedBy·RequestedBy 에 넣는 값).
/// 09-23 `migrate_audit_actor_varchar20.sql` 이후 이 컬럼들은 전부 varchar(20) 이라 GUID·이메일을 그대로 쓰면 잘려서 예외가 난다.
/// 규칙: 로그인 클레임 <see cref="ClaimType"/>(= SYS_UserProfile.EmployeeNo, <see cref="AmesClaimsPrincipalFactory"/> 가 채움)
/// → 없으면 사용자명(이메일이면 @ 앞부분) → 그래도 없으면 fallback. 항상 20바이트 이하로 자른다.
/// </summary>
public static class ActorCode
{
    public const string ClaimType = "ames:actor";
    public const int MaxBytes = 20;

    public static string Of(ClaimsPrincipal? user, string fallback = "system")
    {
        if (user?.Identity?.AuthenticationType == PortalAuth.Scheme && !string.IsNullOrWhiteSpace(user.Identity.Name))
            return Portal(user.Identity.Name);
        var claim = user?.FindFirst(ClaimType)?.Value;
        if (!string.IsNullOrWhiteSpace(claim)) return Normalize(claim, fallback);
        return Normalize(user?.Identity?.Name, fallback);
    }

    /// <summary>임의 문자열(이메일·사번·GUID)을 행위자 코드로: 이메일은 @ 앞부분, 그 뒤 20바이트(CP949 기준 한글 2바이트) 안으로 자른다.</summary>
    public static string Normalize(string? value, string fallback = "system")
    {
        var s = (value ?? "").Trim();
        if (s.Length == 0) return fallback;
        var at = s.IndexOf('@');
        if (at > 0) s = s[..at];
        return Cut(s);
    }

    /// <summary>Portal email identities need a stable, collision-resistant actor within varchar(20).</summary>
    public static string Portal(string email)
        => "P" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant())))[..16];

    // CP949 기준 바이트 수 — 10-29 DB 를 SQL_Latin1(CP1252, 글자당 1바이트)로 통일한 뒤에도 더 보수적인 쪽이라 그대로 둔다.
    // 코드페이지 제공자가 없으면 UTF-8 로 세어 더 보수적으로 자른다.
    static readonly Encoding Enc = LoadEncoding();
    static Encoding LoadEncoding()
    {
        try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); return Encoding.GetEncoding(949); }
        catch { return Encoding.UTF8; }
    }

    static string Cut(string s)
    {
        var enc = Enc;
        if (enc.GetByteCount(s) <= MaxBytes) return s;
        var sb = new StringBuilder();
        foreach (var ch in s)
        {
            if (enc.GetByteCount(sb.ToString() + ch) > MaxBytes) break;
            sb.Append(ch);
        }
        return sb.Length == 0 ? s[..Math.Min(s.Length, MaxBytes / 2)] : sb.ToString();
    }
}
