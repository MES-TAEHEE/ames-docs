using Microsoft.Data.SqlClient;
using AMES.Data.Services;

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

    /// <summary>
    /// Web(PP-002) → Api 수동 실행 서비스 키. GroupAuth 의 예약 행이며 값은 다른 인증 행처럼 Description 에 둔다.
    /// CodeValue 가 MaxKeyLen 보다 길어 소스 키와 겹칠 수 없다. 행이 없거나 꺼져 있으면 서비스 키 경로는 닫힌다.
    /// </summary>
    public const string ServiceKeyCode   = "AMES_SERVICE_KEY";
    public const string ServiceKeyHeader = "X-AMES-Service-Key";
    public const int MinServiceKeyLen    = 16;

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
        var wrows = rows.Select(r => new WorkerCodeRow(r.Group, r.Value, r.Name, r.Attr1, r.Description, r.Use)).ToList();
        var w = WorkerSourceConfig.Resolve(wrows, MonitorCode, DefaultIntervalMin, "CORCD", "BIZCD", "VENDCD", "PURC_ORG");
        if (!TryParseWindow(w.Globals.GetValueOrDefault("WINDOW"), out int gFrom, out int gTo)) (gFrom, gTo) = (DefaultWindowFrom, DefaultWindowTo);
        var sources = w.Sources.Select(s =>
        {
            if (!TryParseWindow(s.Params.GetValueOrDefault("WINDOW"), out int from, out int to)) (from, to) = (gFrom, gTo);
            return new PoSyncSource(s.Key, s.Name, s.CustomerId, s.Url, s.AuthScheme, s.AuthValue,
                s.Params["CORCD"], s.Params["BIZCD"], s.Params["VENDCD"], s.Params["PURC_ORG"], w.GlobalIntervalMin, from, to, w.TimeoutSec);
        }).ToList();
        var errors = w.Errors.Select(e => new PoSyncConfigError(e.Key, e.Name, e.Message)).ToList();
        return new PoSyncConfigResult(sources, errors, w.GlobalIntervalMin, w.TickSec, w.StartupDelaySec, w.TimeoutSec);
    }

    /// <summary>캐시하지 않는다 — MD-26 에서 키를 바꾸면 다음 요청부터 반영된다(수동 실행은 드물다).</summary>
    public static string? LoadServiceKey(SqlConnection conn)
    {
        const string sql = """
            SELECT CodeValue, Description, ISNULL(UseFlag, 1) AS UseFlag
            FROM   dbo.MD_CodeItem
            WHERE  GroupCode = @A AND CodeValue = @V;
            """;
        var rows = new List<CodeRow>();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@A", GroupAuth);
        cmd.Parameters.AddWithValue("@V", ServiceKeyCode);
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
            rows.Add(new CodeRow(GroupAuth, (string)rdr["CodeValue"], null, null,
                rdr["Description"] as string, (bool)rdr["UseFlag"]));
        return ResolveServiceKey(rows);
    }

    public static string? ResolveServiceKey(IReadOnlyList<CodeRow> rows)
    {
        var hits = rows.Where(r => r.Group == GroupAuth && r.Use
                                && r.Value.Trim().Equals(ServiceKeyCode, StringComparison.OrdinalIgnoreCase))
                       .ToList();
        if (hits.Count != 1) return null;
        var key = hits[0].Description?.Trim();
        return key is { Length: >= MinServiceKeyLen } ? key : null;
    }

    public static bool ServiceKeyMatches(string? configured, string? presented)
    {
        if (string.IsNullOrEmpty(configured) || string.IsNullOrEmpty(presented)) return false;
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(configured), System.Text.Encoding.UTF8.GetBytes(presented));
    }

    /// <summary>"키=값;키=값". 빈 조각·'=' 없는 조각은 무시. 키 대소문자 무시.</summary>
    public static Dictionary<string, string> ParseParams(string? s) => WorkerSourceConfig.ParseParams(s);

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
