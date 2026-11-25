namespace AMES.Data.Services.PoSync;

/// <summary>공통코드 4그룹을 합쳐 해석한 고객사(소스) 하나. 값은 모두 트림돼 있다.</summary>
public sealed record PoSyncSource(
    string  Key,
    string  Name,
    string  CustomerId,
    string  Url,
    string? AuthScheme,
    string? AuthValue,
    string  CorCd,
    string  BizCd,
    string  VendCd,
    string  PurcOrg,
    int     IntervalMin,   // 전역 SW_POSYNC.INTERVAL — 소스별 주기는 없다
    int     WindowFrom,
    int     WindowTo,
    int?    TimeoutSec = null,
    string  Type = PoSyncConfig.TypeSrm,   // SRM = SRM INQUERY(GET) · EOS = Savannah EOS 조회 API(POST, 납기일 창)
    string? Sql  = null)                   // EOS 전용 — 조회 이름(SIS.EOS_GET_PO)
{
    /// <summary>자동 실행 때 납기일 창 끝(WindowTo) 뒤로 더 이어 받을 수 있는 최대 일수(오늘 기준) — EOS 만, 원격이 연속으로 비면 그 전에 멈춘다.</summary>
    public int MaxAheadDays { get; init; } = PoSyncConfig.DefaultMaxAheadDays;

    /// <summary>PP-002 에서 사용자가 정한 기간 — 둘 다 있으면 설정 창·자동 확장 대신 이 기간만 받는다.</summary>
    public DateOnly? FixedFrom { get; init; }
    public DateOnly? FixedTo   { get; init; }

    public bool IsEos => string.Equals(Type, PoSyncConfig.TypeEos, StringComparison.OrdinalIgnoreCase);

    public string InterfaceCode => PoSyncConfig.InterfaceCodeFor(Key);
}

public sealed record PoSyncConfigError(string Key, string Name, string Message);

public sealed record PoSyncConfigResult(
    IReadOnlyList<PoSyncSource> Sources,
    IReadOnlyList<PoSyncConfigError> Errors,
    int GlobalIntervalMin,
    int? TickSec = null,
    int? StartupDelaySec = null,
    int? TimeoutSec = null);
