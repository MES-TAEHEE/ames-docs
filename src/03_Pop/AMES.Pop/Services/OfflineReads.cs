using System.Text.Json;
using AMES.Data.Connection;
using AMES.Pop.Common;

namespace AMES.Pop.Services;

internal sealed class OfflineReadFile
{
    /// <summary>조회 키 → 마지막으로 온라인에서 읽은 결과(JSON).</summary>
    public Dictionary<string, string> Values { get; set; } = new();
    public Dictionary<string, DateTime> TakenAtUtc { get; set; } = new();
}

/// <summary>
/// 안돈·재작업·QC 화면의 조회 사본(10-28 사용자 결정 — 이 화면들도 DB 가 끊겨도 쓴다). 온라인 조회 결과를 키별로 남겨 두고,
/// DB 가 끊겼거나 이번 조회가 연결 오류로 실패하면 남겨 둔 값을 돌려준다(없으면 빈 값). 디스크에는 1분에 한 번만 쓴다.
/// 오프라인에서 한 작업의 효과는 각 모듈(OfflineAndon·OfflineRework·OfflineQc)이 큐를 보고 이 값 위에 얹는다.
/// </summary>
internal static class OfflineReads
{
    private const string FileName = "read-cache.bin";
    private static readonly object Gate = new();
    private static OfflineReadFile? _file;
    private static bool _dirty;
    private static long _lastFlushTicks;
    private static readonly Dictionary<string, long> _lastWarmTicks = new();
    private static int _warming;

    internal static void ResetForTests() { lock (Gate) { _file = null; _dirty = false; _lastWarmTicks.Clear(); } }

    // Gate 를 잡고 부른다.
    private static OfflineReadFile File => _file ??= OfflineStore.Load<OfflineReadFile>(FileName) ?? new();

    /// <summary>온라인이면 읽어서 남기고, 끊겼으면(또는 연결 오류면) 남겨 둔 값 — 없으면 fallback.</summary>
    public static T Get<T>(string key, Func<T> online, T fallback)
    {
        if (!PopDb.IsOffline)
        {
            try
            {
                var v = online();
                Put(key, v);
                return v;
            }
            catch (Exception ex) when (DbHealth.IsConnectionFailure(ex)) { /* 사본으로 */ }
        }
        return Cached(key, fallback);
    }

    public static T Cached<T>(string key, T fallback)
    {
        lock (Gate)
        {
            if (!File.Values.TryGetValue(key, out var json)) return fallback;
            try { return JsonSerializer.Deserialize<T>(json) ?? fallback; }
            catch (JsonException) { return fallback; }
        }
    }

    public static bool Has(string key) { lock (Gate) return File.Values.ContainsKey(key); }

    public static DateTime? TakenAt(string key)
    {
        lock (Gate) return File.TakenAtUtc.TryGetValue(key, out var t) ? t.ToLocalTime() : null;
    }

    public static void Put<T>(string key, T value)
    {
        var json = JsonSerializer.Serialize(value);
        lock (Gate)
        {
            if (File.Values.TryGetValue(key, out var old) && old == json) { File.TakenAtUtc[key] = DateTime.UtcNow; return; }
            File.Values[key] = json;
            File.TakenAtUtc[key] = DateTime.UtcNow;
            _dirty = true;
        }
        FlushIfDirty();
    }

    public static void FlushIfDirty(bool force = false)
    {
        lock (Gate)
        {
            if (!_dirty) return;
            if (!force && DateTime.UtcNow.Ticks - _lastFlushTicks < TimeSpan.FromMinutes(1).Ticks) return;
            try { OfflineStore.Save(FileName, File); _dirty = false; _lastFlushTicks = DateTime.UtcNow.Ticks; }
            catch (Exception ex) { PopLog.Append("offline", "OfflineReads", $"save failed: {ex.Message}"); }
        }
    }

    /// <summary>로그인 중 아직 열지 않은 화면도 오프라인에서 보이게 미리 읽어 둔다 — 모듈마다 설정 주기(분)에 한 번, 백그라운드.</summary>
    public static void WarmInBackground(string module, Action warm)
    {
        if (PopDb.IsOffline) return;
        var now = DateTime.UtcNow.Ticks;
        int minutes;
        try { minutes = AppConfig.Current.OfflineRefreshMinutes; } catch { minutes = 10; }
        lock (Gate)
        {
            if (_lastWarmTicks.TryGetValue(module, out var last) && now - last < TimeSpan.FromMinutes(minutes).Ticks) return;
            _lastWarmTicks[module] = now;
        }
        if (Interlocked.CompareExchange(ref _warming, 1, 0) != 0) return;
        _ = Task.Run(() =>
        {
            try { warm(); FlushIfDirty(force: true); }
            catch (Exception ex) { PopLog.Append("offline", "OfflineReads", $"warm {module} failed: {ex.Message}"); }
            finally { Interlocked.Exchange(ref _warming, 0); }
        });
    }
}
