using AMES.Data.Services;
using AMES.Data.Services.DemandPlan;
using Xunit;

namespace AMES.Data.Tests.DemandPlan;

/// <summary>SW_DPSYNC* 공통코드 → DemandPlanSource. 해석 규칙은 PoSync 와 공용(WorkerSourceConfig).</summary>
public class DemandPlanConfigTests
{
    const string Params = "CORCD=7700;BIZCD=7710;VENDCD=310471;PURC_ORG=1A7700";
    static WorkerCodeRow G(string v, string a) => new(DemandPlanConfig.GroupGlobal, v, null, a, null, true);
    static WorkerCodeRow Src(string k, string cust, string desc, bool use = true) => new(DemandPlanConfig.GroupSource, k, "Name " + k, cust, desc, use);
    static WorkerCodeRow Url(string k, string url) => new(DemandPlanConfig.GroupUrl, k, null, null, url, true);
    static WorkerCodeRow Auth(string k, string scheme, string val) => new(DemandPlanConfig.GroupAuth, k, null, scheme, val, true);

    [Fact]
    public void Groups_fit_the_code_column_and_share_the_prefix()
    {
        Assert.Equal("SW_DPSYNC", DemandPlanConfig.GroupGlobal);
        Assert.Equal("SW_DPSYNC_SOURCE", DemandPlanConfig.GroupSource);
        Assert.All(new[] { DemandPlanConfig.GroupGlobal, DemandPlanConfig.GroupSource, DemandPlanConfig.GroupUrl, DemandPlanConfig.GroupAuth }, g => Assert.True(g.Length <= 20));
        Assert.Equal("DPSYNC-SEMS", DemandPlanConfig.InterfaceCodeFor("SEMS"));
    }

    [Fact]
    public void Resolve_builds_source_without_window()
    {
        var r = DemandPlanConfig.Resolve([G("INTERVAL", "90"), G("TIMEOUT_SEC", "40"), Src("SEMS", "C-SEMS", Params), Url("SEMS", "https://x/mip"), Auth("SEMS", "Query:APIKEY", "k")]);
        Assert.Empty(r.Errors);
        Assert.Equal(90, r.GlobalIntervalMin);
        Assert.Equal(40, r.TimeoutSec);
        var s = Assert.Single(r.Sources);
        Assert.Equal(("SEMS", "C-SEMS", "https://x/mip", "Query:APIKEY", "k", "7700", "7710", "310471", "1A7700", 90),
                     (s.Key, s.CustomerId, s.Url, s.AuthScheme, s.AuthValue, s.CorCd, s.BizCd, s.VendCd, s.PurcOrg, s.IntervalMin));
        Assert.Equal("DPSYNC-SEMS", s.InterfaceCode);
    }

    [Fact]
    public void Resolve_defaults_interval_to_60_and_reports_missing_pieces()
    {
        var r = DemandPlanConfig.Resolve([Src("A", "C", "CORCD=1;BIZCD=2"), Url("A", "https://x")]);
        Assert.Equal(60, r.GlobalIntervalMin);
        Assert.Empty(r.Sources);
        var e = Assert.Single(r.Errors);
        Assert.Contains("VENDCD", e.Message); Assert.Contains("PURC_ORG", e.Message);
    }

    [Fact]
    public void Resolve_duplicate_source_key_is_an_error_and_inactive_is_skipped()
    {
        var r = DemandPlanConfig.Resolve([Src("A", "C", Params), Src("A", "C", Params), Src("B", "C", Params, use: false), Url("A", "u"), Url("B", "u")]);
        Assert.Empty(r.Sources);
        Assert.Single(r.Errors, e => e.Key == "A");
    }

    [Fact]
    public void Resolve_two_active_sources_same_customer_are_config_errors_and_excluded()
    {
        var r = DemandPlanConfig.Resolve([
            Src("SEMS", "C-DUP", Params), Url("SEMS", "https://x/1"), Auth("SEMS", "Query:APIKEY", "k1"),
            Src("GEO", "c-dup", Params), Url("GEO", "https://x/2"), Auth("GEO", "Query:APIKEY", "k2"),
        ]);
        Assert.Empty(r.Sources);
        Assert.Equal(2, r.Errors.Count);
        Assert.Contains(r.Errors, e => e.Key == "SEMS");
        Assert.Contains(r.Errors, e => e.Key == "GEO");
        Assert.All(r.Errors, e =>
        {
            Assert.Contains("C-DUP", e.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("고객당 1개만 허용", e.Message);
        });
    }

    [Fact]
    public void Resolve_distinct_customers_are_unaffected_by_duplicate_customer_check()
    {
        var r = DemandPlanConfig.Resolve([
            Src("SEMS", "C-1", Params), Url("SEMS", "https://x/1"), Auth("SEMS", "Query:APIKEY", "k1"),
            Src("GEO", "C-2", Params), Url("GEO", "https://x/2"), Auth("GEO", "Query:APIKEY", "k2"),
        ]);
        Assert.Empty(r.Errors);
        Assert.Equal(2, r.Sources.Count);
    }
}
