using Microsoft.Data.SqlClient;

namespace AMES.Data.Services;

/// <summary>공통코드 4그룹(전역·SOURCE·URL·AUTH) → 소스 하나. 정본은 PoSync(SW_POSYNC*) — Worker 마다
/// 그룹 이름만 다르고 해석 규칙(전역 last-wins, 소스 중복 키 오류, URL/AUTH 중복 오류, 키 길이, CustomerID,
/// URL, 필수 파라미터)은 같아 여기로 뽑았다. WINDOW 는 다루지 않는다 — PoSync 처럼 필요한 Worker 가
/// Globals/Params 에서 직접 읽는다.</summary>
public sealed record WorkerCodeRow(string Group, string Value, string? Name, string? Attr1, string? Description, bool Use);

public sealed record WorkerSource(
    string Key,
    string Name,
    string CustomerId,
    string Url,
    string? AuthScheme,
    string? AuthValue,
    IReadOnlyDictionary<string, string> Params,
    int IntervalMin,
    int? TimeoutSec);

public sealed record WorkerConfigError(string Key, string Name, string Message);

public sealed record WorkerSourceConfigResult(
    IReadOnlyList<WorkerSource> Sources,
    IReadOnlyList<WorkerConfigError> Errors,
    int GlobalIntervalMin,
    IReadOnlyDictionary<string, string?> Globals,
    int? TickSec,
    int? StartupDelaySec,
    int? TimeoutSec);

public static class WorkerSourceConfig
{
    public const int MaxKeyLen = 13;   // Code + "-" + key <= SYS_InterfaceMonitor.InterfaceCode VARCHAR(20)

    /// <summary>ScheduledWorker 설정 그룹은 모두 "SW_" + Code 로 시작해 MD-26 에서 SW_ 로 한 번에 찾는다.</summary>
    public static string GroupGlobal(string code) => "SW_" + code; // + "_SOURCE" / "_URL" / "_AUTH"

    /// <summary>소스 키 → 모니터 InterfaceCode. 길이 초과 키(설정 오류로 들어온 것)도 컬럼에 들어가게 자른다.</summary>
    public static string InterfaceCodeFor(string code, string key)
        => code + "-" + (key.Length > MaxKeyLen ? key[..MaxKeyLen] : key);

