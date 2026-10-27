namespace AMES.Data.Services;

/// <summary>
/// SYS-004 권한 상승 방지(10-28 사용자 결정) — 자기가 속한 역할의 권한은 바꿀 수 없다. System Administrator(<see cref="SystemRoles.AdminId"/>)만 예외다
/// (관리자는 이미 모든 화면을 다시 줄 수 있어 자기 역할을 고쳐도 더 오를 권한이 없다). SYS-004 수정 권한만 받은 사람이 자기 역할에 권한을 붙여
/// 스스로 범위를 넓히지 못하게 한다. 화면(체크박스 끔·저장 거부)과 저장소(<see cref="Repositories.SysRepository.SaveRolePermissions"/>)가 같은 규칙을 쓴다.
/// </summary>
public static class RoleSelfEditRules
{
    public static bool Blocks(IEnumerable<string>? editorRoleIds, string? targetRoleId)
    {
        if (string.IsNullOrWhiteSpace(targetRoleId) || editorRoleIds is null) return false;
        var roles = editorRoleIds.Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
        if (roles.Contains(SystemRoles.AdminId, StringComparer.OrdinalIgnoreCase)) return false;
        return roles.Contains(targetRoleId, StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>자기 역할의 권한을 바꾸려 한 저장(<see cref="RoleSelfEditRules"/>) — 아무것도 저장하지 않았다.</summary>
public sealed class RoleSelfEditException(string roleId)
    : InvalidOperationException($"Permissions of your own role '{roleId}' cannot be changed.")
{
    public string RoleId { get; } = roleId;
}
