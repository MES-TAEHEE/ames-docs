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
    int?    TimeoutSec = null)
{
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
