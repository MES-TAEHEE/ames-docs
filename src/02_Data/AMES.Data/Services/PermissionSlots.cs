namespace AMES.Data.Services;

/// <summary>
/// 권한 값 3자리 형식(10-10 사용자 결정) — 자리 순서 R(읽기)·E(수정)·A(승인).
/// <list type="bullet">
/// <item>화면 기본 권한 <c>SYS_Screen.PermissionCriteria</c>(varchar(10), 지금은 3자리) = 그 화면에 있는 기능 — 자리마다 '_'(기능 있음) 또는 'X'(기능 없음).
///   읽기 자리는 항상 '_'(모든 화면은 열 수 있어야 한다 — 숨기려면 표시를 끈다). 예: "___" 승인까지, "__X" 수정까지, "_XX" 조회 전용.</item>
/// <item>역할 권한 <c>SYS_RolePermission.PermissionLevel</c> = 부여 — 자리마다 글자(부여) · '_'(기능은 있지만 부여 안 함) · 'X'(기능 없음).
///   예: "REX" "R_X" "RXX" "R_A".</item>
/// </list>
/// SYS-003 이 화면 기본 권한을 정하고, SYS-004 는 그 값으로 기능 없는 칸을 끄고 X 로 저장한다.
/// 권한 판정(PermissionService)은 R/E/A 글자만 읽으므로 '_'·'X'·옛 형식("REA" "RE" "R")이 섞여도 결과가 같다.
/// </summary>
public static class PermissionSlots
{
    public const string Slots = "REA";
    public const char NotGranted = '_', NoFeature = 'X';

    /// <summary>화면 기본 권한의 기본값 — 세 기능이 모두 있다고 본다(새 화면).</summary>
    public const string Default = "___";

    /// <summary>화면 기본 권한을 3자리로 맞춘다 — 'X' 가 아니면 '_', 읽기 자리는 항상 '_'. 비었거나 짧으면 나머지는 '_'.</summary>
    public static string NormalizeTemplate(string? template)
    {
        var t = (template ?? "").ToUpperInvariant();
        return new string(Slots.Select((_, i) => i > 0 && i < t.Length && t[i] == NoFeature ? NoFeature : NotGranted).ToArray());
    }

    /// <summary>화면 기본 권한에 그 기능(R/E/A)이 있는가.</summary>
    public static bool Has(string? template, char slot)
    {
        var i = Slots.IndexOf(char.ToUpperInvariant(slot));
        return i >= 0 && NormalizeTemplate(template)[i] != NoFeature;
    }

    /// <summary>화면 기본 권한을 기능 글자로("R" / "RE" / "RA" / "REA").</summary>
    public static string FeaturesOf(string? template) => new(Slots.Where(c => Has(template, c)).ToArray());

    /// <summary>기능 글자("RE" 등)로 화면 기본 권한("__X")을 만든다 — 읽기는 항상 있다.</summary>
    public static string TemplateOf(bool hasEdit, bool hasApprove)
        => $"{NotGranted}{(hasEdit ? NotGranted : NoFeature)}{(hasApprove ? NotGranted : NoFeature)}";

    /// <summary>역할 권한 저장값에서 부여된 글자(R/E/A, REA 순). 화면 기본 권한을 주면 그 화면에 없는 기능 글자는 뺀다.</summary>
    public static string Granted(string? level, string? template = null)
    {
        if (string.IsNullOrWhiteSpace(level)) return "";
        var up = level.ToUpperInvariant();
        return new string(Slots.Where(c => up.IndexOf(c) >= 0 && (template is null || Has(template, c))).ToArray());
    }

    /// <summary>부여된 글자로 역할 권한 3자리를 만든다. 아무것도 부여하지 않으면 null(행 없음 = 권한 없음).</summary>
    public static string? Compose(string? template, string? granted)
    {
        var g = Granted(granted, template);
        if (g.Length == 0) return null;
        return new string(Slots.Select(c => !Has(template, c) ? NoFeature : g.IndexOf(c) >= 0 ? c : NotGranted).ToArray());
    }
}
