using Microsoft.Data.SqlClient;

namespace AMES.Data.Services.PoSync;

/// <summary>
/// 공통코드 SW_POSYNC(전역) · SW_POSYNC_SOURCE · SW_POSYNC_URL · SW_POSYNC_AUTH → <see cref="PoSyncSource"/> 목록.
/// 캐시하지 않는다 — MD-26 에서 고친 값이 다음 틱부터 바로 반영돼야 한다(ProdCalendar 와 같은 원칙).
/// 설정이 깨진 소스는 예외가 아니라 <see cref="PoSyncConfigError"/> 로 돌려 다른 소스를 막지 않는다.
/// </summary>
public static class PoSyncConfig
{
    public const int DefaultIntervalMin = 30;
    public const int DefaultWindowFrom  = -60;
    public const int DefaultWindowTo    = 0;
    /// <summary>모니터 행 접두어. Worker 의 Code 와 소스 행 InterfaceCode 가 모두 이 값을 쓴다 — 어긋나면 모니터 행이 갈라진다.</summary>
    public const string MonitorCode     = "POSYNC";
    public const int MaxKeyLen          = 13;   // MonitorCode + "-" + key <= SYS_InterfaceMonitor.InterfaceCode VARCHAR(20)

    /// <summary>
    /// 공통코드 그룹. ScheduledWorker 설정 그룹은 모두 "SW_" + Worker Code 로 시작해 MD-26 에서 SW_ 로 한 번에 찾는다.
    /// MD_CodeGroup.GroupCode VARCHAR(20) 이라 가장 긴 _SOURCE 까지 들어가야 한다.
    /// </summary>
    public const string GroupGlobal     = "SW_" + MonitorCode;
    public const string GroupSource     = GroupGlobal + "_SOURCE";
    public const string GroupUrl        = GroupGlobal + "_URL";
    public const string GroupAuth       = GroupGlobal + "_AUTH";

    /// <summary>소스 키 → 모니터 InterfaceCode. 길이 초과 키(설정 오류로 들어온 것)도 컬럼에 들어가게 자른다.</summary>
    public static string InterfaceCodeFor(string key)
        => MonitorCode + "-" + (key.Length > MaxKeyLen ? key[..MaxKeyLen] : key);

    public sealed record CodeRow(string Group, string Value, string? Name, string? Attr1, string? Description, bool Use);

