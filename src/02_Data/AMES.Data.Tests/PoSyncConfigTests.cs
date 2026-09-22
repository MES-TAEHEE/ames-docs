using AMES.Data.Services.PoSync;
using Xunit;
using static AMES.Data.Services.PoSync.PoSyncConfig;

namespace AMES.Data.Tests;

/// <summary>공통코드 4그룹 → 소스 목록. 오버라이드·폴백·누락 오류의 정본.</summary>
public class PoSyncConfigTests
{
    static CodeRow G(string v, string attr) => new(GroupGlobal, v, null, attr, null, true);
    static CodeRow Src(string key, string? cust, string? desc, bool use = true, string name = "N")
        => new(GroupSource, key, name, cust, desc, use);
    static CodeRow Url(string key, string? url) => new(GroupUrl, key, null, null, url, true);
    static CodeRow Auth(string key, string? scheme, string? val) => new(GroupAuth, key, null, scheme, val, true);

    const string Params = "CORCD=7700;BIZCD=7710;VENDCD=310471;PURC_ORG=1A7700";

    [Fact]
    public void Groups_share_the_scheduled_worker_prefix_and_fit_the_group_code_column()
    {
        Assert.Equal("SW_POSYNC", GroupGlobal);
        foreach (var g in new[] { GroupGlobal, GroupSource, GroupUrl, GroupAuth })
        {
            Assert.StartsWith("SW_", g);
            Assert.True(g.Length <= 20, g);
        }
    }

    [Fact]
    public void ParseParams_is_case_insensitive_and_trims()
    {
        var p = ParseParams(" corcd = 7700 ; BIZCD=7710;;PURC_ORG=1A7700 ");
        Assert.Equal("7700", p["CORCD"]);
        Assert.Equal("7710", p["bizcd"]);
        Assert.Equal("1A7700", p["PURC_ORG"]);
        Assert.Empty(ParseParams(null));
    }

    [Theory]
    [InlineData("-7,60", -7, 60)]
    [InlineData(" -3 , 30 ", -3, 30)]
    [InlineData("0,0", 0, 0)]
    public void TryParseWindow_parses(string s, int from, int to)
    {
        Assert.True(TryParseWindow(s, out var f, out var t));
        Assert.Equal((from, to), (f, t));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("7")]
    [InlineData("a,b")]
    [InlineData("10,-5")]   // from > to
    public void TryParseWindow_rejects(string? s)
        => Assert.False(TryParseWindow(s, out _, out _));

    [Fact]
    public void Resolve_builds_source_with_globals()
    {
        var r = Resolve([G("INTERVAL", "45"), G("WINDOW", "-5,40"),
                         Src("SEMS", "C-SEMS", Params), Url("SEMS", "https://x/y"), Auth("SEMS", "Bearer", "tok")]);
        Assert.Empty(r.Errors);
        Assert.Equal(45, r.GlobalIntervalMin);
        var s = Assert.Single(r.Sources);
        Assert.Equal("SEMS", s.Key);
        Assert.Equal("POSYNC-SEMS", s.InterfaceCode);
        Assert.Equal("POSYNC-SEMS", InterfaceCodeFor("SEMS"));
        Assert.Equal("POSYNC-" + new string('K', MaxKeyLen), InterfaceCodeFor(new string('K', MaxKeyLen + 5)));
        Assert.Equal("C-SEMS", s.CustomerId);
        Assert.Equal("https://x/y", s.Url);
        Assert.Equal("Bearer", s.AuthScheme);
        Assert.Equal("tok", s.AuthValue);
        Assert.Equal(("7700", "7710", "310471", "1A7700"), (s.CorCd, s.BizCd, s.VendCd, s.PurcOrg));
        Assert.Equal(45, s.IntervalMin);
        Assert.Equal((-5, 40), (s.WindowFrom, s.WindowTo));
    }

    [Fact]
    public void Resolve_source_overrides_window_and_ignores_unknown_params()
    {
        // 주기는 전역만 쓴다(소스별 INTERVAL 폐지) — 설명란에 남은 구 파라미터는 조용히 무시돼야 한다.
        var r = Resolve([G("INTERVAL", "45"), G("WINDOW", "-5,40"),
                         Src("SEMS", "C", Params + ";INTERVAL=120;WINDOW=-3,30;PO_TYPE=1KNB;STR_LOC=3110"), Url("SEMS", "u")]);
        Assert.Empty(r.Errors);
        var s = Assert.Single(r.Sources);
        Assert.Equal(45, s.IntervalMin);
        Assert.Equal((-3, 30), (s.WindowFrom, s.WindowTo));
    }

