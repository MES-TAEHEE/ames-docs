using AMES.Pop.Services;
using Xunit;

namespace AMES.Pop.Tests;

/// <summary>INJ-MAIN 오늘 스캔 이력(10-29 INJ-MAIN-TC-019) — 다시 열어도 남고, 최근 50건, 전일·다른 라인 제외.</summary>
[Collection("OfflineAuth")]
public sealed class ScanHistoryStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ames-scanhist-" + Guid.NewGuid().ToString("N"));
    private readonly string _origDir = OfflineStore.Dir;
    private static readonly DateTime Today = new(2026, 10, 29);

    public ScanHistoryStoreTests()
    {
        Directory.CreateDirectory(_dir);
        OfflineStore.Dir = _dir;
    }

    public void Dispose()
    {
        OfflineStore.Dir = _origDir;
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static ScanHistoryEntry E(int n, bool ok = true) => new() { LotCode = $"L{n:000}", At = Today.AddHours(8).AddSeconds(n), Ok = ok };

    [Fact]
    public void History_survives_reopening_and_keeps_the_newest_fifty()
    {
        for (var i = 1; i <= 51; i++) ScanHistoryStore.Add("LINE-INJ-01", E(i, ok: i % 2 == 0), Today);

        var list = ScanHistoryStore.Load("LINE-INJ-01", Today);   // 화면을 다시 연 것과 같다 — 파일에서 읽는다

        Assert.Equal(50, list.Count);
        Assert.Equal("L051", list[0].LotCode);                     // 최신순
        Assert.Equal("L002", list[^1].LotCode);                    // 가장 오래된 1건(L001)은 밀려남
        Assert.False(list[0].Ok);
        Assert.True(list[1].Ok);
    }

    [Fact]
    public void Previous_day_and_other_lines_are_not_shown()
    {
        ScanHistoryStore.Add("LINE-INJ-01", E(1), Today.AddDays(-1));
        ScanHistoryStore.Add("LINE-INJ-02", E(2), Today);
        ScanHistoryStore.Add("LINE-INJ-01", E(3), Today);

        var list = ScanHistoryStore.Load("LINE-INJ-01", Today);

        Assert.Equal(new[] { "L003" }, list.Select(x => x.LotCode));
        Assert.Empty(ScanHistoryStore.Load("LINE-INJ-01", Today.AddDays(1)));   // 하루가 바뀌면 빈다
    }
}