    public static PoSyncConfigResult Load(SqlConnection conn)
    {
        const string sql = """
            SELECT GroupCode, CodeValue, CodeName, Attribute1, Description, ISNULL(UseFlag, 1) AS UseFlag
            FROM   dbo.MD_CodeItem
            WHERE  GroupCode IN (@G, @S, @U, @A)
            ORDER  BY GroupCode, ISNULL(SortOrder, 0), CodeValue;
            """;
        var rows = new List<CodeRow>();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@G", GroupGlobal);
        cmd.Parameters.AddWithValue("@S", GroupSource);
        cmd.Parameters.AddWithValue("@U", GroupUrl);
        cmd.Parameters.AddWithValue("@A", GroupAuth);
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            if (rdr["GroupCode"] is not string g || rdr["CodeValue"] is not string v) continue;
            rows.Add(new CodeRow(g, v, rdr["CodeName"] as string, rdr["Attribute1"] as string,
                rdr["Description"] as string, (bool)rdr["UseFlag"]));
        }
        return Resolve(rows);
    }

    public static PoSyncConfigResult Resolve(IReadOnlyList<CodeRow> rows)
    {
        // PK 는 CodeID 지 (GroupCode, CodeValue) 가 아니라 MD-26 에서 같은 키로 행을 2개 만들 수 있다.
        // ToDictionary 는 그 경우 ArgumentException 을 던져 전체 소스를 막으므로 last-wins 로 흡수한다.
        var globals = rows.Where(r => r.Group == GroupGlobal && r.Use)
                          .GroupBy(r => r.Value.Trim(), StringComparer.OrdinalIgnoreCase)
                          .ToDictionary(g => g.Key, g => g.Last().Attr1, StringComparer.OrdinalIgnoreCase);
        int gInterval = globals.TryGetValue("INTERVAL", out var gi) && int.TryParse(gi?.Trim(), out var giv) && giv >= 0
            ? giv : DefaultIntervalMin;
        if (!TryParseWindow(globals.GetValueOrDefault("WINDOW"), out int gFrom, out int gTo))
            (gFrom, gTo) = (DefaultWindowFrom, DefaultWindowTo);
        // Worker 시간 설정 — 없으면 null 로 두고 Api 가 appsettings ScheduledWorker 공통 기본값을 쓴다.
        int? tickSec    = ParseInt(globals.GetValueOrDefault("TICK_SEC"));
        int? delaySec   = ParseInt(globals.GetValueOrDefault("STARTUP_DELAY_SEC"));
        int? timeoutSec = ParseInt(globals.GetValueOrDefault("TIMEOUT_SEC"));

        var (urls, dupUrlKeys)   = GroupLastWins(rows, GroupUrl);
        var (auths, dupAuthKeys) = GroupLastWins(rows, GroupAuth);

        var sources = new List<PoSyncSource>();
        var errors  = new List<PoSyncConfigError>();

        // 같은 키의 소스 행이 2개 이상이면 어느 쪽을 써야 할지 알 수 없다 —
        // last-wins 로 조용히 하나를 고르지 않고 둘 다 건너뛴 채 오류 하나만 남긴다.
        var sourceRows = rows.Where(r => r.Group == GroupSource && r.Use).ToList();
        var dupSourceKeys = sourceRows
            .GroupBy(r => r.Value.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var dupKey in dupSourceKeys)
        {
            var first = sourceRows.First(r => r.Value.Trim().Equals(dupKey, StringComparison.OrdinalIgnoreCase));
            var name  = string.IsNullOrWhiteSpace(first.Name) ? dupKey : first.Name.Trim();
            errors.Add(new PoSyncConfigError(dupKey, name, $"{GroupSource} 에 같은 키 행이 2개 이상입니다"));
        }

        foreach (var s in sourceRows)
        {
            var key  = s.Value.Trim();
            if (dupSourceKeys.Contains(key)) continue;

            var name = string.IsNullOrWhiteSpace(s.Name) ? key : s.Name.Trim();
            var problems = new List<string>();

            if (key.Length == 0 || key.Length > MaxKeyLen)
                problems.Add($"소스 키는 1~{MaxKeyLen}자여야 합니다");

            var cust = s.Attr1?.Trim();
            if (string.IsNullOrEmpty(cust)) problems.Add("Attribute1 에 CustomerID 가 없습니다");

            if (dupUrlKeys.Contains(key)) problems.Add($"{GroupUrl} 에 같은 키 행이 2개 이상입니다");
            var url = urls.TryGetValue(key, out var u) ? u.Description?.Trim() : null;
            if (string.IsNullOrEmpty(url)) problems.Add($"{GroupUrl} 에 URL 행이 없습니다");

            if (dupAuthKeys.Contains(key)) problems.Add($"{GroupAuth} 에 같은 키 행이 2개 이상입니다");

            var p = ParseParams(s.Description);
            foreach (var req in new[] { "CORCD", "BIZCD", "VENDCD", "PURC_ORG" })
                if (!p.TryGetValue(req, out var val) || val.Length == 0)
                    problems.Add($"파라미터 {req} 가 없습니다");

            if (problems.Count > 0)
            {
                errors.Add(new PoSyncConfigError(key, name, string.Join("; ", problems)));
                continue;
            }

            int interval = p.TryGetValue("INTERVAL", out var iv) && int.TryParse(iv, out var ivn) && ivn >= 0 ? ivn : gInterval;
            if (!TryParseWindow(p.GetValueOrDefault("WINDOW"), out int from, out int to)) (from, to) = (gFrom, gTo);

            string? scheme = null, authVal = null;
            if (auths.TryGetValue(key, out var a))
            {
                scheme  = string.IsNullOrWhiteSpace(a.Attr1) ? null : a.Attr1.Trim();
                authVal = a.Description?.Trim();
            }

            sources.Add(new PoSyncSource(key, name, cust!, url!, scheme, authVal,
                p["CORCD"], p["BIZCD"], p["VENDCD"], p["PURC_ORG"],
                interval, from, to, timeoutSec));
        }

        return new PoSyncConfigResult(sources, errors, gInterval, tickSec, delaySec, timeoutSec);
    }

    /// <summary>그룹 하나를 키(trim, 대소문자 무시)로 last-wins 딕셔너리로 묶고, 중복 키 집합을 같이 돌려준다.</summary>
    private static (Dictionary<string, CodeRow> Rows, HashSet<string> DupKeys) GroupLastWins(
        IReadOnlyList<CodeRow> rows, string group)
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

    /// <summary>"-N,M" → (from, to). from &lt;= to 여야 한다.</summary>
    public static bool TryParseWindow(string? s, out int from, out int to)
    {
        from = 0; to = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var parts = s.Split(',');
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0].Trim(), out from) || !int.TryParse(parts[1].Trim(), out to)) return false;
        return from <= to;
    }
}