    [Fact]
    public void Resolve_falls_back_to_defaults_when_globals_missing_or_bad()
    {
        var r = Resolve([G("INTERVAL", "abc"), Src("SEMS", "C", Params), Url("SEMS", "u")]);
        Assert.Equal(30, r.GlobalIntervalMin);
        var s = Assert.Single(r.Sources);
        Assert.Equal(30, s.IntervalMin);
        Assert.Equal((-60, 0), (s.WindowFrom, s.WindowTo));
        Assert.Null(s.AuthScheme);
    }

    [Fact]
    public void Resolve_skips_inactive_source_silently()
    {
        var r = Resolve([Src("SEMS", "C", Params, use: false), Url("SEMS", "u")]);
        Assert.Empty(r.Sources);
        Assert.Empty(r.Errors);
    }

    [Theory]
    [InlineData(null, "u", "CustomerID")]
    [InlineData("C", null, "URL")]
    [InlineData("C", "  ", "URL")]
    public void Resolve_reports_missing_customer_or_url(string? cust, string? url, string expectedWord)
    {
        var rows = new List<CodeRow> { Src("SEMS", cust, Params) };
        if (url is not null) rows.Add(Url("SEMS", url));
        var r = Resolve(rows);
        Assert.Empty(r.Sources);
        var e = Assert.Single(r.Errors);
        Assert.Equal("SEMS", e.Key);
        Assert.Contains(expectedWord, e.Message);
    }

    [Fact]
    public void Resolve_reports_missing_required_param()
    {
        var r = Resolve([Src("SEMS", "C", "CORCD=7700;BIZCD=7710;VENDCD=310471"), Url("SEMS", "u")]);
        Assert.Contains("PURC_ORG", Assert.Single(r.Errors).Message);
    }

    [Fact]
    public void Resolve_reports_key_too_long()
    {
        var key = new string('K', 14);
        var r = Resolve([Src(key, "C", Params), Url(key, "u")]);
        Assert.Contains("13", Assert.Single(r.Errors).Message);
    }

    [Fact]
    public void Resolve_global_interval_zero_disables_every_source_without_error()
    {
        var r = Resolve([G("INTERVAL", "0"), Src("SEMS", "C", Params), Url("SEMS", "u"),
                         Src("OTHER", "C2", Params), Url("OTHER", "u2")]);
        Assert.Equal(0, r.GlobalIntervalMin);
        Assert.All(r.Sources, s => Assert.Equal(0, s.IntervalMin));
        Assert.Empty(r.Errors);
    }

    [Fact]
    public void Resolve_matches_url_and_auth_keys_case_insensitively()
    {
        var r = Resolve([Src("SEMS", "C", Params), Url("sems", "u"), Auth("Sems", "Basic", "a:b")]);
        var s = Assert.Single(r.Sources);
        Assert.Equal("u", s.Url);
        Assert.Equal("Basic", s.AuthScheme);
    }

    [Fact]
    public void Resolve_isolates_a_broken_source_from_a_good_one()
    {
        var r = Resolve([Src("BAD", null, Params), Src("SEMS", "C", Params), Url("SEMS", "u")]);
        var s = Assert.Single(r.Sources);
        Assert.Equal("SEMS", s.Key);
        var e = Assert.Single(r.Errors);
        Assert.Equal("BAD", e.Key);
        Assert.Contains("CustomerID", e.Message);
    }

    [Fact]
    public void Resolve_reports_duplicate_url_rows_but_other_sources_still_resolve()
    {
        var r = Resolve([Src("SEMS", "C", Params), Url("SEMS", "u1"), Url("SEMS", "u2"),
                         Src("OK", "C2", Params), Url("OK", "u3")]);
        var s = Assert.Single(r.Sources);
        Assert.Equal("OK", s.Key);
        var e = Assert.Single(r.Errors);
        Assert.Equal("SEMS", e.Key);
        Assert.Contains(GroupUrl, e.Message);
    }

    [Fact]
    public void Resolve_reports_duplicate_source_rows_as_one_error_and_skips_both()
    {
        var r = Resolve([Src("SEMS", "C", Params, name: "First"), Src("SEMS", "C2", Params, name: "Second"),
                         Url("SEMS", "u")]);
        Assert.Empty(r.Sources);
        var e = Assert.Single(r.Errors);
        Assert.Equal("SEMS", e.Key);
        Assert.Contains(GroupSource, e.Message);
    }

