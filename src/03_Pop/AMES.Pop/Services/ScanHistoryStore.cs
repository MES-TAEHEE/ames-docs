using AMES.Data.Services;

namespace AMES.Pop.Services;

/// <summary>INJ-MAIN 「오늘 스캔 이력」 한 줄 — 성공·실패 모두(실패 스캔은 DB 어디에도 남지 않는다).</summary>
internal sealed class ScanHistoryEntry
{
    public string LotCode { get; set; } = "";
    public DateTime At { get; set; }
    public bool Ok { get; set; }
    public string Message { get; set; } = "";
}

/// <summary>
/// INJ-MAIN 오늘 스캔 이력을 터미널에 남긴다(10-29 INJ-MAIN-TC-019) — 전에는 화면 메모리뿐이라 다시 열거나 다시 로그인하면 사라지고,
/// 하루가 바뀌어도 전일 건이 남았다. 라인·전기일(07시 경계, <see cref="PopDay"/>)별로 최근 <see cref="Max"/> 건만 두고,
/// 다른 날 기록은 저장할 때 버린다. 오프라인에서도 같다.
/// </summary>
internal static class ScanHistoryStore
{
    public const int Max = 50;
    internal const string FileName = "scan-history.bin";
    private static readonly object Gate = new();

    private sealed class Data
    {
        public Dictionary<string, List<ScanHistoryEntry>> Lines { get; set; } = new();
    }

    private static string Key(string lineId, DateTime prodDay) => $"{lineId.ToUpperInvariant()}|{prodDay:yyyy-MM-dd}";

    /// <summary>이 라인의 오늘(전기일) 이력 — 최신순.</summary>
    public static List<ScanHistoryEntry> Load(string lineId) => Load(lineId, PopDay.Today);

    internal static List<ScanHistoryEntry> Load(string lineId, DateTime prodDay)
    {
        lock (Gate)
        {
            var data = OfflineStore.Load<Data>(FileName);
            return data is not null && data.Lines.TryGetValue(Key(lineId, prodDay), out var list) ? list.Take(Max).ToList() : new();
        }
    }

    /// <summary>맨 앞에 더하고 최근 <see cref="Max"/> 건만 남긴다. 저장 실패는 화면 동작을 막지 않는다 — 기록만.</summary>
    public static List<ScanHistoryEntry> Add(string lineId, ScanHistoryEntry entry) => Add(lineId, entry, PopDay.Today);

    internal static List<ScanHistoryEntry> Add(string lineId, ScanHistoryEntry entry, DateTime prodDay)
    {
        lock (Gate)
        {
            var key  = Key(lineId, prodDay);
            var data = OfflineStore.Load<Data>(FileName) ?? new Data();
            var today = $"|{prodDay:yyyy-MM-dd}";
            // 오늘이 아닌 기록은 버린다 — 파일이 계속 커지지 않게
            data.Lines = data.Lines.Where(kv => kv.Key.EndsWith(today, StringComparison.Ordinal))
                                   .ToDictionary(kv => kv.Key, kv => kv.Value);
            var list = data.Lines.TryGetValue(key, out var l) ? l : new();
            list.Insert(0, entry);
            if (list.Count > Max) list.RemoveRange(Max, list.Count - Max);
            data.Lines[key] = list;
            try { OfflineStore.Save(FileName, data); }
            catch (Exception ex) { AMES.Pop.Common.PopLog.Append("scan", "ScanHistory", $"save failed: {ex.Message}"); }
            return list.ToList();
        }
    }
}
