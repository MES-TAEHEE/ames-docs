using Microsoft.Data.SqlClient;
using AMES.Data.Services;

namespace AMES.Data.Services.DemandPlan;

/// <summary>공통코드 4그룹 SW_DPSYNC(전역) · SW_DPSYNC_SOURCE · SW_DPSYNC_URL · SW_DPSYNC_AUTH → <see cref="DemandPlanSource"/> 목록.
/// 해석 규칙은 PoSync 와 공용(<see cref="WorkerSourceConfig"/>) — WINDOW 는 다루지 않는다.</summary>
public sealed record DemandPlanSource(
    string Key,
    string Name,
    string CustomerId,
    string Url,
    string? AuthScheme,
    string? AuthValue,
    string CorCd,
    string BizCd,
    string VendCd,
    string PurcOrg,
    int IntervalMin,
    int? TimeoutSec = null)
{
    public string InterfaceCode => DemandPlanConfig.InterfaceCodeFor(Key);
}

public sealed record DemandPlanConfigResult(
    IReadOnlyList<DemandPlanSource> Sources,
    IReadOnlyList<WorkerConfigError> Errors,
    int GlobalIntervalMin,
    int? TickSec = null,
    int? StartupDelaySec = null,
    int? TimeoutSec = null);

public static class DemandPlanConfig
{
    /// <summary>모니터 행 접두어. Worker 의 Code 와 소스 행 InterfaceCode 가 모두 이 값을 쓴다.</summary>
    public const string MonitorCode = "DPSYNC";
    public const int DefaultIntervalMin = 60;

    public const string GroupGlobal = "SW_" + MonitorCode;
    public const string GroupSource = GroupGlobal + "_SOURCE";
    public const string GroupUrl    = GroupGlobal + "_URL";
    public const string GroupAuth   = GroupGlobal + "_AUTH";

    public static string InterfaceCodeFor(string key) => WorkerSourceConfig.InterfaceCodeFor(MonitorCode, key);

    /// <summary>캐시하지 않는다 — MD-26 에서 고친 값이 다음 틱부터 바로 반영돼야 한다.</summary>
    public static DemandPlanConfigResult Load(SqlConnection conn)
    {
        var rows = WorkerSourceConfig.Load(conn, MonitorCode);
        return Resolve(rows);
    }

    public static DemandPlanConfigResult Resolve(IReadOnlyList<WorkerCodeRow> rows)
    {
        var w = WorkerSourceConfig.Resolve(rows, MonitorCode, DefaultIntervalMin, "CORCD", "BIZCD", "VENDCD", "PURC_ORG");

        // 고객당 소스는 1개 — 2개 이상이면 WINDOW 가 서로 덮어써 조회 구간이 흔들리므로 전부 설정 오류로 뺀다.
        var errors = new List<WorkerConfigError>(w.Errors);
        var dupGroups = w.Sources
            .GroupBy(s => s.CustomerId.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();
        var dupKeys = dupGroups.SelectMany(g => g).Select(s => s.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var g in dupGroups)
        {
            var keys = g.Select(s => s.Key).ToList();
            var keysJoined = string.Join(", ", keys);
            foreach (var s in g)
                errors.Add(new WorkerConfigError(s.Key, s.Name,
                    $"고객 {g.Key} 에 DPSYNC 소스가 2개 이상입니다 ({keysJoined}) — 고객당 1개만 허용"));
        }

        var sources = w.Sources.Where(s => !dupKeys.Contains(s.Key)).Select(s => new DemandPlanSource(
            s.Key, s.Name, s.CustomerId, s.Url, s.AuthScheme, s.AuthValue,
            s.Params["CORCD"], s.Params["BIZCD"], s.Params["VENDCD"], s.Params["PURC_ORG"],
            s.IntervalMin, s.TimeoutSec)).ToList();
        return new DemandPlanConfigResult(sources, errors, w.GlobalIntervalMin, w.TickSec, w.StartupDelaySec, w.TimeoutSec);
    }
}
