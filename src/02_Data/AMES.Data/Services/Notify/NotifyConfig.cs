using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Services.Notify;

/// <summary>PUSH 발송기 설정 — 공통코드 SW_NOTIFY(CodeValue → Attribute1). 캐시하지 않는다(MD-26 에서 고치면 다음 틱부터).</summary>
public sealed record NotifyConfigResult(
    int IntervalMin,      // INTERVAL — 감지·발송 주기(분), 0 = 스케줄러 중지(기본 1)
    int LookbackMin,      // LOOKBACK_MIN — 고장·안돈·QC 홀드를 「새로 생긴 것」으로 볼 시간(분, 기본 60, 1~1440)
    int MaxRetry,         // MAX_RETRY — 실패 재시도 횟수(기본 3, 0~10)
    int BatchSize,        // BATCH — 한 번에 보내는 최대 건수(기본 100, 1~500)
    int? TickSec,
    int? StartupDelaySec);

public static class NotifyConfig
{
    public const int DefaultIntervalMin = 1;
    public const int DefaultLookbackMin = 60;
    public const int DefaultMaxRetry    = 3;
    public const int DefaultBatchSize   = 100;

    public static NotifyConfigResult Load(SqlConnection conn)
    {
        const string sql = """
            SELECT CodeValue, Attribute1
            FROM   dbo.MD_CodeItem
            WHERE  GroupCode = @G AND ISNULL(UseFlag, 1) = 1
            ORDER  BY ISNULL(SortOrder, 0), CodeValue;
            """;
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        using var cmd = new SqlCommand(sql, conn);
        ColumnParams.AddFilter(cmd, "@G", "MD_CodeItem.GroupCode", NotifyRules.GroupGlobal);
        using (var rdr = cmd.ExecuteReader())
            while (rdr.Read())
                if (rdr["CodeValue"] is string v) values[v.Trim()] = rdr["Attribute1"] as string;   // 같은 키가 둘이면 뒤 행
        return Resolve(values);
    }

    public static NotifyConfigResult Resolve(IReadOnlyDictionary<string, string?> v) => new(
        Pick(v, "INTERVAL", DefaultIntervalMin, 0, 1440),
        Pick(v, "LOOKBACK_MIN", DefaultLookbackMin, 1, 1440),
        Pick(v, "MAX_RETRY", DefaultMaxRetry, 0, 10),
        Pick(v, "BATCH", DefaultBatchSize, 1, 500),
        Opt(v, "TICK_SEC"),
        Opt(v, "STARTUP_DELAY_SEC"));

    private static int Pick(IReadOnlyDictionary<string, string?> v, string key, int def, int min, int max)
        => v.TryGetValue(key, out var s) && int.TryParse(s?.Trim(), out var n) && n >= min && n <= max ? n : def;

    private static int? Opt(IReadOnlyDictionary<string, string?> v, string key)
        => v.TryGetValue(key, out var s) && int.TryParse(s?.Trim(), out var n) ? n : null;
}
