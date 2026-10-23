using AMES.Data.Services.Notify;
using Xunit;

namespace AMES.Data.Tests;

// 10-24 PUSH 알림 발송기 규칙(NotifyRules·NotifyConfig) — 방해 금지 시간, 날마다 한 번 보내는 원천 번호, 재시도 간격, FCM 토큰, 설정 기본값
public class NotifyRulesTests
{
    static TimeSpan T(int h, int m = 0) => new(h, m, 0);

    [Theory]
    [InlineData(23, 0, 22, 0, 6, 0, true)]    // 자정을 넘는 구간 — 밤
    [InlineData(5, 59, 22, 0, 6, 0, true)]    // 새벽
    [InlineData(6, 0, 22, 0, 6, 0, false)]    // 끝 시각은 포함하지 않음
    [InlineData(12, 0, 22, 0, 6, 0, false)]
    [InlineData(13, 0, 12, 0, 14, 0, true)]   // 같은 날 구간
    [InlineData(14, 0, 12, 0, 14, 0, false)]
    [InlineData(12, 0, 12, 0, 12, 0, false)]  // 시작 = 끝 → 방해 금지 없음
    public void Quiet_hours_cover_ranges_across_midnight(int h, int m, int sh, int sm, int eh, int em, bool expected)
        => Assert.Equal(expected, NotifyRules.InQuietHours(T(h, m), T(sh, sm), T(eh, em)));

    [Fact]
    public void No_quiet_hours_when_either_end_is_missing()
    {
        Assert.False(NotifyRules.InQuietHours(T(23), null, T(6)));
        Assert.False(NotifyRules.InQuietHours(T(23), T(22), null));
    }

    [Fact]
    public void Daily_ref_appends_the_date_and_fits_the_column()
    {
        Assert.Equal("PM-2610-001@20261024", NotifyRules.DailyRef("PM-2610-001", new DateTime(2026, 10, 24)));
        var longId = new string('X', 60);
        var r = NotifyRules.DailyRef(longId, new DateTime(2026, 10, 24));
        Assert.Equal(NotifyRules.SourceRefIdMax, r.Length);
        Assert.EndsWith("@20261024", r);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(3, 8)]
    [InlineData(5, 30)]
    [InlineData(9, 30)]
    public void Retry_delay_doubles_up_to_thirty_minutes(int retry, int minutes)
        => Assert.Equal(minutes, NotifyRules.RetryDelayMin(retry));

    [Theory]
    [InlineData("dXyZ:APA91bH-abc_123", true)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData("has space", false)]
    [InlineData(null, false)]
    public void Token_must_be_non_empty_without_spaces(string? token, bool ok)
        => Assert.Equal(ok, NotifyRules.IsValidToken(token));

    [Fact]
    public void Token_longer_than_the_address_column_is_rejected()
        => Assert.False(NotifyRules.IsValidToken(new string('a', NotifyRules.TokenMax + 1)));

    [Fact]
    public void Config_uses_defaults_and_rejects_out_of_range_values()
    {
        var d = NotifyConfig.Resolve(new Dictionary<string, string?>());
        Assert.Equal((1, 60, 3, 100), (d.IntervalMin, d.LookbackMin, d.MaxRetry, d.BatchSize));
        Assert.Null(d.TickSec);

        var c = NotifyConfig.Resolve(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["INTERVAL"] = "0", ["LOOKBACK_MIN"] = "0", ["MAX_RETRY"] = "11", ["BATCH"] = " 50 ", ["TICK_SEC"] = "30",
        });
        Assert.Equal(0, c.IntervalMin);             // 0 = 중지
        Assert.Equal(60, c.LookbackMin);            // 범위 밖 → 기본값
        Assert.Equal(3, c.MaxRetry);
        Assert.Equal(50, c.BatchSize);
        Assert.Equal(30, c.TickSec);
    }

    [Fact]
    public void Operator_role_selects_field_workers()
        => Assert.Equal("ROLE-OPERATOR", NotifyRules.WorkerRoleId);
}
