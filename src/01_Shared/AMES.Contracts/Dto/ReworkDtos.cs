namespace AMES.Contracts.Dto;

/// <summary>PR_DefectDetail 1행(불량 LOT 1건) + LOT·품번·불량코드 조인 — REWORK 대기열·이력 한 줄.</summary>
public sealed class ReworkItemDto
{
    public required int       DefectId         { get; init; }
    public required int       LotId            { get; init; }
    public required string    LotCode          { get; init; }
    public required string    ItemNo           { get; init; }
    public required string    ItemName         { get; init; }
    /// <summary>불량을 등록한 공정 INJ / IMG — 판정 시 어느 LOT 테이블을 갱신할지 정한다.</summary>
    public required string    ProcessCode      { get; init; }
    /// <summary>LOT 의 원래 라인. 수리 양품의 WO 를 이 라인에서 해석한다.</summary>
    public required string    LineId           { get; init; }
    public required string    DefectCode       { get; init; }
    public string?            DefectName       { get; init; }
    public string?            DefectNameEn     { get; init; }
    public string?            DefaultCauseCode { get; init; }
    /// <summary>등록 당시 LOT 상태 RAW / CONFIRMED / NG_BLOCKED.</summary>
    public string?            PriorStatus      { get; init; }
    public string?            ReasonNote       { get; init; }
    public DateTime           DetectedAt       { get; init; }
    /// <summary>등록자 사번(CreatedBy).</summary>
    public string?            RegisteredBy     { get; init; }
    public int?               WoId             { get; init; }
    public string?            WoNumber         { get; init; }
    public string?            Disposition      { get; init; }
    public string?            CauseCode        { get; init; }
    public string?            CorrectiveAction { get; init; }
    public string?            DispositionBy    { get; init; }
    public DateTime?          DispositionAt    { get; init; }

    public bool IsPending => string.IsNullOrEmpty(Disposition);
}

/// <summary>REWORK 상단 칩 — 대기(전체) · 오늘 수리 · 오늘 폐기 (판정일 기준).</summary>
public sealed class ReworkStatsDto
{
    public required int Pending        { get; init; }
    public required int TodayReworked  { get; init; }
    public required int TodayScrapped  { get; init; }
}

/// <summary>MD_DefectCause 활성 행 — REWORK 원인코드 버튼.</summary>
public sealed class DefectCauseDto
{
    public required string CauseCode   { get; init; }
    public required string CauseName   { get; init; }
    public string?         CauseNameEn { get; init; }
    public string?         ProcessCode { get; init; }
}
