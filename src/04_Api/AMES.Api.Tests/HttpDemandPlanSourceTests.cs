using AMES.Api.Workers.DemandPlanSync;
using AMES.Data.Services.DemandPlan;
using Microsoft.AspNetCore.WebUtilities;
using Xunit;

namespace AMES.Api.Tests;

/// <summary>MM30011 요청 형식(잠정 계약): GET + 신원 4개 + PLAN_DATE(+인증). 실제 매개변수 이름이 다르면 HttpDemandPlanSource 상수만 바꾼다.</summary>
public class HttpDemandPlanSourceTests
{
    static DemandPlanSource Src(string? scheme = null, string? value = null)
        => new("SEMS", "SEMS", "CUS-SAV", "http://srm/Service/WEBSRV_INQUERY_MIP.ashx", scheme, value, "7700", "7710", "310471", "1A7700", 60, null);
    static Dictionary<string, string> Query(HttpRequestMessage req) => QueryHelpers.ParseQuery(req.RequestUri!.Query).ToDictionary(kv => kv.Key, kv => kv.Value.ToString());

    [Fact]
    public void Builds_a_get_with_identity_and_plan_date()
    {
        using var req = HttpDemandPlanSource.BuildRequest(Src(), new DateOnly(2026, 9, 29));
        Assert.Equal(HttpMethod.Get, req.Method);
        Assert.Equal(new Dictionary<string, string> { ["CORCD"] = "7700", ["BIZCD"] = "7710", ["PURC_ORG"] = "1A7700", ["VENDCD"] = "310471", ["PLAN_DATE"] = "2026-09-29" }, Query(req));
        Assert.Null(req.Headers.Authorization);
    }

    [Theory]
    [InlineData("Query:APIKEY", "k1")]
    [InlineData("Bearer", "tok")]
    [InlineData("Basic", "u:p")]
    [InlineData("Header:X-Api-Key", "hk")]
    public void Applies_auth_like_po_source(string scheme, string value)
    {
        using var req = HttpDemandPlanSource.BuildRequest(Src(scheme, value), new DateOnly(2026, 9, 29));
        switch (scheme)
        {
            case "Query:APIKEY":    Assert.Equal("k1", Query(req)["APIKEY"]); break;
            case "Bearer":          Assert.Equal("tok", req.Headers.Authorization!.Parameter); break;
            case "Basic":           Assert.Equal(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("u:p")), req.Headers.Authorization!.Parameter); break;
            case "Header:X-Api-Key": Assert.Equal("hk", req.Headers.GetValues("X-Api-Key").Single()); break;
        }
    }

    [Fact]
    public void Unknown_auth_scheme_throws()
        => Assert.Throws<InvalidOperationException>(() => HttpDemandPlanSource.BuildRequest(Src("Digest", "x"), new DateOnly(2026, 9, 29)));
}