    public static List<WorkerCodeRow> Load(SqlConnection conn, string code)
    {
        var groupGlobal = GroupGlobal(code);
        var groupSource = groupGlobal + "_SOURCE";
        var groupUrl    = groupGlobal + "_URL";
        var groupAuth   = groupGlobal + "_AUTH";

        const string sql = """
            SELECT GroupCode, CodeValue, CodeName, Attribute1, Description, ISNULL(UseFlag, 1) AS UseFlag
            FROM   dbo.MD_CodeItem
            WHERE  GroupCode IN (@G, @S, @U, @A)
            ORDER  BY GroupCode, ISNULL(SortOrder, 0), CodeValue;
            """;
        var rows = new List<WorkerCodeRow>();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@G", groupGlobal);
        cmd.Parameters.AddWithValue("@S", groupSource);
        cmd.Parameters.AddWithValue("@U", groupUrl);
        cmd.Parameters.AddWithValue("@A", groupAuth);
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            if (rdr["GroupCode"] is not string g || rdr["CodeValue"] is not string v) continue;
            rows.Add(new WorkerCodeRow(g, v, rdr["CodeName"] as string, rdr["Attribute1"] as string,
                rdr["Description"] as string, (bool)rdr["UseFlag"]));
        }
        return rows;
    }

    public static WorkerSourceConfigResult Resolve(IReadOnlyList<WorkerCodeRow> rows, string code, int defaultIntervalMin,
        params string[] requiredParams)
    {
        var groupGlobal = GroupGlobal(code);
        var groupSource = groupGlobal + "_SOURCE";
        var groupUrl    = groupGlobal + "_URL";
        var groupAuth   = groupGlobal + "_AUTH";

        // PK 는 CodeID 지 (GroupCode, CodeValue) 가 아니라 MD-26 에서 같은 키로 행을 2개 만들 수 있다.
        // ToDictionary 는 그 경우 ArgumentException 을 던져 전체 소스를 막으므로 last-wins 로 흡수한다.
        var globals = rows.Where(r => r.Group == groupGlobal && r.Use)
                          .GroupBy(r => r.Value.Trim(), StringComparer.OrdinalIgnoreCase)
                          .ToDictionary(g => g.Key, g => g.Last().Attr1, StringComparer.OrdinalIgnoreCase);
        int gInterval = globals.TryGetValue("INTERVAL", out var gi) && int.TryParse(gi?.Trim(), out var giv) && giv >= 0
            ? giv : defaultIntervalMin;
        // Worker 시간 설정 — 없으면 null 로 두고 Api 가 appsettings ScheduledWorker 공통 기본값을 쓴다.
        int? tickSec    = ParseInt(globals.GetValueOrDefault("TICK_SEC"));
        int? delaySec   = ParseInt(globals.GetValueOrDefault("STARTUP_DELAY_SEC"));
        int? timeoutSec = ParseInt(globals.GetValueOrDefault("TIMEOUT_SEC"));

        var (urls, dupUrlKeys)   = GroupLastWins(rows, groupUrl);
        var (auths, dupAuthKeys) = GroupLastWins(rows, groupAuth);

        var sources = new List<WorkerSource>();
        var errors  = new List<WorkerConfigError>();

        // 같은 키의 소스 행이 2개 이상이면 어느 쪽을 써야 할지 알 수 없다 —
        // last-wins 로 조용히 하나를 고르지 않고 둘 다 건너뛴 채 오류 하나만 남긴다.
        var sourceRows = rows.Where(r => r.Group == groupSource && r.Use).ToList();
        var dupSourceKeys = sourceRows
            .GroupBy(r => r.Value.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var dupKey in dupSourceKeys)
        {
            var first = sourceRows.First(r => r.Value.Trim().Equals(dupKey, StringComparison.OrdinalIgnoreCase));
            var name  = string.IsNullOrWhiteSpace(first.Name) ? dupKey : first.Name.Trim();
            errors.Add(new WorkerConfigError(dupKey, name, $"{groupSource} 에 같은 키 행이 2개 이상입니다"));
        }

        foreach (var s in sourceRows)
        {
            var key = s.Value.Trim();
            if (dupSourceKeys.Contains(key)) continue;

            var name = string.IsNullOrWhiteSpace(s.Name) ? key : s.Name.Trim();
            var problems = new List<string>();

            if (key.Length == 0 || key.Length > MaxKeyLen)
                problems.Add($"소스 키는 1~{MaxKeyLen}자여야 합니다");

            var cust = s.Attr1?.Trim();
            if (string.IsNullOrEmpty(cust)) problems.Add("Attribute1 에 CustomerID 가 없습니다");

            if (dupUrlKeys.Contains(key)) problems.Add($"{groupUrl} 에 같은 키 행이 2개 이상입니다");
            var url = urls.TryGetValue(key, out var u) ? u.Description?.Trim() : null;
            if (string.IsNullOrEmpty(url)) problems.Add($"{groupUrl} 에 URL 행이 없습니다");

            if (dupAuthKeys.Contains(key)) problems.Add($"{groupAuth} 에 같은 키 행이 2개 이상입니다");

            var p = ParseParams(s.Description);
            foreach (var req in requiredParams)
                if (!p.TryGetValue(req, out var val) || val.Length == 0)
                    problems.Add($"파라미터 {req} 가 없습니다");

            if (problems.Count > 0)
            {
                errors.Add(new WorkerConfigError(key, name, string.Join("; ", problems)));
                continue;
            }

            string? scheme = null, authVal = null;
            if (auths.TryGetValue(key, out var a))
            {
                scheme  = string.IsNullOrWhiteSpace(a.Attr1) ? null : a.Attr1.Trim();
                authVal = a.Description?.Trim();
            }

            sources.Add(new WorkerSource(key, name, cust!, url!, scheme, authVal, p, gInterval, timeoutSec));
        }

        return new WorkerSourceConfigResult(sources, errors, gInterval, globals, tickSec, delaySec, timeoutSec);
    }

    /// <summary>그룹 하나를 키(trim, 대소문자 무시)로 last-wins 딕셔너리로 묶고, 중복 키 집합을 같이 돌려준다.</summary>
    private static (Dictionary<string, WorkerCodeRow> Rows, HashSet<string> DupKeys) GroupLastWins(
        IReadOnlyList<WorkerCodeRow> rows, string group)
    {
        var byKey = rows.Where(r => r.Group == group && r.Use)
                        .GroupBy(r => r.Value.Trim(), StringComparer.OrdinalIgnoreCase)
                        .ToList();
        var dict = byKey.ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
        var dups = byKey.Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (dict, dups);
    }

    /// <summary>"키=값;키=값". 빈 조각·'=' 없는 조각은 무시. 키 대소문자 무시.</summary>
    public static Dictionary<string, string> ParseParams(string? s)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(s)) return d;
        foreach (var part in s.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) continue;
            var k = part[..eq].Trim();
            var v = part[(eq + 1)..].Trim();
            if (k.Length > 0) d[k] = v;
        }
        return d;
    }

    private static int? ParseInt(string? s) => int.TryParse(s?.Trim(), out var v) ? v : null;
}
