using System.Text.RegularExpressions;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Web.Components.Pages.Display;

/// <summary>
/// 공장 디스플레이 — 사출기 금형 Shot 현황 조회(읽기 전용). 사출 라인(MD_WorkCenter.ProcessCode='INJ', 활성)마다 타일 1개 —
/// 조회할 때마다 MD_Line 을 다시 읽으므로 라인·사출기를 등록하면 다음 갱신에 자동으로 추가된다. 대표 사출기의 장착 금형과 "현재 Shot / 최대 Shot". 장착 금형은 MNT_EquipmentStatus.MountedMoldID, 비어 있으면 그 사출기의
/// 가장 최근 사출 LOT(PR_InjLot) 금형. Shot 은 MD_Mold.CurrentShots(장착 후 타수 — InjAgent 가 사출마다 +1, 교체 시 0) / RatedShots.
/// 디스플레이 여러 대가 같은 주기로 부르므로 결과를 CacheSec 동안 공유한다(DB 조회 1회).
/// </summary>
public static partial class InjShotQuery
{
    public sealed record Tile(
        string Code, string LineId, string? LineName, string? EquipId, string? EquipName,
        int? Tonnage, string Status, string? MoldId, string? MoldName, bool MoldFromLastLot,
        int? CurrentShots, int? RatedShots, decimal? Percent);

    const int CacheSec = 5;
    static readonly SemaphoreSlim Gate = new(1, 1);
    static IReadOnlyList<Tile>? _cached;
    static DateTime _cachedAt;

    public static async Task<IReadOnlyList<Tile>> LoadAsync(AmesConnectionFactory factory, CancellationToken ct = default)
    {
        if (_cached is { } c && DateTime.UtcNow - _cachedAt < TimeSpan.FromSeconds(CacheSec)) return c;
        await Gate.WaitAsync(ct);
        try
        {
            if (_cached is { } c2 && DateTime.UtcNow - _cachedAt < TimeSpan.FromSeconds(CacheSec)) return c2;
            _cached = await QueryAsync(factory, ct);
            _cachedAt = DateTime.UtcNow;
            return _cached;
        }
        finally { Gate.Release(); }
    }

    static async Task<IReadOnlyList<Tile>> QueryAsync(AmesConnectionFactory factory, CancellationToken ct)
    {
        const string sql = """
            SELECT l.LineID, l.LineName, l.LotPrefix,
                   e.EquipID, e.EquipName, e.MakerModel,
                   st.Status AS EquipStatus,
                   COALESCE(st.MountedMoldID, lastLot.MoldID) AS MoldID,
                   CAST(CASE WHEN st.MountedMoldID IS NULL AND lastLot.MoldID IS NOT NULL THEN 1 ELSE 0 END AS bit) AS FromLastLot,
                   m.MoldName, m.RatedShots, m.CurrentShots, m.Tonnage AS MoldTonnage, l.LineNameEn, lastLot.LotID AS LastLotID
            FROM dbo.MD_Line l
            JOIN dbo.MD_WorkCenter w ON w.WCID = l.WCID AND w.ProcessCode = 'INJ'
            LEFT JOIN dbo.MD_Equipment e
                   ON e.LineID = l.LineID AND ISNULL(e.ActiveFlag, 1) = 1 AND e.EquipType LIKE 'INJ%'
            OUTER APPLY (SELECT TOP 1 s.Status, s.MountedMoldID
                         FROM dbo.MNT_EquipmentStatus s
                         WHERE s.EquipID = e.EquipID
                         ORDER BY s.ModifiedTS DESC, s.EquipStatusID DESC) st
            OUTER APPLY (SELECT TOP 1 p.MoldID, p.LotID
                         FROM dbo.PR_InjLot p
                         WHERE p.EquipID = e.EquipID AND p.MoldID IS NOT NULL
                         ORDER BY p.LotID DESC) lastLot
            LEFT JOIN dbo.MD_Mold m ON m.MoldID = COALESCE(st.MountedMoldID, lastLot.MoldID)
            WHERE ISNULL(l.Status, 'ACTIVE') = 'ACTIVE'
            ORDER BY l.LineID, e.EquipID;
            """;
        await using var conn = factory.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 15 };
        await using var r = await cmd.ExecuteReaderAsync(ct);

