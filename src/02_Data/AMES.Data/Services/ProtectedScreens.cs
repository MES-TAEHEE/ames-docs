namespace AMES.Data.Services;

/// <summary>
/// 권한을 다시 줄 수 있는 화면 — SYS-003(화면 관리)·SYS-004(RBAC). 이 둘을 잃으면 화면으로는 복구할 길이 없어 SQL 로만 풀린다(10-12 사용자 결정).
/// <list type="bullet">
/// <item>System Administrator(<see cref="SystemRoles.AdminId"/>)는 두 화면의 읽기·수정을 항상 가진다(SYS-004 에서 끌 수 없다).</item>
/// <item>두 화면의 화면 기본 권한에서 수정 기능을 끌 수 없고, 코드·주소를 바꾸거나 삭제할 수 없다(SYS-003).</item>
/// </list>
/// 화면은 주소(HRef)로 가른다 — 권한 판정(PermissionService)이 주소로 화면을 찾고, 주소는 소스의 @page 라 바뀌지 않는다.
/// 이미 잠겼으면 dist/restore_admin_rbac.sql 로 되살린다.
/// </summary>
public static class ProtectedScreens
{
    public static readonly IReadOnlyList<string> Hrefs = ["sys/screens", "sys/rbac"];

    /// <summary>관리자가 두 화면에서 늘 가지는 권한 글자.</summary>
    public const string AdminSlots = "RE";

    /// <summary>SQL IN 목록 — <c>TRIM('/' FROM HRef) IN (…)</c> 에 쓴다(상수라 매개변수가 아니어도 안전하다).</summary>
    public static string SqlList => string.Join(", ", Hrefs.Select(h => $"'{h}'"));

    public static bool IsProtected(string? href)
        => href is not null && Hrefs.Contains(href.Trim().Trim('/'), StringComparer.OrdinalIgnoreCase);

    /// <summary>그 역할·화면에서 그 칸(R/E/A)이 고정인가 — SYS-004 가 체크박스를 끈다.</summary>
    public static bool IsLocked(string? roleId, string? href, char slot)
        => roleId == SystemRoles.AdminId && IsProtected(href) && AdminSlots.IndexOf(char.ToUpperInvariant(slot)) >= 0;

    /// <summary>역할 부여 글자에 고정 권한을 더한다(REA 순). 대상이 아니면 그대로.</summary>
    public static string ApplyRole(string? roleId, string? href, string? granted)
        => roleId == SystemRoles.AdminId && IsProtected(href)
            ? PermissionSlots.Granted((granted ?? "") + AdminSlots)
            : PermissionSlots.Granted(granted);

    /// <summary>화면 기본 권한에서 두 화면의 수정 기능을 되살린다. 대상이 아니면 3자리로 맞추기만 한다.</summary>
    public static string ApplyTemplate(string? href, string? template)
    {
        var t = PermissionSlots.NormalizeTemplate(template);
        return IsProtected(href) ? $"{t[0]}{PermissionSlots.NotGranted}{t[2]}" : t;
    }
}
