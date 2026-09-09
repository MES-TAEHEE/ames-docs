namespace AMES.Contracts.Dto;

/// <summary>PR_AndonCall 한 건 + 부서 호출 행. Status: OPEN → SUP_ACKED → DEPT_CALLED → RESOLVED.</summary>
public sealed class AndonCallDto
{
    public required int      AndonId        { get; init; }
    public required string   LineId         { get; init; }
    public string?           EquipId        { get; init; }
    public required string   Status         { get; init; }
    public required DateTime TriggeredAt    { get; init; }
    public required string   TriggeredBy    { get; init; }
    public string?           SupervisorNo   { get; init; }
    public string?           SupervisorName { get; init; }
    public DateTime?         SupervisorAt   { get; init; }
    public string?           CauseCode      { get; init; }
    public DateTime?         ResolvedAt     { get; init; }
    public List<AndonDeptCallDto> Depts     { get; init; } = new();
}

public sealed class AndonDeptCallDto
{
    public required int      DeptCallId  { get; init; }
    public required string   DeptCode    { get; init; }
    public required string   DeptName    { get; init; }
    public string?           DeptNameEn  { get; init; }
    public required DateTime CalledAt    { get; init; }
    public DateTime?         ArrivedAt   { get; init; }
    public string?           ArrivedNo   { get; init; }
    public string?           ArrivedName { get; init; }
    public DateTime?         AckedAt     { get; init; }

    public bool IsArrived => ArrivedAt is not null;
    public bool IsAcked   => AckedAt   is not null;
}

/// <summary>MD_CodeItem ANDON_CAUSE. DefaultDeptCode = Attribute1 (ANDON_DEPT.CodeValue), 없으면 null.</summary>
public sealed record AndonCauseDto(string Code, string Name, string? NameEn, string? DefaultDeptCode);

/// <summary>MD_CodeItem ANDON_DEPT.</summary>
public sealed record AndonDeptDto(string Code, string Name, string? NameEn);