    [Fact]
    public void Resolve_duplicate_globals_last_wins_without_error()
    {
        var r = Resolve([G("INTERVAL", "10"), G("INTERVAL", "20"), Src("SEMS", "C", Params), Url("SEMS", "u")]);
        Assert.Empty(r.Errors);
        Assert.Equal(20, r.GlobalIntervalMin);
    }

    [Fact]
    public void Resolve_reads_worker_timing_from_globals()
    {
        var r = Resolve([G("TICK_SEC", " 15 "), G("STARTUP_DELAY_SEC", "0"), G("TIMEOUT_SEC", "120"),
                         Src("SEMS", "C", Params), Url("SEMS", "u")]);
        Assert.Equal((15, 0, 120), (r.TickSec, r.StartupDelaySec, r.TimeoutSec));
        Assert.Equal(120, Assert.Single(r.Sources).TimeoutSec);
    }

    [Fact]
    public void Resolve_leaves_worker_timing_null_when_missing_or_not_a_number()
    {
        // null 이면 Api 가 appsettings ScheduledWorker 공통 기본값을 쓴다. 범위 검사도 그쪽에서 한다.
        var r = Resolve([G("TICK_SEC", "abc"), Src("SEMS", "C", Params), Url("SEMS", "u")]);
        Assert.Equal(((int?)null, (int?)null, (int?)null), (r.TickSec, r.StartupDelaySec, r.TimeoutSec));
        Assert.Null(Assert.Single(r.Sources).TimeoutSec);
    }

    // ── Web → Api 수동 실행 서비스 키 (SW_POSYNC_AUTH / AMES_SERVICE_KEY) ──

    const string Key32 = "0123456789abcdef0123456789abcdef";

    [Fact]
    public void ServiceKey_code_can_never_be_a_source_key()
        => Assert.True(ServiceKeyCode.Length > MaxKeyLen);

    [Fact]
    public void ResolveServiceKey_reads_the_description_of_the_reserved_auth_row()
    {
        Assert.Equal(Key32, ResolveServiceKey([Auth("SEMS", "Bearer", "other"), Auth(ServiceKeyCode, null, $" {Key32} ")]));
        Assert.Equal(Key32, ResolveServiceKey([Auth("ames_service_key", null, Key32)]));
    }

    [Fact]
    public void ResolveServiceKey_is_null_when_missing_disabled_short_or_duplicated()
    {
        Assert.Null(ResolveServiceKey([Auth("SEMS", "Bearer", Key32)]));
        Assert.Null(ResolveServiceKey([new CodeRow(GroupAuth, ServiceKeyCode, null, null, Key32, false)]));
        Assert.Null(ResolveServiceKey([Auth(ServiceKeyCode, null, new string('a', MinServiceKeyLen - 1))]));
        Assert.Null(ResolveServiceKey([Auth(ServiceKeyCode, null, null)]));
        // 어느 쪽이 맞는 키인지 알 수 없다 — last-wins 로 조용히 고르지 않고 서비스 키 경로를 끈다
        Assert.Null(ResolveServiceKey([Auth(ServiceKeyCode, null, Key32), Auth(ServiceKeyCode, null, Key32 + "x")]));
        // 다른 그룹의 같은 CodeValue 는 키가 아니다
        Assert.Null(ResolveServiceKey([new CodeRow(GroupUrl, ServiceKeyCode, null, null, Key32, true)]));
    }

    [Fact]
    public void ServiceKeyMatches_requires_a_configured_key_and_an_exact_match()
    {
        Assert.True(ServiceKeyMatches(Key32, Key32));
        Assert.False(ServiceKeyMatches(Key32, Key32 + "x"));
        Assert.False(ServiceKeyMatches(Key32, Key32.ToUpperInvariant()));
        Assert.False(ServiceKeyMatches(Key32, null));
        Assert.False(ServiceKeyMatches(Key32, ""));
        Assert.False(ServiceKeyMatches(null, null));
        Assert.False(ServiceKeyMatches(null, ""));
        Assert.False(ServiceKeyMatches("", ""));
    }

    [Fact]
    public void Resolve_ignores_the_service_key_row()
    {
        var r = Resolve([Src("SEMS", "C", Params), Url("SEMS", "u"), Auth(ServiceKeyCode, null, Key32)]);
        Assert.Empty(r.Errors);
        Assert.Null(Assert.Single(r.Sources).AuthScheme);
    }
}
