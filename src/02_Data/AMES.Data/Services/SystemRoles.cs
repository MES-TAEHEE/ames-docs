namespace AMES.Data.Services;

/// <summary>
/// 기본 역할 — ID 가 고정이다(ROLE-xxx, dist/migrate_system_roles.sql·웹 기동 시드가 만든다).
/// 이름은 SYS-002 에서 바꿀 수 있고 코드는 ID 로만 판정하므로 이름이 바뀌어도 동작은 같다. 기본 역할은 삭제할 수 없다.
/// 그 밖의 역할(SYS-002 에서 새로 만든 역할)은 새 GUID 를 받는다.
/// </summary>
public static class SystemRoles
{
    public const string AdminId       = "ROLE-SYSADMIN";
    public const string SupervisorId  = "ROLE-SUPERVISOR";
    public const string OperatorId    = "ROLE-OPERATOR";
    public const string MaintenanceId = "ROLE-MAINTENANCE";
    public const string QualityId     = "ROLE-QUALITY";
    public const string ProductionId  = "ROLE-PRODUCTION";

    /// <summary>역할이 아직 없는 DB 에 처음 만들 때의 이름(10-09 개발 DB 이름).</summary>
    public const string AdminDefaultName      = "System Administrator";
    public const string SupervisorDefaultName = "Supervisor";

    /// <summary>OldName = 고정 ID 이전(10-09)의 이름 — 옛 DB 에서 같은 역할을 찾을 때만 쓴다(dist/migrate_system_roles.sql 과 같은 목록).</summary>
    public static readonly IReadOnlyList<(string Id, string DefaultName, string? OldName)> All =
    [
        (AdminId,       AdminDefaultName,      "Admin"),
        (SupervisorId,  SupervisorDefaultName, null),
        (OperatorId,    "Operator",            null),
        (MaintenanceId, "Maintenance Manager", "Maintenance"),
        (QualityId,     "Quality Manager",     "QC"),
        (ProductionId,  "Production Manager",  "Planner"),
    ];

    public static bool IsSystem(string? roleId) =>
        roleId is not null && All.Any(r => string.Equals(r.Id, roleId, StringComparison.OrdinalIgnoreCase));
}