        var rows = new List<(string Line, string? LineName, string? Prefix, string? Equip, string? EquipName, string? Model,
            string? Status, string? MoldId, bool FromLastLot, string? MoldName, int? Rated, int? Current, int? MoldTon, string? LineNameEn, long? LastLotId)>();
        while (await r.ReadAsync(ct))
            rows.Add((r.GetString(0), Str(r, 1), Str(r, 2), Str(r, 3), Str(r, 4), Str(r, 5),
                Str(r, 6), Str(r, 7), r.GetBoolean(8), Str(r, 9), Int(r, 10), Int(r, 11), Int(r, 12), Str(r, 13), r.IsDBNull(14) ? null : Convert.ToInt64(r.GetValue(14))));

        // 라인당 타일 1개 — 사출기가 여러 대면 대표 1대: 장착 금형 기록 → 가장 최근 사출(LOT) → 설비 코드 순
        var tiles = new List<Tile>();
        foreach (var line in rows.GroupBy(x => x.Line))
        {
            var x = line
                .OrderByDescending(m => m.MoldId is not null && !m.FromLastLot)
                .ThenByDescending(m => m.LastLotId ?? 0)
                .ThenBy(m => m.Equip is null)
                .ThenBy(m => m.Equip, StringComparer.Ordinal)
                .First();
            tiles.Add(new Tile(
                TileCode(x.Line, x.Prefix), x.Line,
                string.IsNullOrWhiteSpace(x.LineNameEn) ? x.LineName : x.LineNameEn,   // 디스플레이 표시 언어는 영어
                x.Equip, x.EquipName,
                Tonnage(x.EquipName) ?? Tonnage(x.Model) ?? Tonnage(x.LineNameEn) ?? Tonnage(x.LineName) ?? x.MoldTon,
                x.Equip is null ? "NONE" : NormalizeStatus(x.Status),
                x.MoldId, x.MoldName, x.FromLastLot, x.Current, x.Rated, Percent(x.Current, x.Rated)));
        }
        return tiles;
    }

    /// <summary>타일 코드 = 라인 LOT 접두어(I1…). 접두어가 없으면 라인 ID 끝 번호.</summary>
    public static string TileCode(string lineId, string? lotPrefix)
        => !string.IsNullOrWhiteSpace(lotPrefix) ? lotPrefix.Trim()
            : TrailingNumber().Match(lineId) is { Success: true } m ? "I" + int.Parse(m.Value) : lineId;

    /// <summary>설비·라인 이름의 "650T"·"(1800T)" 에서 톤수를 읽는다.</summary>
    public static int? Tonnage(string? text)
        => text is not null && TonnageRx().Match(text) is { Success: true } m ? int.Parse(m.Groups[1].Value) : null;

    /// <summary>설비 상태 → 화면 상태 RUN / IDLE / STOP / PM / OFF(기록 없음). 사출기 미등록 라인은 NONE.</summary>
    public static string NormalizeStatus(string? status) => (status ?? "").Trim().ToUpperInvariant() switch
    {
        "RUN" or "RUNNING" or "ACTIVE" => "RUN",
        "IDLE" or "WAIT" or "SETUP" => "IDLE",
        "STOP" or "STOPPED" or "DOWN" or "FAULT" or "ERROR" or "ALARM" or "BREAKDOWN" => "STOP",
        "PM" or "MAINT" or "MAINTENANCE" => "PM",
        _ => "OFF",
    };

    public static decimal? Percent(int? current, int? rated)
        => current is { } c && rated is > 0 ? Math.Round(c * 100m / rated.Value, 1) : null;

    static string? Str(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetValue(i).ToString();
    static int? Int(SqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i));

    [GeneratedRegex(@"(\d{3,4})\s*T\b", RegexOptions.IgnoreCase)]
    private static partial Regex TonnageRx();

    [GeneratedRegex(@"\d+$")]
    private static partial Regex TrailingNumber();
}
